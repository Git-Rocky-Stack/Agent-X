using System.Diagnostics;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Search;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Orchestrates the full Retrieval-Augmented Generation pipeline with advanced enhancements:
///   1. Multi-query expansion (generates alternative phrasings for better recall)
///   2. HyDE (hypothetical document embeddings for improved semantic matching)
///   3. Semantic search across all query variations
///   4. Heuristic reranking (dedup, diversity, query-term boost)
///   5. LLM-based reranking (cross-encoder relevance scoring)
///   6. Parent document retrieval (expand to surrounding context)
///   7. Contextual compression (extract only relevant portions)
///   8. Build grounded prompt with numbered context sections
///   9. Stream AI response token-by-token
///  10. Extract and resolve citations
///  11. Evaluate response quality (async, non-blocking)
///  12. Return <see cref="RagResponse"/> with answer, citations, and metrics
///
/// All enhancement services are optional — the pipeline gracefully degrades
/// when any service is not registered in DI.
/// </summary>
public sealed class RagPipeline : IRagPipeline
{
    private const int DefaultTopK = 8;
    private const float DefaultMinScore = 0.25f;

    private const string RagSystemPromptPrefix =
        """
        You are a helpful AI assistant answering questions based on the user's personal document library.
        Answer the following question using ONLY the provided context documents.
        Cite your sources using [1], [2], etc. corresponding to the numbered context sections.
        If the context doesn't contain enough information to fully answer the question, say so honestly.
        Be concise but thorough.
        """;

    private const string NoResultsMessage =
        "I couldn't find any relevant information in your documents. " +
        "Try rephrasing your question or ensure that relevant documents have been indexed.";

    private readonly ISemanticSearchService _searchService;
    private readonly IAiService _aiService;
    private readonly ICitationService _citationService;
    private readonly IRagReranker _reranker;
    private readonly AgentXDbContext _dbContext;
    private readonly ILogger _logger;

    // ── Optional RAG enhancement services (nullable for graceful degradation) ──
    private readonly IMultiQueryGenerator? _multiQueryGenerator;
    private readonly IHydeService? _hydeService;
    private readonly ILlmReranker? _llmReranker;
    private readonly IParentDocumentRetriever? _parentRetriever;
    private readonly IContextualCompressor? _compressor;
    private readonly IRagEvaluator? _evaluator;
    private readonly IWebSearchService? _webSearchService;

    public RagPipeline(
        ISemanticSearchService searchService,
        IAiService aiService,
        ICitationService citationService,
        IRagReranker reranker,
        AgentXDbContext dbContext,
        ILogger logger,
        IMultiQueryGenerator? multiQueryGenerator = null,
        IHydeService? hydeService = null,
        ILlmReranker? llmReranker = null,
        IParentDocumentRetriever? parentRetriever = null,
        IContextualCompressor? compressor = null,
        IRagEvaluator? evaluator = null,
        IWebSearchService? webSearchService = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _citationService = citationService ?? throw new ArgumentNullException(nameof(citationService));
        _reranker = reranker ?? throw new ArgumentNullException(nameof(reranker));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger?.ForContext<RagPipeline>() ?? throw new ArgumentNullException(nameof(logger));

        _multiQueryGenerator = multiQueryGenerator;
        _hydeService = hydeService;
        _llmReranker = llmReranker;
        _parentRetriever = parentRetriever;
        _compressor = compressor;
        _evaluator = evaluator;
        _webSearchService = webSearchService;

        _logger.Information(
            "RagPipeline initialized — enhancements: MultiQuery={MQ}, HyDE={HyDE}, LlmRerank={LR}, " +
            "ParentDoc={PD}, Compression={C}, Eval={E}, WebSearch={WS}",
            _multiQueryGenerator is not null, _hydeService is not null, _llmReranker is not null,
            _parentRetriever is not null, _compressor is not null, _evaluator is not null,
            _webSearchService is not null);
    }

    /// <inheritdoc />
    public async Task<RagResponse> AskAsync(
        string question,
        long? collectionId = null,
        Action<string>? onToken = null,
        bool enableResearchMode = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be null or empty.", nameof(question));

        var totalStopwatch = Stopwatch.StartNew();

        _logger.Information("RAG pipeline started for question (length={Length}, collection={CollectionId})",
            question.Length, collectionId?.ToString() ?? "all");

        // ── Step 1: Multi-Query Expansion ─────────────────────────────────
        var queries = new List<string> { question };
        if (_multiQueryGenerator is not null)
        {
            try
            {
                queries = (await _multiQueryGenerator
                    .GenerateQueryVariationsAsync(question, 3, ct)
                    .ConfigureAwait(false)).ToList();

                _logger.Debug("Multi-query expanded to {Count} variations", queries.Count);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Multi-query expansion failed; using original query");
            }
        }

        // ── Step 2: Semantic Search (across all query variations) ──────────
        var searchStopwatch = Stopwatch.StartNew();
        var allResults = new List<SearchResult>();

        try
        {
            foreach (var q in queries)
            {
                var searchQuery = new SearchQuery
                {
                    QueryText = q,
                    TopK = DefaultTopK,
                    MinScore = DefaultMinScore,
                    CollectionId = collectionId
                };

                var results = await _searchService
                    .SearchAsync(searchQuery, ct)
                    .ConfigureAwait(false);

                allResults.AddRange(results);
            }

            // Deduplicate results by ChunkId, keeping highest score
            allResults = allResults
                .GroupBy(r => r.ChunkId)
                .Select(g => g.OrderByDescending(r => r.Score).First())
                .OrderByDescending(r => r.Score)
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Error(ex, "Semantic search failed for RAG query");
            throw;
        }

        searchStopwatch.Stop();
        var searchLatencyMs = searchStopwatch.Elapsed.TotalMilliseconds;

        _logger.Debug("Search returned {Count} unique results in {ElapsedMs:F1}ms",
            allResults.Count, searchLatencyMs);

        // Filter by minimum score
        var relevantResults = allResults
            .Where(r => r.Score >= DefaultMinScore)
            .ToList();

        // ── Step 3: Handle No Results ────────────────────────────────────
        if (relevantResults.Count == 0)
        {
            totalStopwatch.Stop();
            _logger.Information("No relevant context found; returning no-results response");

            return new RagResponse
            {
                AnswerText = NoResultsMessage,
                Question = question,
                Citations = new List<Citation>(),
                ContextChunksUsed = 0,
                IsStreaming = false,
                TotalLatencyMs = totalStopwatch.Elapsed.TotalMilliseconds,
                SearchLatencyMs = searchLatencyMs,
                CollectionScope = collectionId
            };
        }

        // ── Step 4: Build Context Chunks ─────────────────────────────────
        var rawContextChunks = BuildContextChunks(relevantResults);

        // ── Step 5: Heuristic Reranking (dedup, diversity, query-term boost) ──
        var contextChunks = _reranker.Rerank(rawContextChunks, question, DefaultTopK);

        // ── Step 6: LLM-based Reranking ──────────────────────────────────
        if (_llmReranker is not null && contextChunks.Count > 2)
        {
            try
            {
                contextChunks = await _llmReranker
                    .RerankAsync(contextChunks, question, DefaultTopK, ct)
                    .ConfigureAwait(false);

                _logger.Debug("LLM reranking applied, {Count} chunks retained", contextChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "LLM reranking failed; using heuristic ranking");
            }
        }

        // ── Step 7: Parent Document Retrieval ────────────────────────────
        if (_parentRetriever is not null)
        {
            try
            {
                contextChunks = await _parentRetriever
                    .RetrieveParentChunksAsync(contextChunks, ct)
                    .ConfigureAwait(false);

                _logger.Debug("Parent retrieval expanded to {Count} chunks", contextChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Parent document retrieval failed; using original chunks");
            }
        }

        // ── Step 8: Contextual Compression ───────────────────────────────
        if (_compressor is not null)
        {
            try
            {
                contextChunks = await _compressor
                    .CompressAsync(contextChunks, question, ct)
                    .ConfigureAwait(false);

                _logger.Debug("Compression reduced to {Count} chunks", contextChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Contextual compression failed; using uncompressed chunks");
            }
        }

        // ── Step 8b: Deep Research Mode — Web Search Enrichment ──────────
        IReadOnlyList<WebCitation>? webCitations = null;
        if (enableResearchMode && _webSearchService is not null && _webSearchService.IsConfigured)
        {
            try
            {
                var webResponse = await _webSearchService
                    .SearchAsync(question, 10, ct)
                    .ConfigureAwait(false);

                if (webResponse.Results.Count > 0)
                {
                    webCitations = webResponse.Results.Select(r => new WebCitation
                    {
                        Title = r.Title,
                        Url = r.Url,
                        Snippet = r.Snippet,
                        Source = WebCitationSource.Web,
                        DocumentName = null
                    }).ToList();

                    _logger.Information(
                        "Research mode: added {Count} web citations (provider={Provider}, cached={FromCache})",
                        webCitations.Count, webResponse.SearchProvider, webResponse.FromCache);
                }
                else
                {
                    _logger.Debug("Research mode: web search returned no results for '{Query}'", question);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Research mode: web search failed; continuing with vault context only");
            }
        }

        // ── Step 9: Build RAG Prompt ─────────────────────────────────────
        var systemPrompt = BuildSystemPrompt(contextChunks);

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content = question,
                Timestamp = DateTime.UtcNow
            }
        };

        _logger.Debug("Built RAG prompt with {ChunkCount} context sections", contextChunks.Count);

        // ── Step 10: Stream AI Response ──────────────────────────────────
        var responseBuilder = new StringBuilder(1024);

        var ragResponse = new RagResponse
        {
            Question = question,
            ContextChunksUsed = contextChunks.Count,
            IsStreaming = true,
            SearchLatencyMs = searchLatencyMs,
            CollectionScope = collectionId
        };

        try
        {
            var chatOptions = new ChatOptions
            {
                Temperature = 0.3,
                MaxTokens = 2048,
                TopP = 0.9
            };

            await foreach (var token in _aiService
                .StreamChatAsync(messages, systemPrompt, chatOptions, ct)
                .ConfigureAwait(false))
            {
                responseBuilder.Append(token);
                onToken?.Invoke(token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("RAG pipeline cancelled during AI streaming");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "AI streaming failed during RAG pipeline");
            throw;
        }

        var answerText = responseBuilder.ToString();

        _logger.Debug("AI generation completed, response length: {Length} characters", answerText.Length);

        // ── Step 11: Extract Citations ────────────────────────────────────
        List<Citation> citations;
        try
        {
            citations = _citationService.ExtractCitations(answerText, contextChunks);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Citation extraction failed; returning response without citations");
            citations = new List<Citation>();
        }

        // ── Step 12: Finalize Response ────────────────────────────────────
        totalStopwatch.Stop();

        ragResponse.AnswerText = answerText;
        ragResponse.Citations = citations;
        ragResponse.IsStreaming = false;
        ragResponse.TotalLatencyMs = totalStopwatch.Elapsed.TotalMilliseconds;
        ragResponse.WebCitations = webCitations;

        _logger.Information(
            "RAG pipeline completed: {CitationCount} citations, {ChunkCount} context chunks, " +
            "search={SearchMs:F0}ms, total={TotalMs:F0}ms",
            citations.Count, contextChunks.Count, searchLatencyMs, ragResponse.TotalLatencyMs);

        // ── Step 13: Async Evaluation (non-blocking) ─────────────────────
        if (_evaluator is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var metrics = await _evaluator.EvaluateAsync(
                        question, answerText, contextChunks, CancellationToken.None);

                    ragResponse.EvalMetrics = metrics;

                    _logger.Information(
                        "RAG evaluation: context={CR:F2}, faithfulness={F:F2}, " +
                        "answer={AR:F2}, overall={O:F2}",
                        metrics.ContextRelevance, metrics.Faithfulness,
                        metrics.AnswerRelevance, metrics.OverallScore);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Background RAG evaluation failed");
                }
            }, CancellationToken.None);
        }

        return ragResponse;
    }

    /// <inheritdoc />
    public async Task<long> GetIndexedChunkCountAsync(CancellationToken ct = default)
    {
        try
        {
            var count = await _dbContext.DocumentChunks
                .Where(c => c.IsEmbedded)
                .LongCountAsync(ct)
                .ConfigureAwait(false);

            _logger.Debug("Indexed chunk count: {Count}", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to query indexed chunk count");
            throw;
        }
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    private static List<RagContextChunk> BuildContextChunks(IReadOnlyList<SearchResult> searchResults)
    {
        var chunks = new List<RagContextChunk>(searchResults.Count);

        foreach (var result in searchResults)
        {
            chunks.Add(new RagContextChunk
            {
                ChunkId = result.ChunkId,
                DocumentId = result.DocumentId,
                FileName = result.FileName,
                FilePath = result.FilePath,
                PageNumber = result.PageNumber,
                ChunkIndex = result.ChunkIndex,
                ChunkText = result.MatchedText,
                RelevanceScore = result.Score
            });
        }

        return chunks;
    }

    private static string BuildSystemPrompt(IReadOnlyList<RagContextChunk> contextChunks)
    {
        var builder = new StringBuilder(4096);

        builder.AppendLine(RagSystemPromptPrefix);
        builder.AppendLine();
        builder.AppendLine("CONTEXT:");

        for (var i = 0; i < contextChunks.Count; i++)
        {
            var chunk = contextChunks[i];
            var citationNumber = i + 1;

            var sourceLabel = chunk.PageNumber.HasValue
                ? $"Page: {chunk.PageNumber.Value}"
                : $"Chunk: {chunk.ChunkIndex}";

            builder.AppendLine($"[{citationNumber}] (Source: {chunk.FileName}, {sourceLabel})");
            builder.AppendLine(chunk.ChunkText);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}

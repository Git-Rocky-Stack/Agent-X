using System.Diagnostics;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Observability;
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

    private readonly IHybridSearchOrchestrator _searchOrchestrator;
    private readonly IAiService _aiService;
    private readonly ICitationService _citationService;
    private readonly IRagReranker _reranker;
    private readonly AgentXDbContext _dbContext;
    private readonly ILogger _logger;
    private readonly IRagConfiguration _ragConfiguration;

    // ── Optional RAG enhancement services (nullable for graceful degradation) ──
    private readonly IMultiQueryGenerator? _multiQueryGenerator;
    private readonly IHydeService? _hydeService;
    private readonly ILlmReranker? _llmReranker;
    private readonly IParentDocumentRetriever? _parentRetriever;
    private readonly IContextualCompressor? _compressor;
    private readonly IRagEvaluator? _evaluator;
    private readonly IWebSearchService? _webSearchService;
    private readonly IRagMetrics? _metrics;
    private readonly IPiiDetector? _piiDetector;

    public RagPipeline(
        IHybridSearchOrchestrator searchOrchestrator,
        IAiService aiService,
        ICitationService citationService,
        IRagReranker reranker,
        AgentXDbContext dbContext,
        ILogger logger,
        IRagConfiguration ragConfiguration,
        IMultiQueryGenerator? multiQueryGenerator = null,
        IHydeService? hydeService = null,
        ILlmReranker? llmReranker = null,
        IParentDocumentRetriever? parentRetriever = null,
        IContextualCompressor? compressor = null,
        IRagEvaluator? evaluator = null,
        IWebSearchService? webSearchService = null,
        IRagMetrics? metrics = null,
        IPiiDetector? piiDetector = null)
    {
        _searchOrchestrator = searchOrchestrator ?? throw new ArgumentNullException(nameof(searchOrchestrator));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _citationService = citationService ?? throw new ArgumentNullException(nameof(citationService));
        _reranker = reranker ?? throw new ArgumentNullException(nameof(reranker));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger?.ForContext<RagPipeline>() ?? throw new ArgumentNullException(nameof(logger));
        _ragConfiguration = ragConfiguration ?? throw new ArgumentNullException(nameof(ragConfiguration));

        _multiQueryGenerator = multiQueryGenerator;
        _hydeService = hydeService;
        _llmReranker = llmReranker;
        _parentRetriever = parentRetriever;
        _compressor = compressor;
        _evaluator = evaluator;
        _webSearchService = webSearchService;
        _metrics = metrics;
        _piiDetector = piiDetector;

        var hydeActive = _hydeService is not null && _ragConfiguration.EnableHyde;
        var piiActive = _piiDetector is not null && _ragConfiguration.EnablePiiRedaction;

        _logger.Information(
            "RagPipeline initialized — enhancements: MultiQuery={MQ}, HyDE={HyDE}, LlmRerank={LR}, " +
            "ParentDoc={PD}, Compression={C}, Eval={E}, WebSearch={WS}, Metrics={M}, PiiRedaction={PII}",
            _multiQueryGenerator is not null, hydeActive, _llmReranker is not null,
            _parentRetriever is not null, _compressor is not null, _evaluator is not null,
            _webSearchService is not null, _metrics is not null, piiActive);
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

        // ── Step 2: HyDE — generate hypothetical answer document and use it as another query ──
        // HyDE is most effective on longer / abstract queries; we threshold on character count
        // to avoid the LLM round-trip cost on short keyword-style questions.
        if (_hydeService is not null
            && _ragConfiguration.EnableHyde
            && question.Length >= _ragConfiguration.HydeMinQueryLength)
        {
            try
            {
                var hypothetical = await _hydeService
                    .GenerateHypotheticalDocumentAsync(question, ct)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(hypothetical))
                {
                    queries.Add(hypothetical);
                    _logger.Debug("HyDE added hypothetical document ({Length} chars) as additional query",
                        hypothetical.Length);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning(ex, "HyDE generation failed; continuing without hypothetical document");
            }
        }

        // ── Step 3: Hybrid Search (across all query variations) ─────────────
        // Uses IHybridSearchOrchestrator so RAG queries benefit from BOTH semantic (vector)
        // and keyword (BM25) backends, merged via Reciprocal Rank Fusion. The mode is
        // configurable — operators can fall back to pure semantic / keyword via appsettings.
        var searchMode = ResolveSearchMode(_ragConfiguration.DefaultSearchMode);
        var searchStopwatch = Stopwatch.StartNew();
        var allResults = new List<SearchResult>();

        try
        {
            foreach (var q in queries)
            {
                var searchQuery = new SearchQuery
                {
                    QueryText = q,
                    TopK = _ragConfiguration.DefaultTopK,
                    MinScore = _ragConfiguration.DefaultMinScore,
                    CollectionId = collectionId,
                    Mode = searchMode
                };

                var results = await _searchOrchestrator
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
            _logger.Error(ex, "Search failed for RAG query (mode={Mode})", searchMode);
            throw;
        }

        searchStopwatch.Stop();
        var searchLatencyMs = searchStopwatch.Elapsed.TotalMilliseconds;

        _logger.Debug("Search returned {Count} unique results in {ElapsedMs:F1}ms (mode={Mode}, queries={QueryCount})",
            allResults.Count, searchLatencyMs, searchMode, queries.Count);

        // Record search metrics (P0-3)
        if (_metrics is not null)
        {
            _metrics.RecordSearch(MapSearchType(searchMode), allResults.Count, 0, 0, searchStopwatch);
        }

        // Filter by minimum score
        var relevantResults = allResults
            .Where(r => r.Score >= _ragConfiguration.DefaultMinScore)
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
        var contextChunks = _reranker.Rerank(rawContextChunks, question, _ragConfiguration.DefaultTopK);

        // ── Step 6: LLM-based Reranking ──────────────────────────────────
        if (_llmReranker is not null && contextChunks.Count > 2)
        {
            try
            {
                contextChunks = await _llmReranker
                    .RerankAsync(contextChunks, question, _ragConfiguration.DefaultTopK, ct)
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

        // ── Step 8c: PII Redaction ──────────────────────────────────────
        // Redact emails / phone numbers / SSNs / credit cards / API keys / IPs from
        // context chunks BEFORE they enter the system prompt sent to the LLM provider.
        if (_piiDetector is not null && _ragConfiguration.EnablePiiRedaction)
        {
            int redactedCount = 0;
            for (int i = 0; i < contextChunks.Count; i++)
            {
                var chunk = contextChunks[i];
                if (string.IsNullOrEmpty(chunk.ChunkText))
                    continue;

                if (_piiDetector.ContainsPii(chunk.ChunkText))
                {
                    contextChunks[i] = new RagContextChunk
                    {
                        ChunkId = chunk.ChunkId,
                        DocumentId = chunk.DocumentId,
                        FileName = chunk.FileName,
                        FilePath = chunk.FilePath,
                        PageNumber = chunk.PageNumber,
                        ChunkIndex = chunk.ChunkIndex,
                        ChunkText = _piiDetector.RedactPii(chunk.ChunkText, _ragConfiguration.PiiRedactionMask),
                        RelevanceScore = chunk.RelevanceScore
                    };
                    redactedCount++;
                }
            }

            if (redactedCount > 0)
            {
                _logger.Information("PII redacted in {Count} of {Total} context chunks before LLM call",
                    redactedCount, contextChunks.Count);
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
                // NOTE on P1-1: the RAG system prompt is currently a single string with
                // the static instruction prefix concatenated to per-question context
                // chunks. Anthropic prompt caching keys on the full block content, so
                // caching the whole prompt would never hit. To benefit here we need to
                // split into two system blocks (cacheable prefix + non-cached context).
                // Tracked as a follow-up; cleanly cacheable callers (RagEvaluator,
                // LlmReranker, HydeService) already opt in.
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
                    var evalMetrics = await _evaluator.EvaluateAsync(
                        question, answerText, contextChunks, CancellationToken.None)
                        .ConfigureAwait(false);

                    ragResponse.EvalMetrics = evalMetrics;

                    _logger.Information(
                        "RAG evaluation: context={CR:F2}, faithfulness={F:F2}, " +
                        "answer={AR:F2}, overall={O:F2}",
                        evalMetrics.ContextRelevance, evalMetrics.Faithfulness,
                        evalMetrics.AnswerRelevance, evalMetrics.OverallScore);

                    // Record quality metrics (P0-3) — but only when the eval scores
                    // are real LLM judgements. Placeholder defaults (IsDefault=true)
                    // would skew rolling averages with a constant 0.5 floor.
                    if (!evalMetrics.IsDefault)
                    {
                        _metrics?.RecordEvaluation(new EvaluationMetrics
                        {
                            ContextRelevance = evalMetrics.ContextRelevance,
                            Faithfulness = evalMetrics.Faithfulness,
                            AnswerRelevance = evalMetrics.AnswerRelevance
                        });
                    }
                    else
                    {
                        _logger.Debug(
                            "Skipping metric recording for default-eval (reason={Reason})",
                            evalMetrics.DefaultReason);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Background RAG evaluation failed");
                }
            }, CancellationToken.None);
        }

        return ragResponse;
    }

    /// <summary>
    /// Parses the configured search-mode string into the <see cref="SearchMode"/> enum.
    /// Falls back to Hybrid on unrecognized values (the safer default — gives both
    /// semantic and keyword backends a chance to contribute).
    /// </summary>
    private SearchMode ResolveSearchMode(string configured)
    {
        if (Enum.TryParse<SearchMode>(configured, ignoreCase: true, out var parsed))
            return parsed;

        _logger.Warning("Unknown DefaultSearchMode '{Mode}'; falling back to Hybrid", configured);
        return SearchMode.Hybrid;
    }

    /// <summary>Maps a <see cref="SearchMode"/> to the metrics-side <see cref="SearchType"/>.</summary>
    private static SearchType MapSearchType(SearchMode mode) => mode switch
    {
        SearchMode.Semantic => SearchType.Semantic,
        SearchMode.Keyword => SearchType.Keyword,
        SearchMode.Hybrid => SearchType.Hybrid,
        _ => SearchType.Hybrid
    };

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

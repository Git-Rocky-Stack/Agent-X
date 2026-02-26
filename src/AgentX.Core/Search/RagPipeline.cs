using System.Diagnostics;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Search.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Orchestrates the full Retrieval-Augmented Generation pipeline:
///   1. Performs semantic search to retrieve relevant document chunks
///   2. Builds a grounded prompt with numbered context sections
///   3. Streams the AI response token-by-token
///   4. Extracts and resolves citations from the completed response
///   5. Returns a <see cref="RagResponse"/> with answer text, citations, and latency metrics
/// </summary>
public sealed class RagPipeline : IRagPipeline
{
    /// <summary>Default number of top-K chunks to retrieve from semantic search.</summary>
    private const int DefaultTopK = 8;

    /// <summary>Minimum similarity threshold for including a chunk as context.</summary>
    private const float DefaultMinScore = 0.25f;

    /// <summary>
    /// The system prompt template used to instruct the AI to answer from context only.
    /// Includes placeholders: the context sections are appended dynamically.
    /// </summary>
    private const string RagSystemPromptPrefix =
        """
        You are a helpful AI assistant answering questions based on the user's personal document library.
        Answer the following question using ONLY the provided context documents.
        Cite your sources using [1], [2], etc. corresponding to the numbered context sections.
        If the context doesn't contain enough information to fully answer the question, say so honestly.
        Be concise but thorough.
        """;

    /// <summary>Response returned when no relevant documents are found for the query.</summary>
    private const string NoResultsMessage =
        "I couldn't find any relevant information in your documents. " +
        "Try rephrasing your question or ensure that relevant documents have been indexed.";

    private readonly ISemanticSearchService _searchService;
    private readonly IAiService _aiService;
    private readonly ICitationService _citationService;
    private readonly AgentXDbContext _dbContext;
    private readonly ILogger _logger;

    public RagPipeline(
        ISemanticSearchService searchService,
        IAiService aiService,
        ICitationService citationService,
        AgentXDbContext dbContext,
        ILogger logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _citationService = citationService ?? throw new ArgumentNullException(nameof(citationService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger?.ForContext<RagPipeline>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RagResponse> AskAsync(
        string question,
        long? collectionId = null,
        Action<string>? onToken = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be null or empty.", nameof(question));

        var totalStopwatch = Stopwatch.StartNew();

        _logger.Information("RAG pipeline started for question (length={Length}, collection={CollectionId})",
            question.Length, collectionId?.ToString() ?? "all");

        // ── Step 1: Semantic Search ──────────────────────────────────────
        var searchStopwatch = Stopwatch.StartNew();
        IReadOnlyList<SearchResult> searchResults;

        try
        {
            var searchQuery = new SearchQuery
            {
                QueryText = question,
                TopK = DefaultTopK,
                MinScore = DefaultMinScore,
                CollectionId = collectionId
            };
            searchResults = await _searchService
                .SearchAsync(searchQuery, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Information("RAG pipeline cancelled during semantic search");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Semantic search failed for RAG query");
            throw;
        }

        searchStopwatch.Stop();
        var searchLatencyMs = searchStopwatch.Elapsed.TotalMilliseconds;

        _logger.Debug("Semantic search returned {Count} results in {ElapsedMs:F1}ms",
            searchResults.Count, searchLatencyMs);

        // Filter results below the minimum similarity threshold
        var relevantResults = searchResults
            .Where(r => r.Score >= DefaultMinScore)
            .ToList();

        _logger.Debug("{RelevantCount} of {TotalCount} results passed the minimum score threshold of {MinScore}",
            relevantResults.Count, searchResults.Count, DefaultMinScore);

        // ── Step 2: Handle No Results ────────────────────────────────────
        if (relevantResults.Count == 0)
        {
            totalStopwatch.Stop();

            _logger.Information("No relevant context found for question; returning no-results response");

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

        // ── Step 3: Build Context Chunks ─────────────────────────────────
        var contextChunks = BuildContextChunks(relevantResults);

        // ── Step 4: Build RAG Prompt ─────────────────────────────────────
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

        // ── Step 5: Stream AI Response ───────────────────────────────────
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
                Temperature = 0.3,   // Lower temperature for factual, grounded answers
                MaxTokens = 2048,
                TopP = 0.9
            };

            await foreach (var token in _aiService
                .StreamChatAsync(messages, systemPrompt, chatOptions, ct)
                .ConfigureAwait(false))
            {
                responseBuilder.Append(token);

                // Invoke the caller's token callback for real-time UI streaming
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

        // ── Step 6: Extract Citations ────────────────────────────────────
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

        // ── Step 7: Finalize Response ────────────────────────────────────
        totalStopwatch.Stop();

        ragResponse.AnswerText = answerText;
        ragResponse.Citations = citations;
        ragResponse.IsStreaming = false;
        ragResponse.TotalLatencyMs = totalStopwatch.Elapsed.TotalMilliseconds;

        _logger.Information(
            "RAG pipeline completed: {CitationCount} citations, {ChunkCount} context chunks, " +
            "search={SearchMs:F0}ms, total={TotalMs:F0}ms",
            citations.Count, contextChunks.Count, searchLatencyMs, ragResponse.TotalLatencyMs);

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

    /// <summary>
    /// Converts semantic search results into <see cref="RagContextChunk"/> objects
    /// that carry all metadata needed for citation resolution.
    /// </summary>
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

    /// <summary>
    /// Builds the full system prompt including the RAG instruction prefix and
    /// all numbered context sections from the retrieved chunks.
    /// </summary>
    private static string BuildSystemPrompt(IReadOnlyList<RagContextChunk> contextChunks)
    {
        var builder = new StringBuilder(4096);

        builder.AppendLine(RagSystemPromptPrefix);
        builder.AppendLine();
        builder.AppendLine("CONTEXT:");

        for (var i = 0; i < contextChunks.Count; i++)
        {
            var chunk = contextChunks[i];
            var citationNumber = i + 1; // 1-based citation numbering

            // Build the source label: include page number if available, otherwise chunk index
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

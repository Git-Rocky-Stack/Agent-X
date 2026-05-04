using AgentX.Core.Observability;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Search;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Orchestrates search across multiple backends (semantic, keyword, or both)
/// based on the <see cref="SearchMode"/> specified in the query.
/// </summary>
public interface IHybridSearchOrchestrator
{
    /// <summary>
    /// Executes a search using the mode specified in the query:
    /// <list type="bullet">
    ///   <item><see cref="SearchMode.Semantic"/>: delegates to vector similarity search.</item>
    ///   <item><see cref="SearchMode.Keyword"/>: delegates to FTS5 keyword search.</item>
    ///   <item><see cref="SearchMode.Hybrid"/>: runs both in parallel and merges via Reciprocal Rank Fusion (RRF).</item>
    /// </list>
    /// </summary>
    /// <param name="query">The search query with mode, text, and optional filters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of search results, highest relevance first.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);
}

/// <summary>
/// Production implementation of <see cref="IHybridSearchOrchestrator"/>.
/// In Hybrid mode, results from semantic and keyword search are merged using
/// Reciprocal Rank Fusion (RRF), which produces a unified ranking that benefits
/// from both exact keyword matching and semantic understanding.
/// </summary>
public sealed class HybridSearchOrchestrator : IHybridSearchOrchestrator
{
    private readonly ISemanticSearchService _semanticSearch;
    private readonly IKeywordSearchService _keywordSearch;
    private readonly ISearchCacheService? _searchCacheService;
    private readonly IRagMetrics? _metrics;
    private readonly ILogger _logger;

    /// <summary>
    /// The RRF constant 'k'. A value of 60 is the standard default from the original
    /// Cormack, Clarke & Buettcher (2009) paper. It controls how much weight is given
    /// to high-ranking results vs. lower-ranking ones.
    /// </summary>
    private const int RrfK = 60;

    public HybridSearchOrchestrator(
        ISemanticSearchService semanticSearch,
        IKeywordSearchService keywordSearch,
        ILogger logger,
        ISearchCacheService? searchCacheService = null,
        IRagMetrics? metrics = null)
    {
        _semanticSearch = semanticSearch ?? throw new ArgumentNullException(nameof(semanticSearch));
        _keywordSearch = keywordSearch ?? throw new ArgumentNullException(nameof(keywordSearch));
        _logger = logger?.ForContext<HybridSearchOrchestrator>() ?? throw new ArgumentNullException(nameof(logger));
        _searchCacheService = searchCacheService;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Check cache first (when available)
        if (_searchCacheService is not null)
        {
            var cached = _searchCacheService.TryGetCached(query);
            if (cached is not null)
            {
                stopwatch.Stop();
                _logger.Debug("Cache hit for {Mode} search query: {Query}", query.Mode, TruncateForLog(query.QueryText));
                _metrics?.RecordSearch(MapSearchType(query.Mode), cached.Count, cacheHits: 1, cacheMisses: 0, stopwatch);
                return cached;
            }
        }

        var results = query.Mode switch
        {
            SearchMode.Semantic => await ExecuteSemanticSearchAsync(query, ct).ConfigureAwait(false),
            SearchMode.Keyword => await ExecuteKeywordSearchAsync(query, ct).ConfigureAwait(false),
            SearchMode.Hybrid => await ExecuteHybridSearchAsync(query, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(query), $"Unknown search mode: {query.Mode}")
        };

        // Cache the results (when available)
        if (_searchCacheService is not null && results.Count > 0)
        {
            _searchCacheService.Cache(query, results);
            _logger.Debug("Cached {Count} results for {Mode} search query: {Query}",
                results.Count, query.Mode, TruncateForLog(query.QueryText));
        }

        stopwatch.Stop();
        _metrics?.RecordSearch(MapSearchType(query.Mode), results.Count,
            cacheHits: 0, cacheMisses: _searchCacheService is not null ? 1 : 0, stopwatch);

        return results;
    }

    private static SearchType MapSearchType(SearchMode mode) => mode switch
    {
        SearchMode.Semantic => SearchType.Semantic,
        SearchMode.Keyword => SearchType.Keyword,
        SearchMode.Hybrid => SearchType.Hybrid,
        _ => SearchType.Hybrid
    };

    // ═══════════════════════════════════════════════════════════════════
    //  Mode-specific execution
    // ═══════════════════════════════════════════════════════════════════

    private async Task<IReadOnlyList<SearchResult>> ExecuteSemanticSearchAsync(SearchQuery query, CancellationToken ct)
    {
        _logger.Debug("Delegating to semantic search for query: {Query}", TruncateForLog(query.QueryText));
        return await _semanticSearch.SearchAsync(query, ct);
    }

    private async Task<IReadOnlyList<SearchResult>> ExecuteKeywordSearchAsync(SearchQuery query, CancellationToken ct)
    {
        _logger.Debug("Delegating to keyword search for query: {Query}", TruncateForLog(query.QueryText));
        return await _keywordSearch.SearchAsync(query, ct);
    }

    private async Task<IReadOnlyList<SearchResult>> ExecuteHybridSearchAsync(SearchQuery query, CancellationToken ct)
    {
        _logger.Information("Executing hybrid search (semantic + keyword) for query: {Query}", TruncateForLog(query.QueryText));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Run both searches in parallel for maximum throughput.
        // Request extra results from each backend so RRF has a larger candidate pool.
        var expandedQuery = new SearchQuery
        {
            QueryText = query.QueryText,
            TopK = Math.Min(query.TopK * 3, 500),
            MinScore = 0.0f, // RRF handles scoring; don't pre-filter aggressively
            CollectionId = query.CollectionId,
            FileTypeFilter = query.FileTypeFilter,
            CreatedAfter = query.CreatedAfter,
            CreatedBefore = query.CreatedBefore,
            Mode = SearchMode.Semantic // Mode is irrelevant for direct calls but keeps the model consistent
        };

        // Launch both searches in parallel
        var semanticTask = _semanticSearch.SearchAsync(expandedQuery, ct);
        var keywordTask = _keywordSearch.SearchAsync(expandedQuery, ct);

        // Await both, handling partial failures gracefully
        IReadOnlyList<SearchResult> semanticHits;
        IReadOnlyList<SearchResult> keywordHits;

        try
        {
            await Task.WhenAll(semanticTask, keywordTask);
            semanticHits = semanticTask.Result;
            keywordHits = keywordTask.Result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "One or both search backends failed during hybrid search");

            // Graceful degradation: recover results from whichever backend succeeded
            semanticHits = semanticTask.Status == TaskStatus.RanToCompletion
                ? semanticTask.Result
                : Array.Empty<SearchResult>();

            keywordHits = keywordTask.Status == TaskStatus.RanToCompletion
                ? keywordTask.Result
                : Array.Empty<SearchResult>();

            // If both failed, return empty
            if (semanticHits.Count == 0 && keywordHits.Count == 0)
            {
                return Array.Empty<SearchResult>();
            }

            // If only one succeeded, return its results directly
            if (semanticHits.Count > 0 && keywordHits.Count == 0)
            {
                return semanticHits.Take(query.TopK).ToList();
            }

            if (keywordHits.Count > 0 && semanticHits.Count == 0)
            {
                return keywordHits.Take(query.TopK).ToList();
            }
        }

        _logger.Debug("Hybrid search backends returned: semantic={SemanticCount}, keyword={KeywordCount}",
            semanticHits.Count, keywordHits.Count);

        // Merge results using Reciprocal Rank Fusion
        var merged = MergeWithRrf(semanticHits, keywordHits, query.TopK);

        stopwatch.Stop();

        _logger.Information(
            "Hybrid search completed: {ResultCount} results returned in {ElapsedMs}ms " +
            "(semantic={SemanticCount}, keyword={KeywordCount})",
            merged.Count, stopwatch.ElapsedMilliseconds, semanticHits.Count, keywordHits.Count);

        return merged;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Reciprocal Rank Fusion (RRF)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Merges two ranked result lists using Reciprocal Rank Fusion.
    ///
    /// For each result, the RRF score is computed as:
    ///   score = sum( 1 / (k + rank_i) ) across all result sets where the item appears.
    ///
    /// Results are deduplicated by ChunkId, then sorted by combined RRF score descending.
    /// </summary>
    private static IReadOnlyList<SearchResult> MergeWithRrf(
        IReadOnlyList<SearchResult> semanticResults,
        IReadOnlyList<SearchResult> keywordResults,
        int topK)
    {
        // Dictionary to accumulate RRF scores keyed by ChunkId.
        // Also stores the best SearchResult object for each chunk (preferring semantic for richer metadata).
        var rrfScores = new Dictionary<long, (double Score, SearchResult Result)>();

        // Process semantic results
        for (int rank = 0; rank < semanticResults.Count; rank++)
        {
            var result = semanticResults[rank];
            double rrfContribution = 1.0 / (RrfK + rank + 1); // rank is 0-based, RRF uses 1-based

            if (rrfScores.TryGetValue(result.ChunkId, out var existing))
            {
                rrfScores[result.ChunkId] = (existing.Score + rrfContribution, existing.Result);
            }
            else
            {
                rrfScores[result.ChunkId] = (rrfContribution, result);
            }
        }

        // Process keyword results
        for (int rank = 0; rank < keywordResults.Count; rank++)
        {
            var result = keywordResults[rank];
            double rrfContribution = 1.0 / (RrfK + rank + 1);

            if (rrfScores.TryGetValue(result.ChunkId, out var existing))
            {
                rrfScores[result.ChunkId] = (existing.Score + rrfContribution, existing.Result);
            }
            else
            {
                rrfScores[result.ChunkId] = (rrfContribution, result);
            }
        }

        // Sort by combined RRF score descending and take top K.
        // Normalize the RRF score to a 0-1 range for display consistency.
        // Maximum possible RRF score = 2 / (k + 1) when an item is ranked #1 in both lists.
        double maxPossibleRrf = 2.0 / (RrfK + 1);

        var merged = rrfScores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => new SearchResult
            {
                ChunkId = x.Result.ChunkId,
                DocumentId = x.Result.DocumentId,
                FileName = x.Result.FileName,
                FilePath = x.Result.FilePath,
                FileType = x.Result.FileType,
                PageNumber = x.Result.PageNumber,
                ChunkIndex = x.Result.ChunkIndex,
                MatchedText = x.Result.MatchedText,
                Excerpt = x.Result.Excerpt,
                Score = (float)Math.Clamp(x.Score / maxPossibleRrf, 0.0, 1.0),
                CollectionNames = x.Result.CollectionNames
            })
            .ToList();

        return merged;
    }

    /// <summary>
    /// Truncates a string for safe inclusion in log messages.
    /// </summary>
    private static string TruncateForLog(string text, int maxLength = 80)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

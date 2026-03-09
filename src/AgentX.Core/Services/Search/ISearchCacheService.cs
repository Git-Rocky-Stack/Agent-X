using AgentX.Core.Search.Models;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Provides an in-memory LRU (Least Recently Used) caching layer for search results.
/// Caching avoids redundant embedding generation and vector-store round-trips for
/// repeated or similar queries, significantly reducing latency and resource consumption.
/// </summary>
public interface ISearchCacheService
{
    /// <summary>
    /// Attempts to retrieve cached search results for the given query.
    /// Returns <c>null</c> if the query is not cached or if the cached entry has expired.
    /// </summary>
    /// <param name="query">The search query to look up in the cache.</param>
    /// <returns>
    /// The cached list of <see cref="SearchResult"/> instances if a valid cache entry exists;
    /// otherwise <c>null</c>.
    /// </returns>
    IReadOnlyList<SearchResult>? TryGetCached(SearchQuery query);

    /// <summary>
    /// Stores search results in the cache, keyed by the given query.
    /// If the cache is at capacity, the least recently used entry is evicted.
    /// </summary>
    /// <param name="query">The search query that produced the results.</param>
    /// <param name="results">The search results to cache.</param>
    void Cache(SearchQuery query, IReadOnlyList<SearchResult> results);

    /// <summary>
    /// Invalidates all cached entries. This should be called when a bulk operation
    /// (such as re-indexing the entire corpus) renders all cached results potentially stale.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Invalidates all cached entries whose results reference the specified document.
    /// This should be called when a document is updated, re-indexed, or deleted
    /// to ensure stale results are not served.
    /// </summary>
    /// <param name="documentId">The ID of the document whose cached entries should be evicted.</param>
    void InvalidateForDocument(long documentId);

    /// <summary>
    /// Returns a snapshot of the current cache statistics, including entry count,
    /// hit/miss counts, and the computed hit rate.
    /// </summary>
    /// <returns>A <see cref="CacheStatistics"/> record with the current metrics.</returns>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Represents a point-in-time snapshot of search cache performance metrics.
/// </summary>
/// <param name="EntryCount">The number of entries currently stored in the cache.</param>
/// <param name="HitCount">The total number of cache hits since the service was created or last reset.</param>
/// <param name="MissCount">The total number of cache misses since the service was created or last reset.</param>
/// <param name="HitRate">
/// The cache hit rate as a value between 0.0 and 1.0. Returns 0.0 if no lookups have been performed.
/// </param>
public sealed record CacheStatistics(int EntryCount, int HitCount, int MissCount, double HitRate);

using AgentX.Core.Search.Models;

namespace AgentX.Core.Search;

/// <summary>
/// Service for performing semantic (vector-based) search across indexed document chunks.
/// Combines embedding generation, vector similarity search, and result enrichment
/// with document metadata.
/// </summary>
public interface ISemanticSearchService
{
    /// <summary>
    /// Performs a semantic search using the given query.
    /// The query text is embedded, matched against the vector store,
    /// and results are enriched with document metadata.
    /// </summary>
    /// <param name="query">The search query with optional filters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of search results, highest relevance first.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);

    /// <summary>
    /// Saves a search query to the search history for later re-use.
    /// </summary>
    Task SaveSearchHistoryAsync(string queryText, int resultCount);

    /// <summary>
    /// Retrieves recent search history entries.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20);

    /// <summary>
    /// Clears all search history.
    /// </summary>
    Task ClearSearchHistoryAsync();
}

/// <summary>
/// Represents a saved search history entry.
/// </summary>
public class SearchHistoryEntry
{
    public long Id { get; init; }
    public string QueryText { get; init; } = string.Empty;
    public int ResultCount { get; init; }
    public DateTime SearchedAt { get; init; }
}

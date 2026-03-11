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
    /// Accepts optional advanced filter settings for full filter persistence.
    /// </summary>
    Task SaveSearchHistoryAsync(string queryText, int resultCount,
        double? minScore = null, int? maxResults = null,
        DateTime? dateAfter = null, DateTime? dateBefore = null,
        string? sortOrder = null);

    /// <summary>
    /// Retrieves recent search history entries.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20);

    /// <summary>
    /// Clears all search history.
    /// </summary>
    Task ClearSearchHistoryAsync();

    /// <summary>
    /// Marks a search history entry as a saved filter for quick re-use.
    /// </summary>
    Task SaveSearchFilterAsync(long historyId);

    /// <summary>
    /// Removes the saved-filter flag from a search history entry.
    /// </summary>
    Task UnsaveSearchFilterAsync(long historyId);

    /// <summary>
    /// Retrieves all search history entries that have been saved as filters.
    /// </summary>
    Task<IReadOnlyList<SearchHistoryEntry>> GetSavedFiltersAsync();
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
    public bool IsSaved { get; init; }
    public string SearchType { get; init; } = "semantic";
    public string? CollectionFilter { get; init; }

    // ── Advanced filter settings ─────────────────────────────────
    public double? MinScore { get; init; }
    public int? MaxResults { get; init; }
    public DateTime? DateAfter { get; init; }
    public DateTime? DateBefore { get; init; }
    public string? SortOrder { get; init; }
}

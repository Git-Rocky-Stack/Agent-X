namespace AgentX.Core.Search;

/// <summary>
/// Service for full-text keyword search powered by SQLite FTS5.
/// Provides BM25-ranked keyword matching as a complement to semantic (vector) search.
/// </summary>
public interface IKeywordSearchService
{
    /// <summary>
    /// Creates the FTS5 virtual table if it does not already exist.
    /// Must be called once during application startup after the database is initialized.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeFtsAsync(CancellationToken ct = default);

    /// <summary>
    /// Indexes all chunks belonging to the specified document into the FTS5 table.
    /// Typically called after a document has been successfully chunked and embedded.
    /// </summary>
    /// <param name="documentId">The document whose chunks should be indexed.</param>
    /// <param name="ct">Cancellation token.</param>
    Task IndexDocumentChunksAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Removes all FTS5 entries for the specified document.
    /// Should be called before re-indexing or when a document is deleted.
    /// </summary>
    /// <param name="documentId">The document to remove from the FTS index.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveDocumentFromFtsAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Performs a full-text keyword search using FTS5 MATCH with BM25 ranking.
    /// Stop words are ignored and any remaining query term may match. Collection, file-type
    /// and date filters are applied inside the query. Results are returned as
    /// <see cref="Models.SearchResult"/> objects scored 0-1 relative to the best match, and
    /// <see cref="Models.SearchQuery.MinScore"/> is applied on that relative scale.
    /// </summary>
    /// <param name="query">The search query with text and optional filters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of search results, highest relevance first.</returns>
    Task<IReadOnlyList<Models.SearchResult>> SearchAsync(Models.SearchQuery query, CancellationToken ct = default);
}

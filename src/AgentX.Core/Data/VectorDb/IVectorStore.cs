namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// Abstraction over the vector database used for semantic embedding storage and retrieval.
/// Implementations may use SQLite with custom distance functions, FAISS, or other backends.
/// </summary>
public interface IVectorStore : IAsyncDisposable
{
    /// <summary>
    /// Initializes the vector store (creates tables, loads indexes, etc.).
    /// Must be called before any other operations.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts a single embedding vector associated with a document chunk.
    /// </summary>
    /// <param name="chunkId">The ID of the document chunk this embedding represents.</param>
    /// <param name="embedding">The embedding vector (float array).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The row ID of the inserted embedding record.</returns>
    Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default);

    /// <summary>
    /// Searches for the nearest neighbors to the given query embedding.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="minSimilarity">Minimum cosine similarity threshold (0.0 to 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of search results ranked by similarity (highest first).</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, double minSimilarity = 0.3, CancellationToken ct = default);

    /// <summary>
    /// Deletes the embedding associated with a specific chunk.
    /// </summary>
    Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all embeddings associated with a document's chunks.
    /// </summary>
    /// <param name="documentId">The parent document ID (for logging/auditing).</param>
    /// <param name="chunkIds">The chunk IDs whose embeddings should be removed.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteEmbeddingsForDocumentAsync(long documentId, IReadOnlyList<long> chunkIds, CancellationToken ct = default);

    /// <summary>
    /// Returns the total number of embedding vectors currently stored.
    /// </summary>
    Task<long> GetEmbeddingCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Optimizes the vector index for faster search (e.g., rebuild HNSW, vacuum, etc.).
    /// May be a no-op for some implementations.
    /// </summary>
    Task OptimizeAsync(CancellationToken ct = default);

    /// <summary>
    /// Waits for the operations already running to finish, then closes the store's connection to
    /// the database file, so the file can be replaced (restore) or re-encrypted: on Windows any
    /// open handle makes that fail. Operations called while suspended wait until the matching
    /// <see cref="ResumeAsync"/>. Suspensions nest: the store reopens when the last one ends.
    /// When <paramref name="ct"/> is cancelled while waiting, the store stays in service and the
    /// suspension does not count.
    /// </summary>
    Task SuspendAsync(CancellationToken ct = default);

    /// <summary>
    /// Ends one suspension. The last one reopens the connection with the database key that is
    /// current now and lets waiting operations continue, also when reopening fails (they then
    /// fail instead of waiting). With <paramref name="reloadFromDatabase"/> the store first drops
    /// what it derived from the previous file (an in-memory index, index files on disk) and loads
    /// again from the database, as after a restore. A store that was never initialized stays
    /// uninitialized. Does nothing when the store is not suspended.
    /// </summary>
    Task ResumeAsync(bool reloadFromDatabase, CancellationToken ct = default);
}

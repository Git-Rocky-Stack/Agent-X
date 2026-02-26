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
}

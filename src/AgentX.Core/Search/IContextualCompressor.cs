namespace AgentX.Core.Search;

/// <summary>
/// Extracts only the portions of retrieved chunks that are directly relevant
/// to the user's query, reducing noise and improving RAG answer quality.
/// </summary>
public interface IContextualCompressor
{
    /// <summary>
    /// Compresses context chunks by extracting only relevant portions for the query.
    /// </summary>
    /// <param name="chunks">The retrieved context chunks.</param>
    /// <param name="query">The user's question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Compressed chunks with only relevant text retained.</returns>
    Task<List<RagContextChunk>> CompressAsync(
        List<RagContextChunk> chunks,
        string query,
        CancellationToken ct = default);
}

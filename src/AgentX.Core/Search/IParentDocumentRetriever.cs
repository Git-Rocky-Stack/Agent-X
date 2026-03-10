namespace AgentX.Core.Search;

/// <summary>
/// Retrieves parent (larger) document chunks when child (smaller) chunks are matched.
/// Small chunks give precise retrieval matching, but returning the larger parent
/// provides the LLM with better surrounding context for answer generation.
/// </summary>
public interface IParentDocumentRetriever
{
    /// <summary>
    /// Given matched child chunks, retrieves their parent chunks for richer context.
    /// If a chunk has no parent (i.e., it was not split into smaller children),
    /// the original chunk is returned unchanged.
    /// </summary>
    /// <param name="childChunks">The small, precisely-matched chunks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Parent chunks with full surrounding context.</returns>
    Task<List<RagContextChunk>> RetrieveParentChunksAsync(
        List<RagContextChunk> childChunks,
        CancellationToken ct = default);
}

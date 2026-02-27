namespace AgentX.Core.Search;

/// <summary>
/// Reranks and deduplicates RAG context chunks to improve answer quality.
/// Removes near-duplicate chunks, boosts diversity across documents,
/// and ensures the most relevant context is prioritized.
/// </summary>
public interface IRagReranker
{
    /// <summary>
    /// Reranks and deduplicates context chunks for optimal RAG quality.
    /// </summary>
    /// <param name="chunks">Raw chunks from vector search, ordered by similarity score.</param>
    /// <param name="query">The original user query.</param>
    /// <param name="maxChunks">Maximum number of chunks to return after reranking.</param>
    /// <returns>Reranked, deduplicated chunks.</returns>
    List<RagContextChunk> Rerank(List<RagContextChunk> chunks, string query, int maxChunks = 8);
}

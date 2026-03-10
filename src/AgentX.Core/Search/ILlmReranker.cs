namespace AgentX.Core.Search;

/// <summary>
/// LLM-based reranker that uses the local model to score the relevance
/// of each context chunk to the user's query. Provides more accurate
/// relevance scoring than heuristic-based approaches.
/// </summary>
public interface ILlmReranker
{
    /// <summary>
    /// Reranks chunks using LLM-based relevance scoring.
    /// </summary>
    /// <param name="chunks">Chunks to rerank (pre-filtered by the heuristic reranker).</param>
    /// <param name="query">The user's question.</param>
    /// <param name="maxChunks">Maximum chunks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Reranked chunks ordered by LLM-assessed relevance.</returns>
    Task<List<RagContextChunk>> RerankAsync(
        List<RagContextChunk> chunks,
        string query,
        int maxChunks = 8,
        CancellationToken ct = default);
}

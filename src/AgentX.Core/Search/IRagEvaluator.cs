namespace AgentX.Core.Search;

/// <summary>
/// Evaluates RAG pipeline quality by measuring retrieval precision,
/// answer faithfulness, and context relevance using the local LLM as judge.
/// </summary>
public interface IRagEvaluator
{
    /// <summary>
    /// Evaluates the quality of a RAG response.
    /// </summary>
    /// <param name="question">The original user question.</param>
    /// <param name="answer">The generated answer text.</param>
    /// <param name="contextChunks">The retrieved context chunks used.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Evaluation metrics scored 0.0 to 1.0.</returns>
    Task<RagEvalMetrics> EvaluateAsync(
        string question,
        string answer,
        IReadOnlyList<RagContextChunk> contextChunks,
        CancellationToken ct = default);
}

/// <summary>
/// Quality metrics for a RAG pipeline response.
/// All scores are normalized to 0.0 (worst) to 1.0 (best).
/// </summary>
public class RagEvalMetrics
{
    /// <summary>How relevant the retrieved context is to the question (0-1).</summary>
    public double ContextRelevance { get; set; }

    /// <summary>How well the answer is grounded in the provided context (0-1).</summary>
    public double Faithfulness { get; set; }

    /// <summary>How well the answer addresses the question (0-1).</summary>
    public double AnswerRelevance { get; set; }

    /// <summary>Overall quality score (weighted average of all metrics).</summary>
    public double OverallScore => ContextRelevance * 0.3 + Faithfulness * 0.4 + AnswerRelevance * 0.3;
}

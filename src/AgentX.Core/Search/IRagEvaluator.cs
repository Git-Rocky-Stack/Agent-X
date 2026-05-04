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

    /// <summary>
    /// True when these metrics are placeholder defaults (0.5 / 0.5 / 0.5) emitted
    /// because the evaluator's LLM call failed or its JSON output could not be parsed.
    /// Distinguishes "the model judged this 0.5" from "we have no signal."
    /// Aggregators / dashboards should EXCLUDE entries where this is true.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Why these metrics are defaults (when <see cref="IsDefault"/> is true).
    /// One of: "InputValidation", "JsonParseFailure", "LlmCallFailure", "Cancelled".
    /// Empty when the metrics are real LLM scores.
    /// </summary>
    public string DefaultReason { get; set; } = string.Empty;
}

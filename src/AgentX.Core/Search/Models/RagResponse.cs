using AgentX.Core.Services.Search;

namespace AgentX.Core.Search.Models;

/// <summary>
/// Represents the complete response from a RAG (Retrieval-Augmented Generation) query.
/// Contains the AI-generated answer text along with all citations to source documents.
/// </summary>
public class RagResponse
{
    /// <summary>The AI-generated answer text (may contain [N] citation references).</summary>
    public string AnswerText { get; set; } = string.Empty;

    /// <summary>The original user question.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>
    /// All citations referenced in the answer, keyed by their citation number.
    /// </summary>
    public List<Citation> Citations { get; set; } = new();

    /// <summary>Number of context chunks that were provided to the AI.</summary>
    public int ContextChunksUsed { get; init; }

    /// <summary>Whether the response is still being streamed.</summary>
    public bool IsStreaming { get; set; }

    /// <summary>Total time taken for the search + generation in milliseconds.</summary>
    public double TotalLatencyMs { get; set; }

    /// <summary>Time taken for the semantic search portion in milliseconds.</summary>
    public double SearchLatencyMs { get; set; }

    /// <summary>The collection scope used for the query (null = all collections).</summary>
    public long? CollectionScope { get; init; }

    /// <summary>
    /// RAG quality evaluation metrics (populated asynchronously after response generation).
    /// Null if the evaluator is not available or has not yet completed.
    /// </summary>
    public RagEvalMetrics? EvalMetrics { get; set; }

    /// <summary>
    /// Citations from external web sources, populated when Deep Research Mode is enabled.
    /// Null when research mode is off or no web results were found.
    /// </summary>
    public IReadOnlyList<WebCitation>? WebCitations { get; set; }
}

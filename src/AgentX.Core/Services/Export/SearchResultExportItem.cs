namespace AgentX.Core.Services.Export;

/// <summary>
/// Represents a single search result item to be included in an export.
/// Captures the content, source document, relevance, and any citations
/// that were generated during a RAG search operation.
/// </summary>
public class SearchResultExportItem
{
    /// <summary>
    /// The original search query that produced this result.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// The AI-generated or retrieved content for this result.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The name of the source document this result was derived from.
    /// </summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>
    /// The relevance score of this result relative to the query (0.0 to 1.0).
    /// </summary>
    public float RelevanceScore { get; init; }

    /// <summary>
    /// Optional list of citation strings (e.g., "Document.pdf, page 3") associated
    /// with this result.
    /// </summary>
    public IReadOnlyList<string> Citations { get; init; } = Array.Empty<string>();
}

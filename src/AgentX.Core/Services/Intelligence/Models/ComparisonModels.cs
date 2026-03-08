namespace AgentX.Core.Services.Intelligence.Models;

/// <summary>
/// Configuration options that govern how a comparative analysis is performed.
/// All properties have sensible defaults so callers only need to provide overrides.
/// </summary>
public class ComparisonOptions
{
    /// <summary>
    /// Maximum number of semantic-search chunks to retrieve per document for the
    /// AI context window. A higher value provides more coverage but consumes more
    /// tokens. Defaults to 5.
    /// </summary>
    public int MaxChunksPerDoc { get; init; } = 5;

    /// <summary>
    /// An optional topic or question to focus the comparison on. When provided,
    /// the semantic search for each document is biased toward this topic and the
    /// AI prompt instructs the model to emphasise it. When null the comparison
    /// covers the full breadth of each document.
    /// </summary>
    public string? FocusQuery { get; init; }

    /// <summary>
    /// Controls how much detail the AI produces in the report.
    /// Accepted values: <c>"detailed"</c> (default) or <c>"summary"</c>.
    /// Any other value is treated as <c>"detailed"</c>.
    /// </summary>
    public string DetailLevel { get; init; } = "detailed";
}

/// <summary>
/// The structured output produced by <see cref="IComparisonService"/>.
/// Contains every dimension of the AI-generated cross-document analysis as
/// discrete, UI-ready collections rather than a single blob of prose.
/// </summary>
public class ComparisonReport
{
    /// <summary>
    /// Display names (file names) of each document that was analysed,
    /// in the same order as the document IDs supplied to the service.
    /// </summary>
    public List<string> DocumentNames { get; init; } = new();

    /// <summary>
    /// Themes, claims, or facts that appear consistently across all compared documents.
    /// Each entry is a self-contained statement suitable for bullet-point display.
    /// </summary>
    public List<string> Similarities { get; init; } = new();

    /// <summary>
    /// Notable ways in which the documents diverge in scope, perspective, methodology,
    /// or conclusions. Each entry is a self-contained statement.
    /// </summary>
    public List<string> Differences { get; init; } = new();

    /// <summary>
    /// Direct contradictions where two or more documents make mutually exclusive
    /// claims about the same topic. Each entry names the conflicting positions
    /// so the user can evaluate them.
    /// </summary>
    public List<string> Contradictions { get; init; } = new();

    /// <summary>
    /// Information that exists exclusively in one document and is not covered by
    /// any of the others. Keyed by document name; each value is the list of unique
    /// points found only in that document.
    /// </summary>
    public Dictionary<string, List<string>> UniquePoints { get; init; } = new();

    /// <summary>
    /// A concise executive-level narrative (2-4 sentences) synthesising the overall
    /// comparison findings for display in a summary header.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp at which the analysis was generated.
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Approximate total number of tokens consumed by the AI request (prompt +
    /// completion). Populated from the character count of the assembled prompt and
    /// response using a 4-chars-per-token heuristic when the provider does not
    /// return an exact count.
    /// </summary>
    public long TotalTokensUsed { get; init; }

    /// <summary>
    /// Wall-clock duration of the entire comparison operation in milliseconds,
    /// including document retrieval, semantic search, AI inference, and parsing.
    /// </summary>
    public double DurationMs { get; init; }
}

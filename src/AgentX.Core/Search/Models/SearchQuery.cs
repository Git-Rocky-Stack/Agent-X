namespace AgentX.Core.Search.Models;

/// <summary>
/// Represents a semantic search query with optional filters.
/// </summary>
public class SearchQuery
{
    /// <summary>The natural language query text.</summary>
    public required string QueryText { get; init; }

    /// <summary>Maximum number of results to return.</summary>
    public int TopK { get; init; } = 10;

    /// <summary>Minimum similarity score (0.0 to 1.0) to include in results.</summary>
    public float MinScore { get; init; } = 0.3f;

    /// <summary>Optional collection ID to scope the search.</summary>
    public long? CollectionId { get; init; }

    /// <summary>Optional file type filter (e.g., "pdf", "docx").</summary>
    public string? FileTypeFilter { get; init; }

    /// <summary>Optional date range: only include documents created after this date.</summary>
    public DateTime? CreatedAfter { get; init; }

    /// <summary>Optional date range: only include documents created before this date.</summary>
    public DateTime? CreatedBefore { get; init; }
}

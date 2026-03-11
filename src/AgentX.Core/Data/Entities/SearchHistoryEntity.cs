namespace AgentX.Core.Data.Entities;

public class SearchHistoryEntity
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = "semantic"; // "semantic", "keyword", "rag"
    public int ResultCount { get; set; }
    public DateTime SearchedAt { get; set; }
    public bool IsSaved { get; set; }
    public string? CollectionFilter { get; set; } // comma-separated collection IDs

    // ── Advanced filter settings ─────────────────────────────────
    public double? MinScore { get; set; }
    public int? MaxResults { get; set; }
    public DateTime? DateAfter { get; set; }
    public DateTime? DateBefore { get; set; }
    public string? SortOrder { get; set; }
}

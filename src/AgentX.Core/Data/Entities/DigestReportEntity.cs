namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a weekly digest report summarizing knowledge vault activity.
/// Contains aggregate statistics and JSON-serialized detail data for the report period.
/// </summary>
public class DigestReportEntity
{
    public long Id { get; set; }

    /// <summary>
    /// Timestamp when the report was generated.
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Start of the reporting period (inclusive).
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// End of the reporting period (inclusive).
    /// </summary>
    public DateTime PeriodEnd { get; set; }

    // ── Summary Statistics ──────────────────────────────────────
    public int NewDocumentsCount { get; set; }
    public int NewConversationsCount { get; set; }
    public int TotalSearches { get; set; }
    public int TotalTokensUsed { get; set; }
    public long StorageDeltaBytes { get; set; }

    // ── JSON Detail Fields ──────────────────────────────────────
    /// <summary>JSON array of top search queries with counts.</summary>
    public string? TopSearchesJson { get; set; }

    /// <summary>JSON array of top collections with document counts.</summary>
    public string? TopCollectionsJson { get; set; }

    /// <summary>JSON array of file type distribution for the period.</summary>
    public string? FileTypeBreakdownJson { get; set; }

    /// <summary>JSON array of conversation highlights (most active).</summary>
    public string? HighlightsJson { get; set; }

    /// <summary>
    /// Whether the user has viewed this report.
    /// </summary>
    public bool IsRead { get; set; }
}

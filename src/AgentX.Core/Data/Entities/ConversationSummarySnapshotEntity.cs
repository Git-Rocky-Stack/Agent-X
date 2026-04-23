namespace AgentX.Core.Data.Entities;

/// <summary>
/// Immutable summary snapshot captured for a conversation at a point in time.
/// New refreshes append a row instead of overwriting prior summaries.
/// </summary>
public class ConversationSummarySnapshotEntity
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public int SnapshotVersion { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public string PreviewText { get; set; } = string.Empty;
    public string KeyPointsJson { get; set; } = "[]";
    public int CoveredMessageCount { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime SourceConversationUpdatedAt { get; set; }
    public bool IsIncremental { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
}

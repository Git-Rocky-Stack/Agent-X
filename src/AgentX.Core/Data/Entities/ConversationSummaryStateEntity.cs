namespace AgentX.Core.Data.Entities;

/// <summary>
/// Mutable per-conversation state used to track whether a durable summary
/// needs refresh and which immutable snapshot is currently active.
/// </summary>
public class ConversationSummaryStateEntity
{
    public long ConversationId { get; set; }
    public long? LatestSnapshotId { get; set; }
    public int LatestSnapshotVersion { get; set; }
    public int LastCoveredMessageCount { get; set; }
    public int PendingMessageCount { get; set; }
    public bool IsStale { get; set; }
    public DateTime? LastRefreshRequestedAt { get; set; }
    public DateTime? LastRefreshAttemptedAt { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailureCount { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
    public ConversationSummarySnapshotEntity? LatestSnapshot { get; set; }
}

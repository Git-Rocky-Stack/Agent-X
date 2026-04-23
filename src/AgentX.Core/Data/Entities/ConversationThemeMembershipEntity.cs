namespace AgentX.Core.Data.Entities;

/// <summary>
/// Current durable theme assignment for a conversation's latest summary snapshot.
/// </summary>
public class ConversationThemeMembershipEntity
{
    public long ConversationId { get; set; }
    public long SnapshotId { get; set; }
    public long ClusterId { get; set; }
    public float SimilarityScore { get; set; }
    public DateTime AssignedAt { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
    public ConversationSummarySnapshotEntity Snapshot { get; set; } = null!;
    public ConversationThemeClusterEntity Cluster { get; set; } = null!;
}

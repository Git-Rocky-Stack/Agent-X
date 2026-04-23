namespace AgentX.Core.Data.Entities;

/// <summary>
/// Durable daily activity materialization for a conversation theme cluster.
/// The first pass stores a bounded current-state-oriented window so Analytics
/// can surface momentum and recent activity without query-time recomputation.
/// </summary>
public class ConversationThemeDailyMetricEntity
{
    public long ClusterId { get; set; }
    public DateTime Date { get; set; }
    public int ActiveConversationCount { get; set; }
    public int NewConversationCount { get; set; }
    public int SnapshotRefreshCount { get; set; }
    public DateTime MaterializedAt { get; set; }

    public ConversationThemeClusterEntity Cluster { get; set; } = null!;
}

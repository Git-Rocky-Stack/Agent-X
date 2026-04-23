namespace AgentX.Core.Data.Entities;

/// <summary>
/// Materialized durable theme derived from the latest summary snapshots of
/// related conversations.
/// </summary>
public class ConversationThemeClusterEntity
{
    public long Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string PreviewText { get; set; } = string.Empty;
    public string KeyPointsJson { get; set; } = "[]";
    public int ConversationCount { get; set; }
    public int ActiveConversationCount7d { get; set; }
    public int ActiveConversationCount30d { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public DateTime MaterializedAt { get; set; }

    public ICollection<ConversationThemeMembershipEntity> Memberships { get; set; } = new List<ConversationThemeMembershipEntity>();
}

namespace AgentX.Core.Services.Chat.Models;

/// <summary>
/// A semantically recalled message match across persisted conversations.
/// </summary>
public sealed record ConversationRecallResult
{
    public long MessageId { get; init; }
    public long ConversationId { get; init; }
    public string ConversationTitle { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ContentPreview { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public int SortOrder { get; init; }
    public float Similarity { get; init; }
    public DateTime? EmbeddedAt { get; init; }
}

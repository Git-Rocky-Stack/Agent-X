namespace AgentX.Core.Data.Entities;

public class MessageEntity
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public string Role { get; set; } = string.Empty; // "user", "assistant", "system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int TokenCount { get; set; }
    public double? GenerationTimeMs { get; set; }
    public string? ModelId { get; set; }
    public string? CitationsJson { get; set; } // JSON array of Citation objects
    public int SortOrder { get; set; }

    // Navigation
    public ConversationEntity Conversation { get; set; } = null!;
}

namespace AgentX.Core.Data.Entities;

/// <summary>
/// Stores an extracted memory/fact from a conversation.
/// Memories persist across conversations and are injected into
/// system prompts to provide personalized context.
/// </summary>
public class MemoryEntity
{
    public long Id { get; set; }

    /// <summary>The extracted fact or preference (e.g., "User prefers Python over JavaScript")</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Category: preference, fact, topic, context, instruction</summary>
    public string Category { get; set; } = "fact";

    /// <summary>Source conversation ID where this was extracted</summary>
    public long? SourceConversationId { get; set; }

    /// <summary>Importance score (0.0-1.0). Higher = more likely to be included in context.</summary>
    public double Importance { get; set; } = 0.5;

    /// <summary>How many times this memory has been used in prompts</summary>
    public int UsageCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft delete - user can dismiss memories</summary>
    public bool IsActive { get; set; } = true;
}

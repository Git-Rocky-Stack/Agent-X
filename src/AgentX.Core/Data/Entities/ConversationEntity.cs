namespace AgentX.Core.Data.Entities;

public class ConversationEntity
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public int MessageCount { get; set; }
    public long TokensUsed { get; set; }

    // Navigation
    public ICollection<MessageEntity> Messages { get; set; } = new List<MessageEntity>();
}

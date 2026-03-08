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

    // Branching support
    /// <summary>
    /// The ID of the parent conversation this branch was forked from. Null for root conversations.
    /// </summary>
    public long? ParentConversationId { get; set; }

    /// <summary>
    /// The message ID in the parent conversation where this branch diverges.
    /// All messages up to and including this point were copied when the branch was created.
    /// </summary>
    public long? BranchPointMessageId { get; set; }

    /// <summary>
    /// An optional user-provided label describing the purpose of this branch.
    /// </summary>
    public string? BranchLabel { get; set; }

    // Navigation
    public ICollection<MessageEntity> Messages { get; set; } = new List<MessageEntity>();

    /// <summary>
    /// The parent conversation this branch was forked from. Null for root conversations.
    /// </summary>
    public ConversationEntity? ParentConversation { get; set; }

    /// <summary>
    /// Child branches forked from this conversation.
    /// </summary>
    public ICollection<ConversationEntity> Branches { get; set; } = new List<ConversationEntity>();
}

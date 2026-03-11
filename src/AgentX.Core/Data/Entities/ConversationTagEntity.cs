namespace AgentX.Core.Data.Entities;

public class ConversationTagEntity
{
    public long ConversationId { get; set; }
    public long TagId { get; set; }
    public DateTime AssignedAt { get; set; }

    // Navigation
    public ConversationEntity Conversation { get; set; } = null!;
    public TagEntity Tag { get; set; } = null!;
}

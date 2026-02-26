namespace AgentX.Core.Data.Entities;

public class DocumentCollectionEntity
{
    public long DocumentId { get; set; }
    public long CollectionId { get; set; }
    public DateTime AddedAt { get; set; }

    // Navigation
    public DocumentEntity Document { get; set; } = null!;
    public CollectionEntity Collection { get; set; } = null!;
}

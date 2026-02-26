namespace AgentX.Core.Data.Entities;

public class CollectionEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconGlyph { get; set; } // Segoe Fluent Icons glyph
    public string? ColorHex { get; set; }
    public long? ParentCollectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int DocumentCount { get; set; }
    public int SortOrder { get; set; }

    // Navigation
    public CollectionEntity? ParentCollection { get; set; }
    public ICollection<CollectionEntity> ChildCollections { get; set; } = new List<CollectionEntity>();
    public ICollection<DocumentCollectionEntity> DocumentCollections { get; set; } = new List<DocumentCollectionEntity>();
}

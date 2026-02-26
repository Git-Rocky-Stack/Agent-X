namespace AgentX.Core.Data.Entities;

public class DocumentChunkEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int StartCharOffset { get; set; }
    public int EndCharOffset { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public int TokenCount { get; set; }
    public bool IsEmbedded { get; set; }
    public long? VectorRowId { get; set; } // Foreign key to sqlite-vec virtual table

    // Navigation
    public DocumentEntity Document { get; set; } = null!;
}

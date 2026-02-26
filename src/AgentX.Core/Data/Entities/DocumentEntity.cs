namespace AgentX.Core.Data.Entities;

public class DocumentEntity
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty; // "pdf", "docx", "txt", etc.
    public string? MimeType { get; set; }
    public long FileSizeBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty; // SHA256 for duplicate detection
    public DateTime ImportedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public string IndexingStatus { get; set; } = "pending"; // pending, processing, completed, failed
    public string? IndexingError { get; set; }
    public int ChunkCount { get; set; }
    public int PageCount { get; set; }
    public long WordCount { get; set; }
    public string? Summary { get; set; } // AI-generated summary
    public string? ExtractedTitle { get; set; }
    public string? Language { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? MetadataJson { get; set; } // Additional metadata as JSON

    // Navigation
    public ICollection<DocumentChunkEntity> Chunks { get; set; } = new List<DocumentChunkEntity>();
    public ICollection<DocumentCollectionEntity> DocumentCollections { get; set; } = new List<DocumentCollectionEntity>();
    public ICollection<DocumentTagEntity> DocumentTags { get; set; } = new List<DocumentTagEntity>();
}

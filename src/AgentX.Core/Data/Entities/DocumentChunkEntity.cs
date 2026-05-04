namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a chunk of a document that has been processed and indexed.
/// Chunks are the atomic units of semantic search retrieval.
/// </summary>
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

    // ═══════════════════════════════════════════════════════════════════
    //  Embedding Model Versioning (Added: Phase 1)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// The embedding model version used to generate the embedding for this chunk.
    /// Format: "{ModelName}:{Version}" (e.g., "all-minilm:1.0", "nomic-embed-text:1.5").
    /// Null indicates legacy embedding from before versioning was introduced.
    /// </summary>
    public string? EmbeddingModelVersion { get; set; }

    /// <summary>
    /// The dimensionality of the embedding vector stored for this chunk.
    /// Used to validate compatibility before using embeddings in similarity search.
    /// Null for chunks without embeddings or legacy data.
    /// </summary>
    public int? EmbeddingDimensions { get; set; }

    /// <summary>
    /// Timestamp when the embedding was generated or last updated.
    /// Used for tracking model version changes and determining when re-embedding is needed.
    /// </summary>
    public DateTime? EmbeddedAt { get; set; }

    // Navigation
    public DocumentEntity Document { get; set; } = null!;
}

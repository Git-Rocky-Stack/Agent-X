namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a highlight or annotation placed on a section of a document.
/// Stores the character offsets of the highlighted text, an optional user note,
/// and a color label for visual categorisation.
/// </summary>
public class AnnotationEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>The document that contains this annotation.</summary>
    public long DocumentId { get; set; }

    /// <summary>
    /// Optional reference to the specific chunk where the highlight falls.
    /// Null when the annotation is positioned against the full document text
    /// rather than an indexed chunk.
    /// </summary>
    public long? ChunkId { get; set; }

    /// <summary>
    /// Zero-based character offset of the first character of the highlighted
    /// range within the chunk (or document) text.
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>
    /// Zero-based character offset of the character immediately after the last
    /// character of the highlighted range (exclusive end).
    /// </summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// The verbatim text that was highlighted by the user. Stored so the
    /// highlight can be displayed without re-fetching document content.
    /// </summary>
    public string HighlightedText { get; set; } = string.Empty;

    /// <summary>
    /// Optional free-text note or comment the user attached to the highlight.
    /// </summary>
    public string? NoteText { get; set; }

    /// <summary>
    /// Colour label for this annotation. One of: "yellow", "green", "blue",
    /// "red", "purple".
    /// </summary>
    public string Color { get; set; } = "yellow";

    /// <summary>Timestamp when the annotation was created (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Timestamp when the annotation was last modified (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    // Navigation
    /// <summary>The document that owns this annotation.</summary>
    public DocumentEntity Document { get; set; } = null!;
}

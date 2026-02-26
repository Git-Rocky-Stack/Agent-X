namespace AgentX.Core.Services.Intelligence.Models;

/// <summary>
/// Represents a group of documents that share identical or near-identical content.
/// Used by the duplicate detection service to report exact-hash or semantic duplicates.
/// </summary>
public class DuplicateGroup
{
    /// <summary>
    /// The content hash shared by all documents in this group (for exact duplicates),
    /// or the hash of the reference document (for near-duplicates).
    /// </summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>
    /// The documents that belong to this duplicate group.
    /// The first document is considered the "original" and subsequent entries are duplicates.
    /// </summary>
    public List<DuplicateDocument> Documents { get; init; } = new();

    /// <summary>
    /// The total storage consumed by duplicate copies (all documents except the first/original).
    /// </summary>
    public long WastedStorageBytes => Documents.Skip(1).Sum(d => d.FileSizeBytes);
}

/// <summary>
/// Represents a single document within a duplicate group,
/// capturing the metadata needed to identify and manage the duplicate.
/// </summary>
public class DuplicateDocument
{
    /// <summary>
    /// The primary key of the document in the database.
    /// </summary>
    public long DocumentId { get; init; }

    /// <summary>
    /// The original file name of the document.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// The absolute file path where the document was imported from.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// The size of the document file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// The timestamp when this document was imported into the knowledge vault.
    /// </summary>
    public DateTime ImportedAt { get; init; }
}

/// <summary>
/// Represents an AI-generated suggestion for organizing an uncategorized document
/// into a collection with appropriate tags.
/// </summary>
public class OrganizationSuggestion
{
    /// <summary>
    /// The primary key of the document this suggestion applies to.
    /// </summary>
    public long DocumentId { get; init; }

    /// <summary>
    /// The file name of the document this suggestion applies to.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// The name of the collection the AI suggests this document belongs to.
    /// May be an existing collection name or a suggestion for a new collection.
    /// </summary>
    public string SuggestedCollection { get; init; } = string.Empty;

    /// <summary>
    /// A list of 2-3 descriptive tags the AI suggests for this document.
    /// </summary>
    public List<string> SuggestedTags { get; init; } = new();

    /// <summary>
    /// The AI's reasoning for why it chose this collection and these tags.
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// The AI's confidence in this suggestion, ranging from 0.0 (no confidence) to 1.0 (certain).
    /// </summary>
    public float Confidence { get; init; }
}

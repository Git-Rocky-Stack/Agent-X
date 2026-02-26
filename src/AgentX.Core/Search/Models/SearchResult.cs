namespace AgentX.Core.Search.Models;

/// <summary>
/// A single semantic search result with matched chunk, relevance score,
/// and source document metadata.
/// </summary>
public class SearchResult
{
    /// <summary>The document chunk entity ID.</summary>
    public long ChunkId { get; init; }

    /// <summary>The parent document entity ID.</summary>
    public long DocumentId { get; init; }

    /// <summary>The source document file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The source document file path.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>The source document file type (e.g. "pdf", "docx").</summary>
    public string FileType { get; init; } = string.Empty;

    /// <summary>Page number within the source document (if available).</summary>
    public int? PageNumber { get; init; }

    /// <summary>The chunk index within the document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>The matched text from the chunk.</summary>
    public string MatchedText { get; init; } = string.Empty;

    /// <summary>
    /// A shorter excerpt of the matched text suitable for display,
    /// with the most relevant section highlighted.
    /// </summary>
    public string Excerpt { get; init; } = string.Empty;

    /// <summary>
    /// Cosine similarity score between 0.0 and 1.0.
    /// Higher = more relevant.
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Relevance as a percentage (0-100), derived from the Score.
    /// </summary>
    public int RelevancePercent => (int)(Score * 100);

    /// <summary>Collection names this document belongs to.</summary>
    public List<string> CollectionNames { get; init; } = new();
}

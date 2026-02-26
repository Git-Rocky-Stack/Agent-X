namespace AgentX.Core.Search.Models;

/// <summary>
/// Represents a citation reference extracted from an AI-generated RAG response.
/// Links back to the source document and chunk that was used as context.
/// </summary>
public class Citation
{
    /// <summary>The citation number as it appears in the response text (e.g. 1 for [1]).</summary>
    public int Number { get; init; }

    /// <summary>The source document entity ID.</summary>
    public long DocumentId { get; init; }

    /// <summary>The source chunk entity ID.</summary>
    public long ChunkId { get; init; }

    /// <summary>The source document file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The source document file path.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Page number within the source document (if available).</summary>
    public int? PageNumber { get; init; }

    /// <summary>The chunk index within the document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>A short excerpt from the cited chunk.</summary>
    public string Excerpt { get; init; } = string.Empty;

    /// <summary>The relevance score of this chunk to the original query.</summary>
    public float RelevanceScore { get; init; }
}

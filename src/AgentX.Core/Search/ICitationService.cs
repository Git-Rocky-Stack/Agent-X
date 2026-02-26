using AgentX.Core.Search.Models;

namespace AgentX.Core.Search;

/// <summary>
/// Extracts and resolves citation references from AI-generated RAG responses.
/// Citation format: [N] where N is the 1-based index of the context chunk.
/// </summary>
public interface ICitationService
{
    /// <summary>
    /// Extracts all [N] citation references from the response text
    /// and maps them to the corresponding source chunks.
    /// </summary>
    /// <param name="responseText">The AI-generated response text containing [N] references.</param>
    /// <param name="contextChunks">The ordered list of context chunks that were provided to the AI (1-indexed in citations).</param>
    /// <returns>List of resolved citations with document metadata.</returns>
    List<Citation> ExtractCitations(string responseText, IReadOnlyList<RagContextChunk> contextChunks);
}

/// <summary>
/// Represents a single chunk of context that was provided to the AI during RAG.
/// Used by CitationService to resolve citation references back to source documents.
/// </summary>
public class RagContextChunk
{
    public long ChunkId { get; init; }
    public long DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public int ChunkIndex { get; init; }
    public string ChunkText { get; init; } = string.Empty;
    public float RelevanceScore { get; init; }
}

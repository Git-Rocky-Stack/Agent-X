using AgentX.Core.Documents.Models;

namespace AgentX.Core.Documents;

/// <summary>
/// Splits text content into overlapping chunks suitable for embedding generation.
/// Uses a recursive character text splitter strategy: paragraphs -> sentences -> words.
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// Splits raw text into overlapping chunks with metadata.
    /// </summary>
    /// <param name="text">The text content to chunk.</param>
    /// <param name="chunkSize">Maximum number of tokens (approximated as word count) per chunk.</param>
    /// <param name="chunkOverlap">Number of overlapping tokens between consecutive chunks.</param>
    /// <param name="sectionTitle">Optional section title to attach to all generated chunks.</param>
    /// <param name="pageNumber">Optional page number to attach to all generated chunks.</param>
    /// <returns>An ordered list of document chunks with content, offsets, and token counts.</returns>
    IReadOnlyList<DocumentChunk> ChunkText(
        string text,
        int chunkSize = 512,
        int chunkOverlap = 50,
        string? sectionTitle = null,
        int? pageNumber = null);

    /// <summary>
    /// Splits a processed document into overlapping chunks, respecting page boundaries
    /// when page-level text is available.
    /// </summary>
    /// <param name="document">The processed document containing extracted text and metadata.</param>
    /// <param name="chunkSize">Maximum number of tokens (approximated as word count) per chunk.</param>
    /// <param name="chunkOverlap">Number of overlapping tokens between consecutive chunks.</param>
    /// <returns>An ordered list of document chunks covering the entire document.</returns>
    IReadOnlyList<DocumentChunk> ChunkDocument(
        ProcessedDocument document,
        int chunkSize = 512,
        int chunkOverlap = 50);
}

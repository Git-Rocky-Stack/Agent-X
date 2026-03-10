using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Retrieves parent document chunks by loading adjacent chunks from the same document.
/// When a small chunk matches precisely, this service expands it by including the
/// preceding and following chunks from the same document, providing the LLM with
/// richer surrounding context without losing retrieval precision.
/// </summary>
public sealed class ParentDocumentRetriever : IParentDocumentRetriever
{
    private readonly AgentXDbContext _dbContext;
    private readonly ILogger _logger;

    /// <summary>Number of adjacent chunks to include on each side of the matched chunk.</summary>
    private const int AdjacentChunkRadius = 1;

    public ParentDocumentRetriever(AgentXDbContext dbContext, ILogger logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger?.ForContext<ParentDocumentRetriever>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<List<RagContextChunk>> RetrieveParentChunksAsync(
        List<RagContextChunk> childChunks,
        CancellationToken ct = default)
    {
        if (childChunks is null || childChunks.Count == 0)
            return new List<RagContextChunk>();

        _logger.Debug("Expanding {Count} chunks with parent context (radius={Radius})",
            childChunks.Count, AdjacentChunkRadius);

        var expandedChunks = new List<RagContextChunk>(childChunks.Count);
        var processedDocChunks = new HashSet<string>(); // Track "docId:chunkIndex" to avoid duplicates

        foreach (var child in childChunks)
        {
            ct.ThrowIfCancellationRequested();

            var dedupeKey = $"{child.DocumentId}:{child.ChunkIndex}";
            if (!processedDocChunks.Add(dedupeKey))
                continue;

            try
            {
                // Load adjacent chunks from the same document
                var minIndex = Math.Max(0, child.ChunkIndex - AdjacentChunkRadius);
                var maxIndex = child.ChunkIndex + AdjacentChunkRadius;

                var adjacentChunks = await _dbContext.DocumentChunks
                    .Where(c => c.DocumentId == child.DocumentId
                                && c.ChunkIndex >= minIndex
                                && c.ChunkIndex <= maxIndex)
                    .OrderBy(c => c.ChunkIndex)
                    .Select(c => c.Content)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                if (adjacentChunks.Count > 1)
                {
                    // Merge adjacent chunks into a single expanded context
                    var mergedText = string.Join("\n\n", adjacentChunks);

                    expandedChunks.Add(new RagContextChunk
                    {
                        ChunkId = child.ChunkId,
                        DocumentId = child.DocumentId,
                        FileName = child.FileName,
                        FilePath = child.FilePath,
                        PageNumber = child.PageNumber,
                        ChunkIndex = child.ChunkIndex,
                        ChunkText = mergedText,
                        RelevanceScore = child.RelevanceScore
                    });
                }
                else
                {
                    // No adjacent chunks found — keep original
                    expandedChunks.Add(child);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to expand chunk {ChunkId}; keeping original", child.ChunkId);
                expandedChunks.Add(child);
            }
        }

        _logger.Debug("Parent retrieval complete: {Input} -> {Output} chunks",
            childChunks.Count, expandedChunks.Count);

        return expandedChunks;
    }
}

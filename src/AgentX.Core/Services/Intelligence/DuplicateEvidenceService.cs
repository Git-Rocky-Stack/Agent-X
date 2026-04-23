using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Intelligence.Models;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

public sealed class DuplicateEvidenceService : IDuplicateEvidenceService
{
    private readonly ILogger _logger;

    public DuplicateEvidenceService(ILogger logger)
    {
        _logger = logger?.ForContext<DuplicateEvidenceService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<DuplicateEvidence> BuildEvidence(
        IReadOnlyList<VectorSearchResult> searchResults,
        IReadOnlyDictionary<long, long> chunkToDocument)
    {
        ArgumentNullException.ThrowIfNull(searchResults);
        ArgumentNullException.ThrowIfNull(chunkToDocument);

        var grouped = searchResults
            .Where(result => chunkToDocument.ContainsKey(result.ChunkId))
            .GroupBy(result => chunkToDocument[result.ChunkId])
            .Select(group =>
            {
                var similarities = group.Select(result => result.Similarity).ToList();
                var maxSimilarity = similarities.Max();
                var averageSimilarity = similarities.Average();
                var supportingChunks = group.Select(result => result.ChunkId).Distinct().Count();

                return new DuplicateEvidence
                {
                    DocumentId = group.Key,
                    SupportingChunkCount = supportingChunks,
                    MaxSimilarity = maxSimilarity,
                    AverageSimilarity = averageSimilarity,
                    Confidence = Math.Min(1.0, (maxSimilarity * 0.7) + (averageSimilarity * 0.2) + Math.Min(0.1 * supportingChunks, 0.2))
                };
            })
            .OrderByDescending(evidence => evidence.Confidence)
            .ThenByDescending(evidence => evidence.MaxSimilarity)
            .ToList();

        _logger.Debug("Built duplicate evidence groups for {Count} candidate documents", grouped.Count);
        return grouped;
    }
}

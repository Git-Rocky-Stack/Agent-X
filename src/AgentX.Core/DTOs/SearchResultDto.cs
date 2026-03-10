using AgentX.Core.Helpers;

namespace AgentX.Core.DTOs;

/// <summary>
/// Display-ready search result with pre-computed formatting.
/// Eliminates N+1 queries by including document metadata inline.
/// </summary>
public sealed record SearchResultDto
{
    public required Guid DocumentId { get; init; }
    public required Guid ChunkId { get; init; }
    public required string FileName { get; init; }
    public required string FileType { get; init; }
    public required string Excerpt { get; init; }
    public required double Score { get; init; }
    public int? PageNumber { get; init; }
    public IReadOnlyList<string> CollectionNames { get; init; } = [];

    // Pre-computed display values
    public double RelevancePercent => Score * 100;
    public string RelevanceFormatted => FormatHelper.FormatPercent(Score * 100);
    public string FileTypeIcon => DocumentDisplayDto.GetFileTypeIcon(FileType);
}

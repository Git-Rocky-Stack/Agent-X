using AgentX.Core.Helpers;

namespace AgentX.Core.DTOs;

/// <summary>
/// Display-ready items for the dashboard's recent activity lists.
/// </summary>
public sealed record DashboardRecentDocumentDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string FileType { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTime ImportedAtUtc { get; init; }

    public string FileSizeFormatted => FormatHelper.FormatBytes(FileSizeBytes);
    public string ImportedAgo => FormatHelper.TimeAgo(ImportedAtUtc);
}

public sealed record DashboardRecentConversationDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required int MessageCount { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }

    public string UpdatedAgo => FormatHelper.TimeAgo(UpdatedAtUtc);
}

public sealed record FileTypeBreakdownDto
{
    public required string FileType { get; init; }
    public required int Count { get; init; }
    public required double Percentage { get; init; }

    public string PercentFormatted => FormatHelper.FormatPercent(Percentage);
}

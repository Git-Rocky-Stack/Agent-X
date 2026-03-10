using AgentX.Core.Helpers;

namespace AgentX.Core.DTOs;

/// <summary>
/// Display-ready document information for ViewModels.
/// Pre-computes formatted values to avoid duplicate formatting in ViewModels.
/// </summary>
public sealed record DocumentDisplayDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required string FileType { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTime ImportedAtUtc { get; init; }
    public required string IndexingStatus { get; init; }
    public required int ChunkCount { get; init; }
    public required int WordCount { get; init; }
    public required int PageCount { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? CollectionName { get; init; }

    // Pre-computed display values
    public string FileSizeFormatted => FormatHelper.FormatBytes(FileSizeBytes);
    public string ImportedAgo => FormatHelper.TimeAgo(ImportedAtUtc);
    public string FileTypeIcon => GetFileTypeIcon(FileType);

    internal static string GetFileTypeIcon(string fileType) => fileType?.ToLowerInvariant() switch
    {
        ".pdf" => "\uEA90",
        ".docx" or ".doc" => "\uE8A5",
        ".txt" => "\uE8A4",
        ".md" or ".markdown" => "\uE943",
        ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".go" or ".rs" => "\uE943",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "\uEB9F",
        ".mp3" or ".wav" or ".flac" or ".ogg" => "\uE8D6",
        _ => "\uE8A4"
    };
}

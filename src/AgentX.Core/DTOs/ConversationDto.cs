using AgentX.Core.Helpers;

namespace AgentX.Core.DTOs;

/// <summary>
/// Display-ready conversation summary for list views.
/// </summary>
public sealed record ConversationDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public required int MessageCount { get; init; }
    public required bool IsPinned { get; init; }
    public string? LastMessagePreview { get; init; }

    // Pre-computed display values
    public string UpdatedAgo => FormatHelper.TimeAgo(UpdatedAtUtc);
}

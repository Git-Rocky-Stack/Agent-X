using AgentX.Core.Helpers;

namespace AgentX.Core.DTOs;

/// <summary>
/// Display-ready chat message for the conversation view.
/// </summary>
public sealed record MessageDto
{
    public required Guid Id { get; init; }
    public required Guid ConversationId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public int TokenCount { get; init; }
    public double GenerationTimeMs { get; init; }
    public string? ModelId { get; init; }

    // Pre-computed display values
    public string FormattedTime => TimestampUtc.ToLocalTime().ToString("h:mm tt");
    public string FormattedTokens => TokenCount > 0 ? FormatHelper.FormatTokens(TokenCount) : "";
    public string FormattedSpeed => GenerationTimeMs > 0 && TokenCount > 0
        ? $"{TokenCount / (GenerationTimeMs / 1000.0):F1} tok/s"
        : "";
}

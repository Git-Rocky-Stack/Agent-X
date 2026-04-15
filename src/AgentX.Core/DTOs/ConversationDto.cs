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

    // ── Conversation Branching ───────────────────────────────
    /// <summary>ID of the parent conversation this was branched from, or null for a root conversation.</summary>
    public long? ParentConversationId { get; init; }

    /// <summary>ID of the message in the parent conversation where this branch starts.</summary>
    public long? BranchPointMessageId { get; init; }

    /// <summary>Optional user-provided label for this branch (e.g. "Alt approach").</summary>
    public string? BranchLabel { get; init; }

    /// <summary>Number of child branches that have been created from this conversation.</summary>
    public int BranchCount { get; init; }
}

using AgentX.Core.AI.Context;

namespace AgentX.Core.Services.Chat.Models;

/// <summary>
/// Latest in-memory inspection snapshot for one conversation's assembled chat
/// context. This is intentionally ephemeral and is not persisted.
/// </summary>
public sealed record ChatContextInspectionSnapshot
{
    public long ConversationId { get; init; }
    public DateTime CapturedAt { get; init; }
    public string CurrentQuery { get; init; } = string.Empty;
    public ContextAssemblyDiagnostics Diagnostics { get; init; } = new();
    public ConversationSummaryInspection? Summary { get; init; }
    public IReadOnlyList<ChatContextRecallInspectionItem> RecallMatches { get; init; } = Array.Empty<ChatContextRecallInspectionItem>();
    public string AssemblyExplanation { get; init; } = string.Empty;
    public string CompressionExplanation { get; init; } = string.Empty;
    public string RecallExplanation { get; init; } = string.Empty;
    public bool HasLimitedVisibility { get; init; }
    public string? LimitedVisibilityReason { get; init; }

    public static ChatContextInspectionSnapshot CreateLimited(
        long conversationId,
        string currentQuery,
        string reason) =>
        new()
        {
            ConversationId = conversationId,
            CapturedAt = DateTime.UtcNow,
            CurrentQuery = currentQuery,
            HasLimitedVisibility = true,
            LimitedVisibilityReason = reason,
            AssemblyExplanation = "Agent-X generated a response without the full context assembly pipeline.",
            CompressionExplanation = "Compression details are unavailable for this response path.",
            RecallExplanation = "Durable recall details are unavailable for this response path."
        };
}

/// <summary>
/// Structured durable summary state for chat-side inspection.
/// </summary>
public sealed record ConversationSummaryInspection
{
    public long ConversationId { get; init; }
    public string PreviewText { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public IReadOnlyList<string> KeyPoints { get; init; } = Array.Empty<string>();
    public DateTime GeneratedAt { get; init; }
    public DateTime? LastRefreshedAt { get; init; }
    public bool IsStale { get; init; }
    public int PendingMessageCount { get; init; }
}

/// <summary>
/// Chat-facing projection of a recalled message actually included in the
/// assembled context.
/// </summary>
public sealed record ChatContextRecallInspectionItem
{
    public long ConversationId { get; init; }
    public long MessageId { get; init; }
    public string ConversationTitle { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ContentPreview { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public float Similarity { get; init; }
}

/// <summary>
/// Result of a user-triggered durable summary refresh for one conversation.
/// Carries the latest cached inspection snapshot when available.
/// </summary>
public sealed record ConversationSummaryRefreshResult
{
    public bool Succeeded { get; init; }
    public ChatContextInspectionSnapshot? Snapshot { get; init; }
    public string? ErrorMessage { get; init; }

    public static ConversationSummaryRefreshResult Success(ChatContextInspectionSnapshot snapshot) =>
        new()
        {
            Succeeded = true,
            Snapshot = snapshot
        };

    public static ConversationSummaryRefreshResult Failure(
        ChatContextInspectionSnapshot? snapshot,
        string errorMessage) =>
        new()
        {
            Succeeded = false,
            Snapshot = snapshot,
            ErrorMessage = errorMessage
        };
}

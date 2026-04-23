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
    public string ContextStoryText => BuildContextStoryText(this);
    public IReadOnlyList<ChatContextStorySourceChip> ContextStorySourceChips => BuildContextStorySourceChips(this);

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

    private static string BuildContextStoryText(ChatContextInspectionSnapshot snapshot)
    {
        if (snapshot.HasLimitedVisibility)
        {
            return snapshot.LimitedVisibilityReason switch
            {
                "summary_only_refresh" => "Showing a summary-only view because no newly assembled response context has been captured yet.",
                _ => "This response used a limited-visibility path, so only partial chat context details are available."
            };
        }

        if (snapshot.Diagnostics.UsedLegacyFallback)
        {
            return "This response used the legacy context path, so the assembled context story is only partially inspectable.";
        }

        if (snapshot.Diagnostics.UsedLexicalFallback)
        {
            return AppendContextIngredients(
                "Agent-X selected thread context with lexical fallback",
                snapshot);
        }

        var leadClause = snapshot.Summary switch
        {
            { IsStale: true, PendingMessageCount: > 0 } summary =>
                $"Using a stale durable summary with {summary.PendingMessageCount} newer {Pluralize("message", summary.PendingMessageCount)} still outside it",
            { IsStale: true } =>
                "Using a stale durable summary while newer thread changes wait to be folded in",
            not null =>
                "Using a current durable summary",
            _ =>
                "Using live thread context without a durable summary snapshot"
        };

        return AppendContextIngredients(leadClause, snapshot);
    }

    private static IReadOnlyList<ChatContextStorySourceChip> BuildContextStorySourceChips(
        ChatContextInspectionSnapshot snapshot)
    {
        var chips = new List<ChatContextStorySourceChip>(5);

        if (snapshot.HasLimitedVisibility)
        {
            chips.Add(new ChatContextStorySourceChip { Label = "Limited Visibility" });
            if (string.Equals(snapshot.LimitedVisibilityReason, "summary_only_refresh", StringComparison.Ordinal))
            {
                chips.Add(new ChatContextStorySourceChip { Label = "Summary Only" });
            }
        }

        if (snapshot.Diagnostics.UsedLegacyFallback)
        {
            chips.Add(new ChatContextStorySourceChip { Label = "Legacy Fallback" });
        }
        else if (snapshot.Diagnostics.UsedLexicalFallback)
        {
            chips.Add(new ChatContextStorySourceChip { Label = "Lexical Fallback" });
        }

        if (snapshot.Summary is not null)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = snapshot.Summary.IsStale ? "Stale Summary" : "Current Summary"
            });
        }

        if (snapshot.RecallMatches.Count > 0)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = snapshot.RecallMatches.Count == 1
                    ? "1 Recall Match"
                    : $"{snapshot.RecallMatches.Count} Recall Matches"
            });
        }

        if (snapshot.Diagnostics.AddedOverflowSummary)
        {
            chips.Add(new ChatContextStorySourceChip { Label = "Compressed Overflow" });
        }

        return chips;
    }

    private static string AppendContextIngredients(
        string leadClause,
        ChatContextInspectionSnapshot snapshot)
    {
        var ingredients = new List<string>(2);

        if (snapshot.RecallMatches.Count > 0)
        {
            ingredients.Add(snapshot.RecallMatches.Count == 1
                ? "1 recalled message from another conversation"
                : $"{snapshot.RecallMatches.Count} recalled messages from other conversations");
        }

        if (snapshot.Diagnostics.AddedOverflowSummary)
        {
            ingredients.Add("compressed overflow context");
        }

        return ingredients.Count switch
        {
            0 => $"{leadClause}.",
            1 => $"{leadClause} and {ingredients[0]}.",
            _ => $"{leadClause}, {ingredients[0]}, and {ingredients[1]}."
        };
    }

    private static string Pluralize(string noun, int count) =>
        count == 1 ? noun : $"{noun}s";
}

public sealed record ChatContextStorySourceChip
{
    public string Label { get; init; } = string.Empty;
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

using AgentX.Core.AI.Context;
using AgentX.Core.Helpers;

namespace AgentX.Core.Services.Chat.Models;

/// <summary>
/// Latest in-memory inspection snapshot for one conversation's assembled chat
/// context. This is intentionally ephemeral and is not persisted. Its story, chips and
/// explanations are worded in the user's language through <see cref="FormatHelper.LocalizedText"/>
/// (English until the app sets it).
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
        string reason)
    {
        var words = LocalizedWords.Current;
        return new()
        {
            ConversationId = conversationId,
            CapturedAt = DateTime.UtcNow,
            CurrentQuery = currentQuery,
            HasLimitedVisibility = true,
            LimitedVisibilityReason = reason,
            AssemblyExplanation = words.GetString(
                "Chat_ExplainAssemblyLimited",
                "Agent-X generated a response without the full context assembly pipeline."),
            CompressionExplanation = words.GetString(
                "Chat_ExplainCompressionLimited",
                "Compression details are unavailable for this response path."),
            RecallExplanation = words.GetString(
                "Chat_ExplainRecallLimited",
                "Durable recall details are unavailable for this response path.")
        };
    }

    private static string BuildContextStoryText(ChatContextInspectionSnapshot snapshot)
    {
        var words = LocalizedWords.Current;

        if (snapshot.HasLimitedVisibility)
        {
            return snapshot.LimitedVisibilityReason switch
            {
                "summary_only_refresh" => words.GetString(
                    "Chat_StorySummaryOnly",
                    "Showing a summary-only view because no newly assembled response context has been captured yet."),
                _ => words.GetString(
                    "Chat_StoryLimited",
                    "This response used a limited-visibility path, so only partial chat context details are available.")
            };
        }

        if (snapshot.Diagnostics.UsedLegacyFallback)
        {
            return words.GetString(
                "Chat_StoryLegacy",
                "This response used the legacy context path, so the assembled context story is only partially inspectable.");
        }

        if (snapshot.Diagnostics.UsedLexicalFallback)
        {
            return AppendContextIngredients(
                words.GetString("Chat_StoryLeadLexical", "Agent-X selected thread context with lexical fallback"),
                snapshot,
                words);
        }

        var leadClause = snapshot.Summary switch
        {
            { IsStale: true, PendingMessageCount: 1 } => words.GetString(
                "Chat_StoryLeadStaleOne",
                "Using a stale durable summary with 1 newer message still outside it"),
            { IsStale: true, PendingMessageCount: > 1 } summary => words.GetString(
                "Chat_StoryLeadStaleMany",
                "Using a stale durable summary with {0} newer messages still outside it",
                summary.PendingMessageCount),
            { IsStale: true } => words.GetString(
                "Chat_StoryLeadStale",
                "Using a stale durable summary while newer thread changes wait to be folded in"),
            not null => words.GetString("Chat_StoryLeadCurrent", "Using a current durable summary"),
            _ => words.GetString("Chat_StoryLeadLive", "Using live thread context without a durable summary snapshot")
        };

        return AppendContextIngredients(leadClause, snapshot, words);
    }

    private static IReadOnlyList<ChatContextStorySourceChip> BuildContextStorySourceChips(
        ChatContextInspectionSnapshot snapshot)
    {
        var words = LocalizedWords.Current;
        var chips = new List<ChatContextStorySourceChip>(5);

        if (snapshot.HasLimitedVisibility)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = words.GetString("Chat_ChipLimitedVisibility", "Limited Visibility")
            });
            if (string.Equals(snapshot.LimitedVisibilityReason, "summary_only_refresh", StringComparison.Ordinal))
            {
                chips.Add(new ChatContextStorySourceChip
                {
                    Label = words.GetString("Chat_ChipSummaryOnly", "Summary Only")
                });
            }
        }

        if (snapshot.Diagnostics.UsedLegacyFallback)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = words.GetString("Chat_ChipLegacyFallback", "Legacy Fallback")
            });
        }
        else if (snapshot.Diagnostics.UsedLexicalFallback)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = words.GetString("Chat_ChipLexicalFallback", "Lexical Fallback")
            });
        }

        if (snapshot.Summary is not null)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = snapshot.Summary.IsStale
                    ? words.GetString("Chat_ChipStaleSummary", "Stale Summary")
                    : words.GetString("Chat_ChipCurrentSummary", "Current Summary")
            });
        }

        if (snapshot.RecallMatches.Count > 0)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = snapshot.RecallMatches.Count == 1
                    ? words.GetString("Chat_ChipRecallMatchOne", "1 Recall Match")
                    : words.GetString("Chat_ChipRecallMatchesMany", "{0} Recall Matches", snapshot.RecallMatches.Count)
            });
        }

        if (snapshot.Diagnostics.AddedOverflowSummary)
        {
            chips.Add(new ChatContextStorySourceChip
            {
                Label = words.GetString("Chat_ChipCompressedOverflow", "Compressed Overflow")
            });
        }

        return chips;
    }

    /// <summary>
    /// Ends the story's lead clause with what else went into the context. The whole sentence is
    /// a resource template, so each language joins the parts its own way.
    /// </summary>
    private static string AppendContextIngredients(
        string leadClause,
        ChatContextInspectionSnapshot snapshot,
        LocalizedWords words)
    {
        var ingredients = new List<string>(2);

        if (snapshot.RecallMatches.Count > 0)
        {
            ingredients.Add(snapshot.RecallMatches.Count == 1
                ? words.GetString("Chat_StoryRecallOne", "1 recalled message from another conversation")
                : words.GetString(
                    "Chat_StoryRecallMany",
                    "{0} recalled messages from other conversations",
                    snapshot.RecallMatches.Count));
        }

        if (snapshot.Diagnostics.AddedOverflowSummary)
        {
            ingredients.Add(words.GetString("Chat_StoryOverflow", "compressed overflow context"));
        }

        return ingredients.Count switch
        {
            0 => words.GetString("Chat_StorySentence", "{0}.", leadClause),
            1 => words.GetString("Chat_StorySentenceOne", "{0} and {1}.", leadClause, ingredients[0]),
            _ => words.GetString("Chat_StorySentenceTwo", "{0}, {1}, and {2}.", leadClause, ingredients[0], ingredients[1])
        };
    }
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

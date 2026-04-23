using AgentX.Core.AI.Models;
using AgentX.Core.Constants;

namespace AgentX.Core.AI.Context;

public readonly record struct IndexedChatMessage(int Index, ChatMessage Message);

public sealed class ContextAssemblyRequest
{
    public string CurrentQuery { get; init; } = string.Empty;
    public string? SystemPrompt { get; init; }
    public string? MemoryContext { get; init; }
    public IReadOnlyList<ChatMessage> ConversationMessages { get; init; } = Array.Empty<ChatMessage>();
    public int ContextWindow { get; init; } = AppConstants.ChatDefaultContextWindow;
    public int ReserveForResponse { get; init; } = AppConstants.ContextWindowTokenReserve;
    public int RecentAnchorCount { get; init; } = AppConstants.MinPreservedMessages;
}

public sealed class ContextAssemblyResult
{
    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();
    public string? SystemPrompt { get; init; }
    public ContextAssemblyDiagnostics Diagnostics { get; init; } = new();
}

public sealed class ContextAssemblyDiagnostics
{
    public int OriginalMessageCount { get; init; }
    public int SelectedMessageCount { get; init; }
    public int AnchorMessageCount { get; init; }
    public int OverflowMessageCount { get; init; }
    public int EstimatedMessageTokens { get; init; }
    public int EstimatedPromptTokens { get; init; }
    public bool AddedOverflowSummary { get; init; }
    public bool UsedLegacyFallback { get; init; }
    public bool UsedLexicalFallback { get; init; }
    public string? CompressionSkipReason { get; init; }
}

public sealed class ContextSelectionRequest
{
    public string CurrentQuery { get; init; } = string.Empty;
    public IReadOnlyList<IndexedChatMessage> CandidateMessages { get; init; } = Array.Empty<IndexedChatMessage>();
    public int MaxTokenBudget { get; init; }
}

public sealed class ContextSelectionResult
{
    public IReadOnlyList<IndexedChatMessage> SelectedMessages { get; init; } = Array.Empty<IndexedChatMessage>();
    public IReadOnlyList<IndexedChatMessage> OverflowMessages { get; init; } = Array.Empty<IndexedChatMessage>();
    public bool UsedLexicalFallback { get; init; }
    public int EstimatedSelectedTokens { get; init; }
}

public sealed class ConversationCompressionRequest
{
    public string CurrentQuery { get; init; } = string.Empty;
    public IReadOnlyList<IndexedChatMessage> OverflowMessages { get; init; } = Array.Empty<IndexedChatMessage>();
    public int MaxSummaryTokens { get; init; } = AppConstants.CompressionMaxTokens;
}

public sealed class ConversationCompressionResult
{
    public string? Summary { get; init; }
    public int EstimatedSummaryTokens { get; init; }
    public int SourceMessageCount { get; init; }
    public bool WasSkipped { get; init; }
    public string? SkipReason { get; init; }

    public static ConversationCompressionResult Skip(string reason, int sourceMessageCount = 0) =>
        new()
        {
            WasSkipped = true,
            SkipReason = reason,
            SourceMessageCount = sourceMessageCount
        };
}

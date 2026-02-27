using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Manages conversation context to fit within model token limits by trimming
/// older messages while preserving the system prompt and most recent exchanges.
///
/// Token estimation uses a conservative heuristic of ~4 characters per token,
/// which is appropriate for English text with Llama-family models. Each message
/// also incurs a small overhead (~4 tokens) for role labels and formatting.
/// </summary>
public sealed class ContextWindowManager : IContextWindowManager
{
    private readonly ILogger _logger;

    /// <summary>
    /// Approximate number of characters per token for English text.
    /// Conservative estimate suitable for Llama-family and similar models.
    /// </summary>
    private const int CharsPerToken = 4;

    /// <summary>
    /// Per-message token overhead to account for role labels, delimiters,
    /// and formatting tokens injected by the model's chat template.
    /// </summary>
    private const int MessageOverheadTokens = 4;

    /// <summary>
    /// Default context window size when the model does not report one.
    /// 4096 is a safe minimum supported by virtually all instruction-tuned models.
    /// </summary>
    private const int DefaultContextWindow = 4096;

    /// <summary>
    /// Maximum context window cap to prevent unreasonable values.
    /// 128K tokens is the upper bound for current large-context models.
    /// </summary>
    private const int MaxContextWindowCap = 131072;

    /// <summary>
    /// Minimum number of non-system messages to preserve even when trimming.
    /// Ensures at least the most recent user-assistant exchange is retained.
    /// </summary>
    private const int MinPreservedMessages = 4;

    /// <summary>
    /// Creates a new ContextWindowManager with the specified logger.
    /// </summary>
    /// <param name="logger">Serilog logger for diagnostic output.</param>
    public ContextWindowManager(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<List<ChatMessage>> FitToContextWindowAsync(
        List<ChatMessage> messages,
        int maxTokens,
        int reserveForResponse = 1024,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens,
                "Maximum tokens must be a positive value.");

        if (reserveForResponse < 0)
            throw new ArgumentOutOfRangeException(nameof(reserveForResponse), reserveForResponse,
                "Reserve for response cannot be negative.");

        ct.ThrowIfCancellationRequested();

        var availableBudget = maxTokens - reserveForResponse;
        if (availableBudget <= 0)
        {
            _logger.Warning(
                "Token budget exhausted before any messages: maxTokens={MaxTokens}, reserveForResponse={Reserve}",
                maxTokens, reserveForResponse);
            return Task.FromResult(new List<ChatMessage>());
        }

        var originalCount = messages.Count;
        var originalTokens = EstimateTokenCount(messages);

        // If everything fits within the budget, return a copy without modification
        if (originalTokens <= availableBudget)
        {
            _logger.Debug(
                "All {Count} messages fit within budget ({EstimatedTokens}/{Budget} tokens)",
                originalCount, originalTokens, availableBudget);
            return Task.FromResult(new List<ChatMessage>(messages));
        }

        // Separate system prompt from conversation messages
        ChatMessage? systemPrompt = null;
        List<ChatMessage> conversationMessages;

        if (messages.Count > 0 &&
            string.Equals(messages[0].Role, "system", StringComparison.OrdinalIgnoreCase))
        {
            systemPrompt = messages[0];
            conversationMessages = new List<ChatMessage>(messages.Skip(1));
        }
        else
        {
            conversationMessages = new List<ChatMessage>(messages);
        }

        // Calculate tokens consumed by the system prompt (always preserved)
        var systemTokens = systemPrompt is not null
            ? EstimateTokenCountForMessage(systemPrompt)
            : 0;

        var budgetForConversation = availableBudget - systemTokens;

        if (budgetForConversation <= 0)
        {
            _logger.Warning(
                "System prompt alone ({SystemTokens} tokens) exceeds available budget ({Budget} tokens). " +
                "Returning only the system prompt.",
                systemTokens, availableBudget);

            var systemOnlyResult = new List<ChatMessage>();
            if (systemPrompt is not null)
                systemOnlyResult.Add(systemPrompt);
            return Task.FromResult(systemOnlyResult);
        }

        // Phase 1: Remove oldest non-system messages (FIFO) until we fit
        var trimmedConversation = TrimOldestMessages(conversationMessages, budgetForConversation);

        // Phase 2: If still too large after keeping only MinPreservedMessages,
        // truncate the earliest remaining messages by content length
        var trimmedTokens = EstimateTokenCount(trimmedConversation);
        if (trimmedTokens > budgetForConversation)
        {
            trimmedConversation = TruncateEarliestMessages(trimmedConversation, budgetForConversation);
        }

        // Assemble the final result
        var result = new List<ChatMessage>(trimmedConversation.Count + 1);
        if (systemPrompt is not null)
            result.Add(systemPrompt);
        result.AddRange(trimmedConversation);

        var finalTokens = EstimateTokenCount(result);

        _logger.Warning(
            "Context window trimmed: {OriginalCount} -> {FinalCount} messages, " +
            "~{OriginalTokens} -> ~{FinalTokens} estimated tokens " +
            "(budget: {Budget}, reserved: {Reserved})",
            originalCount, result.Count,
            originalTokens, finalTokens,
            availableBudget, reserveForResponse);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Conservative estimate: ~4 characters per token for English text
        return (text.Length + CharsPerToken - 1) / CharsPerToken;
    }

    /// <inheritdoc />
    public int EstimateTokenCount(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var totalTokens = 0;
        foreach (var message in messages)
        {
            totalTokens += EstimateTokenCountForMessage(message);
        }

        return totalTokens;
    }

    /// <inheritdoc />
    public int GetEffectiveContextWindow(int reportedContextLength)
    {
        if (reportedContextLength <= 0)
        {
            _logger.Debug(
                "Model reported context length {Reported}, using default {Default}",
                reportedContextLength, DefaultContextWindow);
            return DefaultContextWindow;
        }

        if (reportedContextLength > MaxContextWindowCap)
        {
            _logger.Debug(
                "Model reported context length {Reported} exceeds cap, clamping to {Cap}",
                reportedContextLength, MaxContextWindowCap);
            return MaxContextWindowCap;
        }

        return reportedContextLength;
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Estimates the token count for a single message, including content
    /// and per-message formatting overhead.
    /// </summary>
    private int EstimateTokenCountForMessage(ChatMessage message)
    {
        var contentTokens = EstimateTokenCount(message.Content);
        return contentTokens + MessageOverheadTokens;
    }

    /// <summary>
    /// Removes the oldest messages (from the front of the list) until the
    /// remaining messages fit within the token budget, preserving at least
    /// <see cref="MinPreservedMessages"/> most recent messages.
    /// </summary>
    private List<ChatMessage> TrimOldestMessages(
        List<ChatMessage> conversation,
        int budgetTokens)
    {
        if (conversation.Count == 0)
            return new List<ChatMessage>();

        // Determine the minimum number of messages to keep from the end
        var minKeep = Math.Min(MinPreservedMessages, conversation.Count);

        // Try progressively removing oldest messages
        for (var removeCount = 1; removeCount <= conversation.Count - minKeep; removeCount++)
        {
            var candidate = conversation.GetRange(removeCount, conversation.Count - removeCount);
            var candidateTokens = EstimateTokenCount(candidate);

            if (candidateTokens <= budgetTokens)
            {
                return candidate;
            }
        }

        // Could not fit even with maximum removal; return the minimum preserved set
        return conversation.GetRange(
            conversation.Count - minKeep,
            minKeep);
    }

    /// <summary>
    /// Truncates the content of the earliest messages in the list to fit
    /// within the token budget. Works forward from the oldest message,
    /// reducing content length until the total fits.
    /// This is a last-resort measure when even keeping only the minimum
    /// preserved messages still exceeds the budget.
    /// </summary>
    private List<ChatMessage> TruncateEarliestMessages(
        List<ChatMessage> conversation,
        int budgetTokens)
    {
        var result = new List<ChatMessage>(conversation.Count);

        // Copy all messages so we can mutate content without affecting originals
        foreach (var msg in conversation)
        {
            result.Add(new ChatMessage
            {
                Role = msg.Role,
                Content = msg.Content,
                Timestamp = msg.Timestamp
            });
        }

        // Calculate how many tokens we need to shed
        var currentTokens = EstimateTokenCount(result);
        var excessTokens = currentTokens - budgetTokens;

        if (excessTokens <= 0)
            return result;

        // Truncate from the earliest message forward
        for (var i = 0; i < result.Count && excessTokens > 0; i++)
        {
            var msg = result[i];
            var msgContentTokens = EstimateTokenCount(msg.Content);

            if (msgContentTokens <= 0)
                continue;

            // Calculate how many characters to remove from this message
            var tokensToRemoveFromThis = Math.Min(msgContentTokens, excessTokens);
            var charsToRemove = tokensToRemoveFromThis * CharsPerToken;

            if (charsToRemove >= msg.Content.Length)
            {
                // Remove entire content, replace with truncation marker
                excessTokens -= msgContentTokens - EstimateTokenCount("[earlier message trimmed]");
                msg.Content = "[earlier message trimmed]";
            }
            else
            {
                // Keep the tail of the message (most recent portion is more relevant)
                var keepFrom = charsToRemove;
                var truncatedContent = "..." + msg.Content[keepFrom..];
                var newTokens = EstimateTokenCount(truncatedContent);
                excessTokens -= (msgContentTokens - newTokens);
                msg.Content = truncatedContent;
            }
        }

        return result;
    }
}

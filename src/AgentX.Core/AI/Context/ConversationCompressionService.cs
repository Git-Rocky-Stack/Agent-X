using System.Text;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.AI.Context;

public sealed class ConversationCompressionService : IConversationCompressionService
{
    private readonly IAiService _aiService;
    private readonly IContextWindowManager _contextWindowManager;
    private readonly ILogger _logger;

    private const int MinOverflowMessages = 2;
    private const int MinOverflowTokens = 48;
    private const int MaxOverflowChars = 3200;

    public ConversationCompressionService(
        IAiService aiService,
        IContextWindowManager contextWindowManager,
        ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _contextWindowManager = contextWindowManager ?? throw new ArgumentNullException(nameof(contextWindowManager));
        _logger = logger?.ForContext<ConversationCompressionService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConversationCompressionResult> CompressAsync(
        ConversationCompressionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OverflowMessages.Count < MinOverflowMessages)
        {
            return ConversationCompressionResult.Skip("overflow_too_small", request.OverflowMessages.Count);
        }

        if (request.MaxSummaryTokens < 32)
        {
            return ConversationCompressionResult.Skip("summary_budget_too_small", request.OverflowMessages.Count);
        }

        var overflowTokens = _contextWindowManager.EstimateTokenCount(
            request.OverflowMessages.Select(x => x.Message));
        if (overflowTokens < MinOverflowTokens)
        {
            return ConversationCompressionResult.Skip("overflow_below_minimum_tokens", request.OverflowMessages.Count);
        }

        var transcript = BuildTranscript(request.OverflowMessages);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return ConversationCompressionResult.Skip("overflow_empty_after_normalization", request.OverflowMessages.Count);
        }

        var prompt = $$"""
                       Summarize the older conversation context that is still relevant to the user's latest request.

                       Latest request:
                       {{request.CurrentQuery}}

                       Requirements:
                       - Focus on decisions, constraints, unresolved issues, and named entities that would help answer the latest request.
                       - Omit pleasantries, filler, and low-value back-and-forth.
                       - Keep the summary concise and factual.
                       - Return plain text only.

                       Older conversation transcript:
                       {{transcript}}
                       """;

        var summary = await _aiService.ChatAsync(
            [ChatMessage.User(prompt)],
            options: new ChatOptions
            {
                Temperature = 0.2,
                MaxTokens = Math.Min(request.MaxSummaryTokens, AppConstants.CompressionMaxTokens)
            },
            ct: ct).ConfigureAwait(false);

        summary = NormalizeSummary(summary, request.MaxSummaryTokens);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return ConversationCompressionResult.Skip("summary_empty", request.OverflowMessages.Count);
        }

        var estimatedTokens = _contextWindowManager.EstimateTokenCount(summary);
        if (estimatedTokens > request.MaxSummaryTokens)
        {
            summary = TrimToTokenBudget(summary, request.MaxSummaryTokens);
            estimatedTokens = _contextWindowManager.EstimateTokenCount(summary);
        }

        if (string.IsNullOrWhiteSpace(summary) || estimatedTokens > request.MaxSummaryTokens)
        {
            return ConversationCompressionResult.Skip("summary_over_budget", request.OverflowMessages.Count);
        }

        _logger.Debug(
            "Compressed {MessageCount} overflow messages into ~{TokenCount} tokens",
            request.OverflowMessages.Count,
            estimatedTokens);

        return new ConversationCompressionResult
        {
            Summary = summary,
            EstimatedSummaryTokens = estimatedTokens,
            SourceMessageCount = request.OverflowMessages.Count
        };
    }

    private static string BuildTranscript(IReadOnlyList<IndexedChatMessage> messages)
    {
        var builder = new StringBuilder(Math.Min(MaxOverflowChars, 2048));
        foreach (var item in messages)
        {
            if (builder.Length >= MaxOverflowChars)
            {
                break;
            }

            var role = string.IsNullOrWhiteSpace(item.Message.Role) ? "message" : item.Message.Role;
            var content = item.Message.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var remaining = MaxOverflowChars - builder.Length;
            var line = $"{role}: {content}";
            if (line.Length > remaining)
            {
                line = line[..remaining];
            }

            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeSummary(string value, int maxSummaryTokens)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("```", string.Empty, StringComparison.Ordinal)
            .Trim();

        var maxChars = Math.Max(64, maxSummaryTokens * AppConstants.CharsPerToken);
        if (normalized.Length > maxChars)
        {
            normalized = normalized[..maxChars].TrimEnd();
        }

        return normalized;
    }

    private static string TrimToTokenBudget(string value, int maxSummaryTokens)
    {
        var maxChars = Math.Max(32, maxSummaryTokens * AppConstants.CharsPerToken);
        return value.Length <= maxChars
            ? value
            : value[..maxChars].TrimEnd();
    }
}

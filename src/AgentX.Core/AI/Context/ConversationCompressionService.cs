using System.Security.Cryptography;
using System.Text;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.AI.Context;

/// <summary>
/// Condenses conversation messages that no longer fit the context window into a short summary.
/// </summary>
/// <remarks>
/// The summary covers the most recent overflow messages that fit the transcript window (older
/// ones are dropped first), and it does not depend on the latest question. That makes it
/// reusable: the same overflow is summarized once and then served from a small cache, instead
/// of costing an extra model call before every answer.
/// </remarks>
public sealed class ConversationCompressionService : IConversationCompressionService
{
    private readonly IAiService _aiService;
    private readonly IContextWindowManager _contextWindowManager;
    private readonly ILogger _logger;

    private const int MinOverflowMessages = 2;
    private const int MinOverflowTokens = 48;
    private const int MaxOverflowChars = 3200;
    private const int MaxCachedSummaries = 32;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> _summaryCache = new(StringComparer.Ordinal);
    private readonly LinkedList<KeyValuePair<string, string>> _summaryOrder = new();

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

    /// <summary>Number of summaries currently cached (for tests).</summary>
    internal int CachedSummaryCount
    {
        get
        {
            lock (_cacheLock)
            {
                return _summaryCache.Count;
            }
        }
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

        var cacheKey = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(transcript)));
        var rawSummary = TryGetCachedSummary(cacheKey);
        if (rawSummary is not null)
        {
            _logger.Debug("Reusing the cached summary of {MessageCount} overflow messages", request.OverflowMessages.Count);
        }
        else
        {
            var prompt = $$"""
                           Summarize the earlier part of this conversation so the summary can stand in for the original messages.

                           Requirements:
                           - Keep decisions, constraints, facts, unresolved issues, and named entities.
                           - Omit pleasantries, filler, and low-value back-and-forth.
                           - Keep the summary concise and factual.
                           - Return plain text only.

                           Earlier conversation transcript:
                           {{transcript}}
                           """;

            var response = await _aiService.ChatAsync(
                [ChatMessage.User(prompt)],
                options: new ChatOptions
                {
                    Temperature = 0.2,
                    MaxTokens = Math.Min(request.MaxSummaryTokens, AppConstants.CompressionMaxTokens)
                },
                ct: ct).ConfigureAwait(false);

            rawSummary = CleanSummary(response);
            if (!string.IsNullOrWhiteSpace(rawSummary))
            {
                StoreSummary(cacheKey, rawSummary);
            }
        }

        var summary = NormalizeSummary(rawSummary, request.MaxSummaryTokens);
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

    /// <summary>
    /// Builds the transcript from the newest overflow messages that fit
    /// <see cref="MaxOverflowChars"/> (older ones are dropped first), in chronological order.
    /// </summary>
    internal static string BuildTranscript(IReadOnlyList<IndexedChatMessage> messages)
    {
        var lines = new List<string>();
        var used = 0;

        foreach (var item in messages.OrderByDescending(x => x.Index))
        {
            var remaining = MaxOverflowChars - used;
            if (remaining <= 0)
            {
                break;
            }

            var role = string.IsNullOrWhiteSpace(item.Message.Role) ? "message" : item.Message.Role;
            var content = item.Message.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var line = $"{role}: {content}";
            if (line.Length > remaining)
            {
                line = line[..remaining];
            }

            lines.Add(line);
            used += line.Length + 1;
        }

        lines.Reverse();
        return string.Join('\n', lines).Trim();
    }

    private string? TryGetCachedSummary(string key)
    {
        lock (_cacheLock)
        {
            if (!_summaryCache.TryGetValue(key, out var node))
            {
                return null;
            }

            _summaryOrder.Remove(node);
            _summaryOrder.AddFirst(node);
            return node.Value.Value;
        }
    }

    private void StoreSummary(string key, string summary)
    {
        lock (_cacheLock)
        {
            if (_summaryCache.TryGetValue(key, out var existing))
            {
                _summaryOrder.Remove(existing);
            }

            var node = _summaryOrder.AddFirst(new KeyValuePair<string, string>(key, summary));
            _summaryCache[key] = node;

            while (_summaryCache.Count > MaxCachedSummaries && _summaryOrder.Last is { } oldest)
            {
                _summaryOrder.RemoveLast();
                _summaryCache.Remove(oldest.Value.Key);
            }
        }
    }

    private static string CleanSummary(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("```", string.Empty, StringComparison.Ordinal).Trim();

    private static string NormalizeSummary(string value, int maxSummaryTokens)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = CleanSummary(value);

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

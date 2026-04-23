using System.Text;
using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Builds and stores durable conversation summaries as immutable snapshots plus
/// a mutable per-conversation state row that tracks freshness.
/// </summary>
public sealed class ConversationSummaryService : IConversationSummaryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly ChatOptions SummaryChatOptions = new()
    {
        Temperature = 0.2,
        MaxTokens = 1024
    };

    private readonly AgentXDbContext _db;
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    public ConversationSummaryService(AgentXDbContext db, IAiService aiService, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<ConversationSummaryService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetConversationSummaryContextAsync(long conversationId, CancellationToken ct = default)
    {
        var summaryData = await _db.ConversationSummaryStates
            .AsNoTracking()
            .Where(state => state.ConversationId == conversationId && state.LatestSnapshotId != null)
            .Select(state => new
            {
                state.IsStale,
                state.PendingMessageCount,
                state.LastRefreshedAt,
                Snapshot = state.LatestSnapshot!
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (summaryData?.Snapshot is null || string.IsNullOrWhiteSpace(summaryData.Snapshot.SummaryText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[Durable Conversation Summary]");
        builder.AppendLine(summaryData.Snapshot.SummaryText.Trim());

        var keyPoints = ParseKeyPoints(summaryData.Snapshot.KeyPointsJson);
        if (keyPoints.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Key points:");
            foreach (var keyPoint in keyPoints.Take(AppConstants.MaxConversationSummaryKeyPoints))
            {
                builder.Append("- ");
                builder.AppendLine(keyPoint);
            }
        }

        if (summaryData.IsStale)
        {
            builder.AppendLine();
            builder.Append("Freshness note: this snapshot may lag behind the latest thread");
            if (summaryData.PendingMessageCount > 0)
            {
                builder.Append($" ({summaryData.PendingMessageCount} newer message");
                if (summaryData.PendingMessageCount != 1)
                {
                    builder.Append('s');
                }
                builder.Append(" not yet folded in)");
            }
            builder.AppendLine(".");
        }

        return builder.ToString().Trim();
    }

    public async Task MarkConversationStaleAsync(
        long conversationId,
        bool forceFullRefresh = false,
        CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            _logger.Debug("Skipping stale mark for missing conversation {ConversationId}", conversationId);
            return;
        }

        var state = await _db.ConversationSummaryStates
            .FirstOrDefaultAsync(s => s.ConversationId == conversationId, ct)
            .ConfigureAwait(false);

        if (state is null)
        {
            state = new ConversationSummaryStateEntity
            {
                ConversationId = conversationId
            };
            _db.ConversationSummaryStates.Add(state);
        }

        if (conversation.MessageCount <= 0)
        {
            state.LastCoveredMessageCount = 0;
            state.PendingMessageCount = 0;
            state.IsStale = false;
            state.LatestSnapshotId = null;
            state.LastError = null;
        }
        else
        {
            if (forceFullRefresh || conversation.MessageCount < state.LastCoveredMessageCount)
            {
                state.LastCoveredMessageCount = 0;
            }

            state.PendingMessageCount = Math.Max(0, conversation.MessageCount - state.LastCoveredMessageCount);
            state.IsStale = state.LatestSnapshotId is null || forceFullRefresh || state.PendingMessageCount > 0;
        }

        state.LastRefreshRequestedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> RefreshConversationSummaryAsync(long conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.SortOrder))
            .Include(c => c.SummaryState)
            .ThenInclude(s => s!.LatestSnapshot)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            .ConfigureAwait(false);
        if (conversation is null || conversation.Messages.Count == 0)
        {
            return false;
        }

        var state = conversation.SummaryState ?? new ConversationSummaryStateEntity
        {
            ConversationId = conversation.Id
        };
        if (conversation.SummaryState is null)
        {
            _db.ConversationSummaryStates.Add(state);
            conversation.SummaryState = state;
        }

        if (!_aiService.IsConnected)
        {
            _logger.Debug(
                "Skipping summary refresh for conversation {ConversationId}: AI service is not connected",
                conversationId);
            return false;
        }

        var totalMessages = conversation.Messages.Count;
        var latestSnapshot = state.LatestSnapshot;
        var canUseExistingCoverage = latestSnapshot is not null
            && state.LastCoveredMessageCount > 0
            && state.LastCoveredMessageCount <= totalMessages;
        var requiresRefresh = state.IsStale
            || latestSnapshot is null
            || state.LastCoveredMessageCount != totalMessages;

        if (!requiresRefresh)
        {
            return false;
        }

        var refreshStartedAt = DateTime.UtcNow;
        state.LastRefreshAttemptedAt = refreshStartedAt;

        try
        {
            var transcript = canUseExistingCoverage
                ? BuildTranscript(
                    conversation.Messages.Skip(state.LastCoveredMessageCount),
                    AppConstants.MaxConversationSummaryTailChars)
                : BuildTranscript(
                    conversation.Messages,
                    AppConstants.MaxConversationSummarySourceChars);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                return false;
            }

            var response = await _aiService.ChatAsync(
                [new ChatMessage
                {
                    Role = "user",
                    Content = BuildSummaryPrompt(conversation, latestSnapshot, transcript, canUseExistingCoverage),
                    Timestamp = refreshStartedAt
                }],
                BuildSummarySystemPrompt(),
                SummaryChatOptions,
                ct).ConfigureAwait(false);

            var payload = ParsePayload(response);
            if (string.IsNullOrWhiteSpace(payload.Summary))
            {
                payload = payload with
                {
                    Summary = NormalizeText(response)
                };
            }

            var nextVersion = Math.Max(state.LatestSnapshotVersion, latestSnapshot?.SnapshotVersion ?? 0) + 1;
            var snapshot = new ConversationSummarySnapshotEntity
            {
                ConversationId = conversation.Id,
                SnapshotVersion = nextVersion,
                SummaryText = payload.Summary,
                PreviewText = BuildPreviewText(payload.Preview, payload.Summary),
                KeyPointsJson = SerializeKeyPoints(payload.KeyPoints),
                CoveredMessageCount = totalMessages,
                GeneratedAt = refreshStartedAt,
                SourceConversationUpdatedAt = conversation.UpdatedAt,
                IsIncremental = canUseExistingCoverage && state.LastCoveredMessageCount > 0
            };

            _db.ConversationSummarySnapshots.Add(snapshot);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            state.LatestSnapshotId = snapshot.Id;
            state.LatestSnapshot = snapshot;
            state.LatestSnapshotVersion = snapshot.SnapshotVersion;
            state.LastCoveredMessageCount = totalMessages;
            state.PendingMessageCount = 0;
            state.IsStale = false;
            state.LastRefreshedAt = refreshStartedAt;
            state.LastError = null;
            state.ConsecutiveFailureCount = 0;

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.Information(
                "Refreshed durable summary for conversation {ConversationId} with snapshot {SnapshotId} v{Version}",
                conversationId, snapshot.Id, snapshot.SnapshotVersion);

            return true;
        }
        catch (Exception ex)
        {
            state.IsStale = true;
            state.LastError = TrimForStorage(ex.Message, 500);
            state.ConsecutiveFailureCount += 1;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.Warning(ex, "Failed to refresh durable summary for conversation {ConversationId}", conversationId);
            return false;
        }
    }

    public async Task<int> RefreshStaleSummariesAsync(int maxConversations = 4, CancellationToken ct = default)
    {
        if (maxConversations <= 0 || !_aiService.IsConnected)
        {
            return 0;
        }

        var candidates = await (
            from conversation in _db.Conversations.AsNoTracking()
            join state in _db.ConversationSummaryStates.AsNoTracking()
                on conversation.Id equals state.ConversationId into stateGroup
            from state in stateGroup.DefaultIfEmpty()
            where conversation.MessageCount > 0
               && (state == null || state.LatestSnapshotId == null || state.IsStale)
            orderby state!.LastRefreshRequestedAt descending,
                conversation.UpdatedAt descending
            select conversation.Id)
            .Take(maxConversations)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var refreshedCount = 0;
        foreach (var conversationId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await RefreshConversationSummaryAsync(conversationId, ct).ConfigureAwait(false))
            {
                refreshedCount++;
            }
        }

        return refreshedCount;
    }

    private static string BuildSummarySystemPrompt()
    {
        return """
               You maintain durable summaries for chat conversations.

               Return a single valid JSON object only.
               Do not include markdown, commentary, or code fences.

               Required schema:
               {
                 "summary": "<2-4 sentence durable summary>",
                 "preview": "<single concise preview sentence>",
                 "keyPoints": ["<short point>", "<short point>"]
               }

               Rules:
               - Focus on durable goals, decisions, constraints, and active threads.
               - Omit greetings and filler.
               - Keep keyPoints to at most 5 items.
               - If the transcript changes prior conclusions, reflect the latest state.
               """;
    }

    private static string BuildSummaryPrompt(
        ConversationEntity conversation,
        ConversationSummarySnapshotEntity? latestSnapshot,
        string transcript,
        bool incremental)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"CONVERSATION TITLE: {conversation.Title}");
        builder.AppendLine($"MODEL: {conversation.ModelId}");
        builder.AppendLine($"UPDATED AT (UTC): {conversation.UpdatedAt:O}");
        builder.AppendLine();

        if (incremental && latestSnapshot is not null)
        {
            builder.AppendLine("EXISTING SUMMARY:");
            builder.AppendLine(latestSnapshot.SummaryText);
            builder.AppendLine();
            builder.AppendLine("NEW TRANSCRIPT TAIL:");
        }
        else
        {
            builder.AppendLine("FULL TRANSCRIPT:");
        }

        builder.AppendLine(transcript);
        builder.AppendLine();
        builder.AppendLine("Return the JSON summary object now.");
        return builder.ToString();
    }

    private static string BuildTranscript(IEnumerable<MessageEntity> messages, int maxChars)
    {
        var orderedMessages = messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        if (orderedMessages.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>(orderedMessages.Count);
        var totalChars = 0;

        for (var i = orderedMessages.Count - 1; i >= 0; i--)
        {
            var message = orderedMessages[i];
            var content = NormalizeText(message.Content);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var line = $"[{message.SortOrder}] {message.Role}: {content}";
            var projectedChars = totalChars + line.Length + Environment.NewLine.Length;
            if (lines.Count > 0 && projectedChars > maxChars)
            {
                break;
            }

            lines.Add(line);
            totalChars = projectedChars;
        }

        lines.Reverse();
        return string.Join(Environment.NewLine, lines);
    }

    private static ConversationSummaryPayload ParsePayload(string rawResponse)
    {
        var normalized = NormalizeJsonEnvelope(rawResponse);

        try
        {
            var parsed = JsonSerializer.Deserialize<ConversationSummaryPayload>(normalized, JsonOptions);
            if (parsed is not null)
            {
                return parsed with
                {
                    Summary = NormalizeText(parsed.Summary),
                    Preview = NormalizeText(parsed.Preview),
                    KeyPoints = NormalizeKeyPoints(parsed.KeyPoints)
                };
            }
        }
        catch (JsonException)
        {
            // Fall through to plain-text fallback.
        }

        return new ConversationSummaryPayload(
            NormalizeText(rawResponse),
            string.Empty,
            Array.Empty<string>());
    }

    private static IReadOnlyList<string> NormalizeKeyPoints(IReadOnlyList<string>? keyPoints)
    {
        if (keyPoints is null || keyPoints.Count == 0)
        {
            return Array.Empty<string>();
        }

        return keyPoints
            .Select(NormalizeText)
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(AppConstants.MaxConversationSummaryKeyPoints)
            .ToList();
    }

    private static IReadOnlyList<string> ParseKeyPoints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var points = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            return points is null
                ? Array.Empty<string>()
                : points.Where(point => !string.IsNullOrWhiteSpace(point)).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string SerializeKeyPoints(IReadOnlyList<string> keyPoints)
    {
        return JsonSerializer.Serialize(NormalizeKeyPoints(keyPoints), JsonOptions);
    }

    private static string BuildPreviewText(string preview, string summary)
    {
        var source = !string.IsNullOrWhiteSpace(preview)
            ? preview
            : summary;
        source = NormalizeText(source);

        if (source.Length <= AppConstants.MaxConversationSummaryPreviewChars)
        {
            return source;
        }

        return source[..AppConstants.MaxConversationSummaryPreviewChars].TrimEnd() + "...";
    }

    private static string NormalizeJsonEnvelope(string rawResponse)
    {
        var normalized = rawResponse.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = normalized.IndexOf('{');
            var lastBrace = normalized.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                normalized = normalized[firstBrace..(lastBrace + 1)];
            }
        }

        return normalized;
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim();
    }

    private static string TrimForStorage(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed record ConversationSummaryPayload(
        string Summary,
        string Preview,
        IReadOnlyList<string> KeyPoints)
    {
        public ConversationSummaryPayload() : this(string.Empty, string.Empty, Array.Empty<string>())
        {
        }
    }
}

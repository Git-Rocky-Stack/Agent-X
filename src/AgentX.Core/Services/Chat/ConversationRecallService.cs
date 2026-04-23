using System.Globalization;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Stores per-message embeddings for durable semantic recall and exposes a
/// bounded similarity search over historical conversation messages.
/// </summary>
public sealed class ConversationRecallService : IConversationRecallService
{
    private const int MaxPreviewChars = 220;
    private const int MaxEmbeddingSourceChars = 4000;

    private readonly AgentXDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger _logger;

    public ConversationRecallService(
        AgentXDbContext db,
        IEmbeddingService embeddingService,
        ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger?.ForContext<ConversationRecallService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> RefreshMessageEmbeddingAsync(
        long messageId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId, ct)
            .ConfigureAwait(false);
        if (message is null || !IsEligibleForRecall(message))
        {
            return false;
        }

        if (!forceRefresh && !string.IsNullOrWhiteSpace(message.Embedding))
        {
            return false;
        }

        try
        {
            var embedding = await _embeddingService
                .EmbedAsync(NormalizeEmbeddingSource(message.Content), ct)
                .ConfigureAwait(false);

            message.Embedding = SerializeEmbedding(embedding);
            message.EmbeddingModel = _embeddingService.ModelName;
            message.EmbeddedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to refresh message embedding for message {MessageId}", messageId);
            return false;
        }
    }

    public async Task<int> RefreshConversationEmbeddingsAsync(
        long conversationId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var messages = await _db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var targets = messages
            .Where(IsEligibleForRecall)
            .Where(m => forceRefresh || string.IsNullOrWhiteSpace(m.Embedding))
            .ToList();

        if (targets.Count == 0)
        {
            return 0;
        }

        try
        {
            var normalizedTexts = targets
                .Select(m => NormalizeEmbeddingSource(m.Content))
                .ToList();

            var embeddings = await _embeddingService
                .EmbedBatchAsync(normalizedTexts, ct)
                .ConfigureAwait(false);

            var embeddedAt = DateTime.UtcNow;
            var refreshCount = Math.Min(targets.Count, embeddings.Count);

            for (var i = 0; i < refreshCount; i++)
            {
                targets[i].Embedding = SerializeEmbedding(embeddings[i]);
                targets[i].EmbeddingModel = _embeddingService.ModelName;
                targets[i].EmbeddedAt = embeddedAt;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return refreshCount;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to refresh conversation embeddings for conversation {ConversationId}", conversationId);
            return 0;
        }
    }

    public async Task<int> RefreshRecentConversationEmbeddingsAsync(
        int maxConversations = 4,
        CancellationToken ct = default)
    {
        var normalizedLimit = Math.Max(1, maxConversations);

        var conversationIds = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Messages.Any(m =>
                (m.Role == "user" || m.Role == "assistant")
                && m.Content != string.Empty
                && m.Embedding == null))
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.Id)
            .Take(normalizedLimit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var refreshCount = 0;
        foreach (var conversationId in conversationIds)
        {
            refreshCount += await RefreshConversationEmbeddingsAsync(conversationId, ct: ct).ConfigureAwait(false);
        }

        return refreshCount;
    }

    public async Task<IReadOnlyList<ConversationRecallResult>> SearchRelevantMessagesAsync(
        string query,
        int maxResults = 6,
        float minSimilarity = 0.65f,
        long? excludeConversationId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ConversationRecallResult>();
        }

        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embeddingService
                .EmbedAsync(NormalizeEmbeddingSource(query), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to embed recall query");
            return Array.Empty<ConversationRecallResult>();
        }

        var candidates = await _db.Messages
            .AsNoTracking()
            .Where(m => m.Embedding != null
                        && (m.Role == "user" || m.Role == "assistant")
                        && (!excludeConversationId.HasValue || m.ConversationId != excludeConversationId.Value))
            .Select(m => new
            {
                m.Id,
                m.ConversationId,
                ConversationTitle = m.Conversation.Title,
                m.Role,
                m.Content,
                m.Timestamp,
                m.SortOrder,
                m.EmbeddedAt,
                m.Embedding
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return Array.Empty<ConversationRecallResult>();
        }

        var results = new List<ConversationRecallResult>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Embedding)
                || !TryParseEmbedding(candidate.Embedding, out var messageEmbedding))
            {
                continue;
            }

            var similarity = CosineSimilarity(queryEmbedding, messageEmbedding);
            if (similarity < minSimilarity)
            {
                continue;
            }

            results.Add(new ConversationRecallResult
            {
                MessageId = candidate.Id,
                ConversationId = candidate.ConversationId,
                ConversationTitle = string.IsNullOrWhiteSpace(candidate.ConversationTitle)
                    ? "Untitled conversation"
                    : candidate.ConversationTitle,
                Role = candidate.Role,
                ContentPreview = BuildPreview(candidate.Content),
                Timestamp = candidate.Timestamp,
                SortOrder = candidate.SortOrder,
                Similarity = similarity,
                EmbeddedAt = candidate.EmbeddedAt
            });
        }

        return results
            .OrderByDescending(result => result.Similarity)
            .ThenByDescending(result => result.Timestamp)
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    private static bool IsEligibleForRecall(MessageEntity message)
    {
        return (message.Role == "user" || message.Role == "assistant")
            && !string.IsNullOrWhiteSpace(message.Content);
    }

    private static string NormalizeEmbeddingSource(string content)
    {
        var normalized = string.Join(" ", content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= MaxEmbeddingSourceChars
            ? normalized
            : normalized[..MaxEmbeddingSourceChars];
    }

    private static string BuildPreview(string content)
    {
        var normalized = NormalizeEmbeddingSource(content);
        return normalized.Length <= MaxPreviewChars
            ? normalized
            : normalized[..MaxPreviewChars].TrimEnd() + "...";
    }

    private static string SerializeEmbedding(IReadOnlyList<float> embedding)
    {
        return string.Join(",", embedding.Select(value => value.ToString("F6", CultureInfo.InvariantCulture)));
    }

    private static bool TryParseEmbedding(string embeddingStr, out float[] embedding)
    {
        embedding = Array.Empty<float>();
        if (string.IsNullOrWhiteSpace(embeddingStr))
        {
            return false;
        }

        var parts = embeddingStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var values = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                embedding = Array.Empty<float>();
                return false;
            }
        }

        embedding = values;
        return true;
    }

    private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || left.Count != right.Count)
        {
            return 0f;
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;

        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude <= 0 || rightMagnitude <= 0)
        {
            return 0f;
        }

        return (float)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
    }
}

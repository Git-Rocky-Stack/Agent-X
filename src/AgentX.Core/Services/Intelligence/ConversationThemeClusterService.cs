using System.Globalization;
using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Builds durable conversation theme clusters from the latest summary snapshot
/// of each conversation. The first pass keeps assignment deterministic and
/// heuristic so Analytics can surface real persisted themes without requiring
/// a heavier background job pipeline.
/// </summary>
public sealed class ConversationThemeClusterService : IConversationThemeClusterService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const float ClusterSimilarityThreshold = 0.75f;
    private const int MaxEmbeddingSourceChars = 4000;
    private const int MaxLabelChars = 72;
    private const int MaxPreviewChars = 180;
    private const int MaxKeyPoints = 4;

    private readonly AgentXDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly IConversationThemeTrendService? _trendService;
    private readonly ILogger _logger;

    public ConversationThemeClusterService(
        AgentXDbContext db,
        IEmbeddingService embeddingService,
        ILogger logger,
        IConversationThemeTrendService? trendService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _trendService = trendService;
        _logger = logger?.ForContext<ConversationThemeClusterService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> MaterializeConversationThemeAsync(
        long conversationId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var state = await _db.ConversationSummaryStates
            .Include(item => item.Conversation)
            .Include(item => item.LatestSnapshot)
            .FirstOrDefaultAsync(item => item.ConversationId == conversationId, ct)
            .ConfigureAwait(false);

        var latestSnapshot = state?.LatestSnapshot;
        var conversation = state?.Conversation;
        if (latestSnapshot is null || conversation is null)
        {
            return false;
        }

        var existingMembership = await _db.ConversationThemeMemberships
            .FirstOrDefaultAsync(item => item.ConversationId == conversationId, ct)
            .ConfigureAwait(false);

        if (!forceRefresh
            && existingMembership is not null
            && existingMembership.SnapshotId == latestSnapshot.Id)
        {
            return false;
        }

        if (!await EnsureSnapshotEmbeddingAsync(latestSnapshot, forceRefresh, ct).ConfigureAwait(false))
        {
            return false;
        }

        if (!TryParseEmbedding(latestSnapshot.Embedding, out var targetEmbedding))
        {
            return false;
        }

        var previousClusterId = existingMembership?.ClusterId;
        var materializedAt = DateTime.UtcNow;

        var candidateMemberships = await _db.ConversationThemeMemberships
            .AsNoTracking()
            .Include(item => item.Snapshot)
            .Where(item => item.ConversationId != conversationId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        long? bestClusterId = null;
        var bestSimilarity = 0f;

        foreach (var clusterGroup in candidateMemberships.GroupBy(item => item.ClusterId))
        {
            var centroid = ComputeCentroid(clusterGroup
                .Select(item => item.Snapshot.Embedding)
                .Where(embedding => TryParseEmbedding(embedding, out _))
                .Select(embedding =>
                {
                    TryParseEmbedding(embedding, out var parsed);
                    return parsed;
                })
                .Where(parsed => parsed.Length > 0)
                .ToList());

            if (centroid.Length == 0)
            {
                continue;
            }

            var similarity = CosineSimilarity(targetEmbedding, centroid);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestClusterId = clusterGroup.Key;
            }
        }

        ConversationThemeClusterEntity targetCluster;
        if (!bestClusterId.HasValue || bestSimilarity < ClusterSimilarityThreshold)
        {
            targetCluster = new ConversationThemeClusterEntity
            {
                Label = "Emerging theme",
                PreviewText = "Theme materialization in progress.",
                KeyPointsJson = "[]",
                FirstSeenAt = latestSnapshot.GeneratedAt,
                LastActiveAt = conversation.UpdatedAt,
                MaterializedAt = materializedAt
            };
            _db.ConversationThemeClusters.Add(targetCluster);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            bestSimilarity = 1f;
        }
        else
        {
            targetCluster = await _db.ConversationThemeClusters
                .FirstAsync(item => item.Id == bestClusterId.Value, ct)
                .ConfigureAwait(false);
        }

        if (existingMembership is null)
        {
            existingMembership = new ConversationThemeMembershipEntity
            {
                ConversationId = conversationId
            };
            _db.ConversationThemeMemberships.Add(existingMembership);
        }

        existingMembership.SnapshotId = latestSnapshot.Id;
        existingMembership.ClusterId = targetCluster.Id;
        existingMembership.SimilarityScore = bestSimilarity;
        existingMembership.AssignedAt = materializedAt;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var affectedClusterIds = new HashSet<long> { targetCluster.Id };
        if (previousClusterId.HasValue)
        {
            affectedClusterIds.Add(previousClusterId.Value);
        }

        foreach (var clusterId in affectedClusterIds)
        {
            await RecomputeClusterAsync(clusterId, ct).ConfigureAwait(false);
            await RefreshClusterTrendAsync(clusterId, ct).ConfigureAwait(false);
        }

        _logger.Information(
            "Materialized conversation theme for conversation {ConversationId} into cluster {ClusterId}",
            conversationId,
            targetCluster.Id);

        return true;
    }

    private async Task RefreshClusterTrendAsync(long clusterId, CancellationToken ct)
    {
        if (_trendService is null)
        {
            return;
        }

        try
        {
            await _trendService
                .RefreshClusterTrendWindowAsync(clusterId, ct: ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to refresh durable theme trends for cluster {ClusterId}",
                clusterId);
        }
    }

    public async Task<int> RefreshStaleClustersAsync(
        int maxConversations = 4,
        CancellationToken ct = default)
    {
        if (maxConversations <= 0)
        {
            return 0;
        }

        var candidates = await (
            from state in _db.ConversationSummaryStates.AsNoTracking()
            join conversation in _db.Conversations.AsNoTracking()
                on state.ConversationId equals conversation.Id
            join membership in _db.ConversationThemeMemberships.AsNoTracking()
                on state.ConversationId equals membership.ConversationId into membershipGroup
            from membership in membershipGroup.DefaultIfEmpty()
            where state.LatestSnapshotId != null
               && (membership == null || membership.SnapshotId != state.LatestSnapshotId.Value)
            orderby conversation.UpdatedAt descending
            select state.ConversationId)
            .Take(maxConversations)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var refreshed = 0;
        foreach (var conversationId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await MaterializeConversationThemeAsync(conversationId, ct: ct).ConfigureAwait(false))
            {
                refreshed++;
            }
        }

        return refreshed;
    }

    private async Task<bool> EnsureSnapshotEmbeddingAsync(
        ConversationSummarySnapshotEntity snapshot,
        bool forceRefresh,
        CancellationToken ct)
    {
        if (!forceRefresh && !string.IsNullOrWhiteSpace(snapshot.Embedding))
        {
            return true;
        }

        try
        {
            var embedding = await _embeddingService
                .EmbedAsync(BuildEmbeddingSource(snapshot), ct)
                .ConfigureAwait(false);

            snapshot.Embedding = SerializeEmbedding(embedding);
            snapshot.EmbeddingModel = _embeddingService.ModelName;
            snapshot.EmbeddedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to embed durable summary snapshot {SnapshotId} for theme clustering",
                snapshot.Id);
            return false;
        }
    }

    private async Task RecomputeClusterAsync(long clusterId, CancellationToken ct)
    {
        var cluster = await _db.ConversationThemeClusters
            .FirstOrDefaultAsync(item => item.Id == clusterId, ct)
            .ConfigureAwait(false);
        if (cluster is null)
        {
            return;
        }

        var members = await _db.ConversationThemeMemberships
            .Include(item => item.Conversation)
            .Include(item => item.Snapshot)
            .Where(item => item.ClusterId == clusterId)
            .OrderByDescending(item => item.Conversation.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (members.Count == 0)
        {
            _db.ConversationThemeClusters.Remove(cluster);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var keyPoints = members
            .SelectMany(item => ParseKeyPoints(item.Snapshot.KeyPointsJson))
            .Select(NormalizePhrase)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Length)
            .Select(group => group.First())
            .Take(MaxKeyPoints)
            .ToList();

        var mostRecentPreview = members
            .Select(item => NormalizePhrase(item.Snapshot.PreviewText))
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

        var fallbackLabel = keyPoints.FirstOrDefault()
            ?? mostRecentPreview
            ?? members.Select(item => NormalizePhrase(item.Conversation.Title))
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? "Emerging theme";

        var now = DateTime.UtcNow;
        cluster.Label = TrimForDisplay(fallbackLabel, MaxLabelChars);
        cluster.PreviewText = TrimForDisplay(
            mostRecentPreview ?? $"Theme spanning {members.Count} related conversation{(members.Count == 1 ? string.Empty : "s")}.",
            MaxPreviewChars);
        cluster.KeyPointsJson = JsonSerializer.Serialize(keyPoints, JsonOptions);
        cluster.ConversationCount = members.Count;
        cluster.ActiveConversationCount7d = members.Count(item => item.Conversation.UpdatedAt >= now.AddDays(-7));
        cluster.ActiveConversationCount30d = members.Count(item => item.Conversation.UpdatedAt >= now.AddDays(-30));
        cluster.FirstSeenAt = members.Min(item => item.Conversation.CreatedAt);
        cluster.LastActiveAt = members.Max(item => item.Conversation.UpdatedAt);
        cluster.MaterializedAt = now;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string BuildEmbeddingSource(ConversationSummarySnapshotEntity snapshot)
    {
        var keyPoints = string.Join(" ", ParseKeyPoints(snapshot.KeyPointsJson));
        var combined = string.Join(
            Environment.NewLine,
            new[]
            {
                NormalizePhrase(snapshot.SummaryText),
                NormalizePhrase(snapshot.PreviewText),
                NormalizePhrase(keyPoints)
            }.Where(item => !string.IsNullOrWhiteSpace(item)));

        return combined.Length <= MaxEmbeddingSourceChars
            ? combined
            : combined[..MaxEmbeddingSourceChars];
    }

    private static List<string> ParseKeyPoints(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            return parsed?
                .Select(NormalizePhrase)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizePhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string TrimForDisplay(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars].TrimEnd(' ', '.', ',', ';', ':') + "...";
    }

    private static string SerializeEmbedding(IReadOnlyList<float> embedding)
    {
        return string.Join(",", embedding.Select(value => value.ToString("F6", CultureInfo.InvariantCulture)));
    }

    private static bool TryParseEmbedding(string? embeddingStr, out float[] embedding)
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

    private static float[] ComputeCentroid(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0)
        {
            return [];
        }

        var dimensions = embeddings[0].Length;
        if (dimensions == 0 || embeddings.Any(embedding => embedding.Length != dimensions))
        {
            return [];
        }

        var centroid = new float[dimensions];
        foreach (var embedding in embeddings)
        {
            for (var i = 0; i < dimensions; i++)
            {
                centroid[i] += embedding[i];
            }
        }

        for (var i = 0; i < dimensions; i++)
        {
            centroid[i] /= embeddings.Count;
        }

        return centroid;
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

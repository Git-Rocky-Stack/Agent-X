using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Persists a bounded current-state-oriented daily activity window for each
/// durable conversation theme cluster. This keeps trend reads cheap and stable
/// without requiring a separate event ledger in the first pass.
/// </summary>
public sealed class ConversationThemeTrendService : IConversationThemeTrendService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _logger;

    public ConversationThemeTrendService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger?.ForContext<ConversationThemeTrendService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> RefreshClusterTrendWindowAsync(
        long clusterId,
        int days = 30,
        CancellationToken ct = default)
    {
        if (clusterId <= 0 || days <= 0)
        {
            return 0;
        }

        var clusterExists = await _db.ConversationThemeClusters
            .AsNoTracking()
            .AnyAsync(cluster => cluster.Id == clusterId, ct)
            .ConfigureAwait(false);
        if (!clusterExists)
        {
            return 0;
        }

        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-(days - 1));
        var windowEndExclusive = today.AddDays(1);

        var members = await _db.ConversationThemeMemberships
            .AsNoTracking()
            .Where(membership => membership.ClusterId == clusterId)
            .Select(membership => new
            {
                membership.AssignedAt,
                ConversationUpdatedAt = membership.Conversation.UpdatedAt,
                SnapshotGeneratedAt = membership.Snapshot.GeneratedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var activeByDate = CountByDate(
            members.Select(member => member.ConversationUpdatedAt),
            windowStart,
            windowEndExclusive);
        var newByDate = CountByDate(
            members.Select(member => member.AssignedAt),
            windowStart,
            windowEndExclusive);
        var snapshotByDate = CountByDate(
            members.Select(member => member.SnapshotGeneratedAt),
            windowStart,
            windowEndExclusive);

        var existingRows = await _db.ConversationThemeDailyMetrics
            .Where(metric => metric.ClusterId == clusterId
                          && metric.Date >= windowStart
                          && metric.Date < windowEndExclusive)
            .ToDictionaryAsync(metric => metric.Date.Date, ct)
            .ConfigureAwait(false);

        var materializedAt = DateTime.UtcNow;
        for (var offset = 0; offset < days; offset++)
        {
            var date = windowStart.AddDays(offset);
            if (!existingRows.TryGetValue(date, out var metric))
            {
                metric = new ConversationThemeDailyMetricEntity
                {
                    ClusterId = clusterId,
                    Date = date
                };
                _db.ConversationThemeDailyMetrics.Add(metric);
            }

            metric.ActiveConversationCount = activeByDate.GetValueOrDefault(date, 0);
            metric.NewConversationCount = newByDate.GetValueOrDefault(date, 0);
            metric.SnapshotRefreshCount = snapshotByDate.GetValueOrDefault(date, 0);
            metric.MaterializedAt = materializedAt;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return days;
    }

    public async Task<int> RefreshRecentClusterTrendsAsync(
        int maxClusters = 4,
        int days = 30,
        CancellationToken ct = default)
    {
        if (maxClusters <= 0 || days <= 0)
        {
            return 0;
        }

        var today = DateTime.UtcNow.Date;

        var clusterIds = await (
            from cluster in _db.ConversationThemeClusters.AsNoTracking()
            join metric in _db.ConversationThemeDailyMetrics.AsNoTracking()
                    .Where(item => item.Date == today)
                on cluster.Id equals metric.ClusterId into metricGroup
            from metric in metricGroup.DefaultIfEmpty()
            where metric == null || metric.MaterializedAt < cluster.MaterializedAt
            orderby cluster.MaterializedAt descending
            select cluster.Id)
            .Take(maxClusters)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var refreshed = 0;
        foreach (var clusterId in clusterIds)
        {
            ct.ThrowIfCancellationRequested();

            if (await RefreshClusterTrendWindowAsync(clusterId, days, ct).ConfigureAwait(false) > 0)
            {
                refreshed++;
            }
        }

        _logger.Debug(
            "Refreshed durable theme trends for {ClusterCount} recent clusters",
            refreshed);

        return refreshed;
    }

    private static Dictionary<DateTime, int> CountByDate(
        IEnumerable<DateTime> timestamps,
        DateTime windowStart,
        DateTime windowEndExclusive)
    {
        return timestamps
            .Select(timestamp => timestamp.Date)
            .Where(date => date >= windowStart && date < windowEndExclusive)
            .GroupBy(date => date)
            .ToDictionary(group => group.Key, group => group.Count());
    }
}

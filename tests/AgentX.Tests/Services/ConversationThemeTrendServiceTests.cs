using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class ConversationThemeTrendServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<ConversationThemeTrendServiceTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task RefreshClusterTrendWindowAsync_upserts_trailing_window_and_stays_idempotent()
    {
        using var db = _dbFactory.CreateContext();
        var today = DateTime.UtcNow.Date;
        var materializedAt = DateTime.UtcNow.AddMinutes(-30);

        var cluster = new ConversationThemeClusterEntity
        {
            Label = "Analytics dashboard",
            PreviewText = "Durable analytics work",
            KeyPointsJson = """["Analytics dashboard"]""",
            ConversationCount = 2,
            ActiveConversationCount7d = 2,
            ActiveConversationCount30d = 2,
            FirstSeenAt = today.AddDays(-4),
            LastActiveAt = today.AddHours(12),
            MaterializedAt = materializedAt
        };
        db.ConversationThemeClusters.Add(cluster);
        await db.SaveChangesAsync();

        var alpha = await SeedConversationAsync(db, "Analytics roadmap", today.AddHours(11));
        var beta = await SeedConversationAsync(db, "Recall health", today.AddDays(-1).AddHours(10));

        var alphaSnapshot = await SeedSnapshotAsync(db, alpha.Id, today.AddHours(9));
        var betaSnapshot = await SeedSnapshotAsync(db, beta.Id, today.AddDays(-2).AddHours(9));

        db.ConversationThemeMemberships.AddRange(
            new ConversationThemeMembershipEntity
            {
                ConversationId = alpha.Id,
                SnapshotId = alphaSnapshot.Id,
                ClusterId = cluster.Id,
                SimilarityScore = 0.92f,
                AssignedAt = today.AddHours(8)
            },
            new ConversationThemeMembershipEntity
            {
                ConversationId = beta.Id,
                SnapshotId = betaSnapshot.Id,
                ClusterId = cluster.Id,
                SimilarityScore = 0.9f,
                AssignedAt = today.AddDays(-1).AddHours(8)
            });
        await db.SaveChangesAsync();

        var sut = new ConversationThemeTrendService(db, _logger);

        (await sut.RefreshClusterTrendWindowAsync(cluster.Id, days: 30)).Should().Be(30);
        (await sut.RefreshClusterTrendWindowAsync(cluster.Id, days: 30)).Should().Be(30);

        var rows = await db.ConversationThemeDailyMetrics
            .AsNoTracking()
            .Where(metric => metric.ClusterId == cluster.Id)
            .OrderBy(metric => metric.Date)
            .ToListAsync();

        rows.Should().HaveCount(30);
        rows.Select(metric => metric.Date).Should().OnlyHaveUniqueItems();
        rows.Single(metric => metric.Date == today).ActiveConversationCount.Should().Be(1);
        rows.Single(metric => metric.Date == today).NewConversationCount.Should().Be(1);
        rows.Single(metric => metric.Date == today).SnapshotRefreshCount.Should().Be(1);
        rows.Single(metric => metric.Date == today.AddDays(-1)).ActiveConversationCount.Should().Be(1);
        rows.Single(metric => metric.Date == today.AddDays(-1)).NewConversationCount.Should().Be(1);
        rows.Single(metric => metric.Date == today.AddDays(-2)).SnapshotRefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshRecentClusterTrendsAsync_refreshes_only_missing_or_stale_clusters()
    {
        using var db = _dbFactory.CreateContext();
        var today = DateTime.UtcNow.Date;
        var now = DateTime.UtcNow;

        var alphaCluster = await SeedClusterAsync(db, "Analytics dashboard", now.AddMinutes(-30));
        var betaCluster = await SeedClusterAsync(db, "Recall health", now.AddMinutes(-40));
        var gammaCluster = await SeedClusterAsync(db, "Sync scope", now.AddMinutes(-50));

        await SeedMembershipAsync(db, alphaCluster.Id, "Analytics roadmap", today.AddHours(11), today.AddHours(9), today.AddHours(8));
        await SeedMembershipAsync(db, betaCluster.Id, "Recall health", today.AddDays(-1).AddHours(11), today.AddDays(-1).AddHours(9), today.AddDays(-1).AddHours(8));
        await SeedMembershipAsync(db, gammaCluster.Id, "Sync cleanup", today.AddDays(-2).AddHours(11), today.AddDays(-2).AddHours(9), today.AddDays(-2).AddHours(8));

        db.ConversationThemeDailyMetrics.AddRange(
            new ConversationThemeDailyMetricEntity
            {
                ClusterId = betaCluster.Id,
                Date = today,
                ActiveConversationCount = 0,
                NewConversationCount = 0,
                SnapshotRefreshCount = 0,
                MaterializedAt = betaCluster.MaterializedAt.AddMinutes(-10)
            },
            new ConversationThemeDailyMetricEntity
            {
                ClusterId = gammaCluster.Id,
                Date = today,
                ActiveConversationCount = 1,
                NewConversationCount = 0,
                SnapshotRefreshCount = 0,
                MaterializedAt = gammaCluster.MaterializedAt.AddMinutes(10)
            });
        await db.SaveChangesAsync();

        var sut = new ConversationThemeTrendService(db, _logger);

        var refreshed = await sut.RefreshRecentClusterTrendsAsync(maxClusters: 5, days: 7);

        refreshed.Should().Be(2);

        var todayRows = await db.ConversationThemeDailyMetrics
            .AsNoTracking()
            .Where(metric => metric.Date == today)
            .OrderBy(metric => metric.ClusterId)
            .ToListAsync();

        todayRows.Should().HaveCount(3);
        todayRows.Single(metric => metric.ClusterId == alphaCluster.Id).MaterializedAt.Should().BeAfter(alphaCluster.MaterializedAt);
        todayRows.Single(metric => metric.ClusterId == betaCluster.Id).MaterializedAt.Should().BeAfter(betaCluster.MaterializedAt);
        todayRows.Single(metric => metric.ClusterId == gammaCluster.Id).MaterializedAt.Should().Be(gammaCluster.MaterializedAt.AddMinutes(10));
    }

    private static async Task<ConversationThemeClusterEntity> SeedClusterAsync(
        AgentX.Core.Data.AgentXDbContext db,
        string label,
        DateTime materializedAt)
    {
        var cluster = new ConversationThemeClusterEntity
        {
            Label = label,
            PreviewText = label,
            KeyPointsJson = $"[\"{label}\"]",
            ConversationCount = 1,
            ActiveConversationCount7d = 1,
            ActiveConversationCount30d = 1,
            FirstSeenAt = materializedAt.AddDays(-2),
            LastActiveAt = materializedAt.AddHours(-1),
            MaterializedAt = materializedAt
        };

        db.ConversationThemeClusters.Add(cluster);
        await db.SaveChangesAsync();
        return cluster;
    }

    private static async Task SeedMembershipAsync(
        AgentX.Core.Data.AgentXDbContext db,
        long clusterId,
        string title,
        DateTime updatedAt,
        DateTime snapshotGeneratedAt,
        DateTime assignedAt)
    {
        var conversation = await SeedConversationAsync(db, title, updatedAt);
        var snapshot = await SeedSnapshotAsync(db, conversation.Id, snapshotGeneratedAt);

        db.ConversationThemeMemberships.Add(new ConversationThemeMembershipEntity
        {
            ConversationId = conversation.Id,
            SnapshotId = snapshot.Id,
            ClusterId = clusterId,
            SimilarityScore = 0.9f,
            AssignedAt = assignedAt
        });
        await db.SaveChangesAsync();
    }

    private static async Task<ConversationEntity> SeedConversationAsync(
        AgentX.Core.Data.AgentXDbContext db,
        string title,
        DateTime updatedAt)
    {
        var conversation = new ConversationEntity
        {
            Title = title,
            ModelId = "llama3.1:8b",
            CreatedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt,
            MessageCount = 4,
            TokensUsed = 120
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static async Task<ConversationSummarySnapshotEntity> SeedSnapshotAsync(
        AgentX.Core.Data.AgentXDbContext db,
        long conversationId,
        DateTime generatedAt)
    {
        var snapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversationId,
            SnapshotVersion = 1,
            SummaryText = "Theme summary",
            PreviewText = "Theme preview",
            KeyPointsJson = """["Theme"]""",
            CoveredMessageCount = 4,
            GeneratedAt = generatedAt,
            SourceConversationUpdatedAt = generatedAt,
            IsIncremental = false
        };

        db.ConversationSummarySnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }
}

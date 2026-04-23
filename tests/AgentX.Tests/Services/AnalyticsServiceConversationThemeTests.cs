using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Analytics;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class AnalyticsServiceConversationThemeTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsServiceConversationThemeTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task GetConversationThemeOverviewAsync_returns_materialized_cluster_metrics()
    {
        using var db = _dbFactory.CreateContext();

        var alpha = await SeedConversationAsync(db, "Analytics roadmap", new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc));
        var beta = await SeedConversationAsync(db, "Recall health", new DateTime(2026, 4, 23, 11, 0, 0, DateTimeKind.Utc));
        var gamma = await SeedConversationAsync(db, "Sync cleanup", new DateTime(2026, 4, 12, 9, 0, 0, DateTimeKind.Utc));

        var alphaSnapshot = await SeedSnapshotAsync(db, alpha.Id, 1, "Analytics summary", "Analytics preview", """["Analytics dashboard","Recall health"]""");
        var betaSnapshot = await SeedSnapshotAsync(db, beta.Id, 1, "Recall summary", "Recall preview", """["Recall health"]""");
        var gammaSnapshot = await SeedSnapshotAsync(db, gamma.Id, 1, "Sync summary", "Sync preview", """["Sync scope"]""");

        var analyticsCluster = new ConversationThemeClusterEntity
        {
            Label = "Analytics dashboard",
            PreviewText = "Analytics and recall themes are converging.",
            KeyPointsJson = """["Analytics dashboard","Recall health"]""",
            ConversationCount = 2,
            ActiveConversationCount7d = 2,
            ActiveConversationCount30d = 2,
            FirstSeenAt = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
            LastActiveAt = beta.UpdatedAt,
            MaterializedAt = new DateTime(2026, 4, 23, 11, 30, 0, DateTimeKind.Utc)
        };

        var syncCluster = new ConversationThemeClusterEntity
        {
            Label = "Sync scope",
            PreviewText = "Sync and collection cleanup work.",
            KeyPointsJson = """["Sync scope"]""",
            ConversationCount = 1,
            ActiveConversationCount7d = 0,
            ActiveConversationCount30d = 1,
            FirstSeenAt = new DateTime(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc),
            LastActiveAt = gamma.UpdatedAt,
            MaterializedAt = new DateTime(2026, 4, 21, 11, 30, 0, DateTimeKind.Utc)
        };

        db.ConversationThemeClusters.AddRange(analyticsCluster, syncCluster);
        await db.SaveChangesAsync();

        db.ConversationThemeMemberships.AddRange(
            new ConversationThemeMembershipEntity
            {
                ConversationId = alpha.Id,
                SnapshotId = alphaSnapshot.Id,
                ClusterId = analyticsCluster.Id,
                SimilarityScore = 0.92f,
                AssignedAt = new DateTime(2026, 4, 23, 10, 5, 0, DateTimeKind.Utc)
            },
            new ConversationThemeMembershipEntity
            {
                ConversationId = beta.Id,
                SnapshotId = betaSnapshot.Id,
                ClusterId = analyticsCluster.Id,
                SimilarityScore = 0.9f,
                AssignedAt = new DateTime(2026, 4, 23, 11, 5, 0, DateTimeKind.Utc)
            },
            new ConversationThemeMembershipEntity
            {
                ConversationId = gamma.Id,
                SnapshotId = gammaSnapshot.Id,
                ClusterId = syncCluster.Id,
                SimilarityScore = 0.88f,
                AssignedAt = new DateTime(2026, 4, 21, 11, 5, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var sut = new AnalyticsService(db, _logger);

        var overview = await sut.GetConversationThemeOverviewAsync(maxClusters: 5);

        overview.ActiveThemeClusters.Should().Be(2);
        overview.ClusteredConversations.Should().Be(3);
        overview.NewThemes7d.Should().Be(2);
        overview.LastMaterializedAt.Should().Be(analyticsCluster.MaterializedAt);
        overview.Clusters.Should().HaveCount(2);

        var topCluster = overview.Clusters[0];
        topCluster.Label.Should().Be("Analytics dashboard");
        topCluster.ConversationCount.Should().Be(2);
        topCluster.ActiveConversationCount7d.Should().Be(2);
        topCluster.KeyPoints.Should().Contain("Recall health");
        topCluster.RecentConversationTitles.Should().ContainInOrder("Recall health", "Analytics roadmap");
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
            CreatedAt = updatedAt.AddHours(-1),
            UpdatedAt = updatedAt,
            MessageCount = 4,
            TokensUsed = 150
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static async Task<ConversationSummarySnapshotEntity> SeedSnapshotAsync(
        AgentX.Core.Data.AgentXDbContext db,
        long conversationId,
        int version,
        string summary,
        string preview,
        string keyPointsJson)
    {
        var snapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversationId,
            SnapshotVersion = version,
            SummaryText = summary,
            PreviewText = preview,
            KeyPointsJson = keyPointsJson,
            CoveredMessageCount = 4,
            GeneratedAt = new DateTime(2026, 4, 23, 9, version, 0, DateTimeKind.Utc),
            SourceConversationUpdatedAt = new DateTime(2026, 4, 23, 9, version, 0, DateTimeKind.Utc),
            IsIncremental = false
        };

        db.ConversationSummarySnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }
}

using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class ConversationThemeClusterServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IEmbeddingService> _embeddingService = new();
    private readonly Mock<IConversationThemeTrendService> _trendService = new();
    private readonly ILogger _logger = Log.ForContext<ConversationThemeClusterServiceTests>();

    public ConversationThemeClusterServiceTests()
    {
        _embeddingService.SetupGet(service => service.ModelName).Returns("all-minilm");
        _trendService
            .Setup(service => service.RefreshClusterTrendWindowAsync(It.IsAny<long>(), 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task MaterializeConversationThemeAsync_groups_similar_snapshots_into_one_cluster()
    {
        using var db = _dbFactory.CreateContext();

        var analytics = await SeedConversationAsync(db, "Analytics rollout", new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc));
        var recall = await SeedConversationAsync(db, "Recall health", new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc));

        await SeedLatestSnapshotAsync(
            db,
            analytics,
            version: 1,
            summary: "Analytics dashboard planning focuses on recall health and durable intelligence.",
            preview: "Analytics and recall health planning.",
            keyPointsJson: """["Analytics dashboard","Recall health"]""");

        await SeedLatestSnapshotAsync(
            db,
            recall,
            version: 1,
            summary: "Recall health work is aligning the analytics dashboard with durable intelligence coverage.",
            preview: "Recall health and analytics alignment.",
            keyPointsJson: """["Recall health","Analytics dashboard"]""");

        _embeddingService
            .Setup(service => service.EmbedAsync(
                It.Is<string>(text => text.Contains("Analytics", StringComparison.OrdinalIgnoreCase)
                                   || text.Contains("Recall", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([1f, 0f, 0f]);

        var sut = new ConversationThemeClusterService(db, _embeddingService.Object, _logger, _trendService.Object);

        (await sut.MaterializeConversationThemeAsync(analytics.Id)).Should().BeTrue();
        (await sut.MaterializeConversationThemeAsync(recall.Id)).Should().BeTrue();

        var clusters = await db.ConversationThemeClusters
            .AsNoTracking()
            .OrderBy(cluster => cluster.Id)
            .ToListAsync();
        var memberships = await db.ConversationThemeMemberships
            .AsNoTracking()
            .OrderBy(item => item.ConversationId)
            .ToListAsync();

        clusters.Should().ContainSingle();
        memberships.Should().HaveCount(2);
        memberships.Select(item => item.ClusterId).Distinct().Should().ContainSingle();
        clusters[0].ConversationCount.Should().Be(2);
        clusters[0].ActiveConversationCount7d.Should().Be(2);
        clusters[0].ActiveConversationCount30d.Should().Be(2);
        clusters[0].Label.Should().NotBeNullOrWhiteSpace();
        clusters[0].PreviewText.Should().ContainEquivalentOf("analytics");
        _trendService.Verify(
            service => service.RefreshClusterTrendWindowAsync(clusters[0].Id, 30, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RefreshStaleClustersAsync_reassigns_membership_when_latest_snapshot_changes()
    {
        using var db = _dbFactory.CreateContext();

        var analytics = await SeedConversationAsync(db, "Analytics rollout", new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc));
        var sync = await SeedConversationAsync(db, "Sync collections", new DateTime(2026, 4, 23, 11, 0, 0, DateTimeKind.Utc));

        var analyticsSnapshotV1 = await SeedLatestSnapshotAsync(
            db,
            analytics,
            version: 1,
            summary: "Analytics dashboard planning focuses on durable recall coverage.",
            preview: "Analytics and recall coverage.",
            keyPointsJson: """["Analytics dashboard","Recall coverage"]""");

        await SeedLatestSnapshotAsync(
            db,
            sync,
            version: 1,
            summary: "Sync work is focused on collection scope and conflict cleanup.",
            preview: "Sync collection scope cleanup.",
            keyPointsJson: """["Sync scope","Collection picker"]""");

        _embeddingService
            .Setup(service => service.EmbedAsync(
                It.Is<string>(text => text.Contains("Analytics", StringComparison.OrdinalIgnoreCase)
                                   || text.Contains("Recall", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([1f, 0f, 0f]);
        _embeddingService
            .Setup(service => service.EmbedAsync(
                It.Is<string>(text => text.Contains("Sync", StringComparison.OrdinalIgnoreCase)
                                   || text.Contains("Collection", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([0f, 1f, 0f]);

        var sut = new ConversationThemeClusterService(db, _embeddingService.Object, _logger, _trendService.Object);

        await sut.MaterializeConversationThemeAsync(analytics.Id);
        await sut.MaterializeConversationThemeAsync(sync.Id);

        var originalMembership = await db.ConversationThemeMemberships
            .AsNoTracking()
            .SingleAsync(item => item.ConversationId == analytics.Id);
        var syncMembership = await db.ConversationThemeMemberships
            .AsNoTracking()
            .SingleAsync(item => item.ConversationId == sync.Id);

        var analyticsSnapshotV2 = await SeedSnapshotAsync(
            db,
            analytics.Id,
            version: 2,
            summary: "The conversation has shifted to sync scope and collection cleanup.",
            preview: "Sync scope cleanup.",
            keyPointsJson: """["Sync scope","Collection picker"]""",
            generatedAt: new DateTime(2026, 4, 23, 12, 0, 0, DateTimeKind.Utc));

        var analyticsState = await db.ConversationSummaryStates.SingleAsync(item => item.ConversationId == analytics.Id);
        analyticsState.LatestSnapshotId = analyticsSnapshotV2.Id;
        analyticsState.LatestSnapshotVersion = 2;
        analyticsState.LastCoveredMessageCount = 8;
        analyticsState.PendingMessageCount = 0;
        analyticsState.IsStale = false;
        await db.SaveChangesAsync();

        var refreshed = await sut.RefreshStaleClustersAsync(4);

        refreshed.Should().Be(1);

        var updatedMembership = await db.ConversationThemeMemberships
            .AsNoTracking()
            .SingleAsync(item => item.ConversationId == analytics.Id);
        updatedMembership.SnapshotId.Should().Be(analyticsSnapshotV2.Id);
        updatedMembership.ClusterId.Should().Be(syncMembership.ClusterId);
        updatedMembership.ClusterId.Should().NotBe(originalMembership.ClusterId);

        var clusters = await db.ConversationThemeClusters
            .AsNoTracking()
            .OrderBy(cluster => cluster.Id)
            .ToListAsync();
        clusters.Should().ContainSingle();
        clusters[0].ConversationCount.Should().Be(2);
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
            CreatedAt = updatedAt.AddHours(-2),
            UpdatedAt = updatedAt,
            MessageCount = 6,
            TokensUsed = 240
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static async Task<ConversationSummarySnapshotEntity> SeedLatestSnapshotAsync(
        AgentX.Core.Data.AgentXDbContext db,
        ConversationEntity conversation,
        int version,
        string summary,
        string preview,
        string keyPointsJson)
    {
        var snapshot = await SeedSnapshotAsync(
            db,
            conversation.Id,
            version,
            summary,
            preview,
            keyPointsJson,
            conversation.UpdatedAt);

        var state = await db.ConversationSummaryStates.SingleOrDefaultAsync(item => item.ConversationId == conversation.Id);
        if (state is null)
        {
            state = new ConversationSummaryStateEntity
            {
                ConversationId = conversation.Id
            };
            db.ConversationSummaryStates.Add(state);
        }

        state.LatestSnapshotId = snapshot.Id;
        state.LatestSnapshotVersion = version;
        state.LastCoveredMessageCount = conversation.MessageCount;
        state.PendingMessageCount = 0;
        state.IsStale = false;
        state.LastRefreshedAt = snapshot.GeneratedAt;
        await db.SaveChangesAsync();

        return snapshot;
    }

    private static async Task<ConversationSummarySnapshotEntity> SeedSnapshotAsync(
        AgentX.Core.Data.AgentXDbContext db,
        long conversationId,
        int version,
        string summary,
        string preview,
        string keyPointsJson,
        DateTime generatedAt)
    {
        var snapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversationId,
            SnapshotVersion = version,
            SummaryText = summary,
            PreviewText = preview,
            KeyPointsJson = keyPointsJson,
            CoveredMessageCount = 6 + version,
            GeneratedAt = generatedAt,
            SourceConversationUpdatedAt = generatedAt,
            IsIncremental = version > 1
        };

        db.ConversationSummarySnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }
}

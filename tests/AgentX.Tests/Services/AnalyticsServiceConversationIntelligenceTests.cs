using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Analytics;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class AnalyticsServiceConversationIntelligenceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsServiceConversationIntelligenceTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task GetConversationIntelligenceAsync_returns_counts_and_recent_summary_projection()
    {
        using var db = _dbFactory.CreateContext();

        var alpha = await SeedConversationAsync(db, 1, "Alpha");
        var beta = await SeedConversationAsync(db, 2, "Beta");
        var gamma = await SeedConversationAsync(db, 3, "Gamma");

        var alphaSnapshotV1 = await SeedSnapshotAsync(
            db,
            alpha.Id,
            1,
            "Alpha summary v1",
            "Alpha preview v1",
            """["First alpha point"]""",
            2,
            new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc));

        var alphaSnapshotV2 = await SeedSnapshotAsync(
            db,
            alpha.Id,
            2,
            "Alpha summary v2",
            "Alpha preview v2",
            """["Second alpha point","Third alpha point"]""",
            4,
            new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc));

        var betaSnapshot = await SeedSnapshotAsync(
            db,
            beta.Id,
            1,
            "Beta summary",
            "Beta preview",
            """["Beta point"]""",
            3,
            new DateTime(2026, 4, 22, 9, 30, 0, DateTimeKind.Utc));

        db.ConversationSummaryStates.AddRange(
            new ConversationSummaryStateEntity
            {
                ConversationId = alpha.Id,
                LatestSnapshotId = alphaSnapshotV2.Id,
                LatestSnapshotVersion = 2,
                LastCoveredMessageCount = 4,
                PendingMessageCount = 0,
                IsStale = false,
                LastRefreshedAt = alphaSnapshotV2.GeneratedAt
            },
            new ConversationSummaryStateEntity
            {
                ConversationId = beta.Id,
                LatestSnapshotId = betaSnapshot.Id,
                LatestSnapshotVersion = 1,
                LastCoveredMessageCount = 2,
                PendingMessageCount = 1,
                IsStale = true,
                LastRefreshedAt = betaSnapshot.GeneratedAt,
                LastError = "Refresh pending"
            });

        await db.SaveChangesAsync();

        var sut = new AnalyticsService(db, _logger);

        var overview = await sut.GetConversationIntelligenceAsync(maxRecent: 5);

        overview.SummarizedConversations.Should().Be(2);
        overview.CurrentSnapshots.Should().Be(3);
        overview.StaleConversations.Should().Be(1);
        overview.PendingRefreshes.Should().Be(2);
        overview.RecentSummaries.Should().HaveCount(2);

        overview.RecentSummaries[0].ConversationId.Should().Be(alpha.Id);
        overview.RecentSummaries[0].PreviewText.Should().Be("Alpha preview v2");
        overview.RecentSummaries[0].KeyPoints.Should().ContainInOrder("Second alpha point", "Third alpha point");
        overview.RecentSummaries[0].IsStale.Should().BeFalse();

        overview.RecentSummaries[1].ConversationId.Should().Be(beta.Id);
        overview.RecentSummaries[1].IsStale.Should().BeTrue();
        overview.RecentSummaries[1].HasRefreshError.Should().BeTrue();
        overview.RecentSummaries[1].PendingMessageCount.Should().Be(1);
    }

    private static async Task<ConversationEntity> SeedConversationAsync(
        AgentX.Core.Data.AgentXDbContext db,
        int order,
        string title)
    {
        var conversation = new ConversationEntity
        {
            Title = title,
            ModelId = "llama3.1:8b",
            CreatedAt = new DateTime(2026, 4, 22, 8, order, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 22, 8, order + 10, 0, DateTimeKind.Utc),
            MessageCount = order == 3 ? 2 : 4,
            TokensUsed = 100 * order
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
        string keyPointsJson,
        int coveredMessageCount,
        DateTime generatedAt)
    {
        var snapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversationId,
            SnapshotVersion = version,
            SummaryText = summary,
            PreviewText = preview,
            KeyPointsJson = keyPointsJson,
            CoveredMessageCount = coveredMessageCount,
            GeneratedAt = generatedAt,
            SourceConversationUpdatedAt = generatedAt,
            IsIncremental = version > 1
        };

        db.ConversationSummarySnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }
}

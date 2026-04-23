using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Analytics;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class AnalyticsServiceConversationThemeTrendTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsServiceConversationThemeTrendTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task GetConversationThemeTrendOverviewAsync_returns_trend_cards_and_ordered_series()
    {
        using var db = _dbFactory.CreateContext();
        var today = DateTime.UtcNow.Date;

        var analyticsCluster = new ConversationThemeClusterEntity
        {
            Label = "Analytics dashboard",
            PreviewText = "Analytics and recall health are accelerating.",
            KeyPointsJson = """["Analytics dashboard","Recall health"]""",
            ConversationCount = 2,
            ActiveConversationCount7d = 2,
            ActiveConversationCount30d = 2,
            FirstSeenAt = today.AddDays(-10),
            LastActiveAt = today.AddHours(11),
            MaterializedAt = today.AddHours(12)
        };

        var syncCluster = new ConversationThemeClusterEntity
        {
            Label = "Sync scope",
            PreviewText = "Collection scope work is steady.",
            KeyPointsJson = """["Sync scope"]""",
            ConversationCount = 1,
            ActiveConversationCount7d = 1,
            ActiveConversationCount30d = 1,
            FirstSeenAt = today.AddDays(-12),
            LastActiveAt = today.AddHours(9),
            MaterializedAt = today.AddHours(10)
        };

        db.ConversationThemeClusters.AddRange(analyticsCluster, syncCluster);
        await db.SaveChangesAsync();

        for (var offset = 0; offset < 30; offset++)
        {
            var date = today.AddDays(offset - 29);
            var analyticsActive = offset >= 24 ? 1 : 0;
            var syncActive = offset >= 16 && offset <= 22 ? 1 : 0;

            db.ConversationThemeDailyMetrics.AddRange(
                new ConversationThemeDailyMetricEntity
                {
                    ClusterId = analyticsCluster.Id,
                    Date = date,
                    ActiveConversationCount = analyticsActive,
                    NewConversationCount = offset == 28 ? 1 : 0,
                    SnapshotRefreshCount = offset >= 27 ? 1 : 0,
                    MaterializedAt = today.AddHours(12)
                },
                new ConversationThemeDailyMetricEntity
                {
                    ClusterId = syncCluster.Id,
                    Date = date,
                    ActiveConversationCount = syncActive,
                    NewConversationCount = 0,
                    SnapshotRefreshCount = offset == 18 ? 1 : 0,
                    MaterializedAt = today.AddHours(10)
                });
        }

        await db.SaveChangesAsync();

        var sut = new AnalyticsService(db, _logger);

        var overview = await sut.GetConversationThemeTrendOverviewAsync(maxThemes: 5, days: 30);

        overview.TrendingThemes.Should().Be(1);
        overview.NewThemeEntries7d.Should().Be(1);
        overview.MostActiveThemeLabel.Should().Be("Analytics dashboard");
        overview.LastTrendRefresh.Should().Be(today.AddHours(12));
        overview.Trends.Should().HaveCount(2);

        var topTrend = overview.Trends[0];
        topTrend.Label.Should().Be("Analytics dashboard");
        topTrend.Recent7DayActivity.Should().Be(6);
        topTrend.Previous7DayActivity.Should().Be(0);
        topTrend.Recent7DayNewEntries.Should().Be(1);
        topTrend.DailySeries.Should().HaveCount(30);
        topTrend.DailySeries[0].Date.Should().Be(today.AddDays(-29));
        topTrend.DailySeries[^1].Date.Should().Be(today);
    }
}

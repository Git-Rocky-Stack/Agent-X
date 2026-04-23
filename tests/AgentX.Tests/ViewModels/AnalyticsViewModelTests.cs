using AgentX.App.ViewModels;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Intelligence;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class AnalyticsViewModelTests
{
    private readonly Mock<IAnalyticsService> _analyticsService = new();
    private readonly Mock<IConversationRecallService> _conversationRecallService = new();
    private readonly Mock<IConversationSummaryService> _conversationSummaryService = new();
    private readonly Mock<IConversationThemeClusterService> _conversationThemeClusterService = new();
    private readonly Mock<IConversationThemeTrendService> _conversationThemeTrendService = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsViewModelTests>();

    [Fact]
    public async Task LoadDataAsync_refreshes_conversation_summaries_and_maps_recent_items()
    {
        _conversationSummaryService
            .Setup(service => service.RefreshStaleSummariesAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _conversationRecallService
            .Setup(service => service.RefreshRecentConversationEmbeddingsAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _conversationThemeClusterService
            .Setup(service => service.RefreshStaleClustersAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _conversationThemeTrendService
            .Setup(service => service.RefreshRecentClusterTrendsAsync(4, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        _analyticsService
            .Setup(service => service.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyticsSummary
            {
                TotalConversations = 4,
                TotalMessages = 12,
                TotalTokensUsed = 2400,
                TotalDocuments = 3,
                DocumentsIndexedCount = 2,
                DocumentsPendingCount = 1
            });

        _analyticsService
            .Setup(service => service.GetDailyConversationMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyMetric>());
        _analyticsService
            .Setup(service => service.GetDailyDocumentMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyMetric>());
        _analyticsService
            .Setup(service => service.GetDailySearchMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyMetric>());
        _analyticsService
            .Setup(service => service.GetModelUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ModelUsageMetric>());
        _analyticsService
            .Setup(service => service.GetFileTypeDistributionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FileTypeMetric>());
        _analyticsService
            .Setup(service => service.GetPerformanceMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceMetrics());
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview
            {
                SummarizedConversations = 2,
                CurrentSnapshots = 3,
                StaleConversations = 1,
                PendingRefreshes = 1,
                RecentSummaries =
                [
                    new ConversationSummaryMetric
                    {
                        ConversationId = 42,
                        Title = "Persistent memory rollout",
                        PreviewText = "The team is moving durable summaries into analytics.",
                        KeyPoints = ["Durable summaries land first.", "Analytics is the inspection surface."],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-20),
                        CoveredMessageCount = 6,
                        PendingMessageCount = 0,
                        IsStale = false
                    }
                ]
            });
        _analyticsService
            .Setup(service => service.GetConversationRecallOverviewAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationRecallOverview
            {
                EmbeddedMessages = 8,
                PendingMessageEmbeddings = 2,
                RecallReadyConversations = 3,
                LastEmbeddedAt = DateTime.UtcNow.AddMinutes(-5)
            });
        _analyticsService
            .Setup(service => service.GetConversationThemeOverviewAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationThemeOverview
            {
                ActiveThemeClusters = 2,
                ClusteredConversations = 3,
                NewThemes7d = 1,
                LastMaterializedAt = DateTime.UtcNow.AddMinutes(-3),
                Clusters =
                [
                    new ConversationThemeClusterMetric
                    {
                        ClusterId = 9,
                        Label = "Analytics dashboard",
                        PreviewText = "Analytics and recall health are converging.",
                        KeyPoints = ["Analytics dashboard", "Recall health"],
                        ConversationCount = 3,
                        ActiveConversationCount7d = 3,
                        ActiveConversationCount30d = 3,
                        LastActiveAt = DateTime.UtcNow.AddMinutes(-4),
                        MaterializedAt = DateTime.UtcNow.AddMinutes(-3),
                        RecentConversationTitles = ["Persistent memory rollout", "Analytics roadmap"]
                    }
                ]
            });
        _analyticsService
            .Setup(service => service.GetConversationThemeTrendOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationThemeTrendOverview
            {
                TrendingThemes = 1,
                NewThemeEntries7d = 2,
                MostActiveThemeLabel = "Analytics dashboard",
                LastTrendRefresh = DateTime.UtcNow.AddMinutes(-1),
                Trends =
                [
                    new ConversationThemeTrendMetric
                    {
                        ClusterId = 9,
                        Label = "Analytics dashboard",
                        PreviewText = "Activity is accelerating around analytics and recall.",
                        Recent7DayActivity = 5,
                        Previous7DayActivity = 2,
                        Recent7DayNewEntries = 2,
                        LastActiveAt = DateTime.UtcNow.AddMinutes(-4),
                        DailySeries = Enumerable.Range(0, 30)
                            .Select(offset => new ConversationThemeDailyPoint
                            {
                                Date = DateTime.UtcNow.Date.AddDays(offset - 29),
                                ActiveConversationCount = offset >= 25 ? 1 : 0,
                                NewConversationCount = offset == 28 ? 1 : 0,
                                SnapshotRefreshCount = offset >= 27 ? 1 : 0
                            })
                            .ToList()
                    }
                ]
            });

        var viewModel = new AnalyticsViewModel(
            _analyticsService.Object,
            _conversationRecallService.Object,
            _conversationSummaryService.Object,
            _conversationThemeClusterService.Object,
            _conversationThemeTrendService.Object,
            _logger);

        await viewModel.LoadDataAsync();

        _conversationSummaryService.Verify(
            service => service.RefreshStaleSummariesAsync(4, It.IsAny<CancellationToken>()),
            Times.Once);
        _conversationRecallService.Verify(
            service => service.RefreshRecentConversationEmbeddingsAsync(4, It.IsAny<CancellationToken>()),
            Times.Once);
        _conversationThemeClusterService.Verify(
            service => service.RefreshStaleClustersAsync(4, It.IsAny<CancellationToken>()),
            Times.Once);
        _conversationThemeTrendService.Verify(
            service => service.RefreshRecentClusterTrendsAsync(4, 30, It.IsAny<CancellationToken>()),
            Times.Once);

        viewModel.SummarizedConversations.Should().Be("2");
        viewModel.CurrentSummarySnapshots.Should().Be("3");
        viewModel.EmbeddedMessages.Should().Be("8");
        viewModel.PendingMessageEmbeddings.Should().Be("2");
        viewModel.ActiveThemeClusters.Should().Be("2");
        viewModel.ClusteredThemeConversations.Should().Be("3");
        viewModel.HasConversationThemeClusters.Should().BeTrue();
        viewModel.ConversationThemeClusters.Should().ContainSingle();
        viewModel.ConversationThemeClusters[0].Label.Should().Be("Analytics dashboard");
        viewModel.ConversationThemeClusters[0].RecentConversationsPreview.Should().Contain("Analytics roadmap");
        viewModel.TrendingThemes.Should().Be("1");
        viewModel.NewThemeEntries7d.Should().Be("2");
        viewModel.MostActiveTheme.Should().Be("Analytics dashboard");
        viewModel.HasConversationThemeTrends.Should().BeTrue();
        viewModel.ConversationThemeTrends.Should().ContainSingle();
        viewModel.ConversationThemeTrends[0].MomentumLabel.Should().Be("+3 vs prior 7d");
        viewModel.ConversationThemeTrends[0].HasNewEntries.Should().BeTrue();
        viewModel.HasRecentConversationSummaries.Should().BeTrue();
        viewModel.RecentConversationSummaries.Should().ContainSingle();
        viewModel.RecentConversationSummaries[0].Title.Should().Be("Persistent memory rollout");
        viewModel.RecentConversationSummaries[0].KeyPointsPreview.Should().Contain("Analytics is the inspection surface.");
        viewModel.RecentConversationSummaries[0].StatusLabel.Should().Be("Current");
    }

    [Fact]
    public async Task RunConversationRecallAsync_maps_recall_results_and_status_message()
    {
        _conversationRecallService
            .Setup(service => service.RefreshRecentConversationEmbeddingsAsync(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _conversationRecallService
            .Setup(service => service.SearchRelevantMessagesAsync(
                "dashboard analytics",
                6,
                0.68f,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AgentX.Core.Services.Chat.Models.ConversationRecallResult
                {
                    ConversationId = 7,
                    MessageId = 71,
                    ConversationTitle = "Analytics roadmap",
                    Role = "assistant",
                    ContentPreview = "The dashboard should surface analytics and recall health together.",
                    Timestamp = DateTime.UtcNow.AddMinutes(-10),
                    Similarity = 0.91f
                }
            ]);
        _analyticsService
            .Setup(service => service.GetConversationRecallOverviewAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationRecallOverview
            {
                EmbeddedMessages = 9,
                PendingMessageEmbeddings = 1,
                RecallReadyConversations = 4,
                LastEmbeddedAt = DateTime.UtcNow.AddMinutes(-2)
            });

        var viewModel = new AnalyticsViewModel(
            _analyticsService.Object,
            _conversationRecallService.Object,
            _conversationSummaryService.Object,
            _conversationThemeClusterService.Object,
            _conversationThemeTrendService.Object,
            _logger)
        {
            RecallQuery = "dashboard analytics"
        };

        await viewModel.RunConversationRecallCommand.ExecuteAsync(null);

        viewModel.HasConversationRecallResults.Should().BeTrue();
        viewModel.ConversationRecallResults.Should().ContainSingle();
        viewModel.ConversationRecallResults[0].ConversationTitle.Should().Be("Analytics roadmap");
        viewModel.ConversationRecallResults[0].RoleLabel.Should().Be("Assistant");
        viewModel.RecallStatusMessage.Should().Be("1 durable recall match found.");
    }
}

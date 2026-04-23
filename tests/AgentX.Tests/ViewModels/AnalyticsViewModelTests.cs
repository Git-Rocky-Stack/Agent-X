using AgentX.App.ViewModels;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Chat;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class AnalyticsViewModelTests
{
    private readonly Mock<IAnalyticsService> _analyticsService = new();
    private readonly Mock<IConversationSummaryService> _conversationSummaryService = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsViewModelTests>();

    [Fact]
    public async Task LoadDataAsync_refreshes_conversation_summaries_and_maps_recent_items()
    {
        _conversationSummaryService
            .Setup(service => service.RefreshStaleSummariesAsync(4, It.IsAny<CancellationToken>()))
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

        var viewModel = new AnalyticsViewModel(
            _analyticsService.Object,
            _conversationSummaryService.Object,
            _logger);

        await viewModel.LoadDataAsync();

        _conversationSummaryService.Verify(
            service => service.RefreshStaleSummariesAsync(4, It.IsAny<CancellationToken>()),
            Times.Once);

        viewModel.SummarizedConversations.Should().Be("2");
        viewModel.CurrentSummarySnapshots.Should().Be("3");
        viewModel.HasRecentConversationSummaries.Should().BeTrue();
        viewModel.RecentConversationSummaries.Should().ContainSingle();
        viewModel.RecentConversationSummaries[0].Title.Should().Be("Persistent memory rollout");
        viewModel.RecentConversationSummaries[0].KeyPointsPreview.Should().Contain("Analytics is the inspection surface.");
        viewModel.RecentConversationSummaries[0].StatusLabel.Should().Be("Current");
    }
}

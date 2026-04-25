using AgentX.App.ViewModels;
using AgentX.App.Services;
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
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsViewModelTests>();

    private AnalyticsViewModel CreateViewModel() =>
        new(
            _analyticsService.Object,
            _conversationRecallService.Object,
            _conversationSummaryService.Object,
            _conversationThemeClusterService.Object,
            _conversationThemeTrendService.Object,
            _logger,
            _operationsDrillInService.Object);

    private void SetupDefaultLoadDataDependencies()
    {
        _conversationSummaryService
            .Setup(service => service.RefreshStaleSummariesAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _conversationRecallService
            .Setup(service => service.RefreshRecentConversationEmbeddingsAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _conversationThemeClusterService
            .Setup(service => service.RefreshStaleClustersAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _conversationThemeTrendService
            .Setup(service => service.RefreshRecentClusterTrendsAsync(4, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _analyticsService
            .Setup(service => service.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyticsSummary());
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
            .Setup(service => service.GetWorkflowIntelligenceOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowIntelligenceOverview());
        _analyticsService
            .Setup(service => service.GetDailyWorkflowRunMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyMetric>());
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview());
        _analyticsService
            .Setup(service => service.GetConversationRecallOverviewAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationRecallOverview());
        _analyticsService
            .Setup(service => service.GetConversationThemeOverviewAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationThemeOverview());
        _analyticsService
            .Setup(service => service.GetConversationThemeTrendOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationThemeTrendOverview());
    }

    [Fact]
    public async Task LoadDataAsync_refreshes_conversation_summaries_and_maps_recent_items()
    {
        SetupDefaultLoadDataDependencies();

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
            .Setup(service => service.GetWorkflowIntelligenceOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowIntelligenceOverview
            {
                TotalRuns = 3,
                SuccessfulRuns = 2,
                FailedOrCancelledRuns = 1,
                SuccessRate = 66.7,
                AverageRunDurationMs = 45000,
                ActiveWorkflowsRecently = 2
            });
        _analyticsService
            .Setup(service => service.GetDailyWorkflowRunMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DailyMetric { Date = DateTime.UtcNow.Date.AddDays(-1), Count = 1, Label = "Apr 22" },
                new DailyMetric { Date = DateTime.UtcNow.Date, Count = 2, Label = "Apr 23" }
            ]);
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

        var viewModel = CreateViewModel();

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
        viewModel.WorkflowRunsTotal.Should().Be("3");
        viewModel.WorkflowSuccessRate.Should().Be("66.7%");
        viewModel.WorkflowAverageRunDuration.Should().Be("45.00 s");
        viewModel.WorkflowActiveRecently.Should().Be("2");
        viewModel.HasWorkflowIntelligence.Should().BeTrue();
        viewModel.HasWorkflowTrendData.Should().BeTrue();
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
        SetupDefaultLoadDataDependencies();

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

        var viewModel = CreateViewModel();
        viewModel.RecallQuery = "dashboard analytics";

        await viewModel.RunConversationRecallCommand.ExecuteAsync(null);

        viewModel.HasConversationRecallResults.Should().BeTrue();
        viewModel.ConversationRecallResults.Should().ContainSingle();
        viewModel.ConversationRecallResults[0].ConversationTitle.Should().Be("Analytics roadmap");
        viewModel.ConversationRecallResults[0].RoleLabel.Should().Be("Assistant");
        viewModel.RecallStatusMessage.Should().Be("1 durable recall match found.");
    }

    [Fact]
    public async Task LoadDataAsync_maps_workflow_intelligence_section()
    {
        SetupDefaultLoadDataDependencies();

        _analyticsService
            .Setup(service => service.GetWorkflowIntelligenceOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowIntelligenceOverview
            {
                TotalRuns = 4,
                SuccessfulRuns = 3,
                FailedOrCancelledRuns = 1,
                SuccessRate = 75.0,
                AverageRunDurationMs = 45000,
                ActiveWorkflowsRecently = 2,
                TopWorkflows =
                [
                    new WorkflowTopWorkflowMetric
                    {
                        WorkflowId = 7,
                        WorkflowName = "Research Brief",
                        Category = "Research",
                        RunCount = 3,
                        SuccessfulRuns = 2,
                        FailedOrCancelledRuns = 1,
                        SuccessRate = 66.7,
                        LastRunAt = DateTime.UtcNow.AddHours(-2)
                    }
                ],
                RecentRuns =
                [
                    new WorkflowRecentRunMetric
                    {
                        WorkflowRunId = 77,
                        WorkflowId = 7,
                        WorkflowName = "Research Brief",
                        Status = "failed",
                        StartedAt = DateTime.UtcNow.AddHours(-1),
                        DurationMs = 30000,
                        PreviewText = "Summarizer timed out during drafting.",
                        HasErrorPreview = true
                    }
                ]
            });
        _analyticsService
            .Setup(service => service.GetDailyWorkflowRunMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DailyMetric { Date = DateTime.UtcNow.Date.AddDays(-1), Count = 1, Label = "Apr 22" },
                new DailyMetric { Date = DateTime.UtcNow.Date, Count = 2, Label = "Apr 23" }
            ]);

        var viewModel = CreateViewModel();

        await viewModel.LoadDataAsync();

        viewModel.WorkflowRunsTotal.Should().Be("4");
        viewModel.WorkflowSuccessRate.Should().Be("75.0%");
        viewModel.WorkflowAverageRunDuration.Should().Be("45.00 s");
        viewModel.WorkflowActiveRecently.Should().Be("2");
        viewModel.HasWorkflowIntelligence.Should().BeTrue();
        viewModel.HasWorkflowTrendData.Should().BeTrue();
        viewModel.HasTopWorkflows.Should().BeTrue();
        viewModel.HasRecentWorkflowRuns.Should().BeTrue();
        viewModel.TopWorkflows.Should().ContainSingle();
        viewModel.TopWorkflows[0].WorkflowName.Should().Be("Research Brief");
        viewModel.TopWorkflows[0].SuccessRateLabel.Should().Be("66.7% success");
        viewModel.RecentWorkflowRuns.Should().ContainSingle();
        viewModel.RecentWorkflowRuns[0].StatusLabel.Should().Be("Failed");
        viewModel.RecentWorkflowRuns[0].PreviewText.Should().Contain("timed out");
    }

    [Fact]
    public async Task LoadDataAsync_consumes_pending_operations_conversation_request_and_focuses_summary()
    {
        SetupDefaultLoadDataDependencies();

        _operationsDrillInService
            .Setup(service => service.ConsumePendingConversationRequest())
            .Returns(new OperationsConversationDrillInRequest(9, "Opened conversation summary \"Analytics roadmap\" from Operations"));
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview
            {
                SummarizedConversations = 2,
                CurrentSnapshots = 2,
                RecentSummaries =
                [
                    new ConversationSummaryMetric
                    {
                        ConversationId = 4,
                        Title = "Persistent memory rollout",
                        PreviewText = "Durable summaries are now visible in chat and analytics.",
                        KeyPoints = ["Persistent memory", "Analytics surface"],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-30),
                        CoveredMessageCount = 5
                    },
                    new ConversationSummaryMetric
                    {
                        ConversationId = 9,
                        Title = "Analytics roadmap",
                        PreviewText = "Operations should deep-link the relevant summary card.",
                        KeyPoints = ["Operations drill-in", "Focused summary"],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-10),
                        CoveredMessageCount = 6
                    }
                ]
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadDataAsync();

        viewModel.RecentConversationSummaries.Should().HaveCount(2);
        viewModel.FocusedConversationSummaryId.Should().Be(9);
        viewModel.HasFocusedConversationLanding.Should().BeTrue();
        viewModel.FocusedConversationSourceLabel.Should().Contain("Analytics roadmap");
        viewModel.RecentConversationSummaries[0].ConversationId.Should().Be(9);
        viewModel.RecentConversationSummaries[0].IsFocused.Should().BeTrue();
        viewModel.RecentConversationSummaries[0].HasSourceLabel.Should().BeTrue();
        viewModel.RecentConversationSummaries[0].SourceLabel.Should().Contain("Analytics roadmap");
        viewModel.RecentConversationSummaries[1].ConversationId.Should().Be(4);
        viewModel.RecentConversationSummaries[1].IsFocused.Should().BeFalse();
    }

    [Fact]
    public async Task DismissFocusedConversationLandingCommand_clears_banner_and_row_focus()
    {
        SetupDefaultLoadDataDependencies();

        _operationsDrillInService
            .Setup(service => service.ConsumePendingConversationRequest())
            .Returns(new OperationsConversationDrillInRequest(9, "Opened conversation summary \"Analytics roadmap\" from Operations"));
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview
            {
                SummarizedConversations = 2,
                CurrentSnapshots = 2,
                RecentSummaries =
                [
                    new ConversationSummaryMetric
                    {
                        ConversationId = 4,
                        Title = "Persistent memory rollout",
                        PreviewText = "Durable summaries are now visible in chat and analytics.",
                        KeyPoints = ["Persistent memory", "Analytics surface"],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-30),
                        CoveredMessageCount = 5
                    },
                    new ConversationSummaryMetric
                    {
                        ConversationId = 9,
                        Title = "Analytics roadmap",
                        PreviewText = "Operations should deep-link the relevant summary card.",
                        KeyPoints = ["Operations drill-in", "Focused summary"],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-10),
                        CoveredMessageCount = 6
                    }
                ]
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadDataAsync();
        viewModel.DismissFocusedConversationLandingCommand.Execute(null);

        viewModel.FocusedConversationSummaryId.Should().Be(0);
        viewModel.HasFocusedConversationLanding.Should().BeFalse();
        viewModel.FocusedConversationSourceLabel.Should().BeEmpty();
        viewModel.RecentConversationSummaries.Should().OnlyContain(item => !item.IsFocused && !item.HasSourceLabel);
    }

    [Fact]
    public async Task RefreshCommand_preserves_focused_conversation_landing_until_dismissed()
    {
        SetupDefaultLoadDataDependencies();

        _operationsDrillInService
            .SetupSequence(service => service.ConsumePendingConversationRequest())
            .Returns(new OperationsConversationDrillInRequest(9, "Opened conversation summary \"Analytics roadmap\" from Operations"))
            .Returns((OperationsConversationDrillInRequest?)null);
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview
            {
                SummarizedConversations = 2,
                CurrentSnapshots = 2,
                RecentSummaries =
                [
                    new ConversationSummaryMetric
                    {
                        ConversationId = 4,
                        Title = "Persistent memory rollout",
                        PreviewText = "Durable summaries are now visible in chat and analytics.",
                        KeyPoints = ["Persistent memory", "Analytics surface"],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-30),
                        CoveredMessageCount = 5
                    },
                    new ConversationSummaryMetric
                    {
                        ConversationId = 9,
                        Title = "Analytics roadmap",
                        PreviewText = "Operations should deep-link the relevant summary card.",
                        KeyPoints = ["Operations drill-in", "Focused summary"],
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-10),
                        CoveredMessageCount = 6
                    }
                ]
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadDataAsync();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.HasFocusedConversationLanding.Should().BeTrue();
        viewModel.FocusedConversationSummaryId.Should().Be(9);
        viewModel.RecentConversationSummaries[0].ConversationId.Should().Be(9);
        viewModel.RecentConversationSummaries[0].IsFocused.Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_leaves_workflow_section_in_empty_state_when_no_runs_exist()
    {
        SetupDefaultLoadDataDependencies();

        var viewModel = CreateViewModel();

        await viewModel.LoadDataAsync();

        viewModel.HasWorkflowIntelligence.Should().BeFalse();
        viewModel.HasWorkflowTrendData.Should().BeFalse();
        viewModel.HasTopWorkflows.Should().BeFalse();
        viewModel.HasRecentWorkflowRuns.Should().BeFalse();
        viewModel.WorkflowIntelligenceStatusMessage.Should().Be("No workflow runs yet. Run a workflow to seed reliability, trend, and result analytics.");
    }
}

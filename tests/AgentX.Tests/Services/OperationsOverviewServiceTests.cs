using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Workflows;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class OperationsOverviewServiceTests
{
    private readonly Mock<IAnalyticsService> _analyticsService = new();
    private readonly Mock<IInboxService> _inboxService = new();
    private readonly Mock<IPluginService> _pluginService = new();
    private readonly Mock<ISyncService> _syncService = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly ILogger _logger = Log.ForContext<OperationsOverviewServiceTests>();

    [Fact]
    public async Task GetSnapshotAsync_maps_connector_backlog_and_workflow_cards()
    {
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview
            {
                SummarizedConversations = 5,
                CurrentSnapshots = 6,
                RecentSummaries =
                [
                    new ConversationSummaryMetric
                    {
                        ConversationId = 101,
                        Title = "Durable memory rollout",
                        GeneratedAt = DateTime.UtcNow.AddMinutes(-10),
                        CoveredMessageCount = 9
                    }
                ]
            });

        _analyticsService
            .Setup(service => service.GetWorkflowIntelligenceOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowIntelligenceOverview
            {
                TotalRuns = 7,
                SuccessfulRuns = 6,
                FailedOrCancelledRuns = 1,
                SuccessRate = 85.7,
                AverageRunDurationMs = 42000,
                ActiveWorkflowsRecently = 2,
                TopWorkflows =
                [
                    new WorkflowTopWorkflowMetric
                    {
                        WorkflowId = 1,
                        WorkflowName = "Research Briefing",
                        Category = "Research",
                        RunCount = 4,
                        SuccessfulRuns = 3,
                        FailedOrCancelledRuns = 1,
                        SuccessRate = 75.0,
                        LastRunAt = DateTime.UtcNow.AddHours(-3)
                    }
                ]
            });

        _inboxService.Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(4);

        _syncService.SetupGet(service => service.Status)
            .Returns(new SyncStatus
            {
                SyncState = SyncState.Idle,
                PendingChanges = 2
            });
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret",
                SyncScope = SyncScope.All
            });

        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                new PluginEntity
                {
                    Id = 1,
                    PluginId = "com.agentx.email",
                    Name = "Email Connector",
                    PluginType = "DataConnector",
                    IsEnabled = true
                },
                new PluginEntity
                {
                    Id = 2,
                    PluginId = "com.agentx.calendar",
                    Name = "Calendar Connector",
                    PluginType = "DataConnector",
                    IsEnabled = true
                },
                new PluginEntity
                {
                    Id = 3,
                    PluginId = "com.agentx.workflowstep",
                    Name = "Workflow Step Kit",
                    PluginType = "WorkflowStep",
                    IsEnabled = false
                }
            ]);

        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity { Id = 1, Name = "Research Briefing", IsEnabled = true },
                new WorkflowEntity { Id = 2, Name = "Inbox Cleanup", IsEnabled = true }
            ]);

        var sut = new OperationsOverviewService(
            _analyticsService.Object,
            _inboxService.Object,
            _pluginService.Object,
            _syncService.Object,
            _workflowService.Object,
            _logger);

        var snapshot = await sut.GetSnapshotAsync();

        snapshot.ConversationIntelligence.Headline.Should().Be("5");
        snapshot.ConversationIntelligence.Status.Should().Be("Durable recall current");

        snapshot.SyncHealth.Headline.Should().Be("Configured");
        snapshot.SyncHealth.Status.Should().Be("2 local changes pending");

        snapshot.IngestionBacklog.Headline.Should().Be("4");
        snapshot.IngestionBacklog.Status.Should().Be("4 items awaiting triage");
        snapshot.IngestionBacklog.Detail.Should().Contain("connector and watch-folder");

        snapshot.Connectors.Headline.Should().Be("2");
        snapshot.Connectors.Status.Should().Be("2 connectors enabled");
        snapshot.Connectors.Detail.Should().Contain("Email Connector");
        snapshot.Connectors.Detail.Should().Contain("Calendar Connector");

        snapshot.WorkflowActivity.Headline.Should().Be("7");
        snapshot.WorkflowActivity.Status.Should().Be("86% success rate");
        snapshot.WorkflowActivity.SupportingPrimary.Should().Be("2 active / 30d");
        snapshot.WorkflowActivity.SupportingSecondary.Should().Be("42s avg run");
        snapshot.WorkflowActivity.Detail.Should().Contain("Research Briefing");
    }
}

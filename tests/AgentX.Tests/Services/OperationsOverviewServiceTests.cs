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
                        PreviewText = "Persistent summary coverage is catching the latest recall state.",
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
                ],
                RecentRuns =
                [
                    new WorkflowRecentRunMetric
                    {
                        WorkflowRunId = 77,
                        WorkflowId = 1,
                        WorkflowName = "Research Briefing",
                        Status = "completed",
                        StartedAt = DateTime.UtcNow.AddMinutes(-7),
                        CompletedAt = DateTime.UtcNow.AddMinutes(-5),
                        DurationMs = 42000,
                        PreviewText = "Executive summary and key findings generated successfully."
                    }
                ]
            });

        _inboxService.Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(4);
        _inboxService.Setup(service => service.GetAllItemsAsync("pending", 0, 3))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 22,
                    FileName = "Board update.docx",
                    FileType = "Document",
                    SourceType = "email-connector",
                    SuggestedCollectionName = "Leadership",
                    AddedAt = DateTime.UtcNow.AddMinutes(-12)
                }
            ]);
        _inboxService.Setup(service => service.GetAllItemsAsync("accepted", 0, 8))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 30,
                    DocumentId = 501,
                    FileName = "Sprint planning email",
                    FileType = "EmailMessage",
                    SourceType = "email-connector",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30),
                    ProcessedAt = DateTime.UtcNow.AddMinutes(-28)
                },
                new InboxItemEntity
                {
                    Id = 31,
                    DocumentId = 502,
                    FileName = "Quarterly roadmap meeting",
                    FileType = "CalendarEvent",
                    SourceType = "calendar-connector",
                    AddedAt = DateTime.UtcNow.AddMinutes(-20),
                    ProcessedAt = DateTime.UtcNow.AddMinutes(-18)
                }
            ]);

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
        _syncService.Setup(service => service.GetSyncHistoryAsync(3))
            .ReturnsAsync(
            [
                new SyncLogEntity
                {
                    Id = 9,
                    Direction = "import",
                    ChangesApplied = 12,
                    ConflictsDetected = 0,
                    DurationMs = 2800,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-9),
                    IsSuccess = true
                }
            ]);

        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                new PluginEntity
                {
                    Id = 1,
                    PluginId = "com.agentx.email",
                    Name = "Email Connector",
                    PluginType = "DataConnector",
                    Description = "Brings inbox mail into Agent-X for triage and search.",
                    IsEnabled = true
                },
                new PluginEntity
                {
                    Id = 2,
                    PluginId = "com.agentx.calendar",
                    Name = "Calendar Connector",
                    PluginType = "DataConnector",
                    Description = "Indexes meeting events and follow-up tasks.",
                    IsEnabled = true
                },
                new PluginEntity
                {
                    Id = 3,
                    PluginId = "com.agentx.slack",
                    Name = "Slack Connector",
                    PluginType = "DataConnector",
                    Description = "Brings team notifications into the workspace.",
                    IsEnabled = false
                },
                new PluginEntity
                {
                    Id = 4,
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
        snapshot.RecentConversationSummaries.Should().ContainSingle();
        snapshot.RecentConversationSummaries[0].Title.Should().Be("Durable memory rollout");

        snapshot.SyncHealth.Headline.Should().Be("Configured");
        snapshot.SyncHealth.Status.Should().Be("2 local changes pending");
        snapshot.RecentSyncPasses.Should().ContainSingle();
        snapshot.RecentSyncPasses[0].Title.Should().Be("Import sync");

        snapshot.IngestionBacklog.Headline.Should().Be("4");
        snapshot.IngestionBacklog.Status.Should().Be("4 items awaiting triage");
        snapshot.IngestionBacklog.Detail.Should().Contain("connector and watch-folder");
        snapshot.PendingInboxItems.Should().ContainSingle();
        snapshot.PendingInboxItems[0].Title.Should().Be("Board update.docx");
        snapshot.PendingInboxItems[0].Status.Should().Be("Email Connector");
        snapshot.RecentImportedDocuments.Should().HaveCount(2);
        snapshot.RecentImportedDocuments[0].DocumentId.Should().Be(502);
        snapshot.RecentImportedDocuments[0].Status.Should().Be("Calendar Connector");
        snapshot.RecentImportedDocuments[1].DocumentId.Should().Be(501);
        snapshot.RecentImportedDocuments[1].Status.Should().Be("Email Connector");

        snapshot.Connectors.Headline.Should().Be("2");
        snapshot.Connectors.Status.Should().Be("2 connectors enabled");
        snapshot.Connectors.Detail.Should().Contain("Email Connector");
        snapshot.Connectors.Detail.Should().Contain("Calendar Connector");
        snapshot.ConnectorPreviews.Should().HaveCount(3);
        snapshot.ConnectorPreviews[0].Title.Should().Be("Calendar Connector");
        snapshot.ConnectorPreviews[0].IsEnabled.Should().BeTrue();
        snapshot.ConnectorPreviews[0].CanEnableFromOperations.Should().BeFalse();
        snapshot.ConnectorPreviews[2].Title.Should().Be("Slack Connector");
        snapshot.ConnectorPreviews[2].Status.Should().Be("Disabled");
        snapshot.ConnectorPreviews[2].CanEnableFromOperations.Should().BeTrue();

        snapshot.WorkflowActivity.Headline.Should().Be("7");
        snapshot.WorkflowActivity.Status.Should().Be("86% success rate");
        snapshot.WorkflowActivity.SupportingPrimary.Should().Be("2 active / 30d");
        snapshot.WorkflowActivity.SupportingSecondary.Should().Be("42s avg run");
        snapshot.WorkflowActivity.Detail.Should().Contain("Research Briefing");
        snapshot.RecentWorkflowRuns.Should().ContainSingle();
        snapshot.RecentWorkflowRuns[0].Status.Should().Be("Completed");
    }
}

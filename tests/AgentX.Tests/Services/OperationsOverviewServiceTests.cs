using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Workflows;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class OperationsOverviewServiceTests
{
    private readonly Mock<IAnalyticsService> _analyticsService = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IInboxService> _inboxService = new();
    private readonly Mock<IPluginService> _pluginService = new();
    private readonly Mock<ISyncService> _syncService = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly ILogger _logger = Log.ForContext<OperationsOverviewServiceTests>();

    [Fact]
    public async Task GetSnapshotAsync_maps_connector_backlog_and_workflow_cards()
    {
        ArrangeBusyWorkspace();
        var sut = CreateSut();

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
        snapshot.RecentImportedDocuments.Should().HaveCount(3);
        snapshot.RecentImportedDocuments[0].DocumentId.Should().Be(503);
        snapshot.RecentImportedDocuments[0].Status.Should().Be("Email Connector");
        snapshot.RecentImportedDocuments[0].HealthStatus.Should().Be("Needs Attention");
        snapshot.RecentImportedDocuments[1].DocumentId.Should().Be(502);
        snapshot.RecentImportedDocuments[1].Status.Should().Be("Calendar Connector");
        snapshot.RecentImportedDocuments[1].HealthStatus.Should().Be("Searchable");
        snapshot.RecentImportedDocuments[2].DocumentId.Should().Be(501);
        snapshot.RecentImportedDocuments[2].Status.Should().Be("Email Connector");
        snapshot.RecentImportedDocuments[2].HealthStatus.Should().Be("Processing");

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

    [Fact]
    public async Task GetSnapshotAsync_reports_what_each_status_means()
    {
        // Operations and the dashboard used to compare this text ("Needs Attention", "Failed",
        // "Not configured"), which breaks once it is translated; they read these kinds instead.
        ArrangeBusyWorkspace();

        var snapshot = await CreateSut().GetSnapshotAsync();

        snapshot.ConversationIntelligence.StatusKind.Should().Be(OperationsStatusKind.RecallCurrent);
        snapshot.SyncHealth.StatusKind.Should().Be(OperationsStatusKind.SyncChangesPending);
        snapshot.IngestionBacklog.StatusKind.Should().Be(OperationsStatusKind.BacklogWaiting);
        snapshot.WorkflowActivity.StatusKind.Should().Be(OperationsStatusKind.WorkflowSuccessRate);
        snapshot.WorkflowActivity.SupportingPrimaryKind.Should().Be(OperationsStatusKind.WorkflowsActiveRecently);
        snapshot.Connectors.StatusKind.Should().Be(OperationsStatusKind.ConnectorsEnabled);
        snapshot.RecentSyncPasses[0].StatusKind.Should().Be(OperationsStatusKind.SyncPassSucceeded);
        snapshot.RecentWorkflowRuns[0].StatusKind.Should().Be(OperationsStatusKind.RunCompleted);
        snapshot.RecentWorkflowRuns[0].NeedsReview.Should().BeFalse();
        snapshot.ConnectorPreviews[2].StatusKind.Should().Be(OperationsStatusKind.ConnectorDisabled);
        snapshot.RecentImportedDocuments.Select(preview => preview.Health).Should().Equal(
            OperationsDocumentHealth.NeedsAttention,
            OperationsDocumentHealth.Searchable,
            OperationsDocumentHealth.Processing);
        snapshot.RecentImportedDocuments[0].CanRetryIndexingFromOperations.Should().BeTrue();
        snapshot.RecentImportedDocuments[1].CanRetryIndexingFromOperations.Should().BeFalse();
    }

    [Fact]
    public async Task GetSnapshotAsync_words_details_with_plain_punctuation()
    {
        ArrangeBusyWorkspace();

        var snapshot = await CreateSut().GetSnapshotAsync();

        snapshot.ConversationIntelligence.Detail.Should().StartWith("6 stored snapshots, latest ");
        snapshot.RecentSyncPasses[0].Detail.Should().StartWith("12 changes, 3s, ");
        snapshot.PendingInboxItems[0].Detail.Should().StartWith("Document, suggested for Leadership, ");
        snapshot.RecentImportedDocuments[0].Detail.Should().Be("Email Message, Embedding request failed.");
        snapshot.RecentImportedDocuments[2].Detail.Should().Be("Email Message, queued for indexing");
        snapshot.Connectors.Detail.Should().Be("Email Connector, Calendar Connector");
        snapshot.WorkflowActivity.Detail.Should().Be("Top workflow: Research Briefing, 4 runs");
        snapshot.ConnectorPreviews[0].Detail.Should().Be("Connector: Indexes meeting events and follow-up tasks.");
        AllTexts(snapshot).Should().NotContain(text => text.Contains('·'));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_colors_are_the_ones_the_english_wording_gave(bool busy)
    {
        // The badges are colored from a tone token, not from the translated text; in English
        // each token must resolve to the tone the status text itself resolved to.
        if (busy)
        {
            ArrangeBusyWorkspace();
        }
        else
        {
            ArrangeEmptyWorkspace();
        }

        var snapshot = await CreateSut().GetSnapshotAsync();

        foreach (var (status, token) in StatusesWithTokens(snapshot))
        {
            StatusToneResolver.Resolve(token).Should().Be(
                StatusToneResolver.Resolve(status), $"the status \"{status}\" keeps its color");
        }
    }

    [Fact]
    public async Task An_empty_workspace_reports_nothing_set_up()
    {
        ArrangeEmptyWorkspace();

        var snapshot = await CreateSut().GetSnapshotAsync();

        snapshot.SyncHealth.Headline.Should().Be("Not configured");
        snapshot.SyncHealth.StatusKind.Should().Be(OperationsStatusKind.SyncNotConfigured);
        snapshot.Connectors.StatusKind.Should().Be(OperationsStatusKind.NoPluginsInstalled);
        snapshot.WorkflowActivity.StatusKind.Should().Be(OperationsStatusKind.WorkflowReadyToAutomate);
        snapshot.WorkflowActivity.SupportingPrimaryKind.Should().Be(OperationsStatusKind.WorkflowsNoRecentRuns);
        snapshot.ConversationIntelligence.StatusKind.Should().Be(OperationsStatusKind.RecallInactive);
        snapshot.IngestionBacklog.StatusKind.Should().Be(OperationsStatusKind.BacklogClear);
        snapshot.RecentConversationSummaries.Single().StatusKind.Should().Be(OperationsStatusKind.Placeholder);
        snapshot.RecentWorkflowRuns.Single().Status.Should().Be("History");
    }

    [Fact]
    public async Task GetSnapshotAsync_reads_every_text_from_the_resources()
    {
        // Resource lookups come back as their keys, so any English left in code would show.
        ArrangeBusyWorkspace();
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => $"<{key}>");
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"<{key}>");

        var snapshot = await CreateSut(localization.Object).GetSnapshotAsync();

        snapshot.ConversationIntelligence.Status.Should().Be("<Ops_RecallCurrent>");
        snapshot.ConversationIntelligence.Detail.Should().Be("<Ops_RecallDetailSnapshots>");
        snapshot.SyncHealth.Headline.Should().Be("<Ops_SyncHeadlineConfigured>");
        snapshot.SyncHealth.Status.Should().Be("<Ops_SyncChangesPending>");
        snapshot.SyncHealth.Detail.Should().Be("<Ops_SyncScopeFull>");
        snapshot.RecentSyncPasses[0].Title.Should().Be("<Ops_SyncPassImport>");
        snapshot.IngestionBacklog.Detail.Should().Be("<Ops_BacklogDetailPending>");
        snapshot.RecentImportedDocuments[1].HealthStatus.Should().Be("<Ops_HealthSearchable>");
        snapshot.WorkflowActivity.SupportingPrimary.Should().Be("<Ops_WorkflowsActiveMany>");
        snapshot.WorkflowActivity.SupportingSecondary.Should().Be("<Ops_WorkflowAvgRun>");
        snapshot.RecentWorkflowRuns[0].Status.Should().Be("<Ops_RunCompleted>");
        snapshot.ConnectorPreviews[2].Status.Should().Be("<Ops_ConnectorDisabled>");

        // The colors do not depend on the text.
        snapshot.SyncHealth.StatusToneToken.Should().Be("pending");
        snapshot.RecentImportedDocuments[0].HealthToneToken.Should().Be("failed");
        snapshot.RecentWorkflowRuns[0].StatusToneToken.Should().Be("success");
    }

    private static IEnumerable<string> AllTexts(OperationsOverviewSnapshot snapshot)
    {
        foreach (var card in new[]
                 {
                     snapshot.ConversationIntelligence, snapshot.SyncHealth, snapshot.IngestionBacklog,
                     snapshot.WorkflowActivity, snapshot.Connectors,
                 })
        {
            yield return card.Headline;
            yield return card.Status;
            yield return card.Detail;
            yield return card.SupportingPrimary;
            yield return card.SupportingSecondary;
        }

        foreach (var p in snapshot.RecentConversationSummaries) { yield return p.Title; yield return p.Status; yield return p.Detail; }
        foreach (var p in snapshot.RecentSyncPasses) { yield return p.Title; yield return p.Status; yield return p.Detail; }
        foreach (var p in snapshot.PendingInboxItems) { yield return p.Title; yield return p.Status; yield return p.Detail; }
        foreach (var p in snapshot.RecentImportedDocuments) { yield return p.Title; yield return p.Status; yield return p.HealthStatus; yield return p.Detail; }
        foreach (var p in snapshot.RecentWorkflowRuns) { yield return p.Title; yield return p.Status; yield return p.Detail; }
        foreach (var p in snapshot.ConnectorPreviews) { yield return p.Title; yield return p.Status; yield return p.Detail; }
    }

    private static IEnumerable<(string Status, string Token)> StatusesWithTokens(OperationsOverviewSnapshot snapshot)
    {
        foreach (var card in new[]
                 {
                     snapshot.ConversationIntelligence, snapshot.SyncHealth, snapshot.IngestionBacklog,
                     snapshot.WorkflowActivity, snapshot.Connectors,
                 })
        {
            yield return (card.Status, card.StatusToneToken);
        }

        foreach (var p in snapshot.RecentConversationSummaries) yield return (p.Status, p.StatusToneToken);
        foreach (var p in snapshot.RecentSyncPasses) yield return (p.Status, p.StatusToneToken);
        foreach (var p in snapshot.PendingInboxItems) yield return (p.Status, p.StatusToneToken);
        foreach (var p in snapshot.RecentWorkflowRuns) yield return (p.Status, p.StatusToneToken);
        foreach (var p in snapshot.ConnectorPreviews) yield return (p.Status, p.StatusToneToken);
        foreach (var p in snapshot.RecentImportedDocuments)
        {
            yield return (p.Status, p.StatusToneToken);
            yield return (p.HealthStatus, p.HealthToneToken);
        }
    }

    /// <summary>A fresh install: nothing summarized, synced, imported, run or installed.</summary>
    private void ArrangeEmptyWorkspace()
    {
        _analyticsService
            .Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationIntelligenceOverview());
        _analyticsService
            .Setup(service => service.GetWorkflowIntelligenceOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowIntelligenceOverview());
        _inboxService.Setup(service => service.GetPendingCountAsync()).ReturnsAsync(0);
        _inboxService.Setup(service => service.GetAllItemsAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<InboxItemEntity>());
        _syncService.SetupGet(service => service.Status).Returns(new SyncStatus());
        _syncService.Setup(service => service.GetConfigurationAsync()).ReturnsAsync((SyncConfiguration?)null);
        _syncService.Setup(service => service.GetSyncHistoryAsync(3)).ReturnsAsync(Array.Empty<SyncLogEntity>());
        _pluginService.Setup(service => service.GetInstalledPluginsAsync()).ReturnsAsync(Array.Empty<PluginEntity>());
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<WorkflowEntity>());
    }

    private OperationsOverviewService CreateSut(ILocalizationService? localization = null) =>
        new(
            _analyticsService.Object,
            _documentService.Object,
            _inboxService.Object,
            _pluginService.Object,
            _syncService.Object,
            _workflowService.Object,
            localization ?? EnglishResources.Create(),
            _logger);

    /// <summary>A workspace with activity in every area: summaries, sync, inbox, imports, workflows and plugins.</summary>
    private void ArrangeBusyWorkspace()
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
                },
                new InboxItemEntity
                {
                    Id = 32,
                    DocumentId = 503,
                    FileName = "Customer escalation thread",
                    FileType = "EmailMessage",
                    SourceType = "email-connector",
                    AddedAt = DateTime.UtcNow.AddMinutes(-16),
                    ProcessedAt = DateTime.UtcNow.AddMinutes(-14)
                }
            ]);

        _documentService.Setup(service => service.GetDocumentAsync(501))
            .ReturnsAsync(new DocumentEntity
            {
                Id = 501,
                FileName = "Sprint planning email",
                IndexingStatus = "pending",
                ChunkCount = 0
            });
        _documentService.Setup(service => service.GetDocumentAsync(502))
            .ReturnsAsync(new DocumentEntity
            {
                Id = 502,
                FileName = "Quarterly roadmap meeting",
                IndexingStatus = "completed",
                ChunkCount = 8,
                LastIndexedAt = DateTime.UtcNow.AddMinutes(-12)
            });
        _documentService.Setup(service => service.GetDocumentAsync(503))
            .ReturnsAsync(new DocumentEntity
            {
                Id = 503,
                FileName = "Customer escalation thread",
                IndexingStatus = "failed",
                ChunkCount = 0,
                IndexingError = "Embedding request failed."
            });

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
    }
}

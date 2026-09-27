using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class OperationsViewModelTests
{
    private readonly Mock<IOperationsActionService> _operationsActionService = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();
    private readonly Mock<IOperationsOverviewService> _operationsOverviewService = new();

    [Fact]
    public async Task LoadAsync_maps_snapshot_and_builds_summary()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "5",
                    Status = "Durable recall current",
                    StatusKind = OperationsStatusKind.RecallCurrent,
                    Detail = "6 stored snapshots · latest 10 minutes ago"
                },
                RecentConversationSummaries =
                [
                    new OperationsConversationPreview
                    {
                        ConversationId = 42,
                        Title = "Durable memory rollout",
                        Status = "Current",
                        StatusKind = OperationsStatusKind.SummaryCurrent,
                        Detail = "Persistent summary coverage is catching the latest recall state."
                    }
                ],
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Syncing the full workspace."
                },
                RecentSyncPasses =
                [
                    new OperationsSyncPreview
                    {
                        Title = "Import sync",
                        Status = "Success",
                        StatusKind = OperationsStatusKind.SyncPassSucceeded,
                        Detail = "12 changes · 3s · 9 minutes ago"
                    }
                ],
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    StatusKind = OperationsStatusKind.BacklogClear,
                    Detail = "Connector and watch-folder imports will surface here."
                },
                PendingInboxItems =
                [
                    new OperationsInboxPreview
                    {
                        Title = "Board update.docx",
                        Status = "Email Connector",
                        Detail = "Document · suggest Leadership · 12 minutes ago"
                    }
                ],
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Searchable",
                        Health = OperationsDocumentHealth.Searchable,
                        Detail = "Email Message · vaulted 28 minutes ago"
                    }
                ],
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
                    StatusKind = OperationsStatusKind.WorkflowSuccessRate,
                    SupportingPrimary = "2 active / 30d",
                    SupportingSecondary = "42s avg run",
                    Detail = "Top workflow: Research Briefing · 4 runs"
                },
                RecentWorkflowRuns =
                [
                    new OperationsWorkflowRunPreview
                    {
                        Title = "Research Briefing",
                        Status = "Completed",
                        StatusKind = OperationsStatusKind.RunCompleted,
                        Detail = "Executive summary and key findings generated successfully."
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Email Connector · Calendar Connector"
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = true,
                        CanEnableFromOperations = false,
                        Title = "Email Connector",
                        Status = "Enabled",
                        StatusKind = OperationsStatusKind.ConnectorEnabled,
                        Detail = "Connector · Brings inbox mail into Agent-X for triage and search."
                    }
                ]
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("Operations running normally");
        viewModel.SummaryDetail.Should().Contain("Durable recall current");
        viewModel.ConversationIntelligence.Headline.Should().Be("5");
        viewModel.RecentConversationSummaries.Should().ContainSingle();
        viewModel.RecentImportedDocuments.Should().ContainSingle();
        viewModel.RecentImportedDocuments.Single().HealthStatus.Should().Be("Searchable");
        viewModel.WorkflowActivity.SupportingPrimary.Should().Be("2 active / 30d");
        viewModel.RecentWorkflowRuns.Should().ContainSingle();
        viewModel.Connectors.Status.Should().Be("2 connectors enabled");
        viewModel.ConnectorPreviews.Should().ContainSingle();
        viewModel.OverviewStatusTiles.Should().HaveCount(5);
        viewModel.OverviewStatusTiles.Select(tile => tile.Title)
            .Should().Equal("Conversation", "Sync", "Backlog", "Workflows", "Connectors");
        viewModel.RecommendedActions.Should().HaveCount(3);
        viewModel.RecommendedActions.Select(action => action.Kind)
            .Should().Equal(
                OperationsRecommendedActionKind.Navigate,
                OperationsRecommendedActionKind.Navigate,
                OperationsRecommendedActionKind.Navigate);
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_builds_clickable_header_status_tiles_for_all_operations_surfaces()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "5",
                    Status = "Durable recall current",
                    StatusKind = OperationsStatusKind.RecallCurrent,
                    Detail = "Ready"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 items awaiting triage",
                    StatusKind = OperationsStatusKind.BacklogWaiting,
                    Detail = "Inbox"
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
                    StatusKind = OperationsStatusKind.WorkflowSuccessRate,
                    Detail = "Healthy"
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Healthy"
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.OverviewStatusTiles.Should().HaveCount(5);
        viewModel.OverviewStatusTiles[0].Route.Should().Be("Analytics");
        viewModel.OverviewStatusTiles[1].Route.Should().Be("SyncSettings");
        viewModel.OverviewStatusTiles[2].Route.Should().Be("Inbox");
        viewModel.OverviewStatusTiles[3].Route.Should().Be("Workflows");
        viewModel.OverviewStatusTiles[4].Route.Should().Be("PluginManager");
        viewModel.OverviewStatusTiles[2].Status.Should().Be("2 items awaiting triage");
        viewModel.OverviewStatusTiles[4].NavigationLabel.Should().Be("Open Plugins");
    }

    [Fact]
    public async Task LoadAsync_flags_attention_when_backlog_or_sync_need_work()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "1 refresh pending",
                    StatusKind = OperationsStatusKind.RecallRefreshesPending,
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    StatusKind = OperationsStatusKind.SyncNotConfigured,
                    Detail = "Configure a shared folder."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    StatusKind = OperationsStatusKind.BacklogWaiting,
                    Detail = "Open Smart Inbox to triage imports."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    StatusKind = OperationsStatusKind.WorkflowReadyToAutomate,
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    StatusKind = OperationsStatusKind.NoPluginsInstalled,
                    Detail = "Install or enable plugins."
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("3 operational areas need attention");
        viewModel.SummaryDetail.Should().Contain("refresh pending");
        viewModel.SummaryDetail.Should().Contain("Collaborative sync is off");
        viewModel.SummaryDetail.Should().Contain("items awaiting triage");
        viewModel.RecommendedActions.Select(action => action.Kind)
            .Should().Equal(
                OperationsRecommendedActionKind.RefreshConversationSummaries,
                OperationsRecommendedActionKind.Navigate,
                OperationsRecommendedActionKind.GenerateInboxPreviews,
                OperationsRecommendedActionKind.Navigate);
    }

    [Fact]
    public async Task LoadAsync_reads_the_kinds_so_translated_text_keeps_the_attention_logic()
    {
        // The snapshot text is in the user's language. Attention, the guided actions, Sync Now
        // and the badge colors used to key off English words in it.
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "1 Aktualisierung ausstehend",
                    StatusKind = OperationsStatusKind.RecallRefreshesPending
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Nicht konfiguriert",
                    Status = "Collaborative Sync ist aus",
                    StatusKind = OperationsStatusKind.SyncNotConfigured
                },
                IngestionBacklog = new OperationsCardSnapshot { Headline = "0", StatusKind = OperationsStatusKind.BacklogClear },
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint.eml",
                        HealthStatus = "Handlungsbedarf",
                        Health = OperationsDocumentHealth.NeedsAttention
                    }
                ],
                RecentWorkflowRuns =
                [
                    new OperationsWorkflowRunPreview
                    {
                        WorkflowId = 3,
                        RunId = 9,
                        Title = "Recherche",
                        Status = "Fehlgeschlagen",
                        StatusKind = OperationsStatusKind.RunFailed
                    }
                ],
                WorkflowActivity = new OperationsCardSnapshot { Headline = "1", StatusKind = OperationsStatusKind.WorkflowRunsRecorded },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Keine Plugins installiert",
                    StatusKind = OperationsStatusKind.NoPluginsInstalled
                }
            });
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("4 operational areas need attention");
        viewModel.RunManualSyncCommand.CanExecute(null).Should().BeFalse();
        viewModel.RecommendedActions.Select(action => action.Kind).Should().Equal(
            OperationsRecommendedActionKind.RefreshConversationSummaries,
            OperationsRecommendedActionKind.Navigate,
            OperationsRecommendedActionKind.RetryImportedDocumentIndexing,
            OperationsRecommendedActionKind.Navigate);
        viewModel.RecommendedActions[2].StatusToneToken.Should().Be("failed");
        viewModel.OverviewStatusTiles[0].StatusToneToken.Should().Be("pending");
        viewModel.OverviewStatusTiles[1].StatusToneToken.Should().Be("idle");
    }

    [Fact]
    public async Task LoadAsync_builds_guided_actions_for_direct_fixes_and_setup()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "1 refresh pending",
                    StatusKind = OperationsStatusKind.RecallRefreshesPending,
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    StatusKind = OperationsStatusKind.SyncNotConfigured,
                    Detail = "Configure a shared folder."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    StatusKind = OperationsStatusKind.BacklogWaiting,
                    Detail = "Open Smart Inbox to triage imports."
                },
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Needs Attention",
                        Health = OperationsDocumentHealth.NeedsAttention,
                        Detail = "Email Message · Embedding request failed."
                    }
                ],
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    StatusKind = OperationsStatusKind.WorkflowReadyToAutomate,
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 plugin installed",
                    StatusKind = OperationsStatusKind.PluginsInstalled,
                    Detail = "Open Plugin Manager to enable connectors."
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = false,
                        CanEnableFromOperations = true,
                        Title = "Email Connector",
                        Status = "Disabled",
                        StatusKind = OperationsStatusKind.ConnectorDisabled,
                        Detail = "Connector · Brings inbox mail into Agent-X."
                    }
                ]
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.RecommendedActions.Should().HaveCount(4);
        viewModel.RecommendedActions[0].Kind.Should().Be(OperationsRecommendedActionKind.RefreshConversationSummaries);
        viewModel.RecommendedActions[1].Kind.Should().Be(OperationsRecommendedActionKind.Navigate);
        viewModel.RecommendedActions[1].Route.Should().Be("SyncSettings");
        viewModel.RecommendedActions[2].Kind.Should().Be(OperationsRecommendedActionKind.GenerateInboxPreviews);
        viewModel.RecommendedActions[3].Kind.Should().Be(OperationsRecommendedActionKind.RetryImportedDocumentIndexing);
        viewModel.RecommendedActions[3].TargetId.Should().Be(501);
        viewModel.HasRecommendedActions.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_flags_attention_for_imported_documents_and_disabled_connectors()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "Durable recall current",
                    StatusKind = OperationsStatusKind.RecallCurrent,
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    StatusKind = OperationsStatusKind.BacklogClear,
                    Detail = "No pending items."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 runs recorded",
                    StatusKind = OperationsStatusKind.WorkflowRunsRecorded,
                    SupportingPrimary = "1 active / 30d",
                    SupportingSecondary = "12s avg run",
                    Detail = "Top workflow: Research Briefing"
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 plugin installed",
                    StatusKind = OperationsStatusKind.PluginsInstalled,
                    Detail = "Open Plugin Manager to enable connectors."
                },
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Needs Attention",
                        Health = OperationsDocumentHealth.NeedsAttention,
                        Detail = "Email Message · Embedding request failed."
                    }
                ],
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = false,
                        CanEnableFromOperations = true,
                        Title = "Email Connector",
                        Status = "Disabled",
                        StatusKind = OperationsStatusKind.ConnectorDisabled,
                        Detail = "Connector · Brings inbox mail into Agent-X."
                    }
                ]
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("2 operational areas need attention");
        viewModel.SummaryDetail.Should().Contain("Imported documents need indexing");
        viewModel.SummaryDetail.Should().Contain("Connectors can be enabled");
    }

    [Fact]
    public async Task LoadAsync_flags_attention_for_failed_workflow_runs()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "Durable recall current",
                    StatusKind = OperationsStatusKind.RecallCurrent,
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    StatusKind = OperationsStatusKind.BacklogClear,
                    Detail = "No pending items."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "9",
                    Status = "78% success rate",
                    StatusKind = OperationsStatusKind.WorkflowSuccessRate,
                    SupportingPrimary = "2 active / 30d",
                    SupportingSecondary = "34s avg run",
                    Detail = "Top workflow: Research Briefing"
                },
                RecentWorkflowRuns =
                [
                    new OperationsWorkflowRunPreview
                    {
                        WorkflowId = 42,
                        RunId = 77,
                        Title = "Research Briefing",
                        Status = "Failed",
                        StatusKind = OperationsStatusKind.RunFailed,
                        Detail = "Step 2 failed while drafting the synthesis."
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Email Connector · Calendar Connector"
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("1 operational area needs attention");
        viewModel.SummaryDetail.Should().Contain("Workflow runs need review");
    }

    [Fact]
    public async Task LoadAsync_summarizes_overflow_attention_areas()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "1 refresh pending",
                    StatusKind = OperationsStatusKind.RecallRefreshesPending,
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    StatusKind = OperationsStatusKind.SyncNotConfigured,
                    Detail = "Configure a shared folder."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    StatusKind = OperationsStatusKind.BacklogWaiting,
                    Detail = "Open Smart Inbox to triage imports."
                },
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Needs Attention",
                        Health = OperationsDocumentHealth.NeedsAttention,
                        Detail = "Email Message · Embedding request failed."
                    }
                ],
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = false,
                        CanEnableFromOperations = true,
                        Title = "Email Connector",
                        Status = "Disabled",
                        StatusKind = OperationsStatusKind.ConnectorDisabled,
                        Detail = "Connector · Brings inbox mail into Agent-X."
                    }
                ],
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "9",
                    Status = "78% success rate",
                    StatusKind = OperationsStatusKind.WorkflowSuccessRate,
                    Detail = "Top workflow: Research Briefing"
                },
                RecentWorkflowRuns =
                [
                    new OperationsWorkflowRunPreview
                    {
                        WorkflowId = 42,
                        RunId = 77,
                        Title = "Research Briefing",
                        Status = "Failed",
                        StatusKind = OperationsStatusKind.RunFailed,
                        Detail = "Step 2 failed while drafting the synthesis."
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 plugin installed",
                    StatusKind = OperationsStatusKind.PluginsInstalled,
                    Detail = "Open Plugin Manager to enable connectors."
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("6 operational areas need attention");
        viewModel.SummaryDetail.Should().Contain("1 refresh pending");
        viewModel.SummaryDetail.Should().Contain("Collaborative sync is off");
        viewModel.SummaryDetail.Should().Contain("3 items awaiting triage");
        viewModel.SummaryDetail.Should().Contain("3 more");

        // Plain punctuation from the resources, not a middle dot.
        viewModel.SummaryDetail.Should().Be(
            "1 refresh pending, Collaborative sync is off, 3 items awaiting triage, 3 more");
    }

    [Fact]
    public void Shows_the_default_overview_in_the_users_language_before_the_first_load()
    {
        var viewModel = CreateViewModel();

        viewModel.SummaryHeadline.Should().Be("Operations ready");
        viewModel.SummaryDetail.Should().StartWith("Unified status for conversation intelligence");
        viewModel.SyncHealth.Headline.Should().Be("Not configured");
        viewModel.WorkflowActivity.SupportingSecondary.Should().Be("Avg duration unavailable");
        viewModel.OverviewStatusTiles.Select(tile => tile.NavigationLabel)
            .Should().Equal("Open Analytics", "Open Sync", "Open Inbox", "Open Workflows", "Open Plugins");
        viewModel.RecommendedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Shows_a_load_failure_in_the_users_language()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var viewModel = CreateViewModel(ReswLocalization.For("de"));
        await viewModel.LoadAsync();

        viewModel.ErrorMessage.Should().StartWith("Die Operations-Übersicht konnte nicht geladen werden.");
        viewModel.SummaryHeadline.Should().Be("Operations nicht verfügbar");
        viewModel.SyncHealth.Status.Should().Be("Collaborative Sync ist aus");
        viewModel.RecommendedActions.Select(action => action.CommandText).Should().Contain("Sync öffnen");
    }

    [Fact]
    public async Task LoadAsync_uses_fallback_snapshot_when_service_fails()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.HasError.Should().BeTrue();
        viewModel.SummaryHeadline.Should().Be("Operations unavailable");
        viewModel.ConversationIntelligence.Status.Should().Be("Durable recall inactive");
        viewModel.SyncHealth.Status.Should().Be("Collaborative sync is off");
        viewModel.RecentConversationSummaries.Should().BeEmpty();
        viewModel.RecentWorkflowRuns.Should().BeEmpty();
    }

    [Fact]
    public void Navigation_commands_route_to_expected_pages()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        viewModel.NavigateToDashboardCommand.Execute(null);
        viewModel.NavigateToAnalyticsCommand.Execute(null);
        viewModel.NavigateToSyncSettingsCommand.Execute(null);
        viewModel.NavigateToInboxCommand.Execute(null);
        viewModel.NavigateToKnowledgeVaultCommand.Execute(null);
        viewModel.NavigateToWorkflowsCommand.Execute(null);
        viewModel.NavigateToPluginManagerCommand.Execute(null);

        navigations.Should().Equal("Dashboard", "Analytics", "SyncSettings", "Inbox", "KnowledgeVault", "Workflows", "PluginManager");
    }

    [Fact]
    public void OpenOverviewStatusTileCommand_routes_to_tile_destination()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        viewModel.OpenOverviewStatusTileCommand.Execute(new OperationsOverviewStatusTile(
            "Connectors",
            "2",
            "2 connectors enabled",
            "PluginManager",
            "Open Plugins"));

        navigations.Should().Equal("PluginManager");
    }

    [Fact]
    public void Drill_in_preview_commands_stage_requests_and_navigate()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        viewModel.OpenInboxPreviewCommand.Execute(new OperationsInboxPreview
        {
            ItemId = 22,
            Title = "Board update.docx"
        });
        viewModel.OpenImportedDocumentPreviewCommand.Execute(new OperationsImportedDocumentPreview
        {
            DocumentId = 501,
            Title = "Sprint planning email"
        });
        viewModel.OpenWorkflowRunPreviewCommand.Execute(new OperationsWorkflowRunPreview
        {
            WorkflowId = 42,
            RunId = 77,
            Title = "Research Briefing"
        });
        viewModel.OpenSyncPreviewCommand.Execute(new OperationsSyncPreview
        {
            SyncLogId = 9,
            Title = "Import sync"
        });
        viewModel.OpenConversationPreviewCommand.Execute(new OperationsConversationPreview
        {
            ConversationId = 42,
            Title = "Durable memory rollout"
        });
        viewModel.OpenConnectorPreviewCommand.Execute(new OperationsConnectorPreview
        {
            PluginId = 301,
            Title = "Email Connector"
        });

        _operationsDrillInService.Verify(service => service.StageInboxRequest(
            It.Is<OperationsInboxDrillInRequest>(request =>
                request.ItemId == 22 &&
                request.SourceLabel.Contains("Board update.docx"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StageDocumentRequest(
            It.Is<OperationsDocumentDrillInRequest>(request =>
                request.DocumentId == 501 &&
                request.SourceLabel.Contains("Sprint planning email"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StageWorkflowRunRequest(
            It.Is<OperationsWorkflowRunDrillInRequest>(request =>
                request.WorkflowId == 42 &&
                request.RunId == 77 &&
                request.SourceLabel.Contains("Research Briefing"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StageSyncRequest(
            It.Is<OperationsSyncDrillInRequest>(request =>
                request.SyncLogId == 9 &&
                request.SourceLabel.Contains("Import sync"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StageConversationRequest(
            It.Is<OperationsConversationDrillInRequest>(request =>
                request.ConversationId == 42 &&
                request.SourceLabel.Contains("Durable memory rollout"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StagePluginRequest(
            It.Is<OperationsPluginDrillInRequest>(request =>
                request.PluginId == 301 &&
                request.SourceLabel.Contains("Email Connector"))), Times.Once);

        navigations.Should().Equal("Inbox", "KnowledgeVault", "Workflows", "SyncSettings", "Analytics", "PluginManager");
    }

    [Fact]
    public async Task RefreshConversationSummariesAsync_runs_action_and_reloads_snapshot()
    {
        _operationsOverviewService.SetupSequence(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "1 refresh pending",
                    StatusKind = OperationsStatusKind.RecallRefreshesPending,
                    Detail = "3 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot()
            })
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "Durable recall current",
                    StatusKind = OperationsStatusKind.RecallCurrent,
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot()
            });
        _operationsActionService.Setup(service => service.RefreshConversationSummariesAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsActionResult(true, "Refreshed 1 conversation summary."));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.RefreshConversationSummariesCommand.ExecuteAsync(null);

        viewModel.ConversationIntelligence.Status.Should().Be("Durable recall current");
        viewModel.HasActionMessage.Should().BeTrue();
        viewModel.ActionMessage.Should().Contain("Refreshed 1 conversation summary");
    }

    [Fact]
    public async Task RunManualSyncCommand_is_disabled_when_sync_is_not_configured()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    StatusKind = OperationsStatusKind.SyncNotConfigured,
                    Detail = "Configure a shared folder."
                },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot()
            });

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        viewModel.RunManualSyncCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateInboxPreviewsAsync_runs_action_and_reloads_snapshot()
    {
        _operationsOverviewService.SetupSequence(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    StatusKind = OperationsStatusKind.BacklogWaiting,
                    Detail = "Open Smart Inbox to triage imports."
                },
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot()
            })
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 item awaiting triage",
                    StatusKind = OperationsStatusKind.BacklogWaiting,
                    Detail = "Open Smart Inbox to triage imports."
                },
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot()
            });
        _operationsActionService.Setup(service => service.GenerateInboxPreviewsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsActionResult(true, "Generated AI previews for pending inbox items."));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.GenerateInboxPreviewsCommand.ExecuteAsync(null);

        viewModel.IngestionBacklog.Headline.Should().Be("1");
        viewModel.HasActionMessage.Should().BeTrue();
        viewModel.ActionMessage.Should().Contain("Generated AI previews");
    }

    [Fact]
    public async Task EnableConnectorAsync_runs_action_and_reloads_snapshot()
    {
        _operationsOverviewService.SetupSequence(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "1 plugin installed",
                    StatusKind = OperationsStatusKind.PluginsInstalled,
                    Detail = "Open Plugin Manager to enable connectors and extensions."
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = false,
                        CanEnableFromOperations = true,
                        Title = "Email Connector",
                        Status = "Disabled",
                        StatusKind = OperationsStatusKind.ConnectorDisabled,
                        Detail = "Connector · Brings inbox mail into Agent-X for triage and search."
                    }
                ]
            })
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 connector enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Email Connector"
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = true,
                        CanEnableFromOperations = false,
                        Title = "Email Connector",
                        Status = "Enabled",
                        StatusKind = OperationsStatusKind.ConnectorEnabled,
                        Detail = "Connector · Brings inbox mail into Agent-X for triage and search."
                    }
                ]
            });
        _operationsActionService.Setup(service => service.EnableConnectorAsync(301, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsActionResult(true, "Enabled Email Connector."));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        var preview = viewModel.ConnectorPreviews.Single();
        viewModel.EnableConnectorCommand.CanExecute(preview).Should().BeTrue();

        await viewModel.EnableConnectorCommand.ExecuteAsync(preview);

        viewModel.Connectors.Status.Should().Be("1 connector enabled");
        viewModel.HasActionMessage.Should().BeTrue();
        viewModel.ActionMessage.Should().Be("Enabled Email Connector.");
        viewModel.ConnectorPreviews.Single().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteRecommendedActionAsync_dispatches_enable_connector_fix()
    {
        _operationsOverviewService.SetupSequence(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 plugin installed",
                    StatusKind = OperationsStatusKind.PluginsInstalled,
                    Detail = "Open Plugin Manager to enable connectors."
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = false,
                        CanEnableFromOperations = true,
                        Title = "Email Connector",
                        Status = "Disabled",
                        StatusKind = OperationsStatusKind.ConnectorDisabled,
                        Detail = "Connector · Brings inbox mail into Agent-X."
                    }
                ]
            })
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 connector enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Email Connector"
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = true,
                        CanEnableFromOperations = false,
                        Title = "Email Connector",
                        Status = "Enabled",
                        StatusKind = OperationsStatusKind.ConnectorEnabled,
                        Detail = "Connector · Brings inbox mail into Agent-X."
                    }
                ]
            });
        _operationsActionService.Setup(service => service.EnableConnectorAsync(301, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsActionResult(true, "Enabled Email Connector."));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        var action = viewModel.RecommendedActions.Single(item => item.Kind == OperationsRecommendedActionKind.EnableConnector);
        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        _operationsActionService.Verify(service => service.EnableConnectorAsync(301, It.IsAny<CancellationToken>()), Times.Once);
        viewModel.ActionMessage.Should().Be("Enabled Email Connector.");
    }

    [Fact]
    public async Task RetryImportedDocumentIndexingAsync_runs_action_and_reloads_snapshot()
    {
        _operationsOverviewService.SetupSequence(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot(),
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Needs Attention",
                        Health = OperationsDocumentHealth.NeedsAttention,
                        Detail = "Email Message · Embedding request failed."
                    }
                ]
            })
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot(),
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Processing",
                        Health = OperationsDocumentHealth.Processing,
                        Detail = "Email Message · queued for indexing"
                    }
                ]
            });
        _operationsActionService.Setup(service => service.ReindexImportedDocumentAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsActionResult(true, "Queued imported document for re-indexing."));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        var preview = viewModel.RecentImportedDocuments.Single();
        viewModel.RetryImportedDocumentIndexingCommand.CanExecute(preview).Should().BeTrue();

        await viewModel.RetryImportedDocumentIndexingCommand.ExecuteAsync(preview);

        viewModel.RecentImportedDocuments.Single().HealthStatus.Should().Be("Processing");
        viewModel.HasActionMessage.Should().BeTrue();
        viewModel.ActionMessage.Should().Be("Queued imported document for re-indexing.");
        _operationsActionService.Verify(service => service.ReindexImportedDocumentAsync(501, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryImportedDocumentIndexingCommand_is_disabled_for_searchable_document()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot(),
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Sprint planning email",
                        Status = "Email Connector",
                        HealthStatus = "Searchable",
                        Health = OperationsDocumentHealth.Searchable,
                        Detail = "Email Message · searchable now"
                    }
                ]
            });

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        viewModel.RetryImportedDocumentIndexingCommand.CanExecute(viewModel.RecentImportedDocuments.Single()).Should().BeFalse();
    }

    [Fact]
    public async Task EnableConnectorCommand_is_disabled_for_enabled_preview()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 connector enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Email Connector"
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = true,
                        CanEnableFromOperations = false,
                        Title = "Email Connector",
                        Status = "Enabled",
                        StatusKind = OperationsStatusKind.ConnectorEnabled,
                        Detail = "Connector · Brings inbox mail into Agent-X for triage and search."
                    }
                ]
            });

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        viewModel.EnableConnectorCommand.CanExecute(viewModel.ConnectorPreviews.Single()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteRecommendedActionAsync_stages_failed_workflow_run_before_navigation()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "5",
                    Status = "Durable recall current",
                    StatusKind = OperationsStatusKind.RecallCurrent,
                    Detail = "Healthy"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    StatusKind = OperationsStatusKind.SyncStandingBy,
                    Detail = "Ready"
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    StatusKind = OperationsStatusKind.BacklogClear,
                    Detail = "Ready"
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
                    StatusKind = OperationsStatusKind.WorkflowSuccessRate,
                    SupportingPrimary = "2 active / 30d",
                    SupportingSecondary = "42s avg run",
                    Detail = "Top workflow: Research Briefing"
                },
                RecentWorkflowRuns =
                [
                    new OperationsWorkflowRunPreview
                    {
                        WorkflowId = 41,
                        RunId = 88,
                        Title = "Research Briefing",
                        Status = "Failed",
                        StatusKind = OperationsStatusKind.RunFailed,
                        Detail = "Model timed out while producing executive summary."
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 connector enabled",
                    StatusKind = OperationsStatusKind.ConnectorsEnabled,
                    Detail = "Email Connector"
                }
            });

        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        await viewModel.LoadAsync();

        var action = viewModel.RecommendedActions.Single(item => item.Route == "Workflows");
        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        _operationsDrillInService.Verify(service => service.StageWorkflowRunRequest(
            It.Is<OperationsWorkflowRunDrillInRequest>(request =>
                request.WorkflowId == 41 &&
                request.RunId == 88 &&
                request.SourceLabel.Contains("Review Research Briefing"))), Times.Once);
        navigations.Should().Equal("Workflows");
    }

    [Fact]
    public async Task RunManualSyncAsync_shows_an_error_when_the_action_throws()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot(),
                SyncHealth = new OperationsCardSnapshot { Headline = "Configured", Status = "Standing by", StatusKind = OperationsStatusKind.SyncStandingBy, Detail = "Ready" },
                IngestionBacklog = new OperationsCardSnapshot(),
                WorkflowActivity = new OperationsCardSnapshot(),
                Connectors = new OperationsCardSnapshot()
            });
        _operationsActionService.Setup(service => service.RunManualSyncAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Sync folder is offline"));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.RunManualSyncCommand.ExecuteAsync(null);

        viewModel.HasActionError.Should().BeTrue();
        viewModel.ActionErrorMessage.Should().Be("Sync failed: Sync folder is offline");
        viewModel.IsRunningManualSync.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshConversationSummariesAsync_names_the_action_that_failed()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot());
        _operationsActionService.Setup(service => service.RefreshConversationSummariesAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Summary store is locked"));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.RefreshConversationSummariesCommand.ExecuteAsync(null);

        viewModel.HasActionError.Should().BeTrue();
        viewModel.ActionErrorMessage.Should().Be(
            "Refreshing conversation summaries failed: Summary store is locked");
    }

    private OperationsViewModel CreateViewModel(ILocalizationService? localization = null) =>
        new(
            _operationsActionService.Object,
            _operationsDrillInService.Object,
            _operationsOverviewService.Object,
            localization ?? EnglishResources.Create(),
            Log.ForContext<OperationsViewModelTests>());
}

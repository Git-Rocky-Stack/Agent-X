using AgentX.App.Services;
using AgentX.App.ViewModels;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class OperationsViewModelTests
{
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
                    Detail = "6 stored snapshots · latest 10 minutes ago"
                },
                RecentConversationSummaries =
                [
                    new OperationsConversationPreview
                    {
                        Title = "Durable memory rollout",
                        Status = "Current",
                        Detail = "Persistent summary coverage is catching the latest recall state."
                    }
                ],
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    Detail = "Syncing the full workspace."
                },
                RecentSyncPasses =
                [
                    new OperationsSyncPreview
                    {
                        Title = "Import sync",
                        Status = "Success",
                        Detail = "12 changes · 3s · 9 minutes ago"
                    }
                ],
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
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
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
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
                        Detail = "Executive summary and key findings generated successfully."
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    Detail = "Email Connector · Calendar Connector"
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        Title = "Email Connector",
                        Status = "Enabled",
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
        viewModel.WorkflowActivity.SupportingPrimary.Should().Be("2 active / 30d");
        viewModel.RecentWorkflowRuns.Should().ContainSingle();
        viewModel.Connectors.Status.Should().Be("2 connectors enabled");
        viewModel.ConnectorPreviews.Should().ContainSingle();
        viewModel.HasError.Should().BeFalse();
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
                    Detail = "4 stored snapshots"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    Detail = "Configure a shared folder."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    Detail = "Open Smart Inbox to triage imports."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    Detail = "Install or enable plugins."
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("3 operational areas need attention");
        viewModel.SummaryDetail.Should().Contain("refresh pending");
        viewModel.SummaryDetail.Should().Contain("Collaborative sync is off");
        viewModel.SummaryDetail.Should().Contain("items awaiting triage");
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
        viewModel.NavigateRequested = page => navigations.Add(page);

        viewModel.NavigateToDashboardCommand.Execute(null);
        viewModel.NavigateToAnalyticsCommand.Execute(null);
        viewModel.NavigateToSyncSettingsCommand.Execute(null);
        viewModel.NavigateToInboxCommand.Execute(null);
        viewModel.NavigateToWorkflowsCommand.Execute(null);
        viewModel.NavigateToPluginManagerCommand.Execute(null);

        navigations.Should().Equal("Dashboard", "Analytics", "SyncSettings", "Inbox", "Workflows", "PluginManager");
    }

    [Fact]
    public void Drill_in_preview_commands_stage_requests_and_navigate()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = page => navigations.Add(page);

        viewModel.OpenInboxPreviewCommand.Execute(new OperationsInboxPreview
        {
            ItemId = 22,
            Title = "Board update.docx"
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
        _operationsDrillInService.Verify(service => service.StageWorkflowRunRequest(
            It.Is<OperationsWorkflowRunDrillInRequest>(request =>
                request.WorkflowId == 42 &&
                request.RunId == 77 &&
                request.SourceLabel.Contains("Research Briefing"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StageSyncRequest(
            It.Is<OperationsSyncDrillInRequest>(request =>
                request.SyncLogId == 9 &&
                request.SourceLabel.Contains("Import sync"))), Times.Once);
        _operationsDrillInService.Verify(service => service.StagePluginRequest(
            It.Is<OperationsPluginDrillInRequest>(request =>
                request.PluginId == 301 &&
                request.SourceLabel.Contains("Email Connector"))), Times.Once);

        navigations.Should().Equal("Inbox", "Workflows", "SyncSettings", "Analytics", "PluginManager");
    }

    private OperationsViewModel CreateViewModel() =>
        new(
            _operationsDrillInService.Object,
            _operationsOverviewService.Object,
            Log.ForContext<OperationsViewModelTests>());
}

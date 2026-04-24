using AgentX.App.Services;
using AgentX.App.ViewModels;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class OperationsViewModelTests
{
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
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    Detail = "Syncing the full workspace."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    Detail = "Connector and watch-folder imports will surface here."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
                    SupportingPrimary = "2 active / 30d",
                    SupportingSecondary = "42s avg run",
                    Detail = "Top workflow: Research Briefing · 4 runs"
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    Detail = "Email Connector · Calendar Connector"
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.SummaryHeadline.Should().Be("Operations running normally");
        viewModel.SummaryDetail.Should().Contain("Durable recall current");
        viewModel.ConversationIntelligence.Headline.Should().Be("5");
        viewModel.WorkflowActivity.SupportingPrimary.Should().Be("2 active / 30d");
        viewModel.Connectors.Status.Should().Be("2 connectors enabled");
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

    private OperationsViewModel CreateViewModel() =>
        new(
            _operationsOverviewService.Object,
            Log.ForContext<OperationsViewModelTests>());
}

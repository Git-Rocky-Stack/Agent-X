using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class DashboardViewModelTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IAiProvider> _aiProvider = new();
    private readonly Mock<IConversationService> _conversationService = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IHardwareDetector> _hardwareDetector = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<IIndexingService> _indexingService = new();
    private readonly Mock<IRagPipeline> _ragPipeline = new();
    private readonly Mock<IOperationsOverviewService> _operationsOverviewService = new();

    public DashboardViewModelTests()
    {
        _aiProvider.Setup(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _aiService.SetupGet(service => service.ActiveProvider).Returns(_aiProvider.Object);
        _aiService.SetupGet(service => service.ActiveModelId).Returns("llama3.1:8b");

        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(12L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(2_048L);
        _documentService.Setup(service => service.GetFileTypeDistributionAsync())
            .ReturnsAsync(new Dictionary<string, int>());
        _documentService.Setup(service => service.GetRecentDocumentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DocumentEntity>());

        _conversationService.Setup(service => service.GetConversationCountAsync()).ReturnsAsync(4);
        _conversationService.Setup(service => service.GetTotalTokensUsedAsync()).ReturnsAsync(1600L);
        _conversationService.Setup(service => service.GetRecentConversationsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConversationEntity>());

        _hardwareDetector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HardwareCapability
            {
                GpuName = "RTX Test",
                GpuVramBytes = 8_000_000_000,
                TotalRamBytes = 32_000_000_000,
                AvailableRamBytes = 24_000_000_000
            });

        _collectionService.Setup(service => service.GetCollectionCountAsync()).ReturnsAsync(3);
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(2);
        _indexingService.Setup(service => service.GetProcessedCountAsync()).ReturnsAsync(8);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _ragPipeline.Setup(pipeline => pipeline.GetIndexedChunkCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(120L);

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
                    Status = "2 local changes pending",
                    Detail = "Syncing the full workspace."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "4 items awaiting triage",
                    Detail = "Open Smart Inbox to triage connector and watch-folder imports."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    Detail = "Email Connector · Calendar Connector"
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
                    SupportingPrimary = "2 active / 30d",
                    SupportingSecondary = "42s avg run",
                    Detail = "Top workflow: Research Briefing · 4 runs"
                }
            });
    }

    [Fact]
    public async Task InitializeAsync_maps_shared_operations_snapshot()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        _documentService.Verify(service => service.GetRecentDocumentsAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(service => service.GetRecentConversationsAsync(5, false, It.IsAny<CancellationToken>()), Times.Once);

        viewModel.ConversationIntelligenceHeadline.Should().Be("5");
        viewModel.ConversationIntelligenceStatus.Should().Be("Durable recall current");
        viewModel.ConversationIntelligenceDetail.Should().Contain("stored snapshots");

        viewModel.SyncHealthHeadline.Should().Be("Configured");
        viewModel.SyncHealthStatus.Should().Be("2 local changes pending");
        viewModel.SyncHealthDetail.Should().Be("Syncing the full workspace.");

        viewModel.InboxHeadline.Should().Be("4");
        viewModel.InboxStatus.Should().Be("4 items awaiting triage");
        viewModel.InboxDetail.Should().Contain("connector and watch-folder");

        viewModel.ConnectorsHeadline.Should().Be("2");
        viewModel.ConnectorsStatus.Should().Be("2 connectors enabled");
        viewModel.ConnectorsDetail.Should().Contain("Email Connector");

        viewModel.WorkflowHeadline.Should().Be("7");
        viewModel.WorkflowStatus.Should().Be("86% success rate");
        viewModel.WorkflowRecentActivity.Should().Be("2 active / 30d");
        viewModel.WorkflowAverageDuration.Should().Be("42s avg run");
        viewModel.WorkflowDetail.Should().Contain("Research Briefing");
    }

    [Fact]
    public async Task InitializeAsync_keeps_operations_cards_actionable_with_empty_snapshot()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Durable recall inactive",
                    Detail = "Open Analytics to inspect summary coverage."
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    Detail = "Configure a shared folder to keep multiple installations aligned."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    Detail = "Watch folders and enabled connectors will surface new items here."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    Detail = "Install or enable plugins to bring external data and workflow extensions into the app."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.ConnectorsStatus.Should().Be("No plugins installed");
        viewModel.InboxStatus.Should().Be("Queue clear");
        viewModel.WorkflowStatus.Should().Be("Ready to automate");
        viewModel.WorkflowRecentActivity.Should().Be("No recent runs");
        viewModel.WorkflowAverageDuration.Should().Be("Avg duration unavailable");
        viewModel.WorkflowDetail.Should().Contain("Vault or Search");
    }

    [Fact]
    public void Operations_navigation_commands_route_to_expected_pages()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = page => navigations.Add(page);

        viewModel.NavigateToAnalyticsCommand.Execute(null);
        viewModel.NavigateToOperationsCommand.Execute(null);
        viewModel.NavigateToInboxCommand.Execute(null);
        viewModel.NavigateToSyncSettingsCommand.Execute(null);
        viewModel.NavigateToWorkflowsCommand.Execute(null);
        viewModel.NavigateToPluginManagerCommand.Execute(null);

        navigations.Should().Equal("Analytics", "Operations", "Inbox", "SyncSettings", "Workflows", "PluginManager");
    }

    private DashboardViewModel CreateViewModel()
    {
        return new DashboardViewModel(
            _aiService.Object,
            _conversationService.Object,
            _documentService.Object,
            _hardwareDetector.Object,
            _collectionService.Object,
            _indexingService.Object,
            _ragPipeline.Object,
            _operationsOverviewService.Object);
    }
}

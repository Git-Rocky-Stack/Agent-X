using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Workflows;
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
    private readonly Mock<IAnalyticsService> _analyticsService = new();
    private readonly Mock<IInboxService> _inboxService = new();
    private readonly Mock<ISyncService> _syncService = new();
    private readonly Mock<IWorkflowService> _workflowService = new();

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

        _analyticsService.Setup(service => service.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyticsSummary
            {
                TotalWorkflowRuns = 7
            });

        _analyticsService.Setup(service => service.GetConversationIntelligenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
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

        _inboxService.Setup(service => service.GetPendingCountAsync()).ReturnsAsync(4);

        _syncService.SetupGet(service => service.Status).Returns(new SyncStatus
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

        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity { Id = 1, Name = "Research Briefing", IsEnabled = true, RunCount = 4 },
                new WorkflowEntity { Id = 2, Name = "Inbox Cleanup", IsEnabled = true, RunCount = 1 },
            ]);
    }

    [Fact]
    public async Task InitializeAsync_maps_operations_overview_metrics()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        _documentService.Verify(service => service.GetRecentDocumentsAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(service => service.GetRecentConversationsAsync(5, false, It.IsAny<CancellationToken>()), Times.Once);

        viewModel.ConversationIntelligenceHeadline.Should().Be("5");
        viewModel.ConversationIntelligenceStatus.Should().Be("Durable recall current");
        viewModel.ConversationIntelligenceDetail.Should().Contain("stored snapshots");

        viewModel.InboxHeadline.Should().Be("4");
        viewModel.InboxStatus.Should().Be("4 items awaiting triage");

        viewModel.WorkflowHeadline.Should().Be("7");
        viewModel.WorkflowStatus.Should().Be("7 runs recorded");
        viewModel.WorkflowDetail.Should().Contain("Research Briefing");

        viewModel.SyncHealthHeadline.Should().Be("Configured");
        viewModel.SyncHealthStatus.Should().Be("2 local changes pending");
        viewModel.SyncHealthDetail.Should().Be("Syncing the full workspace.");
    }

    [Fact]
    public void Operations_navigation_commands_route_to_expected_pages()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = page => navigations.Add(page);

        viewModel.NavigateToAnalyticsCommand.Execute(null);
        viewModel.NavigateToInboxCommand.Execute(null);
        viewModel.NavigateToSyncSettingsCommand.Execute(null);
        viewModel.NavigateToWorkflowsCommand.Execute(null);

        navigations.Should().Equal("Analytics", "Inbox", "SyncSettings", "Workflows");
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
            _analyticsService.Object,
            _inboxService.Object,
            _syncService.Object,
            _workflowService.Object);
    }
}

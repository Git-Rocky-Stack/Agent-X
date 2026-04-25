using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class QuickActionsViewModelTests
{
    private readonly Mock<ISummaryService> _summaryService = new();
    private readonly Mock<IDuplicateDetectionService> _duplicateDetectionService = new();
    private readonly Mock<IOrganizationSuggestionService> _organizationSuggestionService = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IOperationsOverviewService> _operationsOverviewService = new();

    public QuickActionsViewModelTests()
    {
        _documentService.Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DocumentEntity
                {
                    Id = 101,
                    FileName = "Board Brief.pdf",
                    FileType = "pdf",
                    FileSizeBytes = 4096,
                    IndexingStatus = "completed"
                },
                new DocumentEntity
                {
                    Id = 102,
                    FileName = "Connector Intake.msg",
                    FileType = "msg",
                    FileSizeBytes = 2048,
                    IndexingStatus = "pending"
                }
            ]);

        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    Detail = "Open Smart Inbox to triage imports."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    Detail = "Install or enable plugins."
                }
            });

        _summaryService.Setup(service => service.SummarizeDocumentAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Board brief summary");
        _summaryService.Setup(service => service.ExtractKeyPointsAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Point A", "Point B"]);
        _duplicateDetectionService.Setup(service => service.FindNearDuplicatesAsync(It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DuplicateGroup>());
        _organizationSuggestionService.Setup(service => service.SuggestOrganizationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OrganizationSuggestion>());
    }

    [Fact]
    public async Task InitializeAsync_builds_contextual_actions_from_selected_document_and_intake_state()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.SelectedDocument.Should().NotBeNull();
        viewModel.SelectedDocument!.Id.Should().Be(101);
        viewModel.RecommendedActions.Should().HaveCount(4);
        viewModel.RecommendedActions.Select(action => action.Kind)
            .Should().Equal(
                QuickActionRecommendedActionKind.SummarizeSelectedDocument,
                QuickActionRecommendedActionKind.Navigate,
                QuickActionRecommendedActionKind.ExtractKeyPointsSelectedDocument,
                QuickActionRecommendedActionKind.Navigate);
        viewModel.RecommendedActions[1].Route.Should().Be("Inbox");
        viewModel.RecommendedActions[3].Route.Should().Be("PluginManager");
        viewModel.HasRecommendedActions.Should().BeTrue();
    }

    [Fact]
    public async Task SelectedDocumentChanged_rebuilds_actions_for_unready_document()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.SelectedDocument = viewModel.AvailableDocuments.Single(item => item.Id == 102);

        viewModel.RecommendedActions[0].Kind.Should().Be(QuickActionRecommendedActionKind.Navigate);
        viewModel.RecommendedActions[0].Route.Should().Be("KnowledgeVault");
        viewModel.RecommendedActions[0].StatusLabel.Should().Be("Queued");
    }

    [Fact]
    public async Task ExecuteRecommendedActionAsync_runs_selected_document_summary()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        var action = viewModel.RecommendedActions.First(item =>
            item.Kind == QuickActionRecommendedActionKind.SummarizeSelectedDocument);

        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        viewModel.SelectedTabIndex.Should().Be(0);
        viewModel.SummaryResult.Should().Be("Board brief summary");
        viewModel.HasSummaryResult.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteRecommendedActionAsync_navigates_to_requested_page()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = page => navigations.Add(page);

        await viewModel.InitializeAsync();

        var action = viewModel.RecommendedActions.First(item => item.Route == "Inbox");
        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        navigations.Should().Equal("Inbox");
    }

    private QuickActionsViewModel CreateViewModel() =>
        new(
            _summaryService.Object,
            _duplicateDetectionService.Object,
            _organizationSuggestionService.Object,
            _documentService.Object,
            _operationsOverviewService.Object,
            Log.ForContext<QuickActionsViewModelTests>());
}

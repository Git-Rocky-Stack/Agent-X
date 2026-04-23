using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Services.Collections;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class SearchViewModelTests
{
    private readonly Mock<ISemanticSearchService> _searchService = new();
    private readonly Mock<IHybridSearchOrchestrator> _hybridSearch = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly Mock<IWorkflowLaunchService> _workflowLaunchService = new();

    [Fact]
    public void LaunchResultIntoWorkflow_stages_request_and_navigates()
    {
        WorkflowLaunchRequest? stagedRequest = null;
        string? navigatedPage = null;

        _workflowLaunchService.Setup(service => service.StageRequest(It.IsAny<WorkflowLaunchRequest>()))
            .Callback<WorkflowLaunchRequest>(request => stagedRequest = request);

        var viewModel = new SearchViewModel(
            _searchService.Object,
            _hybridSearch.Object,
            _documentService.Object,
            _collectionService.Object,
            _logger.Object,
            _workflowLaunchService.Object)
        {
            QueryText = "market outlook",
            NavigateRequested = page => navigatedPage = page
        };

        viewModel.LaunchResultIntoWorkflowCommand.Execute(new SearchResultItem
        {
            DocumentId = 42,
            FileName = "MarketNotes.md",
            FilePath = @"C:\docs\MarketNotes.md",
            FileType = "md",
            Excerpt = "A focused excerpt from the matched section.",
            RelevancePercent = 87,
            PageNumber = 3
        });

        stagedRequest.Should().NotBeNull();
        stagedRequest!.InputText.Should().Contain("Query: market outlook");
        stagedRequest.InputText.Should().Contain("Document: MarketNotes.md");
        stagedRequest.InputText.Should().Contain("Relevance: 87%");
        stagedRequest.InputText.Should().Contain("Page: 3");
        stagedRequest.RecommendedWorkflowName.Should().Be("Research Brief");
        navigatedPage.Should().Be("Workflows");
    }
}

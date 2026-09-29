using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Collections;
using AgentX.Tests.Helpers;
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
            EnglishResources.Create(),
            _workflowLaunchService.Object)
        {
            QueryText = "market outlook",
            NavigateRequested = (page, _) => navigatedPage = page
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
        stagedRequest.InputText.Should().Contain("Excerpt" + Environment.NewLine + "-------" + Environment.NewLine);
        stagedRequest.SourceLabel.Should().Be("Loaded search context from \"MarketNotes.md\"");
        stagedRequest.RecommendedWorkflowName.Should().Be("Research Brief");
        navigatedPage.Should().Be("Workflows");
    }

    // -- Status wording -------------------------------------------------------
    // The status line reads from the resources, with separate wording for one result.

    [Theory]
    [InlineData(0, "No results found in ")]
    [InlineData(1, "Found 1 result in ")]
    [InlineData(2, "Found 2 results in ")]
    public async Task SearchAsync_ReportsTheResultCountInTheStatusLine(int count, string expectedStart)
    {
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, count).Select(id => Result(id, $"doc{id}.md", 0.9f)).ToArray());
        var viewModel = CreateViewModel();
        viewModel.StatusMessage.Should().Be("Ready to search");
        viewModel.QueryText = "plan";

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StatusMessage.Should().StartWith(expectedStart).And.EndWith("ms");
    }

    [Fact]
    public async Task SavedFilters_ShowTheModeNameAndRestoreTheMode()
    {
        // The badge shows the mode's name from the resources, and applying the filter
        // restores the mode itself rather than parsing the badge text.
        _searchService.Setup(service => service.GetSavedFiltersAsync())
            .ReturnsAsync(new[]
            {
                new SearchHistoryEntry { Id = 7, QueryText = "invoice", SearchType = "hybrid", SearchedAt = DateTime.UtcNow }
            });
        _searchService.Setup(service => service.GetSearchHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<SearchHistoryEntry>());
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SearchResult>());
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        var filter = viewModel.SavedFilters.Should().ContainSingle().Subject;
        filter.SearchType.Should().Be("hybrid");
        filter.Mode.Should().Be(SearchMode.Hybrid);

        await viewModel.ApplySavedFilterCommand.ExecuteAsync(filter);

        viewModel.SearchMode.Should().Be(SearchMode.Hybrid);
        viewModel.CollectionFilters.First().Name.Should().Be("All Collections");
    }

    // -- Navigation payload ---------------------------------------------------
    // Search is reachable from the dashboard search box and the command palette, both of
    // which know what the user typed. Arriving without that query means an empty page.

    [Fact]
    public async Task ApplyNavigationParameterAsync_WithAQuery_SeedsAndRunsTheSearch()
    {
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SearchResult>());

        var viewModel = CreateViewModel();

        await viewModel.ApplyNavigationParameterAsync("quarterly revenue");

        viewModel.QueryText.Should().Be("quarterly revenue");
        _hybridSearch.Verify(
            service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyNavigationParameterAsync_WithNoPayload_LeavesTheQueryUntouched()
    {
        var viewModel = CreateViewModel();
        viewModel.QueryText = "existing";

        await viewModel.ApplyNavigationParameterAsync(null);

        viewModel.QueryText.Should().Be("existing");
        _hybridSearch.Verify(
            service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Sorting

    [Fact]
    public async Task SearchAsync_NewResultsFollowTheSelectedSortOrder()
    {
        // Only a change of the sort box re-sorted; a new search always showed relevance order.
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Result(1, "zeta.md", 0.9f), Result(2, "alpha.md", 0.5f), Result(3, "mid.md", 0.7f) });
        var viewModel = CreateViewModel();
        viewModel.SelectedSortIndex = 3; // name
        viewModel.QueryText = "plan";

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.Results.Select(r => r.FileName).Should().Equal("alpha.md", "mid.md", "zeta.md");
    }

    // Saved filters

    [Fact]
    public async Task SaveCurrentFilterAsync_RemembersTheSearchMode()
    {
        // Every saved filter was stored as "semantic", so keyword and hybrid filters came
        // back in the wrong mode.
        _searchService.Setup(service => service.GetSearchHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<SearchHistoryEntry>());
        var viewModel = CreateViewModel();
        viewModel.QueryText = "invoice 2231";
        viewModel.SearchMode = SearchMode.Keyword;

        await viewModel.SaveCurrentFilterCommand.ExecuteAsync(null);

        _searchService.Verify(service => service.SaveSearchHistoryAsync(
            "invoice 2231", It.IsAny<int>(), It.IsAny<double?>(), It.IsAny<int?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), "keyword"), Times.Once);
    }

    // Overlapping searches

    [Fact]
    public async Task SearchAsync_ASlowerEarlierSearchCannotOverwriteANewerOne()
    {
        // A history click or filter change while a search was running started a second
        // search; whichever finished last won, so stale results could replace newer ones.
        var slow = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken slowToken = default;
        _hybridSearch
            .Setup(service => service.SearchAsync(It.Is<SearchQuery>(q => q.QueryText == "old"), It.IsAny<CancellationToken>()))
            .Returns((SearchQuery _, CancellationToken token) =>
            {
                slowToken = token;
                return slow.Task;
            });
        _hybridSearch
            .Setup(service => service.SearchAsync(It.Is<SearchQuery>(q => q.QueryText == "new"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Result(2, "new.md", 0.8f) });
        var viewModel = CreateViewModel();

        viewModel.QueryText = "old";
        var first = viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.SelectHistoryItemCommand.ExecuteAsync("new");

        slowToken.IsCancellationRequested.Should().BeTrue();
        slow.SetResult(new[] { Result(1, "old.md", 0.9f) });
        await first;

        viewModel.Results.Select(r => r.FileName).Should().Equal("new.md");
        viewModel.IsSearching.Should().BeFalse();
    }

    // Filters

    [Fact]
    public async Task SearchAsync_DateFiltersCoverTheWholeChosenDaysAsUtcBounds()
    {
        SearchQuery? sent = null;
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback((SearchQuery query, CancellationToken _) => sent = query)
            .ReturnsAsync(Array.Empty<SearchResult>());
        var viewModel = CreateViewModel();
        viewModel.QueryText = "report";
        viewModel.CreatedAfterDate = new DateTimeOffset(new DateTime(2026, 3, 2));
        viewModel.CreatedBeforeDate = new DateTimeOffset(new DateTime(2026, 3, 5));

        await viewModel.SearchCommand.ExecuteAsync(null);

        sent!.CreatedAfter.Should().Be(LocalDayRange.StartUtc(new DateTime(2026, 3, 2)));
        sent.CreatedBefore.Should().Be(LocalDayRange.EndUtc(new DateTime(2026, 3, 5)));
    }

    [Fact]
    public async Task SearchAsync_CategoryChipIsAppliedToResultsInsteadOfSentAsAFileType()
    {
        // "code" is not a stored file type, so sending it to the services matched nothing.
        SearchQuery? sent = null;
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback((SearchQuery query, CancellationToken _) => sent = query)
            .ReturnsAsync(new[] { Result(1, "Program.cs", 0.9f, "cs"), Result(2, "notes.md", 0.8f, "md") });
        var viewModel = CreateViewModel();
        viewModel.QueryText = "retry loop";

        await viewModel.FilterByFileTypeCommand.ExecuteAsync("code");

        sent!.FileTypeFilter.Should().BeNull();
        viewModel.Results.Select(r => r.FileName).Should().Equal("Program.cs");
    }

    [Fact]
    public async Task SearchAsync_ConnectorItemsMatchTheirChipByStoredType()
    {
        // Calendar and email items carry a display name, not a file name with an extension,
        // so matching the chip against the extension dropped every one of them.
        _hybridSearch
            .Setup(service => service.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Result(1, "Quarterly planning sync", 0.9f, "CalendarEvent") });
        var viewModel = CreateViewModel();
        viewModel.QueryText = "planning";

        await viewModel.FilterByFileTypeCommand.ExecuteAsync("CalendarEvent");

        viewModel.Results.Should().ContainSingle().Which.FileType.Should().Be("calendarevent");
    }

    private static SearchResult Result(long documentId, string fileName, float score, string fileType = "md") =>
        new()
        {
            DocumentId = documentId,
            ChunkId = documentId * 10,
            FileName = fileName,
            FilePath = Path.Combine(Path.GetTempPath(), fileName),
            FileType = fileType,
            MatchedText = "matched text",
            Score = score
        };

    private SearchViewModel CreateViewModel() =>
        new(
            _searchService.Object,
            _hybridSearch.Object,
            _documentService.Object,
            _collectionService.Object,
            _logger.Object,
            EnglishResources.Create(),
            _workflowLaunchService.Object);
}

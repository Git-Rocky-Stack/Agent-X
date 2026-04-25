using AgentX.App.ViewModels;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Tagging;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class KnowledgeVaultViewModelTests
{
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IIndexingService> _indexingService = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IAutoTagService> _autoTagService = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<IWorkflowLaunchService> _workflowLaunchService = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();

    [Fact]
    public async Task InitializeAsync_batch_loads_tags_for_documents()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
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
                CreateDocument(1, "alpha.md"),
                CreateDocument(2, "beta.pdf"),
            ]);

        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(2L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(3_072L);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(1);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(
                It.Is<IReadOnlyList<long>>(ids => ids.Count == 2 && ids[0] == 1L && ids[1] == 2L)))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>
            {
                [1] =
                [
                    new TagEntity { Id = 11, Name = "research" }
                ],
                [2] =
                [
                    new TagEntity { Id = 12, Name = "policy" },
                    new TagEntity { Id = 13, Name = "urgent" }
                ]
            });

        _autoTagService.Setup(service => service.GetAllTagsAsync())
            .ReturnsAsync(
            [
                new TagEntity { Id = 11, Name = "research", ColorHex = "#111111" },
                new TagEntity { Id = 12, Name = "policy", ColorHex = "#222222" },
                new TagEntity { Id = 13, Name = "urgent", ColorHex = "#333333" }
            ]);

        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object);

        await viewModel.InitializeAsync();

        _autoTagService.Verify(
            service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()),
            Times.Once);
        _autoTagService.Verify(
            service => service.GetTagsForDocumentAsync(It.IsAny<long>()),
            Times.Never);

        viewModel.Documents.Should().HaveCount(2);
        viewModel.Documents[0].Tags.Should().Equal("research");
        viewModel.Documents[1].Tags.Should().Equal("policy", "urgent");
        viewModel.AllTags.Should().HaveCount(3);
        viewModel.AllTags.Single(tag => tag.Name == "research").DocumentCount.Should().Be(1);
        viewModel.AllTags.Single(tag => tag.Name == "policy").DocumentCount.Should().Be(1);
        viewModel.AllTags.Single(tag => tag.Name == "urgent").DocumentCount.Should().Be(1);
    }

    [Fact]
    public async Task LaunchDocumentInWorkflowAsync_stages_request_and_navigates()
    {
        WorkflowLaunchRequest? stagedRequest = null;
        string? navigatedPage = null;

        _documentService.Setup(service => service.GetDocumentAsync(7))
            .ReturnsAsync(new DocumentEntity
            {
                Id = 7,
                FileName = "QuarterlyPlan.pdf",
                FilePath = @"C:\docs\QuarterlyPlan.pdf",
                FileType = "pdf",
                ContentHash = "hash-7",
                ImportedAt = new DateTime(2026, 4, 23, 8, 0, 0, DateTimeKind.Utc),
                FileModifiedAt = new DateTime(2026, 4, 23, 8, 0, 0, DateTimeKind.Utc),
                IndexingStatus = "completed",
                Summary = "Plan summary",
                ExtractedTitle = "Quarterly Planning Overview"
            });
        _documentService.Setup(service => service.GetDocumentPreviewTextAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Preview excerpt");
        _workflowLaunchService.Setup(service => service.StageRequest(It.IsAny<WorkflowLaunchRequest>()))
            .Callback<WorkflowLaunchRequest>(request => stagedRequest = request);

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object)
        {
            NavigateRequested = page => navigatedPage = page
        };

        await viewModel.LaunchDocumentInWorkflowCommand.ExecuteAsync(7L);

        stagedRequest.Should().NotBeNull();
        stagedRequest!.InputText.Should().Contain("Source: Knowledge Vault document");
        stagedRequest.InputText.Should().Contain("Document: QuarterlyPlan.pdf");
        stagedRequest.InputText.Should().Contain("Plan summary");
        stagedRequest.RecommendedWorkflowName.Should().Be("Summarize & Act");
        navigatedPage.Should().Be("Workflows");
    }

    [Fact]
    public async Task InitializeAsync_consumes_pending_operations_document_request_and_focuses_document()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
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
                CreateDocument(1, "alpha.md"),
                CreateDocument(2, "beta.pdf")
            ]);
        _documentService.Setup(service => service.GetDocumentAsync(2))
            .ReturnsAsync(CreateDocument(2, "beta.pdf"));
        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(2L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(3_072L);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>());
        _autoTagService.Setup(service => service.GetAllTagsAsync())
            .ReturnsAsync(Array.Empty<TagEntity>());
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _operationsDrillInService.Setup(service => service.ConsumePendingDocumentRequest())
            .Returns(new OperationsDocumentDrillInRequest(2, "Opened imported document \"beta.pdf\" from Operations"));

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();

        viewModel.Documents[0].Id.Should().Be(2);
        viewModel.Documents[0].HasFocusedSourceLabel.Should().BeTrue();
        viewModel.Documents[0].FocusedSourceLabel.Should().Contain("beta.pdf");
        viewModel.SelectedDocument.Should().NotBeNull();
        viewModel.SelectedDocument!.Id.Should().Be(2);
        viewModel.SelectedDocument.HasFocusedSourceLabel.Should().BeTrue();
        viewModel.FocusedDocumentVisibilityHint.Should().BeEmpty();
        viewModel.IsPreviewOpen.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_widens_filters_for_pending_operations_document_request()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? fileTypeFilter, string? statusFilter, string? tagFilter, long? collectionId, DateTime? importedAfter, DateTime? importedBefore, string? sortBy, CancellationToken ct) =>
                statusFilter == "failed"
                    ? [CreateDocument(1, "alpha.md")]
                    : [CreateDocument(1, "alpha.md"), CreateDocument(2, "beta.pdf")]);
        _documentService.Setup(service => service.GetDocumentAsync(2))
            .ReturnsAsync(CreateDocument(2, "beta.pdf"));
        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(2L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(3_072L);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>());
        _autoTagService.Setup(service => service.GetAllTagsAsync())
            .ReturnsAsync(Array.Empty<TagEntity>());
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _operationsDrillInService.Setup(service => service.ConsumePendingDocumentRequest())
            .Returns(new OperationsDocumentDrillInRequest(2, "Opened imported document \"beta.pdf\" from Operations"));

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object)
        {
            StatusFilter = "failed"
        };

        await viewModel.InitializeAsync();

        viewModel.StatusFilter.Should().BeNull();
        viewModel.Documents[0].Id.Should().Be(2);
        viewModel.SelectedDocument.Should().NotBeNull();
        viewModel.SelectedDocument!.Id.Should().Be(2);
        viewModel.FocusedDocumentVisibilityHint.Should().Be("Filters were widened to show the requested document.");
    }

    [Fact]
    public async Task DismissFocusedDocumentLandingCommand_clears_banner_and_row_focus_labels()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
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
                CreateDocument(1, "alpha.md"),
                CreateDocument(2, "beta.pdf")
            ]);
        _documentService.Setup(service => service.GetDocumentAsync(2))
            .ReturnsAsync(CreateDocument(2, "beta.pdf"));
        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(2L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(3_072L);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>());
        _autoTagService.Setup(service => service.GetAllTagsAsync())
            .ReturnsAsync(Array.Empty<TagEntity>());
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _operationsDrillInService.Setup(service => service.ConsumePendingDocumentRequest())
            .Returns(new OperationsDocumentDrillInRequest(2, "Opened imported document \"beta.pdf\" from Operations"));

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        viewModel.DismissFocusedDocumentLandingCommand.Execute(null);

        viewModel.SelectedDocument.Should().NotBeNull();
        viewModel.SelectedDocument!.Id.Should().Be(2);
        viewModel.SelectedDocument.HasFocusedSourceLabel.Should().BeFalse();
        viewModel.FocusedDocumentVisibilityHint.Should().BeEmpty();
        viewModel.Documents.Should().OnlyContain(document => !document.HasFocusedSourceLabel);
    }

    [Fact]
    public async Task SelectDocumentCommand_clears_previous_operations_focus_when_user_moves_away()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
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
                CreateDocument(1, "alpha.md"),
                CreateDocument(2, "beta.pdf")
            ]);
        _documentService.Setup(service => service.GetDocumentAsync(1))
            .ReturnsAsync(CreateDocument(1, "alpha.md"));
        _documentService.Setup(service => service.GetDocumentAsync(2))
            .ReturnsAsync(CreateDocument(2, "beta.pdf"));
        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(2L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(3_072L);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>());
        _autoTagService.Setup(service => service.GetAllTagsAsync())
            .ReturnsAsync(Array.Empty<TagEntity>());
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _operationsDrillInService.Setup(service => service.ConsumePendingDocumentRequest())
            .Returns(new OperationsDocumentDrillInRequest(2, "Opened imported document \"beta.pdf\" from Operations"));

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.SelectDocumentCommand.ExecuteAsync(1L);

        viewModel.SelectedDocument.Should().NotBeNull();
        viewModel.SelectedDocument!.Id.Should().Be(1);
        viewModel.FocusedDocumentVisibilityHint.Should().BeEmpty();
        viewModel.Documents.Should().OnlyContain(document => !document.HasFocusedSourceLabel);
    }

    private static DocumentEntity CreateDocument(long id, string fileName)
    {
        return new DocumentEntity
        {
            Id = id,
            FileName = fileName,
            FilePath = $@"C:\docs\{fileName}",
            FileType = Path.GetExtension(fileName).TrimStart('.'),
            ContentHash = $"hash-{id}",
            FileSizeBytes = 1_024,
            ImportedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            FileModifiedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            IndexingStatus = "completed",
            WordCount = 120,
            PageCount = 1
        };
    }
}

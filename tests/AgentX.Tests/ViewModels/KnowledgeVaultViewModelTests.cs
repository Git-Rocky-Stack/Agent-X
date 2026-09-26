using System.Collections.Concurrent;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search.Models;
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
            NavigateRequested = (page, _) => navigatedPage = page
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

    // ── Navigation payload ───────────────────────────────────────────────────
    // Jump-To lists individual documents. Selecting one used to open the vault on an
    // unfiltered list, so the document the user picked was never surfaced.

    [Fact]
    public async Task ApplyNavigationParameterAsync_WithADocumentId_FocusesThatDocument()
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

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.ApplyNavigationParameterAsync(2L);

        viewModel.Documents[0].Id.Should().Be(2);
        viewModel.SelectedDocument.Should().NotBeNull();
        viewModel.SelectedDocument!.Id.Should().Be(2);
        viewModel.IsPreviewOpen.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyNavigationParameterAsync_WithNoPayload_SelectsNothing()
    {
        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object);

        await viewModel.ApplyNavigationParameterAsync(null);

        viewModel.SelectedDocument.Should().BeNull();
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

    // Import outcome
    // Every import used to end with "Successfully imported {count} file(s)", although
    // files that failed were only logged and duplicates were silently dropped.

    [Fact]
    public async Task ImportWithDedupCommand_ReportsTheRealOutcomeInsteadOfClaimingSuccess()
    {
        SetupVault();
        var good = Path.Combine(Path.GetTempPath(), "good.md");
        var bad = Path.Combine(Path.GetTempPath(), "bad.pdf");
        _documentService.Setup(service => service.CheckForDuplicateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateCheckResult { IsDuplicate = false });
        var report = new DocumentImportReport();
        report.Imported.Add(CreateDocument(1, "good.md"));
        report.Failed.Add(new DocumentImportFailure(bad, "The file does not exist."));
        _documentService.Setup(service => service.ImportFilesWithReportAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), false, It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var viewModel = CreateViewModel();
        await viewModel.ImportWithDedupCommand.ExecuteAsync(new[] { good, bad });

        viewModel.ImportStatus.Should().Be("Imported 1 of 2 file(s); 1 could not be imported");
        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("Imported 1 of 2 file(s)").And.Contain("bad.pdf: The file does not exist.");
    }

    [Fact]
    public async Task ImportAllAnywayCommand_ImportsTheDuplicatesInsteadOfSkippingThemAgain()
    {
        // "Import all anyway" re-ran the ordinary import, which rejects duplicates, so the
        // duplicates the user explicitly asked for were never imported.
        SetupVault();
        var clean = Path.Combine(Path.GetTempPath(), "clean.md");
        var duplicate = Path.Combine(Path.GetTempPath(), "copy.md");
        _documentService.Setup(service => service.CheckForDuplicateAsync(clean, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateCheckResult { IsDuplicate = false });
        _documentService.Setup(service => service.CheckForDuplicateAsync(duplicate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateCheckResult { IsDuplicate = true, ExistingFileName = "original.md" });
        IReadOnlyList<string>? importedPaths = null;
        bool? allowedDuplicates = null;
        _documentService.Setup(service => service.ImportFilesWithReportAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<string> paths, long? _, bool allowDuplicates, IProgress<int>? _, CancellationToken _) =>
            {
                importedPaths = paths;
                allowedDuplicates = allowDuplicates;
            })
            .ReturnsAsync(new DocumentImportReport());

        var viewModel = CreateViewModel();
        await viewModel.ImportWithDedupCommand.ExecuteAsync(new[] { clean, duplicate });
        viewModel.ShowDuplicateWarning.Should().BeTrue();

        await viewModel.ImportAllAnywayCommand.ExecuteAsync(null);

        allowedDuplicates.Should().BeTrue();
        importedPaths.Should().BeEquivalentTo(new[] { clean, duplicate });
    }

    [Fact]
    public void FormatImportSummary_CleanBatch_ReportsSuccess()
    {
        var report = new DocumentImportReport();
        report.Imported.Add(CreateDocument(1, "a.md"));
        report.Imported.Add(CreateDocument(2, "b.md"));

        KnowledgeVaultViewModel.FormatImportSummary(report, 2).Should().Be("Successfully imported 2 file(s)");
    }

    [Fact]
    public void FormatImportSummary_MixedBatch_GivesTheRealCounts()
    {
        var report = new DocumentImportReport();
        report.Imported.Add(CreateDocument(1, "a.md"));
        var unreadable = CreateDocument(2, "b.pdf");
        unreadable.IndexingStatus = "failed";
        report.Imported.Add(unreadable);
        report.Duplicates.Add("c.md");
        report.Duplicates.Add("d.md");
        report.Failed.Add(new DocumentImportFailure("e.zzz", "No processor"));

        KnowledgeVaultViewModel.FormatImportSummary(report, 5, fromFolder: true).Should().Be(
            "Imported 2 of 5 file(s) from folder; 1 of them could not be read and is marked Failed; " +
            "2 skipped as duplicates; 1 could not be imported");
    }

    // Drag and drop
    // The drop handler imported the first dropped folder and returned, discarding every
    // other dropped file and folder.

    [Fact]
    public async Task HandleDroppedItemsAsync_ImportsTheDroppedFilesAndEveryDroppedFolder()
    {
        SetupVault();
        var root = Path.Combine(Path.GetTempPath(), "agentx-drop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var loose = WriteFile(root, "loose.md");
            var first = WriteFile(Path.Combine(root, "a"), "one.md");
            WriteFile(Path.Combine(root, "a"), "skipped.zzz");
            var nested = WriteFile(Path.Combine(root, "b", "nested"), "two.pdf");

            _documentService.Setup(service => service.GetSupportedExtensions())
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".pdf" });
            _documentService.Setup(service => service.CheckForDuplicateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DuplicateCheckResult { IsDuplicate = false });
            IReadOnlyList<string>? importedPaths = null;
            _documentService.Setup(service => service.ImportFilesWithReportAsync(
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyList<string> paths, long? _, bool _, IProgress<int>? _, CancellationToken _) => importedPaths = paths)
                .ReturnsAsync(new DocumentImportReport());

            var viewModel = CreateViewModel();
            await viewModel.HandleDroppedItemsAsync(
                new[] { loose },
                new[] { Path.Combine(root, "a"), Path.Combine(root, "b") });

            importedPaths.Should().BeEquivalentTo(new[] { loose, first, nested });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Preview versus multi-select
    // Opening a preview ticked the row's multi-select checkbox, and closing it unticked
    // the row, without changing the ids a bulk delete acts on.

    [Fact]
    public async Task PreviewingADocument_LeavesTheMultiSelectCheckboxesAlone()
    {
        SetupVault(CreateDocument(1, "alpha.md"), CreateDocument(2, "beta.pdf"));
        _documentService.Setup(service => service.GetDocumentAsync(It.IsAny<long>()))
            .ReturnsAsync((long id) => CreateDocument(id, "doc.md"));
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.ToggleDocumentSelectionCommand.Execute(1L);
        await viewModel.SelectDocumentCommand.ExecuteAsync(2L);

        viewModel.Documents.Single(d => d.Id == 2).IsSelected.Should().BeFalse();

        await viewModel.SelectDocumentCommand.ExecuteAsync(1L);
        viewModel.ClosePreviewCommand.Execute(null);

        viewModel.Documents.Single(d => d.Id == 1).IsSelected.Should().BeTrue();
        viewModel.SelectedDocumentIds.Should().Equal(1L);
    }

    [Fact]
    public async Task Reload_ReappliesTicksAndDropsSelectionsWhoseRowsAreGone()
    {
        // A reload built fresh, unticked rows while the selected ids survived, so a bulk
        // delete could remove rows shown unchecked, or rows no longer shown at all.
        var visible = new List<DocumentEntity> { CreateDocument(1, "alpha.md"), CreateDocument(2, "beta.pdf") };
        SetupVault();
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => visible.ToList());
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.ToggleDocumentSelectionCommand.Execute(1L);
        viewModel.ToggleDocumentSelectionCommand.Execute(2L);

        visible.RemoveAt(1);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Documents.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
        viewModel.SelectedDocumentIds.Should().Equal(1L);
        viewModel.SelectedCount.Should().Be(1);
    }

    // Status badge

    [Fact]
    public void DocumentDisplayItem_StatusChange_NotifiesTheBadgeLabel()
    {
        // The badge binds IndexingStatusLabel, which never reported a change, so it kept
        // showing the old status after a re-index.
        var item = new DocumentDisplayItem { IndexingStatus = "completed" };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.IndexingStatus = "processing";

        changed.Should().Contain(nameof(DocumentDisplayItem.IndexingStatusLabel));
        item.IndexingStatusLabel.Should().Be("Processing");
    }

    // Date filters

    [Fact]
    public async Task DateFilters_CoverTheWholeChosenDaysAsUtcBounds()
    {
        // ImportedAt is stored in UTC but was compared with the picked day's local
        // midnight, and "before" stopped at the start of the chosen day.
        SetupVault();
        DateTime? importedAfter = null;
        DateTime? importedBefore = null;
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, string? _, string? _, long? _, DateTime? after, DateTime? before, string? _, CancellationToken _) =>
            {
                importedAfter = after;
                importedBefore = before;
            })
            .ReturnsAsync(Array.Empty<DocumentEntity>());
        var viewModel = CreateViewModel();

        viewModel.DateAfterFilter = new DateTime(2026, 3, 2);
        viewModel.DateBeforeFilter = new DateTime(2026, 3, 5);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        importedAfter.Should().Be(LocalDayRange.StartUtc(new DateTime(2026, 3, 2)));
        importedBefore.Should().Be(LocalDayRange.EndUtc(new DateTime(2026, 3, 5)));
        importedBefore!.Value.Should().BeAfter(LocalDayRange.StartUtc(new DateTime(2026, 3, 5)).AddHours(23));
    }

    // Live indexing updates
    // Rows kept the status they were loaded with: a document imported as "pending" stayed
    // "Pending" with 0 chunks after the background pipeline had indexed it, or failed it.

    [Fact]
    public async Task DocumentIndexed_UpdatesTheRowOnTheContextTheViewModelWasCreatedOn()
    {
        var pending = CreateDocument(1, "alpha.md");
        pending.IndexingStatus = "pending";
        pending.ChunkCount = 0;
        SetupVault(pending);
        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(1);
        var indexed = CreateDocument(1, "alpha.md");
        indexed.ChunkCount = 7;
        _documentService.Setup(service => service.GetDocumentAsync(1)).ReturnsAsync(indexed);

        var ui = new QueuedSynchronizationContext();
        var viewModel = CreateViewModelOn(ui);
        await viewModel.InitializeAsync();
        var row = viewModel.Documents.Single();
        row.IndexingStatus.Should().Be("pending");
        viewModel.IndexingQueueLength.Should().Be(1);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        await Task.Run(() => _indexingService.Raise(service => service.DocumentIndexed += null, _indexingService.Object, 1L));

        row.IndexingStatus.Should().Be("pending", "the event arrives on the indexing thread and is posted to the UI context");
        ui.PendingCount.Should().Be(1);

        ui.RunPending();

        row.IndexingStatus.Should().Be("completed");
        row.IndexingStatusLabel.Should().Be("Indexed");
        row.StatusColor.Should().Be("#41E25E");
        row.ChunkCount.Should().Be(7);
        viewModel.IndexingQueueLength.Should().Be(0);
    }

    [Fact]
    public async Task DocumentIndexingFailed_ShowsTheFailureAndItsReasonOnTheRow()
    {
        var pending = CreateDocument(2, "scan.pdf");
        pending.IndexingStatus = "pending";
        SetupVault(pending);
        var failed = CreateDocument(2, "scan.pdf");
        failed.IndexingStatus = "failed";
        failed.IndexingError = "The vector store is not available (disk full).";
        _documentService.Setup(service => service.GetDocumentAsync(2)).ReturnsAsync(failed);

        var ui = new QueuedSynchronizationContext();
        var viewModel = CreateViewModelOn(ui);
        await viewModel.InitializeAsync();

        await Task.Run(() => _indexingService.Raise(
            service => service.DocumentIndexingFailed += null,
            new DocumentIndexingFailedEventArgs(2, failed.IndexingError)));
        ui.RunPending();

        var row = viewModel.Documents.Single();
        row.IndexingStatus.Should().Be("failed");
        row.IndexingStatusLabel.Should().Be("Failed");
        row.IndexingError.Should().Be("The vector store is not available (disk full).");
        row.StatusColor.Should().Be("#C8453E");
    }

    [Fact]
    public async Task Dispose_StopsTheLiveUpdates()
    {
        // The indexing service is a singleton: a view model still subscribed after its page
        // was evicted would stay reachable, and keep updating rows nobody sees.
        var pending = CreateDocument(3, "notes.txt");
        pending.IndexingStatus = "pending";
        SetupVault(pending);
        _documentService.Setup(service => service.GetDocumentAsync(3)).ReturnsAsync(CreateDocument(3, "notes.txt"));
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.Dispose();
        _indexingService.Raise(service => service.DocumentIndexed += null, _indexingService.Object, 3L);

        viewModel.Documents.Single().IndexingStatus.Should().Be("pending");
        _indexingService.VerifyRemove(service => service.DocumentIndexed -= It.IsAny<EventHandler<long>>(), Times.Once());
        _indexingService.VerifyRemove(
            service => service.DocumentIndexingFailed -= It.IsAny<EventHandler<DocumentIndexingFailedEventArgs>>(),
            Times.Once());
    }

    [Fact]
    public void DocumentDisplayItem_CountChanges_NotifyTheBoundLabels()
    {
        var item = new DocumentDisplayItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.ChunkCount = 12;
        item.PageCount = 3;
        item.WordCount = 4_200;

        changed.Should().Contain(new[]
        {
            nameof(DocumentDisplayItem.ChunkCount),
            nameof(DocumentDisplayItem.PageCount),
            nameof(DocumentDisplayItem.WordCount),
            nameof(DocumentDisplayItem.WordCountFormatted),
        });
        item.WordCountFormatted.Should().Be(4.2.ToString("F1") + "K");
    }

    private KnowledgeVaultViewModel CreateViewModel() =>
        new(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object,
            _workflowLaunchService.Object,
            _operationsDrillInService.Object);

    /// <summary>Creates the view model with <paramref name="context"/> as its UI context.</summary>
    private KnowledgeVaultViewModel CreateViewModelOn(SynchronizationContext context)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return CreateViewModel();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// A stand-in for the UI thread: posted work waits in a queue until the test runs it.
    /// </summary>
    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PendingCount => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void RunPending()
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                while (_queue.TryDequeue(out var work))
                {
                    work.Callback(work.State);
                }
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    /// <summary>Configures the services a vault load touches, returning the given documents.</summary>
    private void SetupVault(params DocumentEntity[] documents)
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents);
        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(documents.Length);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(0L);
        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);
        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>());
        _autoTagService.Setup(service => service.GetAllTagsAsync()).ReturnsAsync(Array.Empty<TagEntity>());
        _collectionService.Setup(service => service.GetAllCollectionsAsync()).ReturnsAsync(Array.Empty<CollectionEntity>());
    }

    private static string WriteFile(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "content");
        return path;
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

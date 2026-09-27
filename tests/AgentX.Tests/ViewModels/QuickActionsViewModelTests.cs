using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Core.Services.Localization;
using AgentX.Tests.Helpers;
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
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();

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
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        await viewModel.InitializeAsync();

        var action = viewModel.RecommendedActions.First(item => item.Route == "Inbox");
        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        navigations.Should().Equal("Inbox");
    }

    [Fact]
    public async Task ExecuteRecommendedActionAsync_stages_pending_inbox_item_before_navigation()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "3",
                    Status = "3 items awaiting triage",
                    Detail = "Open Smart Inbox to triage imports."
                },
                PendingInboxItems =
                [
                    new OperationsInboxPreview
                    {
                        ItemId = 901,
                        Title = "Research intake.msg",
                        Status = "Email Connector",
                        Detail = "Awaiting preview"
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 connector enabled",
                    Detail = "Email Connector"
                }
            });

        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        await viewModel.InitializeAsync();

        var action = viewModel.RecommendedActions.First(item => item.Route == "Inbox");
        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        _operationsDrillInService.Verify(service => service.StageInboxRequest(
            It.Is<OperationsInboxDrillInRequest>(request =>
                request.ItemId == 901 &&
                request.SourceLabel.Contains("Triage new incoming content"))), Times.Once);
        navigations.Should().Equal("Inbox");
    }

    [Fact]
    public async Task ExecuteRecommendedActionAsync_stages_selected_document_before_vault_navigation()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        await viewModel.InitializeAsync();
        viewModel.SelectedDocument = viewModel.AvailableDocuments.Single(item => item.Id == 102);

        var action = viewModel.RecommendedActions.First(item => item.Route == "KnowledgeVault");
        await viewModel.ExecuteRecommendedActionCommand.ExecuteAsync(action);

        _operationsDrillInService.Verify(service => service.StageDocumentRequest(
            It.Is<OperationsDocumentDrillInRequest>(request =>
                request.DocumentId == 102 &&
                request.SourceLabel.Contains("Finish indexing Connector Intake.msg"))), Times.Once);
        navigations.Should().Equal("KnowledgeVault");
    }

    // -- Texts in the user's language --
    // Status lines, recommendations, duplicate labels and the language list were English.

    [Fact]
    public async Task TranslateCommand_ListsLanguagesInTheUsersLanguage_AndAsksForTheEnglishName()
    {
        _summaryService.Setup(service => service.TranslateTextAsync("Hallo", "French", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Bonjour");
        var viewModel = CreateViewModel(ReswLocalization.For("de"));
        await viewModel.InitializeAsync();

        viewModel.AvailableLanguages.Select(language => language.DisplayName)
            .Should().Contain(["Spanisch", "Französisch", "Japanisch"]);
        viewModel.SelectedLanguage!.PromptName.Should().Be("Spanish");

        viewModel.TranslationInput = "Hallo";
        viewModel.SelectedLanguage = viewModel.AvailableLanguages.Single(language => language.PromptName == "French");
        await viewModel.TranslateCommand.ExecuteAsync(null);

        viewModel.TranslationOutput.Should().Be("Bonjour");
        viewModel.StatusMessage.Should().Be("Übersetzung (Französisch) abgeschlossen");
        viewModel.SelectedLanguage.ToString().Should().Be("Französisch", "the list shows the item's text");
    }

    [Fact]
    public async Task InitializeAsync_ShowsTheRecommendationsInTheUsersLanguage()
    {
        var viewModel = CreateViewModel(ReswLocalization.For("fr"));

        await viewModel.InitializeAsync();

        viewModel.StatusMessage.Should().Be("2 documents disponibles");
        var summarize = viewModel.RecommendedActions[0];
        summarize.Title.Should().Be("Résumez Board Brief.pdf");
        summarize.StatusLabel.Should().Be("Consultable");
        summarize.CommandText.Should().Be("Générer le résumé");
    }

    [Fact]
    public async Task InitializeAsync_WithOneDocument_SaysDocumentInTheSingular()
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
                new DocumentEntity { Id = 101, FileName = "Board Brief.pdf", FileType = "pdf", IndexingStatus = "completed" }
            ]);
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.StatusMessage.Should().Be("1 document available");
    }

    [Fact]
    public async Task FindNearDuplicatesCommand_LabelsTheGroupsInTheUsersLanguage_AndShowsLocalImportTimes()
    {
        var imported = new DateTime(2026, 9, 27, 14, 30, 0, DateTimeKind.Utc);
        var group = new DuplicateGroup
        {
            ContentHash = "0123456789abcdef",
            MatchKind = DuplicateMatchKind.Semantic,
            Documents =
            [
                new DuplicateDocument
                {
                    DocumentId = 1,
                    FileName = "a.md",
                    FileSizeBytes = 1024,
                    ImportedAt = imported,
                    Evidence = new DuplicateEvidence { Confidence = 0.91, SupportingChunkCount = 3 }
                },
                new DuplicateDocument { DocumentId = 2, FileName = "b.md", FileSizeBytes = 1024, ImportedAt = imported }
            ]
        };
        _duplicateDetectionService.Setup(service => service.FindNearDuplicatesAsync(It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([group]);
        var viewModel = CreateViewModel(ReswLocalization.For("es"));
        await viewModel.InitializeAsync();

        await viewModel.FindNearDuplicatesCommand.ExecuteAsync(null);

        var shown = viewModel.DuplicateGroups.Should().ContainSingle().Subject;
        shown.GroupLabel.Should().Be("2 archivos son semánticamente similares");
        shown.MatchLabel.Should().Be("Semántico");
        shown.DetailLabel.Should().Be("Evidencia de embeddings con hasta un 91 % de confianza");
        shown.Documents[0].EvidenceLabel.Should().Be("91 % de confianza a partir de 3 fragmentos coincidentes");
        shown.Documents[0].ImportedAt.Should().Be(imported.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        viewModel.StatusMessage.Should().StartWith("Se encontró 1 grupo de casi duplicados semánticos");
    }

    private QuickActionsViewModel CreateViewModel(ILocalizationService? localization = null) =>
        new(
            _summaryService.Object,
            _duplicateDetectionService.Object,
            _organizationSuggestionService.Object,
            _documentService.Object,
            _operationsOverviewService.Object,
            Log.ForContext<QuickActionsViewModelTests>(),
            localization ?? EnglishResources.Create(),
            _operationsDrillInService.Object);
}

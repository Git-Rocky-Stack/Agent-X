using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class QuickActionsViewModel : ObservableObject, IDisposable
{
    // ── Services ─────────────────────────────────────────────
    private readonly ISummaryService _summaryService;
    private readonly IDuplicateDetectionService _duplicateDetectionService;
    private readonly IOrganizationSuggestionService _organizationSuggestionService;
    private readonly IDocumentService _documentService;
    private readonly IOperationsOverviewService _operationsOverviewService;
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private readonly ILogger _logger;
    private readonly ILocalizationService _localization;
    private OperationsOverviewSnapshot _operationsSnapshot = new();

    // ── Document Selection ───────────────────────────────────
    [ObservableProperty] private ObservableCollection<QuickActionDocumentItem> _availableDocuments = new();
    [ObservableProperty] private QuickActionDocumentItem? _selectedDocument;
    [ObservableProperty] private ObservableCollection<QuickActionRecommendedItem> _recommendedActions = new();

    // ── Summarize Tab ────────────────────────────────────────
    [ObservableProperty] private string _summaryResult = string.Empty;

    // ── Key Points Tab ───────────────────────────────────────
    [ObservableProperty] private ObservableCollection<string> _keyPoints = new();

    // ── Translate Tab ────────────────────────────────────────
    [ObservableProperty] private string _translationInput = string.Empty;
    [ObservableProperty] private string _translationOutput = string.Empty;
    [ObservableProperty] private QuickActionLanguageOption? _selectedLanguage;

    /// <summary>
    /// The languages Translate offers, named in the user's language. The translation itself is
    /// asked for by each language's English name.
    /// </summary>
    public ObservableCollection<QuickActionLanguageOption> AvailableLanguages { get; }

    // ── Duplicates Tab ───────────────────────────────────────
    [ObservableProperty] private ObservableCollection<QuickActionDuplicateGroupItem> _duplicateGroups = new();

    // ── Organize Tab ─────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<QuickActionOrganizationItem> _suggestions = new();

    // ── UI State ─────────────────────────────────────────────
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _selectedTabIndex;

    // ── Result Visibility ────────────────────────────────────
    [ObservableProperty] private bool _hasSummaryResult;
    [ObservableProperty] private bool _hasKeyPoints;
    [ObservableProperty] private bool _hasTranslationOutput;
    [ObservableProperty] private bool _hasDuplicateResults;
    [ObservableProperty] private bool _hasSuggestionResults;
    public bool HasRecommendedActions => RecommendedActions.Count > 0;
    public NavigateHandler? NavigateRequested { get; set; }

    /// <param name="localization">Every text the page builds: status lines, recommendations, labels and language names.</param>
    public QuickActionsViewModel(
        ISummaryService summaryService,
        IDuplicateDetectionService duplicateDetectionService,
        IOrganizationSuggestionService organizationSuggestionService,
        IDocumentService documentService,
        IOperationsOverviewService operationsOverviewService,
        ILogger logger,
        ILocalizationService localization,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _summaryService = summaryService;
        _duplicateDetectionService = duplicateDetectionService;
        _organizationSuggestionService = organizationSuggestionService;
        _documentService = documentService;
        _operationsOverviewService = operationsOverviewService;
        _operationsDrillInService = operationsDrillInService;
        _logger = logger;
        _localization = localization;

        AvailableLanguages = new ObservableCollection<QuickActionLanguageOption>
        {
            new("Spanish", localization.GetString("QuickAct_LanguageSpanish")),
            new("French", localization.GetString("QuickAct_LanguageFrench")),
            new("German", localization.GetString("QuickAct_LanguageGerman")),
            new("Chinese", localization.GetString("QuickAct_LanguageChinese")),
            new("Japanese", localization.GetString("QuickAct_LanguageJapanese")),
            new("Korean", localization.GetString("QuickAct_LanguageKorean")),
            new("Portuguese", localization.GetString("QuickAct_LanguagePortuguese")),
            new("Italian", localization.GetString("QuickAct_LanguageItalian")),
            new("Russian", localization.GetString("QuickAct_LanguageRussian")),
            new("Arabic", localization.GetString("QuickAct_LanguageArabic"))
        };
        SelectedLanguage = AvailableLanguages[0];
        StatusMessage = localization.GetString("QuickAct_StatusReady");

        _logger.Debug("QuickActionsViewModel created with services");
    }

    public async Task InitializeAsync()
    {
        _logger.Information("QuickActions initializing...");
        await Task.WhenAll(
            LoadAvailableDocumentsAsync(),
            LoadOperationsContextAsync());
        BuildRecommendedActions();
        _logger.Information("QuickActions initialized with {Count} documents", AvailableDocuments.Count);
    }

    private async Task LoadAvailableDocumentsAsync()
    {
        try
        {
            StatusMessage = _localization.GetString("QuickAct_LoadingDocuments");
            var docs = await _documentService.GetAllDocumentsAsync();

            var items = docs.Select(d => new QuickActionDocumentItem
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSizeFormatted = FormatHelper.FormatBytes(d.FileSizeBytes),
                IndexingStatus = d.IndexingStatus,
                DisplayLabel = $"{d.FileName}  ({d.FileType.ToUpperInvariant()}, {FormatHelper.FormatBytes(d.FileSizeBytes)})"
            });

            AvailableDocuments = new ObservableCollection<QuickActionDocumentItem>(items);

            if (AvailableDocuments.Count > 0)
                SelectedDocument = AvailableDocuments[0];

            StatusMessage = DocumentsAvailableText(AvailableDocuments.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load available documents for Quick Actions");
            StatusMessage = _localization.GetString("QuickAct_LoadDocumentsFailed");
            AvailableDocuments = new ObservableCollection<QuickActionDocumentItem>();
        }
    }

    private async Task LoadOperationsContextAsync()
    {
        try
        {
            _operationsSnapshot = await _operationsOverviewService.GetSnapshotAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load Operations context for Quick Actions");
            _operationsSnapshot = new OperationsOverviewSnapshot();
        }
    }

    private void BuildRecommendedActions()
    {
        var items = new List<QuickActionRecommendedItem>();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = SelectedDocument;
        var intakeCount = ParseCompactNumber(_operationsSnapshot.IngestionBacklog.Headline);
        var targetInboxItem = _operationsSnapshot.PendingInboxItems.FirstOrDefault(item => item.ItemId > 0);
        var targetConnector = _operationsSnapshot.ConnectorPreviews.FirstOrDefault(preview =>
            preview.PluginId > 0 &&
            preview.CanEnableFromOperations);
        var selectedReady = selected is not null &&
                            selected.IndexingStatus.Equals("completed", StringComparison.OrdinalIgnoreCase);
        var selectedNeedsIndexing = selected is not null && !selectedReady;

        void AddAction(QuickActionRecommendedItem item)
        {
            if (item.Kind == QuickActionRecommendedActionKind.Navigate)
            {
                if (string.IsNullOrWhiteSpace(item.Route) || !routes.Add(item.Route))
                {
                    return;
                }
            }

            items.Add(item);
        }

        if (selected is null)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategorySetup"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionImportTitle"),
                Detail = _localization.GetString("QuickAct_ActionImportDetail"),
                StatusLabel = _localization.GetString("QuickAct_StatusNoDocument"),
                CommandText = _localization.GetString("QuickAct_CommandOpenVault"),
                Route = "KnowledgeVault",
                Kind = QuickActionRecommendedActionKind.Navigate
            });
        }
        else if (selectedNeedsIndexing)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryReadiness"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionFinishIndexingTitle", selected.FileName),
                Detail = _localization.GetString("QuickAct_ActionFinishIndexingDetail"),
                StatusLabel = NormalizeStatusLabel(selected.IndexingStatus),
                CommandText = _localization.GetString("QuickAct_CommandReviewDocument"),
                Route = "KnowledgeVault",
                Kind = QuickActionRecommendedActionKind.Navigate,
                DocumentId = selected.Id
            });
        }
        else
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryDocument"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionSummarizeTitle", selected.FileName),
                Detail = _localization.GetString("QuickAct_ActionSummarizeDetail"),
                StatusLabel = _localization.GetString("QuickAct_StatusSearchable"),
                CommandText = _localization.GetString("QuickAct_CommandRunSummary"),
                Kind = QuickActionRecommendedActionKind.SummarizeSelectedDocument,
                DocumentId = selected.Id
            });
        }

        if (intakeCount > 0)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryInbox"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionTriageTitle"),
                Detail = _localization.GetString("QuickAct_ActionTriageDetail"),
                StatusLabel = _operationsSnapshot.IngestionBacklog.Status,
                CommandText = targetInboxItem is null
                    ? _localization.GetString("QuickAct_CommandOpenInbox")
                    : _localization.GetString("QuickAct_CommandOpenItem"),
                Route = "Inbox",
                Kind = QuickActionRecommendedActionKind.Navigate,
                TargetId = targetInboxItem?.ItemId ?? 0
            });
        }

        if (selectedReady)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryDocument"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionExtractTitle", selected!.FileName),
                Detail = _localization.GetString("QuickAct_ActionExtractDetail"),
                StatusLabel = _localization.GetString("QuickAct_StatusSearchable"),
                CommandText = _localization.GetString("QuickAct_CommandExtractKeyPoints"),
                Kind = QuickActionRecommendedActionKind.ExtractKeyPointsSelectedDocument,
                DocumentId = selected.Id
            });
        }

        if (ConnectorsNeedSetup(_operationsSnapshot))
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryExpansion"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionConnectTitle"),
                Detail = _localization.GetString("QuickAct_ActionConnectDetail"),
                StatusLabel = string.IsNullOrWhiteSpace(_operationsSnapshot.Connectors.Status)
                    ? _localization.GetString("QuickAct_StatusNoConnectors")
                    : _operationsSnapshot.Connectors.Status,
                CommandText = targetConnector is null
                    ? _localization.GetString("QuickAct_CommandOpenPlugins")
                    : _localization.GetString("QuickAct_CommandOpenConnector"),
                Route = "PluginManager",
                Kind = QuickActionRecommendedActionKind.Navigate,
                TargetId = targetConnector?.PluginId ?? 0
            });
        }

        if (selectedReady && AvailableDocuments.Count > 1)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryReview"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionScanTitle"),
                Detail = _localization.GetString("QuickAct_ActionScanDetail"),
                StatusLabel = DocumentsAvailableText(AvailableDocuments.Count),
                CommandText = _localization.GetString("QuickAct_CommandRunDuplicateScan"),
                Kind = QuickActionRecommendedActionKind.FindNearDuplicates
            });
        }

        if (AvailableDocuments.Count > 0)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryOrganize"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionOrganizeTitle"),
                Detail = _localization.GetString("QuickAct_ActionOrganizeDetail"),
                StatusLabel = DocumentsAvailableText(AvailableDocuments.Count),
                CommandText = _localization.GetString("QuickAct_CommandSuggestOrganization"),
                Kind = QuickActionRecommendedActionKind.SuggestOrganization
            });
        }

        if (items.Count == 0)
        {
            AddAction(new QuickActionRecommendedItem
            {
                CategoryLabel = _localization.GetString("QuickAct_CategoryExplore"),
                IconGlyph = "",
                Title = _localization.GetString("QuickAct_ActionTranslateTitle"),
                Detail = _localization.GetString("QuickAct_ActionTranslateDetail"),
                StatusLabel = _localization.GetString("QuickAct_StatusReady"),
                CommandText = _localization.GetString("QuickAct_CommandOpenTranslate"),
                Kind = QuickActionRecommendedActionKind.SelectTranslateTab
            });
        }

        RecommendedActions = new ObservableCollection<QuickActionRecommendedItem>(items.Take(4));
        OnPropertyChanged(nameof(HasRecommendedActions));
    }

    // ── Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task ExecuteRecommendedActionAsync(QuickActionRecommendedItem? action)
    {
        if (action is null)
        {
            return;
        }

        if (action.DocumentId > 0 && SelectedDocument?.Id != action.DocumentId)
        {
            var matchingDocument = AvailableDocuments.FirstOrDefault(document => document.Id == action.DocumentId);
            if (matchingDocument is not null)
            {
                SelectedDocument = matchingDocument;
            }
        }

        switch (action.Kind)
        {
            case QuickActionRecommendedActionKind.SummarizeSelectedDocument:
                SelectedTabIndex = 0;
                await SummarizeAsync();
                break;

            case QuickActionRecommendedActionKind.ExtractKeyPointsSelectedDocument:
                SelectedTabIndex = 1;
                await ExtractKeyPointsAsync();
                break;

            case QuickActionRecommendedActionKind.FindNearDuplicates:
                SelectedTabIndex = 3;
                await FindNearDuplicatesAsync();
                break;

            case QuickActionRecommendedActionKind.SuggestOrganization:
                SelectedTabIndex = 4;
                await SuggestOrganizationAsync();
                break;

            case QuickActionRecommendedActionKind.SelectTranslateTab:
                SelectedTabIndex = 2;
                break;

            case QuickActionRecommendedActionKind.Navigate:
            default:
                if (!string.IsNullOrWhiteSpace(action.Route))
                {
                    StageRecommendedActionDrillIn(action);
                    NavigateRequested?.Invoke(action.Route);
                }

                break;
        }
    }

    [RelayCommand]
    private async Task SummarizeAsync()
    {
        if (SelectedDocument is null)
        {
            StatusMessage = _localization.GetString("QuickAct_SelectDocumentFirst");
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = _localization.GetString("QuickAct_Summarizing", SelectedDocument.FileName);
            SummaryResult = string.Empty;
            HasSummaryResult = false;

            SummaryResult = await _summaryService.SummarizeDocumentAsync(SelectedDocument.Id);
            HasSummaryResult = !string.IsNullOrWhiteSpace(SummaryResult);

            StatusMessage = _localization.GetString("QuickAct_SummaryDone");
            _logger.Information("Summarized document {DocumentId} ({FileName})",
                SelectedDocument.Id, SelectedDocument.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to summarize document {DocumentId}", SelectedDocument?.Id);
            StatusMessage = _localization.GetString("QuickAct_SummaryFailed", ex.Message);
            SummaryResult = string.Empty;
            HasSummaryResult = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ExtractKeyPointsAsync()
    {
        if (SelectedDocument is null)
        {
            StatusMessage = _localization.GetString("QuickAct_SelectDocumentFirst");
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = _localization.GetString("QuickAct_Extracting", SelectedDocument.FileName);
            KeyPoints.Clear();
            HasKeyPoints = false;

            var points = await _summaryService.ExtractKeyPointsAsync(SelectedDocument.Id);
            KeyPoints = new ObservableCollection<string>(points);
            HasKeyPoints = KeyPoints.Count > 0;

            StatusMessage = points.Count == 1
                ? _localization.GetString("QuickAct_ExtractedOne")
                : _localization.GetString("QuickAct_ExtractedMany", points.Count);
            _logger.Information("Extracted {Count} key points from document {DocumentId}",
                points.Count, SelectedDocument.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to extract key points from document {DocumentId}", SelectedDocument?.Id);
            StatusMessage = _localization.GetString("QuickAct_ExtractFailed", ex.Message);
            KeyPoints.Clear();
            HasKeyPoints = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task TranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationInput))
        {
            StatusMessage = _localization.GetString("QuickAct_EnterTextFirst");
            return;
        }

        if (SelectedLanguage is not { } language)
        {
            StatusMessage = _localization.GetString("QuickAct_SelectLanguageFirst");
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = _localization.GetString("QuickAct_Translating", language.DisplayName);
            TranslationOutput = string.Empty;
            HasTranslationOutput = false;

            TranslationOutput = await _summaryService.TranslateTextAsync(TranslationInput, language.PromptName);
            HasTranslationOutput = !string.IsNullOrWhiteSpace(TranslationOutput);

            StatusMessage = _localization.GetString("QuickAct_TranslationDone", language.DisplayName);
            _logger.Information("Translated {Length} chars to {Language}",
                TranslationInput.Length, language.PromptName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to translate text to {Language}", language.PromptName);
            StatusMessage = _localization.GetString("QuickAct_TranslationFailed", ex.Message);
            TranslationOutput = string.Empty;
            HasTranslationOutput = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = _localization.GetString("QuickAct_ScanningExact");
            DuplicateGroups.Clear();
            HasDuplicateResults = false;

            var groups = await _duplicateDetectionService.FindDuplicatesAsync();
            DuplicateGroups = BuildDuplicateDisplayGroups(groups);
            HasDuplicateResults = true;

            var totalWasted = groups.Sum(g => g.WastedStorageBytes);
            var wasted = FormatHelper.FormatBytes(totalWasted);
            StatusMessage = groups.Count switch
            {
                0 => _localization.GetString("QuickAct_NoExact"),
                1 => _localization.GetString("QuickAct_FoundExactOne", wasted),
                _ => _localization.GetString("QuickAct_FoundExactMany", groups.Count, wasted)
            };

            _logger.Information("Duplicate scan: {GroupCount} groups, {WastedBytes} bytes wasted",
                groups.Count, totalWasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to scan for duplicates");
            StatusMessage = _localization.GetString("QuickAct_ScanExactFailed", ex.Message);
            DuplicateGroups.Clear();
            HasDuplicateResults = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task FindNearDuplicatesAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = _localization.GetString("QuickAct_ScanningSemantic");
            DuplicateGroups.Clear();
            HasDuplicateResults = false;

            var groups = await _duplicateDetectionService.FindNearDuplicatesAsync();
            DuplicateGroups = BuildDuplicateDisplayGroups(groups);
            HasDuplicateResults = true;

            var totalWasted = groups.Sum(g => g.WastedStorageBytes);
            var redundant = FormatHelper.FormatBytes(totalWasted);
            StatusMessage = groups.Count switch
            {
                0 => _localization.GetString("QuickAct_NoSemantic"),
                1 => _localization.GetString("QuickAct_FoundSemanticOne", redundant),
                _ => _localization.GetString("QuickAct_FoundSemanticMany", groups.Count, redundant)
            };

            _logger.Information("Near-duplicate scan: {GroupCount} groups, {WastedBytes} bytes potentially redundant",
                groups.Count, totalWasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to scan for near-duplicates");
            StatusMessage = _localization.GetString("QuickAct_ScanSemanticFailed", ex.Message);
            DuplicateGroups.Clear();
            HasDuplicateResults = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task SuggestOrganizationAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = _localization.GetString("QuickAct_Analyzing");
            Suggestions.Clear();
            HasSuggestionResults = false;

            var results = await _organizationSuggestionService.SuggestOrganizationAsync();

            var displayItems = results.Select(s => new QuickActionOrganizationItem
            {
                DocumentId = s.DocumentId,
                FileName = s.FileName,
                SuggestedCollection = s.SuggestedCollection,
                SuggestedTags = new ObservableCollection<string>(s.SuggestedTags),
                TagsDisplay = s.SuggestedTags.Count > 0
                    ? string.Join(", ", s.SuggestedTags)
                    : _localization.GetString("QuickAct_NoTagsSuggested"),
                Reasoning = s.Reasoning,
                Confidence = s.Confidence,
                ConfidencePercent = (int)Math.Round(s.Confidence * 100),
                ConfidenceLabel = s.Confidence switch
                {
                    >= 0.8f => _localization.GetString("QuickAct_ConfidenceHigh"),
                    >= 0.5f => _localization.GetString("QuickAct_ConfidenceMedium"),
                    _ => _localization.GetString("QuickAct_ConfidenceLow")
                }
            });

            Suggestions = new ObservableCollection<QuickActionOrganizationItem>(displayItems);
            HasSuggestionResults = true;

            StatusMessage = results.Count switch
            {
                0 => _localization.GetString("QuickAct_AllOrganized"),
                1 => _localization.GetString("QuickAct_SuggestionsOne"),
                _ => _localization.GetString("QuickAct_SuggestionsMany", results.Count)
            };

            _logger.Information("Organization suggestion: {Count} suggestions generated", results.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate organization suggestions");
            StatusMessage = _localization.GetString("QuickAct_AnalysisFailed", ex.Message);
            Suggestions.Clear();
            HasSuggestionResults = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void Dispose()
    {
        _logger.Debug("QuickActionsViewModel disposed");
    }

    partial void OnSelectedDocumentChanged(QuickActionDocumentItem? value) => BuildRecommendedActions();

    partial void OnRecommendedActionsChanged(ObservableCollection<QuickActionRecommendedItem> value) =>
        OnPropertyChanged(nameof(HasRecommendedActions));

    private string DocumentsAvailableText(int count) => count == 1
        ? _localization.GetString("QuickAct_DocumentCountOne")
        : _localization.GetString("QuickAct_DocumentCountMany", count);

    private ObservableCollection<QuickActionDuplicateGroupItem> BuildDuplicateDisplayGroups(
        IReadOnlyList<DuplicateGroup> groups)
    {
        return new ObservableCollection<QuickActionDuplicateGroupItem>(
            groups.Select(group =>
            {
                var topConfidence = group.Documents
                    .Where(document => document.Evidence is not null)
                    .Select(document => document.Evidence!.Confidence)
                    .DefaultIfEmpty()
                    .Max();
                var topConfidencePercent = (int)Math.Round(topConfidence * 100);
                var contentHash = TruncateHash(group.ContentHash);
                var isSemantic = group.MatchKind == DuplicateMatchKind.Semantic;

                return new QuickActionDuplicateGroupItem
                {
                    ContentHash = contentHash,
                    MatchKind = group.MatchKind,
                    Documents = new ObservableCollection<QuickActionDuplicateDocItem>(
                        group.Documents.Select(document => new QuickActionDuplicateDocItem
                        {
                            DocumentId = document.DocumentId,
                            FileName = document.FileName,
                            FileSize = FormatHelper.FormatBytes(document.FileSizeBytes),
                            // Stored in UTC; shown in the user's time zone.
                            ImportedAt = document.ImportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                            EvidenceLabel = FormatEvidenceLabel(document.Evidence)
                        })),
                    WastedStorage = FormatHelper.FormatBytes(group.WastedStorageBytes),
                    DocumentCount = group.Documents.Count,
                    TopConfidencePercent = topConfidencePercent,
                    GroupLabel = isSemantic
                        ? _localization.GetString("QuickAct_GroupSemantic", group.Documents.Count)
                        : _localization.GetString("QuickAct_GroupExact", group.Documents.Count),
                    MatchLabel = isSemantic
                        ? _localization.GetString("QuickAct_MatchSemantic")
                        : _localization.GetString("QuickAct_MatchExact"),
                    DetailLabel = !isSemantic
                        ? _localization.GetString("QuickAct_DetailHash", contentHash)
                        : topConfidencePercent > 0
                            ? _localization.GetString("QuickAct_DetailSemanticConfidence", topConfidencePercent)
                            : _localization.GetString("QuickAct_DetailSemantic")
                };
            }));
    }

    private string TruncateHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return _localization.GetString("QuickAct_HashUnavailable");
        }

        return contentHash.Length <= 12
            ? contentHash
            : $"{contentHash[..12]}...";
    }

    private string FormatEvidenceLabel(DuplicateEvidence? evidence)
    {
        if (evidence is null)
        {
            return string.Empty;
        }

        var percent = (int)Math.Round(evidence.Confidence * 100);
        return evidence.SupportingChunkCount == 1
            ? _localization.GetString("QuickAct_EvidenceOne", percent)
            : _localization.GetString("QuickAct_EvidenceMany", percent, evidence.SupportingChunkCount);
    }

    private static int ParseCompactNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.EndsWith("K", StringComparison.Ordinal))
        {
            return double.TryParse(normalized[..^1], out var thousands)
                ? (int)Math.Round(thousands * 1_000)
                : 0;
        }

        if (normalized.EndsWith("M", StringComparison.Ordinal))
        {
            return double.TryParse(normalized[..^1], out var millions)
                ? (int)Math.Round(millions * 1_000_000)
                : 0;
        }

        return int.TryParse(normalized, out var count)
            ? count
            : 0;
    }

    // Typed status, not the card text, which follows the UI language.
    private static bool ConnectorsNeedSetup(OperationsOverviewSnapshot snapshot) =>
        snapshot.Connectors.Headline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        snapshot.Connectors.StatusKind is OperationsStatusKind.NoPluginsInstalled
            or OperationsStatusKind.PluginsInstalled;

    private string NormalizeStatusLabel(string indexingStatus) =>
        indexingStatus switch
        {
            "completed" => _localization.GetString("QuickAct_StatusSearchable"),
            "pending" => _localization.GetString("QuickAct_StatusQueued"),
            "processing" => _localization.GetString("QuickAct_StatusProcessing"),
            "failed" => _localization.GetString("QuickAct_StatusNeedsAttention"),
            _ when string.IsNullOrWhiteSpace(indexingStatus) => _localization.GetString("QuickAct_StatusNeedsReview"),
            _ => indexingStatus
        };

    private void StageRecommendedActionDrillIn(QuickActionRecommendedItem action)
    {
        if (_operationsDrillInService is null)
        {
            return;
        }

        var sourceLabel = _localization.GetString("QuickAct_DrillInSourceLabel", action.Title);
        switch (action.Route)
        {
            case "KnowledgeVault" when action.DocumentId > 0:
                _operationsDrillInService.StageDocumentRequest(
                    new OperationsDocumentDrillInRequest(action.DocumentId, sourceLabel));
                break;

            case "Inbox" when action.TargetId > 0:
                _operationsDrillInService.StageInboxRequest(
                    new OperationsInboxDrillInRequest(action.TargetId, sourceLabel));
                break;

            case "PluginManager" when action.TargetId > 0:
                _operationsDrillInService.StagePluginRequest(
                    new OperationsPluginDrillInRequest(action.TargetId, sourceLabel));
                break;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
//  DISPLAY ITEM CLASSES
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a document available for selection in the Quick Actions document picker.
/// </summary>
public class QuickActionDocumentItem
{
    public long Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public string FileSizeFormatted { get; init; } = string.Empty;
    public string IndexingStatus { get; init; } = string.Empty;
    public string DisplayLabel { get; init; } = string.Empty;

    public override string ToString() => DisplayLabel;
}

/// <summary>
/// A language Translate offers: the name the list shows, in the user's language, and the English
/// name the translation is asked for in.
/// </summary>
public sealed class QuickActionLanguageOption
{
    public QuickActionLanguageOption(string promptName, string displayName)
    {
        PromptName = promptName;
        DisplayName = displayName;
    }

    /// <summary>The English name, as the translation prompt names the language.</summary>
    public string PromptName { get; }

    /// <summary>The name shown in the language list.</summary>
    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}

public enum QuickActionRecommendedActionKind
{
    Navigate,
    SummarizeSelectedDocument,
    ExtractKeyPointsSelectedDocument,
    FindNearDuplicates,
    SuggestOrganization,
    SelectTranslateTab
}

public class QuickActionRecommendedItem
{
    public string CategoryLabel { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public QuickActionRecommendedActionKind Kind { get; init; }
    public long DocumentId { get; init; }
    public long TargetId { get; init; }
}

/// <summary>
/// Represents a group of duplicate documents found by the detection service. The view model sets
/// the labels in the user's language.
/// </summary>
public class QuickActionDuplicateGroupItem
{
    public string ContentHash { get; init; } = string.Empty;
    public DuplicateMatchKind MatchKind { get; init; }
    public ObservableCollection<QuickActionDuplicateDocItem> Documents { get; init; } = new();
    public string WastedStorage { get; init; } = "0 B";
    public int DocumentCount { get; init; }
    public int TopConfidencePercent { get; init; }
    public string GroupLabel { get; init; } = string.Empty;
    public string MatchLabel { get; init; } = string.Empty;
    public string DetailLabel { get; init; } = string.Empty;
}

/// <summary>
/// Represents a single document within a duplicate group.
/// </summary>
public class QuickActionDuplicateDocItem
{
    public long DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileSize { get; init; } = string.Empty;
    public string ImportedAt { get; init; } = string.Empty;
    public string EvidenceLabel { get; init; } = string.Empty;
    public bool HasEvidence => !string.IsNullOrWhiteSpace(EvidenceLabel);
}

/// <summary>
/// Represents an AI-generated organization suggestion for an uncategorized document.
/// </summary>
public class QuickActionOrganizationItem
{
    public long DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string SuggestedCollection { get; init; } = string.Empty;
    public ObservableCollection<string> SuggestedTags { get; init; } = new();

    /// <summary>The suggested tags, or the view model's localized "No tags suggested".</summary>
    public string TagsDisplay { get; init; } = string.Empty;
    public string Reasoning { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public int ConfidencePercent { get; init; }
    public string ConfidenceLabel { get; init; } = string.Empty;
}

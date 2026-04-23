using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class QuickActionsViewModel : ObservableObject, IDisposable
{
    // ── Services ─────────────────────────────────────────────
    private readonly ISummaryService _summaryService;
    private readonly IDuplicateDetectionService _duplicateDetectionService;
    private readonly IOrganizationSuggestionService _organizationSuggestionService;
    private readonly IDocumentService _documentService;
    private readonly ILogger _logger;

    // ── Document Selection ───────────────────────────────────
    [ObservableProperty] private ObservableCollection<QuickActionDocumentItem> _availableDocuments = new();
    [ObservableProperty] private QuickActionDocumentItem? _selectedDocument;

    // ── Summarize Tab ────────────────────────────────────────
    [ObservableProperty] private string _summaryResult = string.Empty;

    // ── Key Points Tab ───────────────────────────────────────
    [ObservableProperty] private ObservableCollection<string> _keyPoints = new();

    // ── Translate Tab ────────────────────────────────────────
    [ObservableProperty] private string _translationInput = string.Empty;
    [ObservableProperty] private string _translationOutput = string.Empty;
    [ObservableProperty] private string _selectedLanguage = "Spanish";
    [ObservableProperty] private ObservableCollection<string> _availableLanguages = new(new[]
    {
        "Spanish", "French", "German", "Chinese", "Japanese",
        "Korean", "Portuguese", "Italian", "Russian", "Arabic"
    });

    // ── Duplicates Tab ───────────────────────────────────────
    [ObservableProperty] private ObservableCollection<QuickActionDuplicateGroupItem> _duplicateGroups = new();

    // ── Organize Tab ─────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<QuickActionOrganizationItem> _suggestions = new();

    // ── UI State ─────────────────────────────────────────────
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private int _selectedTabIndex;

    // ── Result Visibility ────────────────────────────────────
    [ObservableProperty] private bool _hasSummaryResult;
    [ObservableProperty] private bool _hasKeyPoints;
    [ObservableProperty] private bool _hasTranslationOutput;
    [ObservableProperty] private bool _hasDuplicateResults;
    [ObservableProperty] private bool _hasSuggestionResults;

    public QuickActionsViewModel(
        ISummaryService summaryService,
        IDuplicateDetectionService duplicateDetectionService,
        IOrganizationSuggestionService organizationSuggestionService,
        IDocumentService documentService,
        ILogger logger)
    {
        _summaryService = summaryService;
        _duplicateDetectionService = duplicateDetectionService;
        _organizationSuggestionService = organizationSuggestionService;
        _documentService = documentService;
        _logger = logger;

        _logger.Debug("QuickActionsViewModel created with services");
    }

    public async Task InitializeAsync()
    {
        _logger.Information("QuickActions initializing...");
        await LoadAvailableDocumentsAsync();
        _logger.Information("QuickActions initialized with {Count} documents", AvailableDocuments.Count);
    }

    private async Task LoadAvailableDocumentsAsync()
    {
        try
        {
            StatusMessage = "Loading documents...";
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

            StatusMessage = $"{AvailableDocuments.Count} documents available";
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load available documents for Quick Actions");
            StatusMessage = "Failed to load documents";
            AvailableDocuments = new ObservableCollection<QuickActionDocumentItem>();
        }
    }

    // ── Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task SummarizeAsync()
    {
        if (SelectedDocument is null)
        {
            StatusMessage = "Please select a document first";
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = $"Summarizing {SelectedDocument.FileName}...";
            SummaryResult = string.Empty;
            HasSummaryResult = false;

            SummaryResult = await _summaryService.SummarizeDocumentAsync(SelectedDocument.Id);
            HasSummaryResult = !string.IsNullOrWhiteSpace(SummaryResult);

            StatusMessage = "Summary generated successfully";
            _logger.Information("Summarized document {DocumentId} ({FileName})",
                SelectedDocument.Id, SelectedDocument.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to summarize document {DocumentId}", SelectedDocument?.Id);
            StatusMessage = $"Summarization failed: {ex.Message}";
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
            StatusMessage = "Please select a document first";
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = $"Extracting key points from {SelectedDocument.FileName}...";
            KeyPoints.Clear();
            HasKeyPoints = false;

            var points = await _summaryService.ExtractKeyPointsAsync(SelectedDocument.Id);
            KeyPoints = new ObservableCollection<string>(points);
            HasKeyPoints = KeyPoints.Count > 0;

            StatusMessage = $"Extracted {points.Count} key points";
            _logger.Information("Extracted {Count} key points from document {DocumentId}",
                points.Count, SelectedDocument.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to extract key points from document {DocumentId}", SelectedDocument?.Id);
            StatusMessage = $"Extraction failed: {ex.Message}";
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
            StatusMessage = "Please enter text to translate";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedLanguage))
        {
            StatusMessage = "Please select a target language";
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = $"Translating to {SelectedLanguage}...";
            TranslationOutput = string.Empty;
            HasTranslationOutput = false;

            TranslationOutput = await _summaryService.TranslateTextAsync(TranslationInput, SelectedLanguage);
            HasTranslationOutput = !string.IsNullOrWhiteSpace(TranslationOutput);

            StatusMessage = $"Translation to {SelectedLanguage} complete";
            _logger.Information("Translated {Length} chars to {Language}",
                TranslationInput.Length, SelectedLanguage);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to translate text to {Language}", SelectedLanguage);
            StatusMessage = $"Translation failed: {ex.Message}";
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
            StatusMessage = "Scanning for exact duplicate documents...";
            DuplicateGroups.Clear();
            HasDuplicateResults = false;

            var groups = await _duplicateDetectionService.FindDuplicatesAsync();
            DuplicateGroups = BuildDuplicateDisplayGroups(groups);
            HasDuplicateResults = true;

            var totalWasted = groups.Sum(g => g.WastedStorageBytes);
            StatusMessage = groups.Count > 0
                ? $"Found {groups.Count} exact duplicate groups ({FormatHelper.FormatBytes(totalWasted)} wasted)"
                : "No exact duplicates found";

            _logger.Information("Duplicate scan: {GroupCount} groups, {WastedBytes} bytes wasted",
                groups.Count, totalWasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to scan for duplicates");
            StatusMessage = $"Duplicate scan failed: {ex.Message}";
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
            StatusMessage = "Scanning for semantic near-duplicate documents...";
            DuplicateGroups.Clear();
            HasDuplicateResults = false;

            var groups = await _duplicateDetectionService.FindNearDuplicatesAsync();
            DuplicateGroups = BuildDuplicateDisplayGroups(groups);
            HasDuplicateResults = true;

            var totalWasted = groups.Sum(g => g.WastedStorageBytes);
            StatusMessage = groups.Count > 0
                ? $"Found {groups.Count} semantic near-duplicate groups ({FormatHelper.FormatBytes(totalWasted)} potentially redundant)"
                : "No semantic near-duplicates found";

            _logger.Information("Near-duplicate scan: {GroupCount} groups, {WastedBytes} bytes potentially redundant",
                groups.Count, totalWasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to scan for near-duplicates");
            StatusMessage = $"Near-duplicate scan failed: {ex.Message}";
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
            StatusMessage = "Analyzing documents for organization suggestions...";
            Suggestions.Clear();
            HasSuggestionResults = false;

            var results = await _organizationSuggestionService.SuggestOrganizationAsync();

            var displayItems = results.Select(s => new QuickActionOrganizationItem
            {
                DocumentId = s.DocumentId,
                FileName = s.FileName,
                SuggestedCollection = s.SuggestedCollection,
                SuggestedTags = new ObservableCollection<string>(s.SuggestedTags),
                Reasoning = s.Reasoning,
                Confidence = s.Confidence,
                ConfidencePercent = (int)Math.Round(s.Confidence * 100),
                ConfidenceLabel = s.Confidence switch
                {
                    >= 0.8f => "High",
                    >= 0.5f => "Medium",
                    _ => "Low"
                }
            });

            Suggestions = new ObservableCollection<QuickActionOrganizationItem>(displayItems);
            HasSuggestionResults = true;

            StatusMessage = results.Count > 0
                ? $"Generated {results.Count} organization suggestions"
                : "All documents are already organized";

            _logger.Information("Organization suggestion: {Count} suggestions generated", results.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate organization suggestions");
            StatusMessage = $"Analysis failed: {ex.Message}";
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

    private static ObservableCollection<QuickActionDuplicateGroupItem> BuildDuplicateDisplayGroups(
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

                return new QuickActionDuplicateGroupItem
                {
                    ContentHash = TruncateHash(group.ContentHash),
                    MatchKind = group.MatchKind,
                    Documents = new ObservableCollection<QuickActionDuplicateDocItem>(
                        group.Documents.Select(document => new QuickActionDuplicateDocItem
                        {
                            DocumentId = document.DocumentId,
                            FileName = document.FileName,
                            FileSize = FormatHelper.FormatBytes(document.FileSizeBytes),
                            ImportedAt = document.ImportedAt.ToString("yyyy-MM-dd HH:mm"),
                            EvidenceLabel = FormatEvidenceLabel(document.Evidence)
                        })),
                    WastedStorage = FormatHelper.FormatBytes(group.WastedStorageBytes),
                    DocumentCount = group.Documents.Count,
                    TopConfidencePercent = (int)Math.Round(topConfidence * 100)
                };
            }));
    }

    private static string TruncateHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return "n/a";
        }

        return contentHash.Length <= 12
            ? contentHash
            : $"{contentHash[..12]}...";
    }

    private static string FormatEvidenceLabel(DuplicateEvidence? evidence)
    {
        if (evidence is null)
        {
            return string.Empty;
        }

        return $"{(int)Math.Round(evidence.Confidence * 100)}% confidence from {evidence.SupportingChunkCount} matching chunk(s)";
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
/// Represents a group of duplicate documents found by the detection service.
/// </summary>
public class QuickActionDuplicateGroupItem
{
    public string ContentHash { get; init; } = string.Empty;
    public DuplicateMatchKind MatchKind { get; init; }
    public ObservableCollection<QuickActionDuplicateDocItem> Documents { get; init; } = new();
    public string WastedStorage { get; init; } = "0 B";
    public int DocumentCount { get; init; }
    public int TopConfidencePercent { get; init; }
    public string GroupLabel => MatchKind == DuplicateMatchKind.Semantic
        ? $"{DocumentCount} files are semantically similar"
        : $"{DocumentCount} files share identical content";
    public string MatchLabel => MatchKind == DuplicateMatchKind.Semantic ? "Semantic" : "Exact";
    public string DetailLabel => MatchKind == DuplicateMatchKind.Semantic
        ? TopConfidencePercent > 0
            ? $"Embedding evidence up to {TopConfidencePercent}% confidence"
            : "Embedding-based near-duplicate group"
        : $"Hash: {ContentHash}";
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
    public string Reasoning { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public int ConfidencePercent { get; init; }
    public string ConfidenceLabel { get; init; } = string.Empty;
    public string TagsDisplay => SuggestedTags.Count > 0 ? string.Join(", ", SuggestedTags) : "No tags suggested";
}

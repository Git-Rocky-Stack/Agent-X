using System.Collections.ObjectModel;
using System.ComponentModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class ComparisonViewModel : ObservableObject
{
    private readonly IComparisonService _comparisonService;
    private readonly IDocumentService _documentService;
    private readonly ILocalizationService _localization;

    // -- Page State -------------------------------------------
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isComparing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _progressMessage = string.Empty;

    // -- Document Selection -----------------------------------
    public ObservableCollection<DocumentSelectItem> AvailableDocuments { get; } = new();
    public ObservableCollection<DocumentSelectItem> SelectedDocuments { get; } = new();
    [ObservableProperty] private string _focusQuery = string.Empty;

    /// <summary>
    /// The detail level choices. The combo shows each one's translated name and the comparison
    /// is sent its value ("summary" or "detailed").
    /// </summary>
    public IReadOnlyList<ComparisonDetailLevelOption> DetailLevels { get; }

    [ObservableProperty] private ComparisonDetailLevelOption? _detailLevel;

    // -- Report Results ---------------------------------------
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _reportSummary = string.Empty;
    public ObservableCollection<string> Similarities { get; } = new();
    public ObservableCollection<string> Differences { get; } = new();
    public ObservableCollection<string> Contradictions { get; } = new();
    public ObservableCollection<UniquePointGroup> UniquePoints { get; } = new();
    public bool HasUniquePoints => UniquePoints.Count > 0;
    [ObservableProperty] private long _reportTokensUsed;
    [ObservableProperty] private double _reportDurationMs;

    private ComparisonReport? _currentReport;
    private CancellationTokenSource? _compareCts;

    /// <summary>
    /// Saves an exported report (the page shows a file save picker) and returns where it was
    /// saved. Without it, Export Report says saving is unavailable rather than claiming success.
    /// </summary>
    public Func<ComparisonReportExportRequest, Task<ComparisonReportExportResult>>? SaveReportExportAsync { get; set; }

    public ComparisonViewModel(
        IComparisonService comparisonService,
        IDocumentService documentService,
        ILocalizationService localization)
    {
        _comparisonService = comparisonService;
        _documentService = documentService;
        _localization = localization;

        DetailLevels =
        [
            new ComparisonDetailLevelOption("summary", _localization.GetString("Comp_DetailLevelSummary")),
            new ComparisonDetailLevelOption("detailed", _localization.GetString("Comp_DetailLevelDetailed"))
        ];
        DetailLevel = DetailLevels[1];
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var docs = await _documentService.GetAllDocumentsAsync();
            ResetAvailableDocuments();
            AvailableDocuments.Clear();
            SelectedDocuments.Clear();
            foreach (var doc in docs)
            {
                var item = new DocumentSelectItem
                {
                    Id = doc.Id,
                    FileName = doc.FileName,
                    FileType = doc.FileType,
                    IsSelected = false
                };

                item.PropertyChanged += OnDocumentSelectionChanged;
                AvailableDocuments.Add(item);
            }

            StatusMessage = AvailableDocuments.Count == 1
                ? _localization.GetString("Comp_DocumentsAvailableOne", AvailableDocuments.Count)
                : _localization.GetString("Comp_DocumentsAvailableMany", AvailableDocuments.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load documents for comparison");
            StatusMessage = _localization.GetString("Comp_LoadDocumentsFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CompareDocumentsAsync()
    {
        SyncSelectedDocuments();

        if (SelectedDocuments.Count < 2)
        {
            StatusMessage = _localization.GetString("Comp_SelectAtLeastTwo");
            return;
        }

        IsComparing = true;
        HasReport = false;
        ProgressMessage = _localization.GetString("Comp_AnalyzingDocuments");
        _compareCts = new CancellationTokenSource();

        try
        {
            var docIds = SelectedDocuments.Select(d => d.Id).ToList();
            var options = new ComparisonOptions
            {
                FocusQuery = string.IsNullOrWhiteSpace(FocusQuery) ? null : FocusQuery,
                DetailLevel = DetailLevel?.Value ?? "detailed"
            };

            var progress = new Progress<string>(msg =>
            {
                ProgressMessage = msg;
            });

            var report = await _comparisonService.CompareDocumentsAsync(
                docIds, options, progress, _compareCts.Token);

            _currentReport = report;
            ReportSummary = report.Summary;
            ReportTokensUsed = report.TotalTokensUsed;
            ReportDurationMs = report.DurationMs;

            Similarities.Clear();
            foreach (var s in report.Similarities) Similarities.Add(s);

            Differences.Clear();
            foreach (var d in report.Differences) Differences.Add(d);

            Contradictions.Clear();
            foreach (var c in report.Contradictions) Contradictions.Add(c);

            UniquePoints.Clear();
            foreach (var kvp in report.UniquePoints)
            {
                UniquePoints.Add(new UniquePointGroup
                {
                    DocumentName = kvp.Key,
                    Points = new ObservableCollection<string>(kvp.Value)
                });
            }
            OnPropertyChanged(nameof(HasUniquePoints));

            HasReport = true;
            StatusMessage = _localization.GetString("Comp_ComparisonComplete", report.DurationMs.ToString("F0"));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.GetString("Comp_ComparisonCancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Document comparison failed");
            StatusMessage = _localization.GetString("Comp_ComparisonFailed", ex.Message);
        }
        finally
        {
            IsComparing = false;
            _compareCts?.Dispose();
            _compareCts = null;
        }
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (_currentReport is null) return;

        try
        {
            if (SaveReportExportAsync is null)
            {
                StatusMessage = _localization.GetString("Comp_ExportUnavailable");
                return;
            }

            var markdown = await _comparisonService.ExportComparisonAsMarkdownAsync(_currentReport);
            var result = await SaveReportExportAsync(new ComparisonReportExportRequest(
                $"agent-x-comparison-{DateTime.Now:yyyyMMdd-HHmmss}.md",
                markdown));

            if (!result.IsSaved)
            {
                StatusMessage = _localization.GetString("Comp_ExportCancelled");
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(result.FilePath)
                ? _localization.GetString("Comp_MarkdownFile")
                : Path.GetFileName(result.FilePath);
            StatusMessage = _localization.GetString("Comp_ReportSavedTo", fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export comparison report");
            StatusMessage = _localization.GetString("Comp_ExportFailed");
        }
    }

    [RelayCommand]
    private void CancelComparison()
    {
        _compareCts?.Cancel();
    }

    private void OnDocumentSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSelectItem.IsSelected))
        {
            SyncSelectedDocuments();
        }
    }

    private void SyncSelectedDocuments()
    {
        SelectedDocuments.Clear();
        foreach (var item in AvailableDocuments.Where(item => item.IsSelected))
        {
            SelectedDocuments.Add(item);
        }
    }

    private void ResetAvailableDocuments()
    {
        foreach (var item in AvailableDocuments)
        {
            item.PropertyChanged -= OnDocumentSelectionChanged;
        }
    }
}

/// <summary>A comparison report to save: the suggested file name and the Markdown text.</summary>
public sealed record ComparisonReportExportRequest(string SuggestedFileName, string Markdown);

/// <summary>A detail level choice: the value the comparison is sent and the name shown for it.</summary>
public sealed record ComparisonDetailLevelOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Where an exported comparison report was saved, or that the save was cancelled.</summary>
public sealed record ComparisonReportExportResult(bool IsSaved, string? FilePath)
{
    public static ComparisonReportExportResult Saved(string filePath) => new(true, filePath);
    public static ComparisonReportExportResult Cancelled() => new(false, null);
}

public partial class DocumentSelectItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _fileType = string.Empty;
    [ObservableProperty] private bool _isSelected;
}

public partial class UniquePointGroup : ObservableObject
{
    [ObservableProperty] private string _documentName = string.Empty;
    public ObservableCollection<string> Points { get; set; } = new();
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class ComparisonViewModel : ObservableObject
{
    private readonly IComparisonService _comparisonService;
    private readonly IDocumentService _documentService;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isComparing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _progressMessage = string.Empty;

    // ── Document Selection ───────────────────────────────────
    public ObservableCollection<DocumentSelectItem> AvailableDocuments { get; } = new();
    public ObservableCollection<DocumentSelectItem> SelectedDocuments { get; } = new();
    [ObservableProperty] private string _focusQuery = string.Empty;
    [ObservableProperty] private string _detailLevel = "detailed";
    public List<string> DetailLevels { get; } = new() { "summary", "detailed" };

    // ── Report Results ───────────────────────────────────────
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
        IDocumentService documentService)
    {
        _comparisonService = comparisonService;
        _documentService = documentService;
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

            StatusMessage = $"{AvailableDocuments.Count} documents available";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load documents for comparison");
            StatusMessage = "Failed to load documents";
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
            StatusMessage = "Select at least 2 documents to compare";
            return;
        }

        IsComparing = true;
        HasReport = false;
        ProgressMessage = "Analyzing documents...";
        _compareCts = new CancellationTokenSource();

        try
        {
            var docIds = SelectedDocuments.Select(d => d.Id).ToList();
            var options = new ComparisonOptions
            {
                FocusQuery = string.IsNullOrWhiteSpace(FocusQuery) ? null : FocusQuery,
                DetailLevel = DetailLevel
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
            StatusMessage = $"Comparison complete in {report.DurationMs:F0}ms";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Comparison cancelled";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Document comparison failed");
            StatusMessage = $"Comparison failed: {ex.Message}";
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
                StatusMessage = "Export unavailable";
                return;
            }

            var markdown = await _comparisonService.ExportComparisonAsMarkdownAsync(_currentReport);
            var result = await SaveReportExportAsync(new ComparisonReportExportRequest(
                $"agent-x-comparison-{DateTime.Now:yyyyMMdd-HHmmss}.md",
                markdown));

            if (!result.IsSaved)
            {
                StatusMessage = "Export cancelled";
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(result.FilePath)
                ? "Markdown file"
                : Path.GetFileName(result.FilePath);
            StatusMessage = $"Comparison report saved to {fileName}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export comparison report");
            StatusMessage = "Export failed";
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

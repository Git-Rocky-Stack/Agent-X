using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Core.Documents;
using AgentX.Core.Data.Entities;
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
    private void ToggleDocumentSelection(DocumentSelectItem doc)
    {
        doc.IsSelected = !doc.IsSelected;
        SelectedDocuments.Clear();
        foreach (var d in AvailableDocuments.Where(d => d.IsSelected))
        {
            SelectedDocuments.Add(d);
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
            var markdown = await _comparisonService.ExportComparisonAsMarkdownAsync(_currentReport);
            StatusMessage = "Comparison report exported as Markdown";
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

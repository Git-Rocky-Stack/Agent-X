using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Annotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class AnnotationsViewModel : ObservableObject
{
    private readonly IAnnotationService _annotationService;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _searchQuery = string.Empty;

    // ── Filters ──────────────────────────────────────────────
    [ObservableProperty] private string _selectedColorFilter = "All";
    public List<string> ColorOptions { get; } = new() { "All", "yellow", "green", "blue", "red", "purple" };

    // ── Annotation List ──────────────────────────────────────
    public ObservableCollection<AnnotationDisplayItem> Annotations { get; } = new();
    [ObservableProperty] private AnnotationDisplayItem? _selectedAnnotation;
    [ObservableProperty] private bool _hasAnnotations;
    [ObservableProperty] private int _totalCount;

    // ── Stats ────────────────────────────────────────────────
    public ObservableCollection<ColorStatItem> ColorStats { get; } = new();

    // ── Editor State ─────────────────────────────────────────
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editNoteText = string.Empty;
    [ObservableProperty] private string _editColor = "yellow";

    public Func<AnnotationMarkdownExportRequest, Task<AnnotationMarkdownExportResult>>? SaveMarkdownExportAsync { get; set; }

    public AnnotationsViewModel(IAnnotationService annotationService)
    {
        _annotationService = annotationService;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await LoadAnnotationsAsync();
            await LoadColorStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize AnnotationsViewModel");
            StatusMessage = "Failed to load annotations";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAnnotationsAsync()
    {
        try
        {
            IReadOnlyList<AnnotationEntity> annotations;

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                annotations = await _annotationService.SearchAnnotationsAsync(SearchQuery);
            }
            else if (SelectedColorFilter != "All")
            {
                annotations = await _annotationService.GetAnnotationsByColorAsync(SelectedColorFilter);
            }
            else
            {
                annotations = await _annotationService.GetAllAnnotationsAsync(0, 200);
            }

            Annotations.Clear();
            foreach (var a in annotations)
            {
                Annotations.Add(new AnnotationDisplayItem
                {
                    Id = a.Id,
                    DocumentId = a.DocumentId,
                    DocumentName = a.Document?.FileName ?? "Unknown",
                    HighlightedText = a.HighlightedText,
                    NoteText = a.NoteText ?? string.Empty,
                    Color = a.Color,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt
                });
            }

            HasAnnotations = Annotations.Count > 0;
            TotalCount = await _annotationService.GetAnnotationCountAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load annotations");
        }
    }

    private async Task LoadColorStatsAsync()
    {
        try
        {
            var distribution = await _annotationService.GetColorDistributionAsync();
            ColorStats.Clear();
            foreach (var kvp in distribution)
            {
                ColorStats.Add(new ColorStatItem { Color = kvp.Key, Count = kvp.Value });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load color stats");
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadAnnotationsAsync();
    }

    [RelayCommand]
    private async Task FilterByColorAsync(string color)
    {
        SelectedColorFilter = color;
        await LoadAnnotationsAsync();
    }

    [RelayCommand]
    private void EditAnnotation(AnnotationDisplayItem item)
    {
        SelectedAnnotation = item;
        EditNoteText = item.NoteText;
        EditColor = item.Color;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAnnotationAsync()
    {
        if (SelectedAnnotation is null) return;

        try
        {
            await _annotationService.UpdateAnnotationAsync(
                SelectedAnnotation.Id,
                noteText: EditNoteText,
                color: EditColor);

            SelectedAnnotation.NoteText = EditNoteText;
            SelectedAnnotation.Color = EditColor;
            IsEditing = false;
            StatusMessage = "Annotation updated";
            await LoadColorStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update annotation");
            StatusMessage = "Failed to update annotation";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task DeleteAnnotationAsync(long annotationId)
    {
        try
        {
            await _annotationService.DeleteAnnotationAsync(annotationId);
            var item = Annotations.FirstOrDefault(a => a.Id == annotationId);
            if (item is not null)
            {
                Annotations.Remove(item);
            }
            HasAnnotations = Annotations.Count > 0;
            TotalCount--;
            StatusMessage = "Annotation deleted";
            await LoadColorStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete annotation {Id}", annotationId);
            StatusMessage = "Failed to delete annotation";
        }
    }

    [RelayCommand]
    private async Task ExportAnnotationsAsync()
    {
        try
        {
            var markdown = await _annotationService.ExportAnnotationsAsMarkdownAsync();

            if (SaveMarkdownExportAsync is null)
            {
                StatusMessage = "Export unavailable";
                return;
            }

            var result = await SaveMarkdownExportAsync(new AnnotationMarkdownExportRequest(
                CreateSuggestedExportFileName(),
                markdown));

            if (!result.IsSaved)
            {
                StatusMessage = "Export cancelled";
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(result.FilePath)
                ? "Markdown file"
                : Path.GetFileName(result.FilePath);
            StatusMessage = $"Exported {TotalCount} annotations to {fileName}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export annotations");
            StatusMessage = "Export failed";
        }
    }

    private static string CreateSuggestedExportFileName()
    {
        return $"agent-x-annotations-{DateTime.Now:yyyyMMdd-HHmmss}.md";
    }
}

public sealed record AnnotationMarkdownExportRequest(string SuggestedFileName, string Markdown);

public sealed record AnnotationMarkdownExportResult(bool IsSaved, string? FilePath)
{
    public static AnnotationMarkdownExportResult Saved(string filePath) => new(true, filePath);
    public static AnnotationMarkdownExportResult Cancelled() => new(false, null);
}

public partial class AnnotationDisplayItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private long _documentId;
    [ObservableProperty] private string _documentName = string.Empty;
    [ObservableProperty] private string _highlightedText = string.Empty;
    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private string _color = "yellow";
    [ObservableProperty] private DateTime _createdAt;
    [ObservableProperty] private DateTime _updatedAt;
}

public partial class ColorStatItem : ObservableObject
{
    [ObservableProperty] private string _color = string.Empty;
    [ObservableProperty] private int _count;
}

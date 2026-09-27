using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Annotations;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class AnnotationsViewModel : ObservableObject
{
    private readonly IAnnotationService _annotationService;
    private readonly ILocalizationService _localization;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _searchQuery = string.Empty;

    // ── Filters ──────────────────────────────────────────────
    [ObservableProperty] private string _selectedColorFilter = "All";
    public List<string> ColorOptions { get; } = new() { "All", "yellow", "green", "blue", "red", "purple" };

    /// <summary>
    /// Colors an annotation can actually be. This is <see cref="ColorOptions"/> without
    /// the "All" filter sentinel, which is a query term rather than a color and must
    /// never be offered when editing.
    /// </summary>
    public IReadOnlyList<string> EditColorOptions { get; } =
        new[] { "yellow", "green", "blue", "red", "purple" };

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

    /// <summary>
    /// Asks the user to confirm a delete and answers true when they do. The page supplies it (a
    /// ContentDialog). While it is unset, Delete deletes nothing.
    /// </summary>
    public Func<ConfirmationRequest, Task<bool>>? ConfirmDestructiveActionAsync { get; set; }

    public AnnotationsViewModel(IAnnotationService annotationService, ILocalizationService localization)
    {
        _annotationService = annotationService;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
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
            StatusMessage = _localization.GetString("Annot_LoadFailed");
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
                    DocumentName = a.Document?.FileName ?? _localization.GetString("Annot_UnknownDocument"),
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
            StatusMessage = _localization.GetString("Annot_Updated");
            await LoadColorStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update annotation");
            StatusMessage = _localization.GetString("Annot_UpdateFailed");
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    /// <summary>
    /// Deletes an annotation, its highlight and note, once the user confirms. The document it
    /// belongs to is not changed.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAnnotationAsync(long annotationId)
    {
        var documentName = Annotations.FirstOrDefault(a => a.Id == annotationId)?.DocumentName
            ?? _localization.GetString("Annot_UnknownDocument");
        var confirmed = await IsConfirmedAsync(new ConfirmationRequest(
            _localization.GetString("Annot_DeleteConfirmTitle"),
            _localization.GetString("Annot_DeleteConfirmMessage", documentName),
            _localization.GetString("Annot_DeleteConfirmButton"),
            _localization.GetString("Annot_ConfirmCancelButton")));
        if (!confirmed)
        {
            Log.Information("Delete of annotation {Id} was not confirmed", annotationId);
            return;
        }

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
            StatusMessage = _localization.GetString("Annot_Deleted");
            await LoadColorStatsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete annotation {Id}", annotationId);
            StatusMessage = _localization.GetString("Annot_DeleteFailed");
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
                StatusMessage = _localization.GetString("Annot_ExportUnavailable");
                return;
            }

            var result = await SaveMarkdownExportAsync(new AnnotationMarkdownExportRequest(
                CreateSuggestedExportFileName(),
                markdown));

            if (!result.IsSaved)
            {
                StatusMessage = _localization.GetString("Annot_ExportCancelled");
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(result.FilePath)
                ? _localization.GetString("Annot_MarkdownFile")
                : Path.GetFileName(result.FilePath);
            StatusMessage = TotalCount == 1
                ? _localization.GetString("Annot_ExportedOne", fileName)
                : _localization.GetString("Annot_ExportedMany", TotalCount, fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export annotations");
            StatusMessage = _localization.GetString("Annot_ExportFailed");
        }
    }

    private static string CreateSuggestedExportFileName()
    {
        return $"agent-x-annotations-{DateTime.Now:yyyyMMdd-HHmmss}.md";
    }

    /// <summary>
    /// Asks <see cref="ConfirmDestructiveActionAsync"/>. No handler, or a dialog that fails to
    /// open, counts as "not confirmed": nothing is deleted without an answer.
    /// </summary>
    private async Task<bool> IsConfirmedAsync(ConfirmationRequest request)
    {
        if (ConfirmDestructiveActionAsync is not { } confirm)
        {
            Log.Warning("No confirmation handler is attached; '{Title}' was not carried out", request.Title);
            return false;
        }

        try
        {
            return await confirm(request);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The confirmation '{Title}' could not be shown", request.Title);
            return false;
        }
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

using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Annotations;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// The annotation part of the Knowledge Vault preview. It shows the previewed document's
/// indexed text one passage (one chunk) at a time, turns text the operator selects in the
/// passage into an annotation through <see cref="IAnnotationService"/>, and lists the
/// document's annotations with delete.
/// <para>
/// Nothing in the app created annotations before, so the Annotations page was always empty
/// and highlights never reached Temporal Identity, which the service tells about every new
/// annotation.
/// </para>
/// </summary>
public partial class DocumentNotesViewModel : ObservableObject
{
    /// <summary>The ink a new annotation gets. The Annotations page can change it.</summary>
    public const string DefaultColor = "yellow";

    private readonly IAnnotationService? _annotations;
    private readonly ILocalizationService? _localization;

    // A document switch claims the next document generation and every passage load the next
    // passage generation. A load that is no longer the newest when its data arrives belongs to
    // a document or passage the operator has already left, and its result is dropped.
    private int _documentGeneration;
    private int _passageGeneration;
    private long? _documentId;
    private AnnotationPassage? _passage;
    private AnnotationRange? _draftRange;

    public DocumentNotesViewModel(IAnnotationService? annotations, ILocalizationService? localization = null)
    {
        _annotations = annotations;
        _localization = localization;
    }

    // Passage

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoTextHint))]
    private bool _isLoading;

    [ObservableProperty]
    private string _passageText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoTextHint))]
    [NotifyPropertyChangedFor(nameof(ShowSelectionHint))]
    private bool _hasPassage;

    /// <summary>One-based position of the passage shown; 0 when none is shown.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPassageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPassageCommand))]
    private int _passageNumber;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPassageCommand))]
    private int _passageCount;

    // Draft annotation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSelectionHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveAnnotationCommand))]
    private bool _hasDraft;

    /// <summary>The passage text the new annotation will highlight.</summary>
    [ObservableProperty]
    private string _draftHighlight = string.Empty;

    /// <summary>The optional note the operator is writing for the new annotation.</summary>
    [ObservableProperty]
    private string _draftNote = string.Empty;

    // Annotations and status

    [ObservableProperty]
    private bool _hasAnnotations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    [NotifyPropertyChangedFor(nameof(ShowNoTextHint))]
    private string _statusMessage = string.Empty;

    /// <summary>The previewed document's annotations, newest first.</summary>
    public ObservableCollection<AnnotationDisplayItem> Annotations { get; } = new();

    /// <summary>False when the app runs without the annotation service.</summary>
    public bool IsAvailable => _annotations is not null;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>
    /// True once loading has finished and the document has no indexed text. A failed load
    /// says so instead, since the text may well exist.
    /// </summary>
    public bool ShowNoTextHint => !IsLoading && !HasPassage && !HasStatusMessage;

    /// <summary>True while a passage is shown and nothing in it has been selected yet.</summary>
    public bool ShowSelectionHint => HasPassage && !HasDraft;

    /// <summary>
    /// Shows <paramref name="documentId"/>: its first passage and its annotations. Null clears
    /// the panel. Showing the document already shown keeps the passage and the draft.
    /// </summary>
    public async Task ShowDocumentAsync(long? documentId)
    {
        if (documentId == _documentId)
        {
            return;
        }

        var documentGeneration = ++_documentGeneration;
        var passageGeneration = ++_passageGeneration;
        _documentId = documentId;
        ApplyPassage(null);
        ClearDraft();
        StatusMessage = string.Empty;
        Annotations.Clear();
        HasAnnotations = false;

        if (documentId is not { } id || _annotations is null)
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            var passage = await _annotations.GetPassageAsync(id, 0);
            if (documentGeneration != _documentGeneration)
            {
                return;
            }

            if (passageGeneration == _passageGeneration)
            {
                ApplyPassage(passage);
            }

            var annotations = await _annotations.GetAnnotationsForDocumentAsync(id);
            if (documentGeneration != _documentGeneration)
            {
                return;
            }

            foreach (var annotation in annotations.OrderByDescending(a => a.CreatedAt))
            {
                Annotations.Add(ToDisplayItem(annotation));
            }

            HasAnnotations = Annotations.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load the text and annotations of document {DocumentId}", id);
            if (documentGeneration == _documentGeneration)
            {
                StatusMessage = Text(
                    _localization?.GetString("Vault_AnnotationsLoadFailed"),
                    "Vault_AnnotationsLoadFailed",
                    "The text and annotations of this document could not be loaded.");
            }
        }
        finally
        {
            if (documentGeneration == _documentGeneration)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// Takes the text the operator selected in the passage as the highlight of a new
    /// annotation. An empty selection is ignored, because the passage drops its selection as
    /// soon as focus moves to the note box or a button, and that must not discard the draft.
    /// A selection that is not part of the passage shown is ignored as well.
    /// </summary>
    /// <param name="selectedText">The text the passage reports as selected.</param>
    /// <param name="positionHint">Where the passage says the selection starts, or -1.</param>
    public void CaptureSelection(string? selectedText, int positionHint)
    {
        if (_passage is null)
        {
            return;
        }

        if (AnnotationSelection.Locate(_passage.Text, selectedText, positionHint) is not { } range)
        {
            return;
        }

        _draftRange = range;
        DraftHighlight = range.Text;
        HasDraft = true;
        StatusMessage = string.Empty;
    }

    private bool CanGoToPreviousPassage() => _passage is { Position: > 0 };

    private bool CanGoToNextPassage() => _passage is { } passage && passage.Position + 1 < passage.Count;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPassage))]
    private Task PreviousPassageAsync() =>
        _passage is { } passage ? ShowPassageAsync(passage.Position - 1) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanGoToNextPassage))]
    private Task NextPassageAsync() =>
        _passage is { } passage ? ShowPassageAsync(passage.Position + 1) : Task.CompletedTask;

    /// <summary>
    /// Moves to another passage of the document shown. A draft belongs to the passage it was
    /// selected in, so moving away discards it.
    /// </summary>
    private async Task ShowPassageAsync(int position)
    {
        if (_annotations is null || _documentId is not { } id || position < 0)
        {
            return;
        }

        var documentGeneration = _documentGeneration;
        var passageGeneration = ++_passageGeneration;
        ClearDraft();
        StatusMessage = string.Empty;

        try
        {
            var passage = await _annotations.GetPassageAsync(id, position);
            if (documentGeneration == _documentGeneration && passageGeneration == _passageGeneration)
            {
                ApplyPassage(passage);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load passage {Position} of document {DocumentId}", position, id);
            if (documentGeneration == _documentGeneration && passageGeneration == _passageGeneration)
            {
                StatusMessage = Text(
                    _localization?.GetString("Vault_AnnotationsLoadFailed"),
                    "Vault_AnnotationsLoadFailed",
                    "The text and annotations of this document could not be loaded.");
            }
        }
    }

    private bool CanSaveAnnotation() => HasDraft;

    /// <summary>
    /// Saves the draft as an annotation on the passage it was selected in, with the note when
    /// one was written, and lists it first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveAnnotation))]
    private async Task SaveAnnotationAsync()
    {
        if (_annotations is null
            || _documentId is not { } id
            || _passage is not { } passage
            || _draftRange is not { } range)
        {
            return;
        }

        var documentGeneration = _documentGeneration;
        var note = string.IsNullOrWhiteSpace(DraftNote) ? null : DraftNote.Trim();

        try
        {
            var created = await _annotations.CreateAnnotationAsync(
                id, passage.ChunkId, range.Start, range.End, range.Text, DefaultColor, note);

            // Saved either way; the list is only updated while it still shows this document.
            if (documentGeneration != _documentGeneration)
            {
                return;
            }

            Annotations.Insert(0, ToDisplayItem(created));
            HasAnnotations = true;
            StatusMessage = string.Empty;

            // A new selection made while saving is a new draft and stays.
            if (_draftRange == range && ReferenceEquals(_passage, passage))
            {
                ClearDraft();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save an annotation on document {DocumentId}", id);
            if (documentGeneration == _documentGeneration)
            {
                StatusMessage = Text(
                    _localization?.GetString("Vault_AnnotationSaveFailed"),
                    "Vault_AnnotationSaveFailed",
                    "The annotation could not be saved.");
            }
        }
    }

    [RelayCommand]
    private void CancelAnnotation()
    {
        ClearDraft();
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteAnnotationAsync(long annotationId)
    {
        if (_annotations is null)
        {
            return;
        }

        try
        {
            await _annotations.DeleteAnnotationAsync(annotationId);

            var item = Annotations.FirstOrDefault(a => a.Id == annotationId);
            if (item is not null)
            {
                Annotations.Remove(item);
            }

            HasAnnotations = Annotations.Count > 0;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete annotation {AnnotationId}", annotationId);
            StatusMessage = Text(
                _localization?.GetString("Vault_AnnotationDeleteFailed"),
                "Vault_AnnotationDeleteFailed",
                "The annotation could not be deleted.");
        }
    }

    private void ApplyPassage(AnnotationPassage? passage)
    {
        _passage = passage;
        PassageText = passage?.Text ?? string.Empty;
        PassageCount = passage?.Count ?? 0;
        PassageNumber = passage is null ? 0 : passage.Position + 1;
        HasPassage = passage is not null;
    }

    private void ClearDraft()
    {
        _draftRange = null;
        DraftHighlight = string.Empty;
        DraftNote = string.Empty;
        HasDraft = false;
    }

    private static AnnotationDisplayItem ToDisplayItem(AnnotationEntity annotation) => new()
    {
        Id = annotation.Id,
        DocumentId = annotation.DocumentId,
        DocumentName = annotation.Document?.FileName ?? string.Empty,
        HighlightedText = annotation.HighlightedText,
        NoteText = annotation.NoteText ?? string.Empty,
        Color = annotation.Color,
        CreatedAt = annotation.CreatedAt,
        UpdatedAt = annotation.UpdatedAt,
    };

    /// <summary>
    /// The localized text, or <paramref name="english"/> when there is no localization service
    /// or it has no resource (it then answers with the key itself).
    /// </summary>
    private static string Text(string? localized, string key, string english) =>
        !string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal)
            ? localized
            : english;
}

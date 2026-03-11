using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Documents;
using Serilog;

namespace AgentX.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
// COLLECTION MANAGER VIEW MODEL
//
// Comprehensive ViewModel for the Collection Manager experience.
// Handles collection CRUD, document assignment, and tree navigation.
//
// Accepts ICollectionService and IDocumentService via DI and calls real
// services with graceful error handling.
// ═══════════════════════════════════════════════════════════════════════════

public partial class CollectionManagerViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────
    private readonly ICollectionService _collectionService;
    private readonly IDocumentService _documentService;

    // ── Page State ─────────────────────────────────────────────
    [ObservableProperty] private string _pageTitle = "Collections";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // ── Selected Collection ──────────────────────────────────
    [ObservableProperty] private CollectionDisplayItem? _selectedCollection;

    // ── New Collection Input ─────────────────────────────────
    [ObservableProperty] private string _newCollectionName = string.Empty;
    [ObservableProperty] private string _newCollectionDescription = string.Empty;

    // ── Multi-Select State ───────────────────────────────────
    [ObservableProperty] private bool _isMultiSelectMode;
    [ObservableProperty] private int _selectedCount;

    public ObservableCollection<long> SelectedCollectionIds { get; } = new();

    // ── Stats ────────────────────────────────────────────────
    [ObservableProperty] private int _totalCollections;

    // ── Collections ──────────────────────────────────────────
    public ObservableCollection<CollectionDisplayItem> Collections { get; } = new();
    public ObservableCollection<DocumentDisplayItem> SelectedCollectionDocuments { get; } = new();

    // ── Computed Properties ──────────────────────────────────
    public bool HasCollections => Collections.Count > 0;
    public bool HasSelectedCollection => SelectedCollection is not null;
    public bool HasSelectedCollectionDocuments => SelectedCollectionDocuments.Count > 0;
    public bool CanCreateCollection => !string.IsNullOrWhiteSpace(NewCollectionName);

    public CollectionManagerViewModel(ICollectionService collectionService, IDocumentService documentService)
    {
        _collectionService = collectionService;
        _documentService = documentService;
        Log.Debug("CollectionManagerViewModel created with services");
    }

    // ═══════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        Log.Information("CollectionManagerViewModel initializing...");

        try
        {
            IsLoading = true;
            ClearError();

            await LoadCollectionsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize CollectionManagerViewModel");
            SetError("Failed to load collections. Please try refreshing.");
        }
        finally
        {
            IsLoading = false;
        }

        Log.Information("CollectionManagerViewModel initialized");
    }

    private async Task LoadCollectionsAsync()
    {
        Collections.Clear();
        SelectedCollectionDocuments.Clear();
        SelectedCollection = null;

        try
        {
            var rootCollections = await _collectionService.GetRootCollectionsAsync();
            foreach (var entity in rootCollections)
            {
                var display = MapCollectionToDisplay(entity);
                Collections.Add(display);
            }

            TotalCollections = await _collectionService.GetCollectionCountAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load collections from service");
        }

        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(HasSelectedCollection));
    }

    private static CollectionDisplayItem MapCollectionToDisplay(AgentX.Core.Data.Entities.CollectionEntity entity)
    {
        var item = new CollectionDisplayItem
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            IconGlyph = entity.IconGlyph ?? "\uF168",
            ColorHex = entity.ColorHex ?? "#C41E3A",
            ParentCollectionId = entity.ParentCollectionId,
            DocumentCount = entity.DocumentCollections?.Count ?? 0,
            CreatedAtFormatted = entity.CreatedAt.ToString("MMM d, yyyy"),
            UpdatedAtFormatted = FormatHelper.TimeAgoWithMonths(entity.UpdatedAt)
        };

        // Recursively map children
        if (entity.ChildCollections is not null)
        {
            foreach (var child in entity.ChildCollections)
            {
                item.Children.Add(MapCollectionToDisplay(child));
            }
        }

        return item;
    }

    // ═══════════════════════════════════════════════════════════════
    // PROPERTY CHANGE HOOKS
    // ═══════════════════════════════════════════════════════════════

    partial void OnSelectedCollectionChanged(CollectionDisplayItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(HasSelectedCollectionDocuments));
    }

    partial void OnNewCollectionNameChanged(string value)
    {
        CreateCollectionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCreateCollection));
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanCreateCollection))]
    private async Task CreateCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCollectionName)) return;

        var name = NewCollectionName.Trim();
        var description = NewCollectionDescription.Trim();

        Log.Information("Creating collection: {Name}", name);
        ClearError();

        try
        {
            var entity = await _collectionService.CreateCollectionAsync(
                name,
                string.IsNullOrEmpty(description) ? null : description);

            var newCollection = new CollectionDisplayItem
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                IconGlyph = "\uF168",
                ColorHex = "#C41E3A",
                DocumentCount = 0,
                CreatedAtFormatted = "Just now",
                UpdatedAtFormatted = "Just now"
            };

            Collections.Add(newCollection);
            TotalCollections = await _collectionService.GetCollectionCountAsync();
            OnPropertyChanged(nameof(HasCollections));

            // Clear the input fields
            NewCollectionName = string.Empty;
            NewCollectionDescription = string.Empty;

            Log.Information("Collection created: {Name} (ID: {Id})", name, entity.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create collection: {Name}", name);
            SetError($"Failed to create collection: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RenameCollectionAsync(long id)
    {
        Log.Debug("Rename collection requested: {CollectionId}", id);

        var item = FindCollectionById(id);
        if (item is not null)
        {
            Log.Information("Collection rename requested for: {Name}", item.Name);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync(long id)
    {
        Log.Information("Delete collection requested: {CollectionId}", id);
        ClearError();

        try
        {
            await _collectionService.DeleteCollectionAsync(id);

            var item = FindCollectionById(id);
            if (item is not null)
            {
                // Check if it's a root collection or child
                var parentCollection = FindParentCollection(id);
                if (parentCollection is not null)
                {
                    parentCollection.Children.Remove(item);
                }
                else
                {
                    Collections.Remove(item);
                }

                TotalCollections = await _collectionService.GetCollectionCountAsync();
                OnPropertyChanged(nameof(HasCollections));

                // If the deleted collection was selected, clear selection
                if (SelectedCollection?.Id == id)
                {
                    SelectedCollection = null;
                    SelectedCollectionDocuments.Clear();
                    OnPropertyChanged(nameof(HasSelectedCollectionDocuments));
                }

                Log.Information("Collection deleted: {CollectionId}", id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete collection: {CollectionId}", id);
            SetError($"Failed to delete collection: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SelectCollectionAsync(CollectionDisplayItem? collection)
    {
        if (collection is null) return;

        Log.Debug("Select collection: {CollectionId} ({Name})", collection.Id, collection.Name);

        SelectedCollection = collection;
        await LoadCollectionDocumentsAsync(collection.Id);
    }

    /// <summary>
    /// Adds documents to the currently selected collection.
    /// The actual file picker is handled by the code-behind.
    /// This command is invoked with the selected document IDs.
    /// </summary>
    [RelayCommand]
    private async Task AddDocumentsToCollectionAsync(IReadOnlyList<long>? documentIds)
    {
        if (SelectedCollection is null || documentIds is null || documentIds.Count == 0) return;

        Log.Information("Adding {Count} document(s) to collection: {CollectionId}",
            documentIds.Count, SelectedCollection.Id);
        ClearError();

        try
        {
            foreach (var docId in documentIds)
            {
                await _collectionService.AddDocumentToCollectionAsync(docId, SelectedCollection.Id);
            }

            await LoadCollectionDocumentsAsync(SelectedCollection.Id);
            SelectedCollection.DocumentCount = SelectedCollectionDocuments.Count;

            Log.Information("Documents added to collection: {CollectionId}", SelectedCollection.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add documents to collection");
            SetError($"Failed to add documents: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveDocumentFromCollectionAsync(long docId)
    {
        if (SelectedCollection is null) return;

        Log.Information("Remove document {DocumentId} from collection {CollectionId}",
            docId, SelectedCollection.Id);
        ClearError();

        try
        {
            await _collectionService.RemoveDocumentFromCollectionAsync(docId, SelectedCollection.Id);

            var doc = SelectedCollectionDocuments.FirstOrDefault(d => d.Id == docId);
            if (doc is not null)
            {
                SelectedCollectionDocuments.Remove(doc);
                SelectedCollection.DocumentCount = Math.Max(0, SelectedCollection.DocumentCount - 1);
                OnPropertyChanged(nameof(HasSelectedCollectionDocuments));
            }

            Log.Information("Document removed from collection");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove document from collection");
            SetError($"Failed to remove document: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Log.Debug("Collection manager refresh requested");
        await InitializeAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // MULTI-SELECT / BATCH COMMANDS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Toggles multi-select mode on or off. When toggled off, all selections are cleared.
    /// </summary>
    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        if (!IsMultiSelectMode)
        {
            SelectedCollectionIds.Clear();
            SelectedCount = 0;
        }
        Log.Debug("Collection multi-select mode toggled: {IsActive}", IsMultiSelectMode);
    }

    /// <summary>
    /// Toggles the selection state of a single collection by its database ID.
    /// If already selected, it is deselected; otherwise it is added to the selection.
    /// </summary>
    [RelayCommand]
    private void ToggleCollectionSelection(long collectionId)
    {
        if (SelectedCollectionIds.Contains(collectionId))
            SelectedCollectionIds.Remove(collectionId);
        else
            SelectedCollectionIds.Add(collectionId);

        SelectedCount = SelectedCollectionIds.Count;
    }

    /// <summary>
    /// Selects all currently displayed root collections.
    /// </summary>
    [RelayCommand]
    private void SelectAllCollections()
    {
        SelectedCollectionIds.Clear();
        AddAllCollectionIds(Collections);
        SelectedCount = SelectedCollectionIds.Count;
        Log.Debug("Selected all {Count} collections", SelectedCount);
    }

    /// <summary>
    /// Recursively collects IDs from all collections in the tree for Select All.
    /// </summary>
    private void AddAllCollectionIds(ObservableCollection<CollectionDisplayItem> items)
    {
        foreach (var item in items)
        {
            SelectedCollectionIds.Add(item.Id);
            if (item.Children.Count > 0)
            {
                AddAllCollectionIds(item.Children);
            }
        }
    }

    /// <summary>
    /// Deletes all currently selected collections in bulk.
    /// After completion, the selection is cleared, multi-select mode is exited,
    /// and the collection list is refreshed.
    /// </summary>
    [RelayCommand]
    private async Task BulkDeleteCollectionsAsync()
    {
        if (SelectedCollectionIds.Count == 0) return;

        var count = SelectedCollectionIds.Count;
        Log.Information("Bulk deleting {Count} collections", count);
        ClearError();
        IsLoading = true;

        try
        {
            foreach (var id in SelectedCollectionIds.ToList())
            {
                await _collectionService.DeleteCollectionAsync(id);
            }

            await LoadCollectionsAsync();
            TotalCollections = await _collectionService.GetCollectionCountAsync();
            OnPropertyChanged(nameof(HasCollections));

            Log.Information("Bulk deleted {Count} collections", count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk delete collections failed");
            SetError($"Failed to delete collections: {ex.Message}");
        }
        finally
        {
            SelectedCollectionIds.Clear();
            SelectedCount = 0;
            IsMultiSelectMode = false;
            IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    private async Task LoadCollectionDocumentsAsync(long collectionId)
    {
        SelectedCollectionDocuments.Clear();

        try
        {
            var documents = await _collectionService.GetDocumentsInCollectionAsync(collectionId);
            foreach (var doc in documents)
            {
                SelectedCollectionDocuments.Add(new DocumentDisplayItem
                {
                    Id = doc.Id,
                    FileName = doc.FileName,
                    FilePath = doc.FilePath,
                    FileType = doc.FileType,
                    FileSizeFormatted = FormatHelper.FormatBytes(doc.FileSizeBytes),
                    ImportedAtFormatted = FormatHelper.TimeAgoWithMonths(doc.ImportedAt),
                    ChunkCount = doc.ChunkCount,
                    WordCount = doc.WordCount,
                    PageCount = doc.PageCount,
                    IndexingStatus = doc.IndexingStatus,
                    FileTypeIcon = GetFileTypeIcon(doc.FileType),
                    StatusColor = GetStatusColor(doc.IndexingStatus),
                    Tags = new ObservableCollection<string>()
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load documents for collection {CollectionId}", collectionId);
        }

        OnPropertyChanged(nameof(HasSelectedCollectionDocuments));
    }

    private static string GetFileTypeIcon(string fileType) => fileType.ToLowerInvariant() switch
    {
        "pdf" => "\uEA90",
        "docx" or "doc" => "\uE8E5",
        "txt" => "\uE8D2",
        "md" => "\uE8A5",
        _ => "\uE8F1"
    };

    private static string GetStatusColor(string status) => status switch
    {
        "completed" => "#22C55E",
        "processing" => "#F59E0B",
        "pending" => "#3B82F6",
        "failed" => "#EF4444",
        _ => "#6B7280"
    };

    /// <summary>
    /// Recursively finds a collection by ID in the tree.
    /// </summary>
    private CollectionDisplayItem? FindCollectionById(long id)
    {
        foreach (var collection in Collections)
        {
            if (collection.Id == id) return collection;

            var child = FindCollectionByIdRecursive(collection.Children, id);
            if (child is not null) return child;
        }
        return null;
    }

    private static CollectionDisplayItem? FindCollectionByIdRecursive(
        ObservableCollection<CollectionDisplayItem> children, long id)
    {
        foreach (var child in children)
        {
            if (child.Id == id) return child;

            var nested = FindCollectionByIdRecursive(child.Children, id);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>
    /// Finds the parent collection that contains the child with the given ID.
    /// </summary>
    private CollectionDisplayItem? FindParentCollection(long childId)
    {
        foreach (var collection in Collections)
        {
            if (collection.Children.Any(c => c.Id == childId))
                return collection;

            var parent = FindParentCollectionRecursive(collection.Children, childId);
            if (parent is not null) return parent;
        }
        return null;
    }

    private static CollectionDisplayItem? FindParentCollectionRecursive(
        ObservableCollection<CollectionDisplayItem> children, long childId)
    {
        foreach (var child in children)
        {
            if (child.Children.Any(c => c.Id == childId))
                return child;

            var parent = FindParentCollectionRecursive(child.Children, childId);
            if (parent is not null) return parent;
        }
        return null;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // DISPOSAL
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        Log.Debug("CollectionManagerViewModel disposed");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// COLLECTION DISPLAY ITEM
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a collection displayed in the Collection Manager UI.
/// Supports hierarchical nesting via Children collection.
/// </summary>
public class CollectionDisplayItem : ObservableObject
{
    private string _name = string.Empty;
    private string? _description;
    private int _documentCount;
    private bool _isExpanded;

    public long Id { get; set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string? IconGlyph { get; set; }
    public string? ColorHex { get; set; }
    public long? ParentCollectionId { get; set; }

    public int DocumentCount
    {
        get => _documentCount;
        set => SetProperty(ref _documentCount, value);
    }

    public string CreatedAtFormatted { get; set; } = string.Empty;
    public string UpdatedAtFormatted { get; set; } = string.Empty;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<CollectionDisplayItem> Children { get; set; } = new();

    /// <summary>
    /// Whether this collection has child collections.
    /// </summary>
    public bool HasChildren => Children.Count > 0;
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Tagging;
using Serilog;

namespace AgentX.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
// KNOWLEDGE VAULT VIEW MODEL
//
// Comprehensive ViewModel for the document management experience.
// Handles document import, filtering, indexing status, and file management.
//
// Accepts IDocumentService and IIndexingService via DI and calls real
// services with graceful error handling.
// ═══════════════════════════════════════════════════════════════════════════

public partial class KnowledgeVaultViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────
    private readonly IDocumentService _documentService;
    private readonly IIndexingService _indexingService;
    private readonly IAiService _aiService;
    private readonly IAutoTagService _autoTagService;
    private readonly ICollectionService _collectionService;

    // ── Page State ─────────────────────────────────────────────
    [ObservableProperty] private string _pageTitle = "Knowledge Vault";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private int _importProgress;
    [ObservableProperty] private string _importStatus = string.Empty;
    [ObservableProperty] private long _totalDocuments;
    [ObservableProperty] private string _totalStorageFormatted = "0 B";
    [ObservableProperty] private int _indexingQueueLength;
    [ObservableProperty] private bool _isIndexing;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // ── Filters ──────────────────────────────────────────────
    [ObservableProperty] private string? _fileTypeFilter;
    [ObservableProperty] private string? _statusFilter;
    [ObservableProperty] private string? _tagFilter;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _showDropZone = true;

    // ── Advanced Filters (Feature 9) ─────────────────────────
    [ObservableProperty] private long? _collectionFilter;
    [ObservableProperty] private DateTime? _dateAfterFilter;
    [ObservableProperty] private DateTime? _dateBeforeFilter;
    [ObservableProperty] private string _sortBy = "date";

    // ── Multi-Select (Feature 8) ─────────────────────────────
    [ObservableProperty] private bool _isMultiSelectMode;
    [ObservableProperty] private int _selectedCount;

    // ── Duplicate Detection (Feature 14) ────────────────────────
    [ObservableProperty] private bool _showDuplicateWarning;
    [ObservableProperty] private string _duplicateWarningMessage = string.Empty;
    [ObservableProperty] private string? _duplicateFileName;
    private List<string>? _pendingImportPaths;
    private List<string>? _duplicateFilePaths;

    // ── Selected Document Preview ─────────────────────────────
    [ObservableProperty] private DocumentDisplayItem? _selectedDocument;
    [ObservableProperty] private bool _isPreviewOpen;

    // ── Collections ──────────────────────────────────────────
    public ObservableCollection<DocumentDisplayItem> Documents { get; } = new();
    public ObservableCollection<long> SelectedDocumentIds { get; } = new();

    // ── Tags (Feature 7) ────────────────────────────────────
    public ObservableCollection<TagDisplayItem> AllTags { get; } = new();

    // ── Available Collections for Filtering (Feature 9) ──────
    public ObservableCollection<CollectionFilterItem> AvailableCollections { get; } = new();

    // ── Computed Properties ──────────────────────────────────
    public bool HasDocuments => Documents.Count > 0;
    public bool HasSelection => SelectedCount > 0;
    public bool HasActiveFilters =>
        FileTypeFilter is not null
        || StatusFilter is not null
        || TagFilter is not null
        || CollectionFilter is not null
        || DateAfterFilter is not null
        || DateBeforeFilter is not null
        || SortBy != "date"
        || !string.IsNullOrEmpty(SearchQuery);
    public bool HasSelectedDocument => SelectedDocument is not null;

    public KnowledgeVaultViewModel(
        IDocumentService documentService,
        IIndexingService indexingService,
        IAiService aiService,
        IAutoTagService autoTagService,
        ICollectionService collectionService)
    {
        _documentService = documentService;
        _indexingService = indexingService;
        _aiService = aiService;
        _autoTagService = autoTagService;
        _collectionService = collectionService;
        Log.Debug("KnowledgeVaultViewModel created with services");
    }

    // ═══════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        Log.Information("KnowledgeVaultViewModel initializing...");

        try
        {
            IsLoading = true;
            ClearError();

            await LoadDocumentsAsync();
            await Task.WhenAll(
                LoadStatsAsync(),
                CheckIndexingStatusAsync(),
                LoadTagsAsync(),
                LoadCollectionsAsync());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize KnowledgeVaultViewModel");
            SetError("Failed to load documents. Please try refreshing.");
        }
        finally
        {
            IsLoading = false;
        }

        Log.Information("KnowledgeVaultViewModel initialized");
    }

    private async Task LoadDocumentsAsync()
    {
        Documents.Clear();

        try
        {
            var docs = await _documentService.GetAllDocumentsAsync(
                fileTypeFilter: FileTypeFilter,
                statusFilter: StatusFilter,
                tagFilter: TagFilter,
                collectionId: CollectionFilter,
                importedAfter: DateAfterFilter,
                importedBefore: DateBeforeFilter,
                sortBy: SortBy);

            var filteredDocs = new List<AgentX.Core.Data.Entities.DocumentEntity>();
            foreach (var doc in docs)
            {
                // If a search query is active, filter locally by file name
                if (!string.IsNullOrEmpty(SearchQuery) &&
                    !doc.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                filteredDocs.Add(doc);
            }

            IReadOnlyDictionary<long, IReadOnlyList<TagEntity>> tagMap = new Dictionary<long, IReadOnlyList<TagEntity>>();
            if (filteredDocs.Count > 0)
            {
                try
                {
                    tagMap = await _autoTagService.GetTagsForDocumentsAsync(
                        filteredDocs.Select(doc => doc.Id).ToArray());
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to batch-load tags for visible vault documents");
                }
            }

            foreach (var doc in filteredDocs)
            {
                var displayItem = MapDocumentToDisplay(doc);

                if (tagMap.TryGetValue(doc.Id, out var tags))
                {
                    foreach (var tag in tags)
                    {
                        displayItem.Tags.Add(tag.Name);
                    }
                }

                Documents.Add(displayItem);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load documents from service");
        }

        OnPropertyChanged(nameof(HasDocuments));
        UpdateDropZoneVisibility();
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            TotalDocuments = await _documentService.GetTotalDocumentCountAsync();
            var storageBytes = await _documentService.GetTotalStorageBytesAsync();
            TotalStorageFormatted = FormatHelper.FormatBytes(storageBytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load vault stats");
        }
    }

    private async Task CheckIndexingStatusAsync()
    {
        try
        {
            IndexingQueueLength = await _indexingService.GetQueueLengthAsync();
            IsIndexing = _indexingService.IsProcessing;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check indexing status");
            IndexingQueueLength = 0;
            IsIndexing = false;
        }
    }

    private async Task LoadTagsAsync()
    {
        try
        {
            AllTags.Clear();
            var tags = await _autoTagService.GetAllTagsAsync();

            // Build a tag-to-document-count map from the currently loaded documents.
            // This is efficient because we've already loaded all documents and their tags.
            var tagDocCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in Documents)
            {
                foreach (var tagName in doc.Tags)
                {
                    if (tagDocCounts.ContainsKey(tagName))
                        tagDocCounts[tagName]++;
                    else
                        tagDocCounts[tagName] = 1;
                }
            }

            foreach (var tag in tags)
            {
                tagDocCounts.TryGetValue(tag.Name, out var documentCount);

                AllTags.Add(new TagDisplayItem
                {
                    Id = tag.Id,
                    Name = tag.Name,
                    ColorHex = tag.ColorHex ?? "#6B7280",
                    IsAutoGenerated = tag.IsAutoGenerated,
                    DocumentCount = documentCount
                });
            }

            Log.Debug("Loaded {Count} tags", AllTags.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load tags");
        }
    }

    private async Task LoadCollectionsAsync()
    {
        try
        {
            AvailableCollections.Clear();
            var collections = await _collectionService.GetAllCollectionsAsync();

            foreach (var collection in collections)
            {
                AvailableCollections.Add(new CollectionFilterItem
                {
                    Id = collection.Id,
                    Name = collection.Name,
                    DocumentCount = collection.DocumentCount
                });
            }

            Log.Debug("Loaded {Count} collections for filtering", AvailableCollections.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load collections");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PROPERTY CHANGE HOOKS
    // ═══════════════════════════════════════════════════════════════

    partial void OnFileTypeFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnStatusFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnTagFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnCollectionFilterChanged(long? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnDateAfterFilterChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnDateBeforeFilterChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnSortByChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnSelectedDocumentChanged(DocumentDisplayItem? value)
    {
        IsPreviewOpen = value is not null;
        OnPropertyChanged(nameof(HasSelectedDocument));
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens a file picker and imports the selected files.
    /// The actual picker logic is handled in the code-behind because
    /// WinUI 3 file pickers require a window handle (HWND).
    /// This command is invoked after the code-behind obtains file paths.
    /// </summary>
    [RelayCommand]
    private async Task ImportFilesAsync(IReadOnlyList<string>? filePaths)
    {
        if (filePaths is null || filePaths.Count == 0) return;

        Log.Information("Importing {Count} file(s)", filePaths.Count);

        IsImporting = true;
        ImportProgress = 0;
        ImportStatus = $"Importing {filePaths.Count} file(s)...";
        ClearError();

        try
        {
            var progressReporter = new Progress<int>(completed =>
            {
                ImportProgress = (int)((double)completed / filePaths.Count * 100);
                ImportStatus = $"Importing file {completed}/{filePaths.Count}...";
            });

            await _documentService.ImportFilesAsync(filePaths, progress: progressReporter);

            ImportStatus = $"Successfully imported {filePaths.Count} file(s)";
            Log.Information("Import completed: {Count} files", filePaths.Count);

            // Refresh the document list
            await LoadDocumentsAsync();
            await LoadStatsAsync();
            await CheckIndexingStatusAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import files");
            ImportStatus = "Import failed";
            SetError($"Failed to import files: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// Opens a folder picker and imports all supported files from the folder.
    /// The actual picker logic is handled in the code-behind.
    /// This command is invoked after the code-behind obtains the folder path.
    /// </summary>
    [RelayCommand]
    private async Task ImportFolderAsync(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        Log.Information("Importing folder: {FolderPath}", folderPath);

        IsImporting = true;
        ImportProgress = 0;
        ImportStatus = "Scanning folder...";
        ClearError();

        try
        {
            // Enumerate supported files in the folder
            var supportedExtensions = _documentService.GetSupportedExtensions();
            var filePaths = new List<string>();

            foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (supportedExtensions.Contains(ext))
                {
                    filePaths.Add(file);
                }
            }

            if (filePaths.Count == 0)
            {
                ImportStatus = "No supported files found in folder";
                return;
            }

            ImportStatus = $"Found {filePaths.Count} supported file(s). Importing...";

            var progressReporter = new Progress<int>(completed =>
            {
                ImportProgress = (int)((double)completed / filePaths.Count * 100);
                ImportStatus = $"Importing file {completed}/{filePaths.Count}...";
            });

            await _documentService.ImportFilesAsync(filePaths, progress: progressReporter);

            ImportStatus = $"Successfully imported {filePaths.Count} file(s) from folder";
            Log.Information("Folder import completed: {FolderPath} ({Count} files)", folderPath, filePaths.Count);

            await LoadDocumentsAsync();
            await LoadStatsAsync();
            await CheckIndexingStatusAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import folder: {FolderPath}", folderPath);
            ImportStatus = "Folder import failed";
            SetError($"Failed to import folder: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Log.Debug("Refresh requested");
        await InitializeAsync();
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync(long id)
    {
        Log.Information("Delete document requested: {DocumentId}", id);
        ClearError();

        try
        {
            await _documentService.DeleteDocumentAsync(id);

            var item = Documents.FirstOrDefault(d => d.Id == id);
            if (item is not null)
            {
                Documents.Remove(item);
                OnPropertyChanged(nameof(HasDocuments));
                UpdateDropZoneVisibility();
            }

            TotalDocuments = await _documentService.GetTotalDocumentCountAsync();
            Log.Information("Document deleted: {DocumentId}", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete document: {DocumentId}", id);
            SetError($"Failed to delete document: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ReindexDocumentAsync(long id)
    {
        Log.Information("Re-index document requested: {DocumentId}", id);
        ClearError();

        try
        {
            var item = Documents.FirstOrDefault(d => d.Id == id);
            if (item is not null)
            {
                item.IndexingStatus = "processing";
                item.StatusColor = "#F59E0B";
                item.IndexingError = null;
            }

            await _indexingService.IndexDocumentAsync(id);
            await CheckIndexingStatusAsync();

            Log.Information("Document queued for re-indexing: {DocumentId}", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to re-index document: {DocumentId}", id);
            SetError($"Failed to re-index document: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SelectDocumentAsync(long id)
    {
        Log.Debug("Select document for preview: {DocumentId}", id);

        // Deselect previous
        if (SelectedDocument is not null)
        {
            SelectedDocument.IsSelected = false;
        }

        var item = Documents.FirstOrDefault(d => d.Id == id);
        if (item is not null)
        {
            item.IsSelected = true;

            // Enrich with latest data from the database
            try
            {
                var entity = await _documentService.GetDocumentAsync(id);
                if (entity is not null)
                {
                    item.Summary = entity.Summary;
                    item.ExtractedTitle = entity.ExtractedTitle;
                    item.ChunkCount = entity.ChunkCount;
                    item.WordCount = entity.WordCount;
                    item.PageCount = entity.PageCount;
                    item.IndexingStatus = entity.IndexingStatus;
                    item.StatusColor = GetStatusColor(entity.IndexingStatus);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enrich document detail for {DocumentId}", id);
            }
        }

        SelectedDocument = item;
    }

    [RelayCommand]
    private void ClosePreview()
    {
        if (SelectedDocument is not null)
        {
            SelectedDocument.IsSelected = false;
        }
        SelectedDocument = null;
    }

    [RelayCommand]
    private async Task GenerateTitleAsync(long id)
    {
        Log.Information("Generate title requested for document {DocumentId}", id);

        var item = Documents.FirstOrDefault(d => d.Id == id);
        if (item is null) return;

        try
        {
            var entity = await _documentService.GetDocumentAsync(id);
            if (entity is null) return;

            // Get the first chunk's text to generate a title from
            var firstChunk = entity.Chunks?
                .OrderBy(c => c.ChunkIndex)
                .FirstOrDefault();

            if (firstChunk is null || string.IsNullOrWhiteSpace(firstChunk.Content))
            {
                Log.Warning("No chunk content available for title generation on document {DocumentId}", id);
                return;
            }

            // Use AI to generate a concise title
            var contentPreview = firstChunk.Content.Length > 1500
                ? firstChunk.Content[..1500]
                : firstChunk.Content;

            var titleResponse = await _aiService.ChatAsync(
                new List<ChatMessage>
                {
                    new()
                    {
                        Role = "user",
                        Content = contentPreview,
                        Timestamp = DateTime.UtcNow
                    }
                },
                systemPrompt: "Generate a concise, descriptive title (5-10 words maximum) for the following document content. Return ONLY the title text, nothing else. No quotes, no explanation.",
                options: new ChatOptions { Temperature = 0.3, MaxTokens = 50 });

            var generatedTitle = titleResponse?.Trim().Trim('"', '\'', '*');

            if (!string.IsNullOrWhiteSpace(generatedTitle))
            {
                // Update the entity in the database
                entity.ExtractedTitle = generatedTitle;
                var dbContext = App.GetService<AgentX.Core.Data.AgentXDbContext>();
                dbContext.Documents.Update(entity);
                await dbContext.SaveChangesAsync();

                // Update the display item
                item.ExtractedTitle = generatedTitle;

                if (SelectedDocument?.Id == id)
                {
                    SelectedDocument.ExtractedTitle = generatedTitle;
                }

                Log.Information("Generated title for document {DocumentId}: {Title}", id, generatedTitle);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate title for document {DocumentId}", id);
        }
    }

    [RelayCommand]
    private void OpenInExplorer(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        Log.Debug("Open in explorer: {FilePath}", filePath);

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Log.Warning("Directory not found for file: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open explorer for: {FilePath}", filePath);
        }
    }

    [RelayCommand]
    private void FilterByType(string? type)
    {
        FileTypeFilter = type;
        Log.Debug("Filter by type: {Type}", type ?? "all");
    }

    [RelayCommand]
    private void FilterByStatus(string? status)
    {
        StatusFilter = status;
        Log.Debug("Filter by status: {Status}", status ?? "all");
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FileTypeFilter = null;
        StatusFilter = null;
        TagFilter = null;
        CollectionFilter = null;
        DateAfterFilter = null;
        DateBeforeFilter = null;
        SortBy = "date";
        SearchQuery = string.Empty;
        Log.Debug("Filters cleared");
    }

    [RelayCommand]
    private void FilterByTag(string? tagName)
    {
        TagFilter = tagName;
        Log.Debug("Filter by tag: {Tag}", tagName ?? "all");
    }

    // ═══════════════════════════════════════════════════════════════
    // MULTI-SELECT & BULK OPERATIONS (Feature 8)
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        if (!IsMultiSelectMode) ClearSelection();
        Log.Debug("Multi-select mode: {Mode}", IsMultiSelectMode);
    }

    [RelayCommand]
    private void ToggleDocumentSelection(long id)
    {
        if (SelectedDocumentIds.Contains(id))
        {
            SelectedDocumentIds.Remove(id);
            var doc = Documents.FirstOrDefault(d => d.Id == id);
            if (doc != null) doc.IsSelected = false;
        }
        else
        {
            SelectedDocumentIds.Add(id);
            var doc = Documents.FirstOrDefault(d => d.Id == id);
            if (doc != null) doc.IsSelected = true;
        }
        SelectedCount = SelectedDocumentIds.Count;
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void SelectAllDocuments()
    {
        SelectedDocumentIds.Clear();
        foreach (var doc in Documents)
        {
            SelectedDocumentIds.Add(doc.Id);
            doc.IsSelected = true;
        }
        SelectedCount = SelectedDocumentIds.Count;
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ClearSelection()
    {
        foreach (var doc in Documents) doc.IsSelected = false;
        SelectedDocumentIds.Clear();
        SelectedCount = 0;
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private async Task BulkDeleteAsync()
    {
        if (SelectedDocumentIds.Count == 0) return;
        var ids = SelectedDocumentIds.ToList();
        Log.Information("Bulk delete: {Count} documents", ids.Count);
        ClearError();

        try
        {
            await _documentService.BulkDeleteAsync(ids);
            await LoadDocumentsAsync();
            await LoadStatsAsync();
            ClearSelection();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk delete failed");
            SetError("Bulk delete failed");
        }
    }

    [RelayCommand]
    private async Task BulkReindexAsync()
    {
        if (SelectedDocumentIds.Count == 0) return;
        var ids = SelectedDocumentIds.ToList();
        Log.Information("Bulk reindex: {Count} documents", ids.Count);
        ClearError();

        try
        {
            await _documentService.BulkReindexAsync(ids);
            await CheckIndexingStatusAsync();
            ClearSelection();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk reindex failed");
            SetError("Bulk reindex failed");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DRAG AND DROP SUPPORT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by the code-behind when files are dropped onto the drop zone.
    /// Routes through dedup check before importing.
    /// </summary>
    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return;

        Log.Information("Files dropped: {Count}", filePaths.Count);
        await ImportWithDedupAsync(filePaths);
    }

    // ═══════════════════════════════════════════════════════════════
    // DUPLICATE DETECTION (Feature 14)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks each file for duplicates before importing. If any duplicates are
    /// found, shows a warning banner allowing the user to skip, import anyway,
    /// or dismiss. Non-duplicate files are identified for clean import.
    /// </summary>
    [RelayCommand]
    private async Task ImportWithDedupAsync(IReadOnlyList<string>? filePaths)
    {
        if (filePaths is null || filePaths.Count == 0) return;

        Log.Information("Starting duplicate check for {Count} file(s)", filePaths.Count);

        var cleanPaths = new List<string>();
        var duplicatePaths = new List<string>();
        var duplicateNames = new List<string>();

        foreach (var path in filePaths)
        {
            try
            {
                var result = await _documentService.CheckForDuplicateAsync(path);
                if (result.IsDuplicate)
                {
                    duplicatePaths.Add(path);
                    duplicateNames.Add(
                        $"'{Path.GetFileName(path)}' matches '{result.ExistingFileName}'");
                    Log.Debug("Duplicate detected: {FilePath} -> {ExistingFile}",
                        path, result.ExistingFileName);
                }
                else
                {
                    cleanPaths.Add(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Duplicate check failed for {FilePath}, treating as non-duplicate", path);
                cleanPaths.Add(path);
            }
        }

        if (duplicatePaths.Count > 0)
        {
            _pendingImportPaths = cleanPaths;
            _duplicateFilePaths = duplicatePaths;

            DuplicateWarningMessage = duplicatePaths.Count == 1
                ? $"1 file is a duplicate and will be skipped"
                : $"{duplicatePaths.Count} files are duplicates and will be skipped";

            DuplicateFileName = duplicateNames.Count > 0 ? duplicateNames[0] : null;
            ShowDuplicateWarning = true;

            Log.Information("Duplicate check complete: {Duplicates} duplicates, {Clean} clean",
                duplicatePaths.Count, cleanPaths.Count);
        }
        else
        {
            // No duplicates found — import all files directly
            await ImportFilesCommand.ExecuteAsync(filePaths);
        }
    }

    /// <summary>
    /// Skips the duplicate files and imports only the non-duplicate files.
    /// </summary>
    [RelayCommand]
    private async Task SkipDuplicatesAsync()
    {
        ShowDuplicateWarning = false;

        if (_pendingImportPaths is not null && _pendingImportPaths.Count > 0)
        {
            Log.Information("Importing {Count} non-duplicate file(s), skipping duplicates",
                _pendingImportPaths.Count);
            await ImportFilesCommand.ExecuteAsync(_pendingImportPaths);
        }
        else
        {
            Log.Information("No non-duplicate files to import after skipping duplicates");
        }

        _pendingImportPaths = null;
        _duplicateFilePaths = null;
    }

    /// <summary>
    /// Imports all files regardless of duplicate status.
    /// </summary>
    [RelayCommand]
    private async Task ImportAllAnywayAsync()
    {
        ShowDuplicateWarning = false;

        var allPaths = new List<string>();
        if (_pendingImportPaths is not null) allPaths.AddRange(_pendingImportPaths);
        if (_duplicateFilePaths is not null) allPaths.AddRange(_duplicateFilePaths);

        if (allPaths.Count > 0)
        {
            Log.Information("Importing all {Count} file(s) including duplicates", allPaths.Count);
            await ImportFilesCommand.ExecuteAsync(allPaths);
        }

        _pendingImportPaths = null;
        _duplicateFilePaths = null;
    }

    /// <summary>
    /// Dismisses the duplicate warning without importing any files.
    /// </summary>
    [RelayCommand]
    private void DismissDuplicateWarning()
    {
        ShowDuplicateWarning = false;
        _pendingImportPaths = null;
        _duplicateFilePaths = null;
        Log.Debug("Duplicate warning dismissed");
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void ApplyFilters()
    {
        // Re-load documents with the current filter settings
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadDocumentsAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply filters");
            }
        });
    }

    private void UpdateDropZoneVisibility()
    {
        ShowDropZone = Documents.Count == 0;
    }

    private static DocumentDisplayItem MapDocumentToDisplay(AgentX.Core.Data.Entities.DocumentEntity doc)
    {
        return new DocumentDisplayItem
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
            IndexingError = doc.IndexingError,
            Summary = doc.Summary,
            ExtractedTitle = doc.ExtractedTitle,
            FileTypeIcon = GetFileTypeIcon(doc.FileType),
            StatusColor = GetStatusColor(doc.IndexingStatus),
            Tags = new System.Collections.ObjectModel.ObservableCollection<string>()
        };
    }

    private static string GetFileTypeIcon(string fileType) => fileType.ToLowerInvariant() switch
    {
        "pdf" => "\uEA90",
        "docx" or "doc" => "\uE8E5",
        "txt" => "\uE8D2",
        "md" => "\uE8A5",
        "csv" => "\uE9D9",
        "json" or "xml" => "\uE943",
        "html" or "htm" => "\uEB41",
        "py" or "cs" or "js" or "ts" or "java" or "cpp" or "c" or "h" => "\uE943",
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
        Log.Debug("KnowledgeVaultViewModel disposed");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DOCUMENT DISPLAY ITEM
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a document displayed in the Knowledge Vault UI.
/// Contains all formatted display properties for data binding.
/// </summary>
public class DocumentDisplayItem : ObservableObject
{
    private string _indexingStatus = "pending";
    private string _statusColor = "#3B82F6";
    private string? _indexingError;
    private bool _isSelected;

    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string FileSizeFormatted { get; set; } = string.Empty;
    public string ImportedAtFormatted { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public long WordCount { get; set; }
    public int PageCount { get; set; }

    public string IndexingStatus
    {
        get => _indexingStatus;
        set => SetProperty(ref _indexingStatus, value);
    }

    public string? IndexingError
    {
        get => _indexingError;
        set => SetProperty(ref _indexingError, value);
    }

    private string? _summary;
    private string? _extractedTitle;

    public string? Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public string? ExtractedTitle
    {
        get => _extractedTitle;
        set => SetProperty(ref _extractedTitle, value);
    }

    /// <summary>
    /// Segoe Fluent Icons glyph for the file type.
    /// </summary>
    public string FileTypeIcon { get; set; } = "\uE8F1";

    /// <summary>
    /// Hex color string for the indexing status indicator.
    /// Green = completed, Amber = processing, Blue = pending, Red = failed.
    /// </summary>
    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    public ObservableCollection<string> Tags { get; set; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Formatted word count for display (e.g., "12.8K words").
    /// </summary>
    public string WordCountFormatted => WordCount switch
    {
        0 => "--",
        < 1_000 => $"{WordCount}",
        _ => $"{WordCount / 1_000.0:F1}K"
    };

    /// <summary>
    /// Display label for the indexing status badge.
    /// </summary>
    public string IndexingStatusLabel => IndexingStatus switch
    {
        "completed" => "Indexed",
        "processing" => "Processing",
        "pending" => "Pending",
        "failed" => "Failed",
        _ => IndexingStatus
    };

    /// <summary>
    /// File type display label (uppercased).
    /// </summary>
    public string FileTypeLabel => FileType.ToUpperInvariant();
}

// ═══════════════════════════════════════════════════════════════════════════
// TAG DISPLAY ITEM (Feature 7)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a tag displayed in the Knowledge Vault filter UI.
/// Contains display-ready properties for tag filter chips.
/// </summary>
public class TagDisplayItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#6B7280";
    public bool IsAutoGenerated { get; set; }
    public int DocumentCount { get; set; }
    public string DocumentCountFormatted => DocumentCount > 0 ? $"({DocumentCount})" : string.Empty;
}

// ═══════════════════════════════════════════════════════════════════════════
// COLLECTION FILTER ITEM (Feature 9)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a collection option in the advanced filter dropdown.
/// </summary>
public class CollectionFilterItem
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public override string ToString() => $"{Name} ({DocumentCount})";
}

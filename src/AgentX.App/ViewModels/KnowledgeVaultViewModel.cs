using System.Collections.ObjectModel;
using System.Diagnostics;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Tagging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IWorkflowLaunchService? _workflowLaunchService;
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private bool _suppressFilterRefresh;

    // Monotonic load token. Every document reload claims the next value; only the most
    // recent load is allowed to mutate the UI-bound Documents collection, so overlapping
    // reloads (e.g. a filter change landing while initialization is still in flight)
    // can neither tear the collection nor overwrite newer, correct state.
    private int _documentLoadGeneration;

    // ── Page State ─────────────────────────────────────────────
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
    [ObservableProperty] private string _focusedDocumentVisibilityHint = string.Empty;

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
    public NavigateHandler? NavigateRequested { get; set; }

    public KnowledgeVaultViewModel(
        IDocumentService documentService,
        IIndexingService indexingService,
        IAiService aiService,
        IAutoTagService autoTagService,
        ICollectionService collectionService,
        IWorkflowLaunchService? workflowLaunchService = null,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _documentService = documentService;
        _indexingService = indexingService;
        _aiService = aiService;
        _autoTagService = autoTagService;
        _collectionService = collectionService;
        _workflowLaunchService = workflowLaunchService;
        _operationsDrillInService = operationsDrillInService;
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
            await ApplyPendingOperationsDocumentRequestAsync();
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
        // Claim this load's place in line. The collection is only mutated below if this is
        // still the newest load by the time the (awaited) data has been fetched.
        var generation = Interlocked.Increment(ref _documentLoadGeneration);

        List<DocumentDisplayItem>? loaded = null;
        try
        {
            // The pickers yield local calendar days while ImportedAt is stored in UTC; the
            // "before" day is inclusive, so the bound is the end of that day.
            var docs = await _documentService.GetAllDocumentsAsync(
                fileTypeFilter: FileTypeFilter,
                statusFilter: StatusFilter,
                tagFilter: TagFilter,
                collectionId: CollectionFilter,
                importedAfter: DateAfterFilter.HasValue ? LocalDayRange.StartUtc(DateAfterFilter.Value) : null,
                importedBefore: DateBeforeFilter.HasValue ? LocalDayRange.EndUtc(DateBeforeFilter.Value) : null,
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

            loaded = new List<DocumentDisplayItem>(filteredDocs.Count);
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

                loaded.Add(displayItem);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load documents from service");
        }

        // A newer reload started while we were fetching — discard these results instead of
        // overwriting the newer (correct) collection state. This is what prevents the
        // filter-triggered reload from clobbering an in-flight initialization/drill-in.
        if (generation != Volatile.Read(ref _documentLoadGeneration))
        {
            return;
        }

        // Atomic swap on the calling (UI) thread: clear once, then repopulate. No await
        // sits between Clear and Add, so the collection is never observed half-built.
        Documents.Clear();
        if (loaded is not null)
        {
            foreach (var item in loaded)
            {
                Documents.Add(item);
            }
        }

        SyncSelectionWithDocuments();
        OnPropertyChanged(nameof(HasDocuments));
        UpdateDropZoneVisibility();
    }

    /// <summary>
    /// Keeps the multi-select state in step with the rows on screen. A reload creates new
    /// row items, so their checkboxes are re-applied from <see cref="SelectedDocumentIds"/>,
    /// and selections whose rows are no longer shown are dropped: a bulk action must only
    /// touch rows the user can see checked.
    /// </summary>
    private void SyncSelectionWithDocuments()
    {
        var visibleIds = new HashSet<long>(Documents.Select(document => document.Id));
        for (var i = SelectedDocumentIds.Count - 1; i >= 0; i--)
        {
            if (!visibleIds.Contains(SelectedDocumentIds[i]))
            {
                SelectedDocumentIds.RemoveAt(i);
            }
        }

        foreach (var document in Documents)
        {
            document.IsSelected = SelectedDocumentIds.Contains(document.Id);
        }

        SelectedCount = SelectedDocumentIds.Count;
        OnPropertyChanged(nameof(HasSelection));
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
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnStatusFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnTagFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnCollectionFilterChanged(long? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnDateAfterFilterChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnDateBeforeFilterChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnSortByChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilterRefresh)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnSelectedDocumentChanged(DocumentDisplayItem? value)
    {
        IsPreviewOpen = value is not null;
        OnPropertyChanged(nameof(HasSelectedDocument));

        if (value is null)
        {
            ClearFocusedDocumentLanding();
            return;
        }

        if (!value.HasFocusedSourceLabel)
        {
            ClearFocusedDocumentLanding();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Imports a batch of files and reports what actually happened. The file picker lives in
    /// the code-behind because WinUI 3 pickers need a window handle; picked and dropped files
    /// reach this through the duplicate check in <see cref="ImportWithDedupAsync"/>.
    /// </summary>
    /// <param name="filePaths">Files to import.</param>
    /// <param name="allowDuplicates">
    /// True when the user chose "Import all anyway", so files matching an existing document
    /// are imported as separate documents instead of being skipped.
    /// </param>
    /// <param name="fromFolder">Whether the files came from a folder scan (wording only).</param>
    private async Task ImportBatchAsync(IReadOnlyList<string> filePaths, bool allowDuplicates, bool fromFolder = false)
    {
        if (filePaths.Count == 0) return;

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

            var report = await _documentService.ImportFilesWithReportAsync(
                filePaths, allowDuplicates: allowDuplicates, progress: progressReporter);

            var summary = FormatImportSummary(report, filePaths.Count, fromFolder);
            ImportStatus = summary;
            Log.Information(
                "Import completed: {Imported}/{Total} imported, {ExtractionFailed} unreadable, {Duplicates} duplicates skipped, {Failed} failed",
                report.Imported.Count, filePaths.Count, report.ExtractionFailedCount, report.Duplicates.Count, report.Failed.Count);

            // The progress panel disappears when the import ends, so anything short of a
            // clean import is also raised on the page's message banner.
            if (report.Failed.Count > 0 || report.Duplicates.Count > 0 || report.ExtractionFailedCount > 0)
            {
                var firstFailure = report.Failed.FirstOrDefault();
                SetError(firstFailure is null
                    ? summary
                    : $"{summary}. {Path.GetFileName(firstFailure.FilePath)}: {firstFailure.Reason}");
            }

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
    /// One-line outcome of an import: a plain success message when every file was imported
    /// and readable, otherwise the real counts of what was imported, unreadable, skipped as
    /// a duplicate, or not imported.
    /// </summary>
    internal static string FormatImportSummary(DocumentImportReport report, int totalFiles, bool fromFolder = false)
    {
        var origin = fromFolder ? " from folder" : string.Empty;
        var imported = report.Imported.Count;
        var unreadable = report.ExtractionFailedCount;
        var duplicates = report.Duplicates.Count;
        var failed = report.Failed.Count;

        if (unreadable == 0 && duplicates == 0 && failed == 0)
        {
            return $"Successfully imported {imported} file(s){origin}";
        }

        var parts = new List<string> { $"Imported {imported} of {totalFiles} file(s){origin}" };
        if (unreadable > 0)
        {
            parts.Add(unreadable == 1
                ? "1 of them could not be read and is marked Failed"
                : $"{unreadable} of them could not be read and are marked Failed");
        }

        if (duplicates > 0)
        {
            parts.Add(duplicates == 1
                ? "1 skipped as a duplicate"
                : $"{duplicates} skipped as duplicates");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} could not be imported");
        }

        return string.Join("; ", parts);
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

        List<string> filePaths;
        try
        {
            filePaths = await Task.Run(() => EnumerateSupportedFiles(folderPath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan folder: {FolderPath}", folderPath);
            ImportStatus = "Folder import failed";
            SetError($"Failed to import folder: {ex.Message}");
            IsImporting = false;
            return;
        }

        if (filePaths.Count == 0)
        {
            ImportStatus = "No supported files found in folder";
            SetError("No supported files found in the selected folder.");
            IsImporting = false;
            return;
        }

        await ImportBatchAsync(filePaths, allowDuplicates: false, fromFolder: true);
    }

    /// <summary>
    /// Supported files under <paramref name="folderPath"/>, including subfolders. Subfolders
    /// the user cannot read are skipped instead of aborting the whole scan.
    /// </summary>
    private List<string> EnumerateSupportedFiles(string folderPath)
    {
        var supportedExtensions = _documentService.GetSupportedExtensions();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        return Directory.EnumerateFiles(folderPath, "*", options)
            .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .ToList();
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

            if (SelectedDocumentIds.Remove(id))
            {
                SelectedCount = SelectedDocumentIds.Count;
                OnPropertyChanged(nameof(HasSelection));
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
                item.StatusColor = "#FFB000";
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

        // The previewed document is tracked by SelectedDocument alone. IsSelected is the
        // multi-select checkbox, backed by SelectedDocumentIds, and must not change here:
        // toggling it made rows look checked (or unchecked) out of step with what a bulk
        // delete or re-index would actually act on.
        var item = Documents.FirstOrDefault(d => d.Id == id);
        if (item is not null)
        {
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
        // Closing the preview leaves the multi-select checkboxes as they are.
        SelectedDocument = null;
    }

    [RelayCommand]
    private void DismissFocusedDocumentLanding()
    {
        ClearFocusedDocumentLanding();
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
    private async Task LaunchDocumentInWorkflowAsync(long id)
    {
        if (_workflowLaunchService is null)
        {
            SetError("Workflow launch service unavailable.");
            return;
        }

        try
        {
            var document = await _documentService.GetDocumentAsync(id);
            if (document is null)
            {
                SetError("Unable to prepare the selected document for workflows.");
                return;
            }

            var previewText = await _documentService.GetDocumentPreviewTextAsync(id);
            if (string.IsNullOrWhiteSpace(previewText) && string.IsNullOrWhiteSpace(document.Summary))
            {
                SetError("This document does not have enough indexed text to launch into a workflow yet.");
                return;
            }

            _workflowLaunchService.StageRequest(BuildWorkflowLaunchRequest(document, previewText));
            NavigateRequested?.Invoke("Workflows");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch document {DocumentId} into workflow", id);
            SetError("Failed to prepare the document for workflows.");
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
    /// Called by the code-behind when items are dropped onto the drop zone. Dropped folders
    /// are expanded to the supported files they contain, and everything dropped (files and
    /// folders together) is imported as one batch through the duplicate check.
    /// </summary>
    public async Task HandleDroppedItemsAsync(IReadOnlyList<string> filePaths, IReadOnlyList<string> folderPaths)
    {
        if (filePaths.Count == 0 && folderPaths.Count == 0) return;

        Log.Information("Items dropped: {FileCount} file(s), {FolderCount} folder(s)", filePaths.Count, folderPaths.Count);
        ClearError();

        var toImport = new List<string>(filePaths);
        var unreadableFolders = new List<string>();
        foreach (var folderPath in folderPaths)
        {
            try
            {
                toImport.AddRange(await Task.Run(() => EnumerateSupportedFiles(folderPath)));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to scan dropped folder: {FolderPath}", folderPath);
                unreadableFolders.Add(folderPath);
            }
        }

        var distinct = toImport.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 0)
        {
            await ImportWithDedupAsync(distinct);
        }

        // Import errors take precedence on the banner; otherwise say what was left out.
        if (!HasError && unreadableFolders.Count > 0)
        {
            SetError($"Could not read the dropped folder {unreadableFolders[0]}; its files were not imported.");
        }
        else if (distinct.Count == 0 && unreadableFolders.Count == 0)
        {
            SetError("No supported files found in the dropped folder(s).");
        }
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
            // No duplicates found, so import all files directly
            await ImportBatchAsync(filePaths, allowDuplicates: false);
        }
    }

    /// <summary>
    /// Skips the duplicate files and imports only the non-duplicate files.
    /// </summary>
    [RelayCommand]
    private async Task SkipDuplicatesAsync()
    {
        ShowDuplicateWarning = false;

        // Claim the pending batch before awaiting so a second click cannot import it twice.
        var pending = _pendingImportPaths;
        _pendingImportPaths = null;
        _duplicateFilePaths = null;

        if (pending is not null && pending.Count > 0)
        {
            Log.Information("Importing {Count} non-duplicate file(s), skipping duplicates",
                pending.Count);
            await ImportBatchAsync(pending, allowDuplicates: false);
        }
        else
        {
            Log.Information("No non-duplicate files to import after skipping duplicates");
        }
    }

    /// <summary>
    /// Imports all files regardless of duplicate status. The duplicates become separate
    /// documents, because the user explicitly asked for them.
    /// </summary>
    [RelayCommand]
    private async Task ImportAllAnywayAsync()
    {
        ShowDuplicateWarning = false;

        var allPaths = new List<string>();
        if (_pendingImportPaths is not null) allPaths.AddRange(_pendingImportPaths);
        if (_duplicateFilePaths is not null) allPaths.AddRange(_duplicateFilePaths);
        _pendingImportPaths = null;
        _duplicateFilePaths = null;

        if (allPaths.Count > 0)
        {
            Log.Information("Importing all {Count} file(s) including duplicates", allPaths.Count);
            await ImportBatchAsync(allPaths, allowDuplicates: true);
        }
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
        // Reload on the current (UI) thread context. The Documents collection is bound to the
        // view, so it must be mutated on the UI thread — NOT on a thread-pool thread via
        // Task.Run, which races initialization and throws RPC_E_WRONG_THREAD against a live
        // ItemsRepeater. The generation guard in LoadDocumentsAsync coalesces overlapping
        // reloads so the newest filter state always wins.
        _ = ReloadForFilterChangeAsync();
    }

    private async Task ReloadForFilterChangeAsync()
    {
        try
        {
            await LoadDocumentsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply filters");
        }
    }

    private void UpdateDropZoneVisibility()
    {
        ShowDropZone = Documents.Count == 0;
    }

    private async Task ApplyPendingOperationsDocumentRequestAsync()
    {
        var request = _operationsDrillInService?.ConsumePendingDocumentRequest();
        if (request is null || request.DocumentId <= 0)
        {
            return;
        }

        await FocusDocumentAsync(request.DocumentId, request.SourceLabel);
    }

    /// <summary>
    /// Opens the vault on a specific document handed over by another surface (Jump-To,
    /// the command palette, an Operations drill-in). Widens active filters when they
    /// would hide the document, floats it to the top of the list, and selects it.
    /// </summary>
    public async Task ApplyNavigationParameterAsync(object? parameter)
    {
        if (parameter is not long documentId || documentId <= 0)
        {
            return;
        }

        await FocusDocumentAsync(documentId, sourceLabel: null);
    }

    /// <summary>
    /// Brings <paramref name="documentId"/> into view and selects it. Shared by every
    /// caller that navigates here with a specific document in mind.
    /// </summary>
    private async Task FocusDocumentAsync(long documentId, string? sourceLabel)
    {
        ClearFocusedDocumentLanding();

        // The payload can arrive before the page's first load completes, so make sure
        // there is something to search before concluding the document is missing.
        if (Documents.Count == 0)
        {
            await LoadDocumentsAsync();
        }

        var target = Documents.FirstOrDefault(document => document.Id == documentId);
        if (target is null && HasActiveFilters)
        {
            var widenedFilters = ResetFiltersForOperationsDocumentRequest();
            if (widenedFilters)
            {
                await LoadDocumentsAsync();
                target = Documents.FirstOrDefault(document => document.Id == documentId);
                if (target is not null)
                {
                    FocusedDocumentVisibilityHint = "Filters were widened to show the requested document.";
                }
            }
        }

        if (target is null)
        {
            Log.Debug("Requested document {DocumentId} is not present in the vault", documentId);
            return;
        }

        if (sourceLabel is not null)
        {
            target.FocusedSourceLabel = sourceLabel;
        }

        if (Documents.Remove(target))
        {
            Documents.Insert(0, target);
        }

        await SelectDocumentAsync(target.Id);
    }

    private void ClearFocusedDocumentLanding()
    {
        FocusedDocumentVisibilityHint = string.Empty;

        foreach (var document in Documents)
        {
            document.FocusedSourceLabel = string.Empty;
        }
    }

    private bool ResetFiltersForOperationsDocumentRequest()
    {
        var changed = HasActiveFilters;
        if (!changed)
        {
            return false;
        }

        _suppressFilterRefresh = true;
        try
        {
            FileTypeFilter = null;
            StatusFilter = null;
            TagFilter = null;
            CollectionFilter = null;
            DateAfterFilter = null;
            DateBeforeFilter = null;
            SortBy = "date";
            SearchQuery = string.Empty;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        OnPropertyChanged(nameof(HasActiveFilters));
        return true;
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
        "completed" => "#41E25E",
        "processing" => "#FFB000",
        "pending" => "#58C4BC",
        "failed" => "#C8453E",
        _ => "#6B7280"
    };

    private static WorkflowLaunchRequest BuildWorkflowLaunchRequest(
        AgentX.Core.Data.Entities.DocumentEntity document,
        string? previewText)
    {
        var lines = new List<string>
        {
            "Source: Knowledge Vault document",
            $"Document: {document.FileName}"
        };

        if (!string.IsNullOrWhiteSpace(document.ExtractedTitle))
        {
            lines.Add($"Title: {document.ExtractedTitle.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(document.Summary))
        {
            lines.Add(string.Empty);
            lines.Add("Summary");
            lines.Add("-------");
            lines.Add(document.Summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(previewText)
            && !string.Equals(
                document.Summary?.Trim(),
                previewText.Trim(),
                StringComparison.Ordinal))
        {
            lines.Add(string.Empty);
            lines.Add("Document Preview");
            lines.Add("----------------");
            lines.Add(previewText.Trim());
        }

        return new WorkflowLaunchRequest
        {
            InputText = string.Join(Environment.NewLine, lines),
            SourceLabel = $"Loaded document context from \"{document.FileName}\"",
            RecommendedWorkflowName = "Summarize & Act"
        };
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
    private string _statusColor = "#58C4BC";
    private string? _indexingError;
    private bool _isSelected;
    private string _focusedSourceLabel = string.Empty;

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
        set
        {
            if (SetProperty(ref _indexingStatus, value))
            {
                // The status badge binds the derived label, not the raw status.
                OnPropertyChanged(nameof(IndexingStatusLabel));
            }
        }
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

    public string FocusedSourceLabel
    {
        get => _focusedSourceLabel;
        set
        {
            if (SetProperty(ref _focusedSourceLabel, value))
            {
                OnPropertyChanged(nameof(HasFocusedSourceLabel));
            }
        }
    }

    public bool HasFocusedSourceLabel => !string.IsNullOrWhiteSpace(FocusedSourceLabel);

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

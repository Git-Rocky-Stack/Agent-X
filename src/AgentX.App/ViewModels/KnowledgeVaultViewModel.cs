using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Documents;
using AgentX.Core.Services.Indexing;
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
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _showDropZone = true;

    // ── Collections ──────────────────────────────────────────
    public ObservableCollection<DocumentDisplayItem> Documents { get; } = new();

    // ── Computed Properties ──────────────────────────────────
    public bool HasDocuments => Documents.Count > 0;
    public bool HasActiveFilters => FileTypeFilter is not null || StatusFilter is not null || !string.IsNullOrEmpty(SearchQuery);

    public KnowledgeVaultViewModel(IDocumentService documentService, IIndexingService indexingService)
    {
        _documentService = documentService;
        _indexingService = indexingService;
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
            await LoadStatsAsync();
            await CheckIndexingStatusAsync();
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
            var docs = await _documentService.GetAllDocumentsAsync(FileTypeFilter, StatusFilter);
            foreach (var doc in docs)
            {
                // If a search query is active, filter locally by file name
                if (!string.IsNullOrEmpty(SearchQuery) &&
                    !doc.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Documents.Add(MapDocumentToDisplay(doc));
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
            TotalStorageFormatted = FormatBytes(storageBytes);
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
    private void ViewDocumentDetail(long id)
    {
        Log.Debug("View document detail: {DocumentId}", id);
        // Navigation will be handled by the page code-behind
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
        SearchQuery = string.Empty;
        Log.Debug("Filters cleared");
    }

    // ═══════════════════════════════════════════════════════════════
    // DRAG AND DROP SUPPORT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by the code-behind when files are dropped onto the drop zone.
    /// </summary>
    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return;

        Log.Information("Files dropped: {Count}", filePaths.Count);
        await ImportFilesAsync(filePaths);
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
            FileSizeFormatted = FormatBytes(doc.FileSizeBytes),
            ImportedAtFormatted = FormatTimeAgo(doc.ImportedAt),
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

    private static string FormatTimeAgo(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime.ToUniversalTime();
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 43200 => $"{(int)span.TotalDays}d ago",
            _ => $"{(int)(span.TotalDays / 30)}mo ago"
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

    /// <summary>
    /// Formats bytes to a human-readable string.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1_024 => $"{bytes} B",
            < 1_048_576 => $"{bytes / 1_024.0:F1} KB",
            < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
            _ => $"{bytes / 1_073_741_824.0:F2} GB"
        };
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

    public string? Summary { get; set; }
    public string? ExtractedTitle { get; set; }

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

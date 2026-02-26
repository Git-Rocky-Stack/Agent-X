using System.Collections.Concurrent;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Indexing;

/// <summary>
/// Monitors registered watch folders for new or modified files using
/// <see cref="FileSystemWatcher"/>. Detected files are debounced (500ms)
/// to coalesce rapid multiple-event notifications from the OS, then
/// imported via <see cref="IDocumentService"/>.
/// </summary>
public sealed class FileWatcherService : IFileWatcherService
{
    private readonly AgentXDbContext _db;
    private readonly IDocumentService _documentService;
    private readonly ILogger _logger;

    /// <summary>
    /// Maps watch folder entity IDs to their active <see cref="FileSystemWatcher"/> instances.
    /// </summary>
    private readonly ConcurrentDictionary<long, FileSystemWatcher> _watchers = new();

    /// <summary>
    /// Maps watch folder entity IDs to their associated metadata (for filtering and collection assignment).
    /// </summary>
    private readonly ConcurrentDictionary<long, WatchFolderContext> _watcherContexts = new();

    /// <summary>
    /// Per-file debounce timers to avoid processing the same file multiple times
    /// from rapid Created/Changed event pairs that Windows commonly emits.
    /// </summary>
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new();

    /// <summary>
    /// Debounce delay in milliseconds. FileSystemWatcher often fires multiple events
    /// for a single file operation; this delay coalesces them.
    /// </summary>
    private const int DebounceDelayMs = 500;

    private volatile bool _isWatching;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsWatching => _isWatching;

    /// <inheritdoc />
    public event EventHandler<string>? FileDetected;

    public FileWatcherService(
        AgentXDbContext db,
        IDocumentService documentService,
        ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task StartWatchingAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileWatcherService));
        }

        _logger.Information("Starting file watcher service");

        // Load all enabled watch folders from the database
        var watchFolders = await _db.WatchFolders
            .Where(wf => wf.IsEnabled)
            .ToListAsync(ct);

        var started = 0;
        foreach (var folder in watchFolders)
        {
            try
            {
                StartWatcherForFolder(folder);
                started++;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start watcher for folder: {FolderPath}", folder.FolderPath);
            }
        }

        _isWatching = started > 0;
        _logger.Information("File watcher service started: {Count}/{Total} folders being monitored", started, watchFolders.Count);
    }

    /// <inheritdoc />
    public Task StopWatchingAsync()
    {
        _logger.Information("Stopping file watcher service");

        StopAllWatchers();
        _isWatching = false;

        _logger.Information("File watcher service stopped");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AddWatchFolderAsync(
        string path,
        bool includeSubfolders = true,
        string? fileTypeFilter = null,
        long? collectionId = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Watch folder path cannot be empty.", nameof(path));
        }

        var normalizedPath = Path.GetFullPath(path);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Watch folder does not exist: {normalizedPath}");
        }

        // Check for duplicate paths
        var existingFolder = await _db.WatchFolders
            .FirstOrDefaultAsync(wf => wf.FolderPath == normalizedPath);

        if (existingFolder is not null)
        {
            throw new InvalidOperationException($"Watch folder already registered: {normalizedPath} (ID {existingFolder.Id})");
        }

        // Validate collection exists if specified
        if (collectionId.HasValue)
        {
            var collectionExists = await _db.Collections.AnyAsync(c => c.Id == collectionId.Value);
            if (!collectionExists)
            {
                throw new InvalidOperationException($"Collection with ID {collectionId.Value} not found.");
            }
        }

        // Create the entity
        var entity = new WatchFolderEntity
        {
            FolderPath = normalizedPath,
            IsEnabled = true,
            IncludeSubfolders = includeSubfolders,
            FileTypeFilter = NormalizeFileTypeFilter(fileTypeFilter),
            TargetCollectionId = collectionId,
            CreatedAt = DateTime.UtcNow,
            FilesIndexed = 0
        };

        _db.WatchFolders.Add(entity);
        await _db.SaveChangesAsync();

        _logger.Information(
            "Added watch folder: {Path} (ID {Id}, subfolders: {Subfolders}, filter: {Filter}, collection: {CollectionId})",
            normalizedPath, entity.Id, includeSubfolders, entity.FileTypeFilter ?? "all", collectionId?.ToString() ?? "none");

        // Start watching immediately
        try
        {
            StartWatcherForFolder(entity);
            _isWatching = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start watcher for newly added folder: {Path}", normalizedPath);
        }
    }

    /// <inheritdoc />
    public async Task RemoveWatchFolderAsync(long watchFolderId)
    {
        // Stop the watcher if active
        StopWatcherForFolder(watchFolderId);

        // Remove from database
        var entity = await _db.WatchFolders.FindAsync(watchFolderId);
        if (entity is null)
        {
            _logger.Warning("Attempted to remove non-existent watch folder: {Id}", watchFolderId);
            return;
        }

        _db.WatchFolders.Remove(entity);
        await _db.SaveChangesAsync();

        _logger.Information("Removed watch folder: {Path} (ID {Id})", entity.FolderPath, watchFolderId);

        // Update watching state
        _isWatching = !_watchers.IsEmpty;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync()
    {
        return await _db.WatchFolders
            .OrderBy(wf => wf.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.Debug("Disposing FileWatcherService");

        StopAllWatchers();
        ClearDebounceTimers();
    }

    // ─── Private Watcher Management ─────────────────────────────────

    /// <summary>
    /// Creates and starts a <see cref="FileSystemWatcher"/> for the given watch folder entity.
    /// </summary>
    private void StartWatcherForFolder(WatchFolderEntity entity)
    {
        if (!Directory.Exists(entity.FolderPath))
        {
            _logger.Warning("Watch folder path does not exist, skipping: {Path}", entity.FolderPath);
            return;
        }

        // Stop any existing watcher for this folder
        StopWatcherForFolder(entity.Id);

        var watcher = new FileSystemWatcher(entity.FolderPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            Filter = "*.*",
            IncludeSubdirectories = entity.IncludeSubfolders,
            EnableRaisingEvents = true,
            // Use a larger buffer to reduce the chance of missing events under heavy I/O
            InternalBufferSize = 65536
        };

        // Build the context for filtering decisions in the event handlers
        var context = new WatchFolderContext
        {
            WatchFolderId = entity.Id,
            TargetCollectionId = entity.TargetCollectionId,
            AllowedExtensions = ParseFileTypeFilter(entity.FileTypeFilter)
        };

        // Wire up event handlers
        watcher.Created += (_, e) => OnFileEvent(e.FullPath, context);
        watcher.Changed += (_, e) => OnFileEvent(e.FullPath, context);
        watcher.Renamed += (_, e) => OnFileEvent(e.FullPath, context);
        watcher.Error += (_, e) => OnWatcherError(entity.Id, entity.FolderPath, e.GetException());

        _watchers[entity.Id] = watcher;
        _watcherContexts[entity.Id] = context;

        _logger.Debug(
            "Started FileSystemWatcher for folder {Path} (ID {Id}, subfolders: {Subfolders})",
            entity.FolderPath, entity.Id, entity.IncludeSubfolders);
    }

    /// <summary>
    /// Stops and disposes the watcher for a specific folder.
    /// </summary>
    private void StopWatcherForFolder(long watchFolderId)
    {
        if (_watchers.TryRemove(watchFolderId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _logger.Debug("Stopped FileSystemWatcher for folder ID {Id}", watchFolderId);
        }

        _watcherContexts.TryRemove(watchFolderId, out _);
    }

    /// <summary>
    /// Stops and disposes all active watchers.
    /// </summary>
    private void StopAllWatchers()
    {
        foreach (var kvp in _watchers)
        {
            kvp.Value.EnableRaisingEvents = false;
            kvp.Value.Dispose();
        }

        _watchers.Clear();
        _watcherContexts.Clear();
    }

    /// <summary>
    /// Disposes all pending debounce timers.
    /// </summary>
    private void ClearDebounceTimers()
    {
        foreach (var kvp in _debounceTimers)
        {
            kvp.Value.Dispose();
        }

        _debounceTimers.Clear();
    }

    // ─── Event Handlers ─────────────────────────────────────────────

    /// <summary>
    /// Handles Created/Changed/Renamed events with debouncing.
    /// Multiple events for the same file within <see cref="DebounceDelayMs"/> are coalesced.
    /// </summary>
    private void OnFileEvent(string filePath, WatchFolderContext context)
    {
        if (_disposed) return;

        // Quick filter: only process files (not directories)
        if (Directory.Exists(filePath)) return;

        // Check extension against the watcher's filter
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension)) return;

        // If the watch folder has an extension filter, check against it
        if (context.AllowedExtensions is not null && context.AllowedExtensions.Count > 0)
        {
            if (!context.AllowedExtensions.Contains(extension))
            {
                return;
            }
        }

        // Check if any registered processor can handle this file type
        if (!_documentService.CanProcess(filePath))
        {
            return;
        }

        // Debounce: reset the timer for this file path
        var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

        var timer = _debounceTimers.AddOrUpdate(
            normalizedPath,
            // Factory: create a new timer
            _ => new Timer(
                state => OnDebounceElapsed((string)state!, context),
                filePath,
                DebounceDelayMs,
                Timeout.Infinite),
            // Update: reset the existing timer
            (_, existingTimer) =>
            {
                existingTimer.Change(DebounceDelayMs, Timeout.Infinite);
                return existingTimer;
            });
    }

    /// <summary>
    /// Called after the debounce period elapses for a file. Performs the actual import.
    /// </summary>
    private async void OnDebounceElapsed(string filePath, WatchFolderContext context)
    {
        var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

        // Remove the debounce timer
        if (_debounceTimers.TryRemove(normalizedPath, out var timer))
        {
            timer.Dispose();
        }

        if (_disposed) return;

        // Verify the file still exists (it may have been a transient event)
        if (!File.Exists(filePath)) return;

        _logger.Information("File detected in watch folder: {FilePath}", filePath);
        FileDetected?.Invoke(this, filePath);

        try
        {
            // Import the file through DocumentService
            var document = await _documentService.ImportFileAsync(filePath, context.TargetCollectionId);

            // Update the watch folder's stats
            await UpdateWatchFolderStatsAsync(context.WatchFolderId);

            _logger.Information(
                "Auto-imported file from watch folder: {FileName} (document ID {DocumentId})",
                document.FileName, document.Id);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("identical content"))
        {
            // Duplicate file - this is expected and not an error
            _logger.Debug("Skipped duplicate file in watch folder: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-import file from watch folder: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Handles FileSystemWatcher errors (e.g., buffer overflow, access denied).
    /// </summary>
    private void OnWatcherError(long watchFolderId, string folderPath, Exception exception)
    {
        _logger.Error(exception, "FileSystemWatcher error for folder {Path} (ID {Id})", folderPath, watchFolderId);

        // Attempt to restart the watcher after a brief delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);

            if (_disposed) return;

            try
            {
                var entity = await _db.WatchFolders.FindAsync(watchFolderId);
                if (entity is not null && entity.IsEnabled)
                {
                    _logger.Information("Attempting to restart watcher for folder {Path}", folderPath);
                    StartWatcherForFolder(entity);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to restart watcher for folder {Path}", folderPath);
            }
        });
    }

    // ─── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Increments the FilesIndexed counter for a watch folder.
    /// </summary>
    private async Task UpdateWatchFolderStatsAsync(long watchFolderId)
    {
        try
        {
            var entity = await _db.WatchFolders.FindAsync(watchFolderId);
            if (entity is not null)
            {
                entity.FilesIndexed++;
                entity.LastScanAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to update stats for watch folder {Id}", watchFolderId);
        }
    }

    /// <summary>
    /// Normalizes a file type filter string (e.g., "pdf, docx, txt" -> "pdf,docx,txt").
    /// </summary>
    private static string? NormalizeFileTypeFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var extensions = filter
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ext => ext.TrimStart('.').ToLowerInvariant())
            .Where(ext => !string.IsNullOrEmpty(ext))
            .Distinct();

        var normalized = string.Join(",", extensions);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    /// <summary>
    /// Parses a comma-separated file type filter into a set of extensions (with leading dots).
    /// Returns null if the filter is empty (meaning "accept all supported types").
    /// </summary>
    private static HashSet<string>? ParseFileTypeFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var extensions = filter
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ext =>
            {
                var trimmed = ext.TrimStart('.').ToLowerInvariant();
                return "." + trimmed;
            })
            .Where(ext => ext.Length > 1);

        var set = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        return set.Count > 0 ? set : null;
    }

    // ─── Internal Types ─────────────────────────────────────────────

    /// <summary>
    /// Holds runtime context for a watch folder, used by event handlers
    /// to make filtering and routing decisions without database access.
    /// </summary>
    private sealed class WatchFolderContext
    {
        public long WatchFolderId { get; init; }
        public long? TargetCollectionId { get; init; }
        public HashSet<string>? AllowedExtensions { get; init; }
    }
}

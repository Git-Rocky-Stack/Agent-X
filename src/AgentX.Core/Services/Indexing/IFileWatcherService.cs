using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Indexing;

/// <summary>
/// Monitors registered watch folders for new or modified files and automatically
/// imports them into the knowledge vault via <see cref="Documents.IDocumentService"/>.
/// Uses <see cref="FileSystemWatcher"/> with per-file debouncing to avoid duplicate events.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Loads all enabled watch folders from the database and starts
    /// a <see cref="FileSystemWatcher"/> for each one.
    /// </summary>
    Task StartWatchingAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops all active file system watchers and clears internal state.
    /// The watch folder configuration is preserved in the database.
    /// </summary>
    Task StopWatchingAsync();

    /// <summary>
    /// Registers a new watch folder, persists it to the database, and starts watching immediately.
    /// </summary>
    /// <param name="path">Absolute path to the folder to watch.</param>
    /// <param name="includeSubfolders">Whether to recursively monitor subdirectories.</param>
    /// <param name="fileTypeFilter">Comma-separated list of extensions to watch (e.g., "pdf,docx,txt"). Null means all supported types.</param>
    /// <param name="collectionId">Optional collection to associate imported documents with.</param>
    Task AddWatchFolderAsync(string path, bool includeSubfolders = true, string? fileTypeFilter = null, long? collectionId = null);

    /// <summary>
    /// Stops watching a folder, removes its watcher, and deletes the database record.
    /// </summary>
    /// <param name="watchFolderId">The ID of the watch folder to remove.</param>
    Task RemoveWatchFolderAsync(long watchFolderId);

    /// <summary>
    /// Returns all registered watch folders from the database.
    /// </summary>
    Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync();

    /// <summary>
    /// Indicates whether any watch folders are currently being monitored.
    /// </summary>
    bool IsWatching { get; }

    /// <summary>
    /// Raised when a new or modified file is detected in a watched folder.
    /// The event argument is the full file path.
    /// </summary>
    event EventHandler<string>? FileDetected;
}

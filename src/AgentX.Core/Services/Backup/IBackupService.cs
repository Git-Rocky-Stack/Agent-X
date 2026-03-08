using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Backup.Models;

namespace AgentX.Core.Services.Backup;

/// <summary>
/// Provides backup and restore capabilities for the Agent-X local data store.
///
/// A backup is a self-contained ZIP archive (extension .agentxbak) that contains:
///   - A hot-safe copy of the SQLite database obtained via the SQLite Online Backup API.
///   - A manifest.json describing the contents and metadata of the archive.
///   - Optionally, user document files stored in the configured storage path.
///
/// All creation and extraction paths report progress through
/// <see cref="IProgress{BackupProgress}"/> so the UI can display live feedback.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new backup archive according to the supplied <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Destination path, encryption password, and other creation settings.</param>
    /// <param name="progress">
    /// Optional receiver for granular phase/percentage updates during the operation.
    /// </param>
    /// <param name="ct">Token used to cancel a long-running backup.</param>
    /// <returns>A <see cref="BackupResult"/> describing success, file path, size, and timing.</returns>
    Task<BackupResult> CreateBackupAsync(
        BackupOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Restores Agent-X data from an existing backup archive, replacing the current database.
    /// </summary>
    /// <param name="backupFilePath">Absolute path to the .agentxbak archive to restore from.</param>
    /// <param name="progress">
    /// Optional receiver for granular phase/percentage updates during the operation.
    /// </param>
    /// <param name="ct">Token used to cancel a long-running restore.</param>
    /// <returns>A <see cref="RestoreResult"/> with document/conversation/workflow counts and any warnings.</returns>
    Task<RestoreResult> RestoreFromBackupAsync(
        string backupFilePath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all backup history records stored in the database, ordered from newest to oldest.
    /// </summary>
    Task<IReadOnlyList<BackupEntity>> GetBackupHistoryAsync();

    /// <summary>
    /// Deletes the backup history record with the given <paramref name="backupId"/> and,
    /// if the associated archive file still exists on disk, removes it as well.
    /// </summary>
    /// <param name="backupId">Primary key of the <see cref="BackupEntity"/> to delete.</param>
    Task DeleteBackupAsync(long backupId);

    /// <summary>
    /// Estimates the total size of a backup without creating one, based on current file sizes.
    /// </summary>
    Task<BackupSizeEstimate> EstimateBackupSizeAsync();

    /// <summary>
    /// Opens the archive at <paramref name="backupFilePath"/> and checks that it is a valid,
    /// readable .agentxbak archive containing the expected entries.
    /// </summary>
    /// <param name="backupFilePath">Absolute path to the archive to validate.</param>
    /// <returns>True when the archive is valid; false when it is missing, corrupt, or incomplete.</returns>
    Task<bool> ValidateBackupAsync(string backupFilePath);

    /// <summary>
    /// Starts the background scheduled-backup loop using the configuration persisted in settings.
    /// This method is non-blocking; the loop runs on a background task until
    /// <see cref="StopScheduledBackups"/> is called or <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="ct">Token that stops the scheduled loop when cancelled.</param>
    Task StartScheduledBackupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Signals the currently running scheduled-backup loop to stop after the current cycle completes.
    /// Safe to call when no scheduled loop is active.
    /// </summary>
    void StopScheduledBackups();
}

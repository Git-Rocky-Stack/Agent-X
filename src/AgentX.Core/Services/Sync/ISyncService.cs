using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Sync.Models;

namespace AgentX.Core.Services.Sync;

/// <summary>
/// Provides opt-in, encrypted Collaborative Sync between two Agent-X installations
/// via a user-supplied shared storage location (OneDrive, Google Drive, NAS, USB, etc.).
///
/// Sync is directional within a single pass: one installation exports a
/// <see cref="SyncChangeSet"/> to the sync folder; the other detects the file,
/// imports it, resolves any conflicts, and applies the changes to its local database.
///
/// All files written to the sync folder are AES-256 encrypted with a key derived
/// from the user-supplied passphrase, so the shared folder never holds plaintext data.
/// </summary>
public interface ISyncService
{
    // -- Observable state ------------------------------------------------------

    /// <summary>
    /// Current state snapshot of the sync engine.
    /// This property always reflects the most recent status and is safe to read
    /// from any thread.
    /// </summary>
    SyncStatus Status { get; }

    /// <summary>
    /// Raised on the thread-pool immediately after <see cref="Status"/> changes.
    /// Subscribers that update UI must marshal to the dispatcher.
    /// </summary>
    event Action<SyncStatus>? StatusChanged;

    // -- Configuration ---------------------------------------------------------

    /// <summary>
    /// Persists <paramref name="config"/> to the local database so that it survives
    /// application restarts.  Replaces any previously stored configuration.
    /// </summary>
    /// <param name="config">The configuration to persist.</param>
    Task ConfigureAsync(SyncConfiguration config);

    /// <summary>
    /// Reads the stored sync configuration from the local database.
    /// </summary>
    /// <returns>
    /// The previously saved <see cref="SyncConfiguration"/>, or
    /// <see langword="null"/> if the feature has not yet been configured.
    /// </returns>
    Task<SyncConfiguration?> GetConfigurationAsync();

    // -- Core sync operations --------------------------------------------------

    /// <summary>
    /// Collects all local entity changes made after <paramref name="since"/> and
    /// packages them into a <see cref="SyncChangeSet"/>.  The change set is then
    /// AES-256 encrypted and written to the configured sync folder as an
    /// <c>agentx-sync-{deviceId}-{timestamp}.axs</c> file.  Every change carries a
    /// natural key so the receiving installation can match it to its own rows.
    /// </summary>
    /// <param name="since">
    /// Lower-bound timestamp for change collection.  Pass <see langword="null"/>
    /// to export every entity (full sync).  The scheduled and manual passes pass the
    /// persisted export watermark (see <see cref="SyncNowAsync"/>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exported <see cref="SyncChangeSet"/>.</returns>
    Task<SyncChangeSet> ExportChangesAsync(
        DateTime? since = null,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a <see cref="SyncChangeSet"/> received from a peer installation.
    /// Each change is matched to a local row by its natural key (never by the remote
    /// numeric id), settled by last writer wins, and saved on its own, so one bad
    /// change is rolled back without affecting the others or later database writes.
    /// </summary>
    /// <param name="changeSet">The remote change set to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entity changes whose effect is now reflected locally.</returns>
    Task<int> ImportChangesAsync(
        SyncChangeSet changeSet,
        CancellationToken ct = default);

    /// <summary>
    /// Runs one complete sync pass right now: exports local changes made since the
    /// persisted export watermark, then imports every peer file in the sync folder.
    /// Waits for a pass already in progress (for example the auto-sync loop) instead of
    /// running concurrently with it.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was exported, imported, retried and rejected.</returns>
    Task<SyncRunResult> SyncNowAsync(CancellationToken ct = default);

    /// <summary>
    /// Imports every peer file currently in the sync folder without exporting.
    /// A file is renamed to <c>.imported</c> only when all of its changes were applied
    /// (or deliberately discarded); files with failed changes stay in place and are
    /// retried on the next pass.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was imported, retried and rejected.</returns>
    Task<SyncRunResult> ImportNowAsync(CancellationToken ct = default);

    // -- Conflict handling -----------------------------------------------------

    /// <summary>
    /// Compares the changes in <paramref name="incoming"/> against the local
    /// database and returns the ones an import would discard because the local copy
    /// of the same entity (matched by natural key) was modified more recently.
    /// </summary>
    /// <param name="incoming">A remote change set to compare against local state.</param>
    /// <returns>
    /// A list of <see cref="SyncConflict"/> instances, each resolved as
    /// <see cref="SyncResolution.KeepLocal"/> by last writer wins.  Empty when there
    /// are no conflicts.
    /// </returns>
    Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(SyncChangeSet incoming);

    /// <summary>
    /// Applies the chosen <paramref name="resolution"/> strategy to the given
    /// <paramref name="conflict"/>, updating the local database accordingly.
    /// <see cref="SyncResolution.KeepRemote"/> overwrites the local copy (matched by
    /// natural key) even though it is newer.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <param name="resolution">
    /// How the conflict should be settled.  Must not be
    /// <see cref="SyncResolution.Pending"/>.
    /// </param>
    Task ResolveConflictAsync(SyncConflict conflict, SyncResolution resolution);

    // -- History ---------------------------------------------------------------

    /// <summary>
    /// Returns the most recent sync log entries, ordered from newest to oldest.
    /// </summary>
    /// <param name="limit">Maximum number of records to return.  Defaults to <c>20</c>.</param>
    Task<IReadOnlyList<SyncLogEntity>> GetSyncHistoryAsync(int limit = 20);

    // -- Auto-sync loop --------------------------------------------------------

    /// <summary>
    /// Starts a background polling loop that performs a full export/import cycle
    /// at the interval configured in <see cref="SyncConfiguration.SyncIntervalMinutes"/>.
    /// Safe to call when a loop is already running; the existing loop is replaced.
    /// Does nothing when sync is not configured or auto-sync is disabled in the
    /// stored configuration. The loop runs until <paramref name="ct"/> is cancelled or
    /// <see cref="StopAutoSyncAsync"/> is called.
    /// </summary>
    /// <param name="ct">Token that stops the loop when cancelled.</param>
    Task StartAutoSyncAsync(CancellationToken ct = default);

    /// <summary>
    /// Startup entry point: restores the persisted sync status (last sync time) and,
    /// when the stored configuration has auto-sync enabled, starts the loop with a
    /// short first delay so peer files that arrived while the app was closed are picked
    /// up soon after launch. A no-op when sync is unconfigured or auto-sync is off.
    /// </summary>
    /// <param name="ct">Token that stops the loop when cancelled (typically app shutdown).</param>
    Task ResumeAutoSyncAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the running auto-sync loop and waits for an in-flight cycle to finish
    /// unwinding. Safe to call when no loop is active.
    /// </summary>
    Task StopAutoSyncAsync();

    /// <summary>True while the background auto-sync loop is running.</summary>
    bool IsAutoSyncRunning { get; }
}

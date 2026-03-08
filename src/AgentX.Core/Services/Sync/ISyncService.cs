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
    // ── Observable state ──────────────────────────────────────────────────────

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

    // ── Configuration ─────────────────────────────────────────────────────────

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

    // ── Core sync operations ──────────────────────────────────────────────────

    /// <summary>
    /// Collects all local entity changes made after <paramref name="since"/> and
    /// packages them into a <see cref="SyncChangeSet"/>.  The change set is then
    /// AES-256 encrypted and written to the configured sync folder as an
    /// <c>agentx-sync-{deviceId}-{timestamp}.axs</c> file.
    /// </summary>
    /// <param name="since">
    /// Lower-bound timestamp for change collection.  Pass <see langword="null"/>
    /// to export every entity (full sync).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exported <see cref="SyncChangeSet"/>.</returns>
    Task<SyncChangeSet> ExportChangesAsync(
        DateTime? since = null,
        CancellationToken ct = default);

    /// <summary>
    /// Decrypts and deserialises a <see cref="SyncChangeSet"/> received from a
    /// peer installation, resolves conflicts, and applies non-conflicting changes
    /// to the local database.
    /// </summary>
    /// <param name="changeSet">The remote change set to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entity changes successfully applied.</returns>
    Task<int> ImportChangesAsync(
        SyncChangeSet changeSet,
        CancellationToken ct = default);

    // ── Conflict handling ─────────────────────────────────────────────────────

    /// <summary>
    /// Compares the changes in <paramref name="incoming"/> against the local
    /// database to identify entities that were independently modified on both sides
    /// since the last sync.
    /// </summary>
    /// <param name="incoming">A remote change set to compare against local state.</param>
    /// <returns>
    /// A list of <see cref="SyncConflict"/> instances for every entity where both
    /// a local and a remote change exist.  Empty when there are no conflicts.
    /// </returns>
    Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(SyncChangeSet incoming);

    /// <summary>
    /// Applies the chosen <paramref name="resolution"/> strategy to the given
    /// <paramref name="conflict"/>, updating the local database accordingly.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <param name="resolution">
    /// How the conflict should be settled.  Must not be
    /// <see cref="SyncResolution.Pending"/>.
    /// </param>
    Task ResolveConflictAsync(SyncConflict conflict, SyncResolution resolution);

    // ── History ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recent sync log entries, ordered from newest to oldest.
    /// </summary>
    /// <param name="limit">Maximum number of records to return.  Defaults to <c>20</c>.</param>
    Task<IReadOnlyList<SyncLogEntity>> GetSyncHistoryAsync(int limit = 20);

    // ── Auto-sync loop ────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a background polling loop that performs a full export/import cycle
    /// at the interval configured in <see cref="SyncConfiguration.SyncIntervalMinutes"/>.
    /// Safe to call when a loop is already running — the existing loop is replaced.
    /// The loop runs until <paramref name="ct"/> is cancelled or
    /// <see cref="StopAutoSyncAsync"/> is called.
    /// </summary>
    /// <param name="ct">Token that stops the loop when cancelled.</param>
    Task StartAutoSyncAsync(CancellationToken ct = default);

    /// <summary>
    /// Signals the running auto-sync loop to stop after the current cycle completes.
    /// Safe to call when no loop is active.
    /// </summary>
    Task StopAutoSyncAsync();
}

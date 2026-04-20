using AgentX.Core.Services.Sync.Models;

namespace AgentX.Core.Services.Sync.ConflictResolution;

/// <summary>
/// Detects and resolves conflicts between local and remote sync change sets.
/// A conflict occurs when the same entity has been independently modified on
/// both the local installation and a remote peer since the last successful sync.
/// </summary>
public interface ISyncConflictResolver
{
    /// <summary>
    /// Compares <paramref name="incoming"/> changes against local modification
    /// timestamps to identify entities modified on both sides since
    /// <paramref name="lastSyncAt"/>.
    /// </summary>
    /// <param name="incoming">Remote change set to evaluate.</param>
    /// <param name="lastSyncAt">
    /// UTC timestamp of the last successful sync. When <see langword="null"/>,
    /// no prior sync baseline exists and conflict detection is skipped.
    /// </param>
    /// <param name="localDeviceId">
    /// This installation's device ID, used to skip loop-back changes.
    /// </param>
    /// <param name="getLocalModifiedAt">
    /// Function that returns the modification timestamp for a given entity
    /// type and ID from the local database, or <see langword="null"/> if
    /// the entity does not exist locally.
    /// </param>
    /// <returns>
    /// A list of <see cref="SyncConflict"/> instances. Empty when there are
    /// no conflicts.
    /// </returns>
    Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(
        SyncChangeSet incoming,
        DateTime? lastSyncAt,
        string localDeviceId,
        Func<string, long, Task<DateTime?>> getLocalModifiedAt);

    /// <summary>
    /// Applies the chosen <paramref name="resolution"/> to the given
    /// <paramref name="conflict"/>, updating the conflict's Resolution field
    /// and returning the <see cref="SyncChange"/> that should be applied to
    /// the local database (or <see langword="null"/> if no change is needed).
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <param name="resolution">
    /// Resolution strategy. Must not be <see cref="SyncResolution.Pending"/>.
    /// </param>
    /// <returns>
    /// The <see cref="SyncChange"/> to apply to the local database, or
    /// <see langword="null"/> when the local version should be kept as-is.
    /// </returns>
    SyncChange? ResolveConflict(SyncConflict conflict, SyncResolution resolution);
}

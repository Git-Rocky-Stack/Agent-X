using AgentX.Core.Services.Sync.Models;

namespace AgentX.Core.Services.Sync.ConflictResolution;

/// <summary>
/// Detects and resolves conflicts between local and remote sync change sets.
/// Conflicts are settled by last writer wins: an incoming change is applied only when it is
/// newer than the local copy of the same entity, compared by modification timestamp, with a
/// deterministic device-id tie-break so both installations converge on the same version.
/// No wall-clock "last sync" baseline is involved, so the decision survives restarts and is
/// independent of when each side happened to export or import.
/// </summary>
public interface ISyncConflictResolver
{
    /// <summary>
    /// Decides what to do with one incoming change given the local copy's modification
    /// timestamp.
    /// </summary>
    /// <param name="remoteChange">The incoming change (its <see cref="SyncChange.Timestamp"/> is the remote modification time).</param>
    /// <param name="remoteDeviceId">Device that produced the change.</param>
    /// <param name="localModifiedAt">
    /// Modification timestamp of the matching local entity, or <see langword="null"/> when the
    /// entity does not exist locally.
    /// </param>
    /// <param name="localDeviceId">This installation's device id.</param>
    /// <returns>The last-writer-wins decision.</returns>
    SyncDecision Decide(
        SyncChange remoteChange,
        string remoteDeviceId,
        DateTime? localModifiedAt,
        string localDeviceId);

    /// <summary>
    /// Evaluates every change in <paramref name="incoming"/> and returns the ones whose local
    /// copy is newer, which last writer wins resolves by keeping the local version.
    /// </summary>
    /// <param name="incoming">Remote change set to evaluate.</param>
    /// <param name="localDeviceId">
    /// This installation's device ID, used to skip loop-back changes and for the tie-break.
    /// </param>
    /// <param name="getLocalVersion">
    /// Returns the local id and modification timestamp of the entity an incoming change refers
    /// to (matched by natural key, never by the remote numeric id), or <see langword="null"/>
    /// when no such entity exists locally.
    /// </param>
    /// <returns>
    /// A list of <see cref="SyncConflict"/> instances, each already resolved as
    /// <see cref="SyncResolution.KeepLocal"/>. Empty when there are no conflicts.
    /// </returns>
    Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(
        SyncChangeSet incoming,
        string localDeviceId,
        Func<SyncChange, Task<SyncLocalVersion?>> getLocalVersion);

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

/// <summary>
/// The local copy of an entity an incoming change refers to.
/// </summary>
/// <param name="EntityId">Local primary key.</param>
/// <param name="ModifiedAt">
/// Local modification timestamp, or <see langword="null"/> for entity types that carry no
/// modification time (documents, tags); those are merged rather than overwritten.
/// </param>
public sealed record SyncLocalVersion(long EntityId, DateTime? ModifiedAt);

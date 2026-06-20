using AgentX.Core.Services.Sync.Models;
using Serilog;

namespace AgentX.Core.Services.Sync.ConflictResolution;

/// <summary>
/// Production implementation of <see cref="ISyncConflictResolver"/>.
/// Detects conflicts by comparing local modification timestamps against
/// the last sync baseline, and resolves them using KeepLocal, KeepRemote,
/// or Merged strategies.
/// </summary>
public sealed class SyncConflictResolver : ISyncConflictResolver
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ILogger _log;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="SyncConflictResolver"/>.
    /// </summary>
    /// <param name="logger">Serilog logger instance.</param>
    public SyncConflictResolver(ILogger logger)
    {
        _log = (logger ?? throw new ArgumentNullException(nameof(logger)))
               .ForContext<SyncConflictResolver>();

        _log.Debug("SyncConflictResolver initialised");
    }

    // ── ISyncConflictResolver: DetectConflictsAsync ───────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(
        SyncChangeSet incoming,
        DateTime? lastSyncAt,
        string localDeviceId,
        Func<string, long, Task<DateTime?>> getLocalModifiedAt)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(getLocalModifiedAt);

        var conflicts = new List<SyncConflict>();

        // Without a prior sync baseline we have no way to distinguish "new to us"
        // from "independently modified on both sides" — treat all changes as new.
        if (lastSyncAt is null)
        {
            _log.Debug(
                "SyncConflictResolver.DetectConflictsAsync: no prior sync baseline — skipping conflict detection");
            return conflicts;
        }

        // Loop-back guard: never conflict with our own exported files.
        if (string.Equals(incoming.DeviceId, localDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug(
                "SyncConflictResolver.DetectConflictsAsync: change set originates from this device — skipping");
            return conflicts;
        }

        _log.Debug(
            "SyncConflictResolver.DetectConflictsAsync: checking {Count} incoming change(s)",
            incoming.Changes.Count);

        foreach (var remoteChange in incoming.Changes)
        {
            // Query the local modification timestamp for this entity.
            var localTs = await getLocalModifiedAt(
                remoteChange.EntityType, remoteChange.EntityId).ConfigureAwait(false);

            if (localTs is null)
                continue; // entity does not exist locally — nothing to conflict with

            if (localTs.Value <= lastSyncAt.Value)
                continue; // local version not touched since last sync — clean apply

            // Both the local install and the remote device modified the same entity
            // after the most recent sync timestamp — genuine conflict.
            var localChange = new SyncChange
            {
                EntityType = remoteChange.EntityType,
                EntityId = remoteChange.EntityId,
                ChangeType = SyncChangeType.Updated,
                Timestamp = localTs.Value,
                SerializedData = null, // serialised lazily only if the user selects KeepLocal
            };

            conflicts.Add(new SyncConflict
            {
                EntityType = remoteChange.EntityType,
                EntityId = remoteChange.EntityId,
                LocalChange = localChange,
                RemoteChange = remoteChange,
                Resolution = SyncResolution.Pending,
            });

            _log.Debug(
                "SyncConflictResolver.DetectConflictsAsync: conflict on {EntityType} Id={EntityId} " +
                "— local={LocalTs} remote={RemoteTs}",
                remoteChange.EntityType, remoteChange.EntityId,
                localTs.Value.ToString("O"), remoteChange.Timestamp.ToString("O"));
        }

        _log.Information(
            "SyncConflictResolver.DetectConflictsAsync: found {Count} conflict(s)",
            conflicts.Count);

        return conflicts;
    }

    // ── ISyncConflictResolver: ResolveConflict ────────────────────────────────

    /// <inheritdoc />
    public SyncChange? ResolveConflict(SyncConflict conflict, SyncResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        if (resolution == SyncResolution.Pending)
            throw new ArgumentException("Resolution must not be Pending.", nameof(resolution));

        _log.Information(
            "SyncConflictResolver.ResolveConflict: resolving {EntityType} Id={EntityId} as {Resolution}",
            conflict.EntityType, conflict.EntityId, resolution);

        conflict.Resolution = resolution;

        return resolution switch
        {
            SyncResolution.KeepLocal =>
                // The local database already contains the desired state — nothing to apply.
                null,

            SyncResolution.KeepRemote =>
                // Overwrite the local entity with the remote payload.
                conflict.RemoteChange,

            SyncResolution.Merged =>
                // The caller is responsible for populating RemoteChange.SerializedData with
                // the merged JSON representation before invoking this method.
                string.IsNullOrWhiteSpace(conflict.RemoteChange.SerializedData)
                    ? null
                    : conflict.RemoteChange,

            _ => null,
        };
    }
}

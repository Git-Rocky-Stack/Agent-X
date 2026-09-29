using AgentX.Core.Services.Sync.Models;
using Serilog;

namespace AgentX.Core.Services.Sync.ConflictResolution;

/// <summary>
/// Production implementation of <see cref="ISyncConflictResolver"/>.
/// Settles conflicts by last writer wins: the incoming change is applied only when its
/// modification timestamp is newer than the local copy's; on an exact tie the change from the
/// device with the ordinally greater device id wins, so two installations that edited the same
/// entity at the same instant still converge on one version. Explicit KeepLocal, KeepRemote and
/// Merged resolutions remain available through <see cref="ResolveConflict"/>.
/// </summary>
public sealed class SyncConflictResolver : ISyncConflictResolver
{
    // ---- Fields ----

    private readonly ILogger _log;

    // ---- Constructor ----

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

    // ---- ISyncConflictResolver: Decide ----

    /// <inheritdoc />
    public SyncDecision Decide(
        SyncChange remoteChange,
        string remoteDeviceId,
        DateTime? localModifiedAt,
        string localDeviceId)
    {
        ArgumentNullException.ThrowIfNull(remoteChange);

        // Loop-back guard: a change this installation produced is already reflected locally.
        if (string.Equals(remoteDeviceId, localDeviceId, StringComparison.OrdinalIgnoreCase))
            return SyncDecision.AlreadyCurrent;

        if (localModifiedAt is null)
        {
            // Nothing local to compare against: a new entity is inserted, and a deletion of
            // something that is not here has nothing left to do.
            return remoteChange.ChangeType == SyncChangeType.Deleted
                ? SyncDecision.AlreadyCurrent
                : SyncDecision.ApplyRemote;
        }

        var remoteTicks = UtcTicks(remoteChange.Timestamp);
        var localTicks = UtcTicks(localModifiedAt.Value);

        if (remoteTicks > localTicks)
            return SyncDecision.ApplyRemote;

        if (remoteTicks < localTicks)
            return SyncDecision.KeepLocal;

        // Exact tie: usually the same version echoed back. Pick deterministically so that a
        // genuine simultaneous edit still converges: the greater device id wins on both sides.
        return string.CompareOrdinal(remoteDeviceId ?? string.Empty, localDeviceId ?? string.Empty) > 0
            ? SyncDecision.ApplyRemote
            : SyncDecision.AlreadyCurrent;
    }

    // ---- ISyncConflictResolver: DetectConflictsAsync ----

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(
        SyncChangeSet incoming,
        string localDeviceId,
        Func<SyncChange, Task<SyncLocalVersion?>> getLocalVersion)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(getLocalVersion);

        var conflicts = new List<SyncConflict>();

        // Loop-back guard: never conflict with our own exported files.
        if (string.Equals(incoming.DeviceId, localDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug(
                "SyncConflictResolver.DetectConflictsAsync: change set originates from this device, skipping");
            return conflicts;
        }

        _log.Debug(
            "SyncConflictResolver.DetectConflictsAsync: checking {Count} incoming change(s)",
            incoming.Changes.Count);

        foreach (var remoteChange in incoming.Changes)
        {
            var local = await getLocalVersion(remoteChange).ConfigureAwait(false);

            // Entity absent locally, or an entity type without a modification time (merged,
            // never overwritten): nothing to conflict with.
            if (local?.ModifiedAt is null)
                continue;

            if (Decide(remoteChange, incoming.DeviceId, local.ModifiedAt, localDeviceId) != SyncDecision.KeepLocal)
                continue;

            conflicts.Add(new SyncConflict
            {
                EntityType = remoteChange.EntityType,
                EntityId = local.EntityId,
                LocalChange = new SyncChange
                {
                    EntityType = remoteChange.EntityType,
                    EntityId = local.EntityId,
                    ChangeType = SyncChangeType.Updated,
                    Timestamp = local.ModifiedAt.Value,
                    NaturalKey = remoteChange.NaturalKey,
                    SerializedData = null, // the local row is authoritative; nothing to transfer
                },
                RemoteChange = remoteChange,
                Resolution = SyncResolution.KeepLocal,
            });

            _log.Debug(
                "SyncConflictResolver.DetectConflictsAsync: local copy of {EntityType} (local Id={EntityId}) is newer, " +
                "keeping it. local={LocalTs} remote={RemoteTs}",
                remoteChange.EntityType, local.EntityId,
                local.ModifiedAt.Value.ToString("O"), remoteChange.Timestamp.ToString("O"));
        }

        _log.Information(
            "SyncConflictResolver.DetectConflictsAsync: found {Count} conflict(s)",
            conflicts.Count);

        return conflicts;
    }

    // ---- ISyncConflictResolver: ResolveConflict ----

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
                // The local database already contains the desired state; nothing to apply.
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

    // ---- Helpers ----

    /// <summary>
    /// Timestamps are UTC by convention (the database stores them without a kind); a value that
    /// deserialized as local time is converted so both sides compare on the same clock.
    /// </summary>
    private static long UtcTicks(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime().Ticks : value.Ticks;
}

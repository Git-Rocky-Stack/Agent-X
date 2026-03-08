using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Sync.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Sync;

/// <summary>
/// Production implementation of <see cref="ISyncService"/>.
///
/// Sync file layout on disk:
///   {SyncFolder}/agentx-sync-{deviceId}-{timestamp:yyyyMMddHHmmssffff}.axs
///
/// Each .axs file is an AES-256-GCM authenticated-encryption blob. Byte layout:
///   [0 ..7 ]  magic "AXSYNC\0\0" (8 bytes)           — format guard
///   [8 ..9 ]  format version uint16 LE                — forward-compat
///   [10..25]  PBKDF2 salt (16 bytes)                  — unique per file
///   [26..37]  AES-GCM nonce (12 bytes)                — unique per file
///   [38..53]  AES-GCM authentication tag (16 bytes)   — tamper detection
///   [54..  ]  AES-256-GCM ciphertext (UTF-8 JSON)
///
/// Configuration is stored in the <c>user_settings</c> table as two key-value rows:
///   Key = "SyncConfiguration"  →  JSON-serialised <see cref="SyncConfiguration"/>
///   Key = "SyncDeviceId"       →  stable UUID string for this installation
///
/// Thread-safety contract:
///   <see cref="Status"/> and <see cref="StatusChanged"/> are safe to call from any
///   thread. The auto-sync loop is guarded by <see cref="_loopLock"/> so that
///   <see cref="StartAutoSyncAsync"/> and <see cref="StopAutoSyncAsync"/> can be
///   called concurrently without data races.
/// </summary>
public sealed class SyncService : ISyncService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string SyncConfigKey = "SyncConfiguration";
    private const string DeviceIdKey   = "SyncDeviceId";
    private const string SyncFileExtension = ".axs";
    private const string SyncFilePrefix    = "agentx-sync-";

    /// <summary>Monotonically increasing wire-format version written into every .axs header.</summary>
    private const ushort FormatVersion = 1;

    // AES-256-GCM parameters
    private const int AesKeyBytes   = 32; // 256 bits
    private const int GcmNonceBytes = 12; // 96-bit nonce — optimal for GCM
    private const int GcmTagBytes   = 16; // 128-bit authentication tag

    // PBKDF2 parameters
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltBytes        = 16;

    // File header offsets and sizes
    private static readonly byte[] SyncMagic       = "AXSYNC\0\0"u8.ToArray(); // 8 bytes
    private const int MagicLen   = 8;
    private const int VersionLen = 2;   // uint16 LE
    private const int HeaderLen  = MagicLen + VersionLen + SaltBytes + GcmNonceBytes + GcmTagBytes;
    // = 8 + 2 + 16 + 12 + 16 = 54 bytes

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented            = false,
        PropertyNamingPolicy     = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition   = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly AgentXDbContext _db;
    private readonly ILogger         _log;

    /// <summary>Current sync status — mutated only through <see cref="SetStatus"/>.</summary>
    private SyncStatus _status = new();

    /// <summary>Guards <see cref="_status"/> for thread-safe reads and atomic mutations.</summary>
    private readonly object _statusLock = new();

    /// <summary>Guards the auto-sync loop start/stop path to prevent races.</summary>
    private readonly object _loopLock = new();

    /// <summary>Cancels the currently-running auto-sync loop when not null.</summary>
    private CancellationTokenSource? _autoSyncCts;

    /// <summary>
    /// In-process cache of the stable device ID so DB round-trips are avoided on
    /// every export cycle.
    /// </summary>
    private string? _cachedDeviceId;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="SyncService"/> backed by the given database context.
    /// </summary>
    /// <param name="dbContext">The EF Core context for the AgentX SQLite database.</param>
    /// <param name="logger">
    /// Root Serilog logger; the service creates a sub-context via
    /// <see cref="Log.ForContext{T}"/> so all messages carry the type name.
    /// </param>
    public SyncService(AgentXDbContext dbContext, ILogger logger)
    {
        _db  = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _log = (logger  ?? throw new ArgumentNullException(nameof(logger)))
               .ForContext<SyncService>();

        _log.Information("SyncService initialised");
    }

    // ── ISyncService: Status ──────────────────────────────────────────────────

    /// <inheritdoc />
    public SyncStatus Status
    {
        get
        {
            lock (_statusLock)
                return _status;
        }
    }

    /// <inheritdoc />
    public event Action<SyncStatus>? StatusChanged;

    // ── ISyncService: ConfigureAsync ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task ConfigureAsync(SyncConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _log.Information(
            "SyncService.ConfigureAsync: persisting configuration. " +
            "Folder={Folder} AutoSync={AutoSync} Interval={Interval}m Scope={Scope}",
            config.SyncFolderPath,
            config.AutoSyncEnabled,
            config.SyncIntervalMinutes,
            config.SyncScope);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await UpsertSettingAsync(SyncConfigKey, json).ConfigureAwait(false);

        _log.Information("SyncService.ConfigureAsync: configuration saved");
    }

    // ── ISyncService: GetConfigurationAsync ──────────────────────────────────

    /// <inheritdoc />
    public async Task<SyncConfiguration?> GetConfigurationAsync()
    {
        _log.Debug("SyncService.GetConfigurationAsync: loading configuration");

        var json = await GetSettingAsync(SyncConfigKey).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(json))
        {
            _log.Debug("SyncService.GetConfigurationAsync: no configuration found in database");
            return null;
        }

        try
        {
            var config = JsonSerializer.Deserialize<SyncConfiguration>(json, JsonOptions);

            _log.Debug(
                "SyncService.GetConfigurationAsync: configuration loaded. Folder={Folder}",
                config?.SyncFolderPath);

            return config;
        }
        catch (JsonException ex)
        {
            _log.Warning(ex,
                "SyncService.GetConfigurationAsync: failed to deserialise stored configuration — returning null");
            return null;
        }
    }

    // ── ISyncService: ExportChangesAsync ─────────────────────────────────────

    /// <inheritdoc />
    public async Task<SyncChangeSet> ExportChangesAsync(
        DateTime? since = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        _log.Information(
            "SyncService.ExportChangesAsync: starting export. Since={Since}",
            since?.ToString("O") ?? "<full>");

        SetStatus(s =>
        {
            s.SyncState   = SyncState.Syncing;
            s.ErrorMessage = null;
        });

        try
        {
            var config   = await GetRequiredConfigurationAsync().ConfigureAwait(false);
            var deviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

            // ── Collect changed entities from the local database ──────────────
            var changes = await CollectChangesAsync(since, config, ct).ConfigureAwait(false);

            var changeSet = new SyncChangeSet
            {
                DeviceId   = deviceId,
                ExportedAt = DateTime.UtcNow,
                Changes    = changes,
                Version    = FormatVersion,
            };

            _log.Information(
                "SyncService.ExportChangesAsync: collected {Count} change(s)",
                changes.Count);

            // ── Ensure the sync folder is accessible ──────────────────────────
            EnsureSyncFolderExists(config.SyncFolderPath);

            // ── Serialise → encrypt (AES-256-GCM) → write .axs file ──────────
            var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(changeSet, JsonOptions));
            var encrypted = EncryptGcm(plaintext, config.EncryptionKey);

            var fileName = BuildSyncFileName(deviceId, changeSet.ExportedAt);
            var filePath = Path.Combine(config.SyncFolderPath, fileName);

            await File.WriteAllBytesAsync(filePath, encrypted, ct).ConfigureAwait(false);

            sw.Stop();

            _log.Information(
                "SyncService.ExportChangesAsync: complete. File={FileName} Changes={Count} Duration={DurationMs:F1} ms",
                fileName, changes.Count, sw.Elapsed.TotalMilliseconds);

            // ── Persist audit log ─────────────────────────────────────────────
            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt           = DateTime.UtcNow,
                Direction          = "export",
                ChangesApplied     = changes.Count,
                ConflictsDetected  = 0,
                ConflictsResolved  = 0,
                DurationMs         = sw.Elapsed.TotalMilliseconds,
                IsSuccess          = true,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState          = SyncState.Idle;
                s.LastSyncAt         = DateTime.UtcNow;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
                s.PendingChanges     = 0;
                s.ErrorMessage       = null;
            });

            return changeSet;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _log.Warning(
                "SyncService.ExportChangesAsync: cancelled after {DurationMs:F1} ms",
                sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt      = DateTime.UtcNow,
                Direction     = "export",
                DurationMs    = sw.Elapsed.TotalMilliseconds,
                ErrorMessage  = "Export was cancelled.",
                IsSuccess     = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState    = SyncState.Idle;
                s.ErrorMessage = "Export was cancelled.";
            });

            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.Error(ex,
                "SyncService.ExportChangesAsync: failed after {DurationMs:F1} ms",
                sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt      = DateTime.UtcNow,
                Direction     = "export",
                DurationMs    = sw.Elapsed.TotalMilliseconds,
                ErrorMessage  = ex.Message,
                IsSuccess     = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState          = SyncState.Error;
                s.ErrorMessage       = ex.Message;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
            });

            throw;
        }
    }

    // ── ISyncService: ImportChangesAsync ─────────────────────────────────────

    /// <inheritdoc />
    public async Task<int> ImportChangesAsync(
        SyncChangeSet changeSet,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        var sw = Stopwatch.StartNew();

        _log.Information(
            "SyncService.ImportChangesAsync: starting import. " +
            "DeviceId={DeviceId} Changes={Count} ExportedAt={ExportedAt}",
            changeSet.DeviceId,
            changeSet.Changes.Count,
            changeSet.ExportedAt.ToString("O"));

        SetStatus(s =>
        {
            s.SyncState    = SyncState.Syncing;
            s.ErrorMessage = null;
        });

        try
        {
            // ── Detect conflicts before touching the database ─────────────────
            var conflicts        = await DetectConflictsAsync(changeSet).ConfigureAwait(false);
            var conflictEntities = conflicts
                .Select(c => (c.EntityType, c.EntityId))
                .ToHashSet();

            _log.Information(
                "SyncService.ImportChangesAsync: detected {Count} conflict(s)",
                conflicts.Count);

            // ── Apply every non-conflicting change ────────────────────────────
            var applied = 0;

            foreach (var change in changeSet.Changes)
            {
                ct.ThrowIfCancellationRequested();

                if (conflictEntities.Contains((change.EntityType, change.EntityId)))
                {
                    _log.Debug(
                        "SyncService.ImportChangesAsync: skipping conflicting change for " +
                        "{EntityType} Id={EntityId}",
                        change.EntityType, change.EntityId);
                    continue;
                }

                try
                {
                    await ApplyChangeAsync(change, ct).ConfigureAwait(false);
                    applied++;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex,
                        "SyncService.ImportChangesAsync: failed to apply change for " +
                        "{EntityType} Id={EntityId} — skipping",
                        change.EntityType, change.EntityId);
                }
            }

            // Flush all staged upserts / deletions in a single round-trip
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            sw.Stop();

            _log.Information(
                "SyncService.ImportChangesAsync: complete. " +
                "Applied={Applied} Conflicts={Conflicts} Duration={DurationMs:F1} ms",
                applied, conflicts.Count, sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt          = DateTime.UtcNow,
                Direction         = "import",
                ChangesApplied    = applied,
                ConflictsDetected = conflicts.Count,
                ConflictsResolved = 0,
                DurationMs        = sw.Elapsed.TotalMilliseconds,
                IsSuccess         = true,
            }, CancellationToken.None).ConfigureAwait(false);

            var newState = conflicts.Count > 0 ? SyncState.Conflict : SyncState.Idle;

            SetStatus(s =>
            {
                s.SyncState          = newState;
                s.LastSyncAt         = DateTime.UtcNow;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
                s.PendingChanges     = conflicts.Count;
                s.ErrorMessage       = conflicts.Count > 0
                    ? $"{conflicts.Count} conflict(s) require resolution."
                    : null;
            });

            return applied;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _log.Warning(
                "SyncService.ImportChangesAsync: cancelled after {DurationMs:F1} ms",
                sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt     = DateTime.UtcNow,
                Direction    = "import",
                DurationMs   = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = "Import was cancelled.",
                IsSuccess    = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState    = SyncState.Idle;
                s.ErrorMessage = "Import was cancelled.";
            });

            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.Error(ex,
                "SyncService.ImportChangesAsync: failed after {DurationMs:F1} ms",
                sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt     = DateTime.UtcNow,
                Direction    = "import",
                DurationMs   = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message,
                IsSuccess    = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState          = SyncState.Error;
                s.ErrorMessage       = ex.Message;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
            });

            throw;
        }
    }

    // ── ISyncService: DetectConflictsAsync ───────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(SyncChangeSet incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        _log.Debug(
            "SyncService.DetectConflictsAsync: checking {Count} incoming change(s)",
            incoming.Changes.Count);

        var conflicts   = new List<SyncConflict>();
        var lastSyncAt  = Status.LastSyncAt;

        // Without a prior sync baseline we have no way to distinguish "new to us"
        // from "independently modified on both sides" — treat all changes as new.
        if (lastSyncAt is null)
        {
            _log.Debug(
                "SyncService.DetectConflictsAsync: no prior sync baseline — skipping conflict detection");
            return conflicts;
        }

        var localDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

        // Loop-back guard: never conflict with our own exported files.
        if (string.Equals(incoming.DeviceId, localDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug(
                "SyncService.DetectConflictsAsync: change set originates from this device — skipping");
            return conflicts;
        }

        foreach (var remoteChange in incoming.Changes)
        {
            // Query the local modification timestamp for this entity.
            var localTs = await GetLocalModifiedAtAsync(
                remoteChange.EntityType, remoteChange.EntityId).ConfigureAwait(false);

            if (localTs is null)
                continue; // entity does not exist locally — nothing to conflict with

            if (localTs.Value <= lastSyncAt.Value)
                continue; // local version not touched since last sync — clean apply

            // Both the local install and the remote device modified the same entity
            // after the most recent sync timestamp → genuine conflict.
            var localChange = new SyncChange
            {
                EntityType     = remoteChange.EntityType,
                EntityId       = remoteChange.EntityId,
                ChangeType     = SyncChangeType.Updated,
                Timestamp      = localTs.Value,
                SerializedData = null, // serialised lazily only if the user selects KeepLocal
            };

            conflicts.Add(new SyncConflict
            {
                EntityType   = remoteChange.EntityType,
                EntityId     = remoteChange.EntityId,
                LocalChange  = localChange,
                RemoteChange = remoteChange,
                Resolution   = SyncResolution.Pending,
            });

            _log.Debug(
                "SyncService.DetectConflictsAsync: conflict on {EntityType} Id={EntityId} " +
                "— local={LocalTs} remote={RemoteTs}",
                remoteChange.EntityType, remoteChange.EntityId,
                localTs.Value.ToString("O"), remoteChange.Timestamp.ToString("O"));
        }

        _log.Information(
            "SyncService.DetectConflictsAsync: found {Count} conflict(s)",
            conflicts.Count);

        return conflicts;
    }

    // ── ISyncService: ResolveConflictAsync ───────────────────────────────────

    /// <inheritdoc />
    public async Task ResolveConflictAsync(SyncConflict conflict, SyncResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        if (resolution == SyncResolution.Pending)
            throw new ArgumentException(
                "Resolution must not be Pending.", nameof(resolution));

        _log.Information(
            "SyncService.ResolveConflictAsync: resolving {EntityType} Id={EntityId} as {Resolution}",
            conflict.EntityType, conflict.EntityId, resolution);

        conflict.Resolution = resolution;

        switch (resolution)
        {
            case SyncResolution.KeepLocal:
                // The local database already contains the desired state — nothing to write.
                _log.Debug(
                    "SyncService.ResolveConflictAsync: KeepLocal — no database update for " +
                    "{EntityType} Id={EntityId}",
                    conflict.EntityType, conflict.EntityId);
                break;

            case SyncResolution.KeepRemote:
                // Overwrite the local entity with the remote payload.
                await ApplyChangeAsync(conflict.RemoteChange, CancellationToken.None)
                    .ConfigureAwait(false);
                await _db.SaveChangesAsync().ConfigureAwait(false);

                _log.Debug(
                    "SyncService.ResolveConflictAsync: KeepRemote — remote change applied for " +
                    "{EntityType} Id={EntityId}",
                    conflict.EntityType, conflict.EntityId);
                break;

            case SyncResolution.Merged:
                // The caller is responsible for populating RemoteChange.SerializedData with
                // the merged JSON representation before invoking this method.
                if (!string.IsNullOrWhiteSpace(conflict.RemoteChange.SerializedData))
                {
                    await ApplyChangeAsync(conflict.RemoteChange, CancellationToken.None)
                        .ConfigureAwait(false);
                    await _db.SaveChangesAsync().ConfigureAwait(false);
                }

                _log.Debug(
                    "SyncService.ResolveConflictAsync: Merged — merged payload applied for " +
                    "{EntityType} Id={EntityId}",
                    conflict.EntityType, conflict.EntityId);
                break;
        }

        // Decrement the pending-changes counter and clear the Conflict state once
        // all outstanding conflicts have been resolved.
        SetStatus(s =>
        {
            s.PendingChanges = Math.Max(0, s.PendingChanges - 1);

            if (s.PendingChanges == 0 && s.SyncState == SyncState.Conflict)
            {
                s.SyncState    = SyncState.Idle;
                s.ErrorMessage = null;
            }
        });
    }

    // ── ISyncService: GetSyncHistoryAsync ─────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncLogEntity>> GetSyncHistoryAsync(int limit = 20)
    {
        _log.Debug("SyncService.GetSyncHistoryAsync: querying last {Limit} records", limit);

        var history = await _db.SyncLogs
            .OrderByDescending(l => l.SyncedAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);

        _log.Debug(
            "SyncService.GetSyncHistoryAsync: returned {Count} record(s)",
            history.Count);

        return history;
    }

    // ── ISyncService: StartAutoSyncAsync ──────────────────────────────────────

    /// <inheritdoc />
    public async Task StartAutoSyncAsync(CancellationToken ct = default)
    {
        // Always stop the existing loop first so we get a clean restart.
        await StopAutoSyncAsync().ConfigureAwait(false);

        var config = await GetConfigurationAsync().ConfigureAwait(false);

        if (config is null)
        {
            _log.Warning(
                "SyncService.StartAutoSyncAsync: no configuration found — auto-sync not started");
            return;
        }

        if (!config.AutoSyncEnabled)
        {
            _log.Information(
                "SyncService.StartAutoSyncAsync: auto-sync is disabled in configuration — not starting");
            return;
        }

        var intervalMinutes = Math.Max(1, config.SyncIntervalMinutes);

        lock (_loopLock)
        {
            // Build a linked CTS so both the external ct and StopAutoSyncAsync can halt the loop.
            _autoSyncCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var loopCt = _autoSyncCts.Token;

            // Fire-and-forget — the loop runs on the thread pool and handles its own errors.
            _ = Task.Run(() => RunAutoSyncLoopAsync(intervalMinutes, loopCt), loopCt);
        }

        _log.Information(
            "SyncService.StartAutoSyncAsync: auto-sync loop started. Interval={Interval} min",
            intervalMinutes);
    }

    // ── ISyncService: StopAutoSyncAsync ──────────────────────────────────────

    /// <inheritdoc />
    public Task StopAutoSyncAsync()
    {
        lock (_loopLock)
        {
            if (_autoSyncCts is null)
                return Task.CompletedTask;

            _log.Information("SyncService.StopAutoSyncAsync: cancelling auto-sync loop");

            _autoSyncCts.Cancel();
            _autoSyncCts.Dispose();
            _autoSyncCts = null;
        }

        return Task.CompletedTask;
    }

    // ── Private: auto-sync loop ───────────────────────────────────────────────

    /// <summary>
    /// Background polling loop driven by <see cref="PeriodicTimer"/>.
    /// On each tick: exports local changes since the last sync, then scans the sync
    /// folder for peer files and imports any it finds.
    /// Exits cleanly when <paramref name="ct"/> is cancelled.
    /// A single-cycle failure is logged and swallowed so the loop continues.
    /// </summary>
    private async Task RunAutoSyncLoopAsync(int intervalMinutes, CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        using var timer = new PeriodicTimer(interval);

        _log.Debug(
            "SyncService: auto-sync loop running. First tick in {Minutes} min",
            intervalMinutes);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                _log.Information("SyncService: auto-sync cycle triggered");

                try
                {
                    var lastSync = Status.LastSyncAt;

                    // Export local changes accumulated since the last successful sync.
                    await ExportChangesAsync(lastSync, ct).ConfigureAwait(false);

                    // Import any .axs files that peer devices deposited in the sync folder.
                    await ImportPeerFilesAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate to exit the outer while loop
                }
                catch (Exception ex)
                {
                    // Cycle-level failures must not crash the loop — log and continue.
                    _log.Error(ex,
                        "SyncService: unhandled error in auto-sync cycle — loop continues");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _log.Debug("SyncService: auto-sync loop exiting due to cancellation");
        }
    }

    // ── Private: peer file import scan ────────────────────────────────────────

    /// <summary>
    /// Enumerates all <c>.axs</c> files in the configured sync folder, skips files
    /// written by this device, decrypts and imports each peer file, then renames it
    /// to <c>.axs.imported</c> to prevent re-processing on the next cycle.
    /// </summary>
    private async Task ImportPeerFilesAsync(CancellationToken ct)
    {
        var config        = await GetRequiredConfigurationAsync().ConfigureAwait(false);
        var localDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

        if (!Directory.Exists(config.SyncFolderPath))
        {
            _log.Warning(
                "SyncService.ImportPeerFilesAsync: sync folder does not exist — skipping. Path={Path}",
                config.SyncFolderPath);
            return;
        }

        var files = Directory
            .EnumerateFiles(
                config.SyncFolderPath,
                $"{SyncFilePrefix}*{SyncFileExtension}")
            .ToList();

        _log.Debug(
            "SyncService.ImportPeerFilesAsync: found {Count} .axs file(s)",
            files.Count);

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Skip files this device produced.
            if (fileName.Contains(localDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                _log.Debug(
                    "SyncService.ImportPeerFilesAsync: skipping own file {FileName}",
                    fileName);
                continue;
            }

            try
            {
                _log.Information(
                    "SyncService.ImportPeerFilesAsync: processing peer file {FileName}",
                    fileName);

                var encrypted = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

                if (!IsValidSyncFileHeader(encrypted))
                {
                    _log.Warning(
                        "SyncService.ImportPeerFilesAsync: {FileName} has an invalid header — skipping",
                        fileName);
                    continue;
                }

                var plaintext = DecryptGcm(encrypted, config.EncryptionKey);
                var json      = Encoding.UTF8.GetString(plaintext);

                var changeSet = JsonSerializer.Deserialize<SyncChangeSet>(json, JsonOptions);

                if (changeSet is null)
                {
                    _log.Warning(
                        "SyncService.ImportPeerFilesAsync: could not deserialise {FileName} — skipping",
                        fileName);
                    continue;
                }

                await ImportChangesAsync(changeSet, ct).ConfigureAwait(false);

                // Rename so it is not re-imported on the next cycle.
                var processedPath = filePath + ".imported";
                File.Move(filePath, processedPath, overwrite: true);

                _log.Information(
                    "SyncService.ImportPeerFilesAsync: {FileName} processed → renamed to .imported",
                    fileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CryptographicException ex)
            {
                _log.Warning(ex,
                    "SyncService.ImportPeerFilesAsync: decryption failed for {FileName} " +
                    "— wrong passphrase or corrupted file",
                    fileName);
            }
            catch (Exception ex)
            {
                _log.Error(ex,
                    "SyncService.ImportPeerFilesAsync: unexpected error processing {FileName}",
                    fileName);
            }
        }
    }

    // ── Private: change collection ────────────────────────────────────────────

    /// <summary>
    /// Queries all syncable entity types and returns a flat list of
    /// <see cref="SyncChange"/> instances for entities modified after <paramref name="since"/>.
    /// When <paramref name="config"/> specifies <see cref="SyncScope.SelectedCollections"/> the
    /// document query is narrowed to only those collections.
    /// </summary>
    private async Task<List<SyncChange>> CollectChangesAsync(
        DateTime? since,
        SyncConfiguration config,
        CancellationToken ct)
    {
        var changes = new List<SyncChange>();
        var cutoff  = since ?? DateTime.MinValue;

        // Resolve the collection-ID allow-list for scoped syncs.
        HashSet<long>? allowedCollectionIds = null;

        if (config.SyncScope == SyncScope.SelectedCollections
            && !string.IsNullOrWhiteSpace(config.SelectedCollectionIds))
        {
            allowedCollectionIds = config.SelectedCollectionIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var id) ? id : -1L)
                .Where(id => id > 0)
                .ToHashSet();
        }

        // ── Documents ─────────────────────────────────────────────────────────
        var docsQuery = _db.Documents
            .AsNoTracking()
            .Where(d => d.ImportedAt > cutoff);

        if (allowedCollectionIds is not null)
        {
            docsQuery = docsQuery.Where(d =>
                d.DocumentCollections.Any(dc => allowedCollectionIds.Contains(dc.CollectionId)));
        }

        var documents = await docsQuery.ToListAsync(ct).ConfigureAwait(false);

        foreach (var doc in documents)
        {
            changes.Add(new SyncChange
            {
                EntityType     = nameof(DocumentEntity),
                EntityId       = doc.Id,
                ChangeType     = cutoff == DateTime.MinValue ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp      = doc.ImportedAt,
                SerializedData = JsonSerializer.Serialize(doc, JsonOptions),
            });
        }

        // ── Collections ───────────────────────────────────────────────────────
        var colQuery = _db.Collections
            .AsNoTracking()
            .Where(c => c.UpdatedAt > cutoff);

        if (allowedCollectionIds is not null)
            colQuery = colQuery.Where(c => allowedCollectionIds.Contains(c.Id));

        var collections = await colQuery.ToListAsync(ct).ConfigureAwait(false);

        foreach (var col in collections)
        {
            changes.Add(new SyncChange
            {
                EntityType     = nameof(CollectionEntity),
                EntityId       = col.Id,
                ChangeType     = col.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp      = col.UpdatedAt,
                SerializedData = JsonSerializer.Serialize(col, JsonOptions),
            });
        }

        // ── Tags ──────────────────────────────────────────────────────────────
        var tags = await _db.Tags
            .AsNoTracking()
            .Where(t => t.CreatedAt > cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var tag in tags)
        {
            changes.Add(new SyncChange
            {
                EntityType     = nameof(TagEntity),
                EntityId       = tag.Id,
                ChangeType     = SyncChangeType.Created,
                Timestamp      = tag.CreatedAt,
                SerializedData = JsonSerializer.Serialize(tag, JsonOptions),
            });
        }

        // ── Conversations ─────────────────────────────────────────────────────
        var conversations = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.UpdatedAt > cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var conv in conversations)
        {
            changes.Add(new SyncChange
            {
                EntityType     = nameof(ConversationEntity),
                EntityId       = conv.Id,
                ChangeType     = conv.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp      = conv.UpdatedAt,
                SerializedData = JsonSerializer.Serialize(conv, JsonOptions),
            });
        }

        // ── Annotations ───────────────────────────────────────────────────────
        var annotations = await _db.Annotations
            .AsNoTracking()
            .Where(a => a.UpdatedAt > cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var ann in annotations)
        {
            changes.Add(new SyncChange
            {
                EntityType     = nameof(AnnotationEntity),
                EntityId       = ann.Id,
                ChangeType     = ann.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp      = ann.UpdatedAt,
                SerializedData = JsonSerializer.Serialize(ann, JsonOptions),
            });
        }

        // ── System prompts ────────────────────────────────────────────────────
        var systemPrompts = await _db.SystemPrompts
            .AsNoTracking()
            .Where(sp => sp.UpdatedAt > cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var prompt in systemPrompts)
        {
            changes.Add(new SyncChange
            {
                EntityType     = nameof(SystemPromptEntity),
                EntityId       = prompt.Id,
                ChangeType     = prompt.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp      = prompt.UpdatedAt,
                SerializedData = JsonSerializer.Serialize(prompt, JsonOptions),
            });
        }

        _log.Debug(
            "SyncService.CollectChangesAsync: docs={Docs} collections={Cols} tags={Tags} " +
            "conversations={Convs} annotations={Anns} systemPrompts={Prompts}",
            documents.Count, collections.Count, tags.Count,
            conversations.Count, annotations.Count, systemPrompts.Count);

        return changes;
    }

    // ── Private: applying individual changes ──────────────────────────────────

    /// <summary>
    /// Deserialises a <see cref="SyncChange"/> and upserts or removes the entity in
    /// the local database.  Changes are staged on the EF change tracker — the caller
    /// must call <c>SaveChangesAsync</c> to flush them.
    /// </summary>
    private async Task ApplyChangeAsync(SyncChange change, CancellationToken ct)
    {
        if (change.ChangeType == SyncChangeType.Deleted)
        {
            await ApplyDeletionAsync(change).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(change.SerializedData))
        {
            _log.Warning(
                "SyncService.ApplyChangeAsync: {EntityType} Id={EntityId} has no serialised data — skipping",
                change.EntityType, change.EntityId);
            return;
        }

        switch (change.EntityType)
        {
            case nameof(DocumentEntity):
                await UpsertEntityAsync<DocumentEntity>(change, _db.Documents).ConfigureAwait(false);
                break;

            case nameof(CollectionEntity):
                await UpsertEntityAsync<CollectionEntity>(change, _db.Collections).ConfigureAwait(false);
                break;

            case nameof(TagEntity):
                await UpsertEntityAsync<TagEntity>(change, _db.Tags).ConfigureAwait(false);
                break;

            case nameof(ConversationEntity):
                await UpsertEntityAsync<ConversationEntity>(change, _db.Conversations).ConfigureAwait(false);
                break;

            case nameof(AnnotationEntity):
                await UpsertEntityAsync<AnnotationEntity>(change, _db.Annotations).ConfigureAwait(false);
                break;

            case nameof(SystemPromptEntity):
                await UpsertEntityAsync<SystemPromptEntity>(change, _db.SystemPrompts).ConfigureAwait(false);
                break;

            default:
                _log.Warning(
                    "SyncService.ApplyChangeAsync: unrecognised entity type '{EntityType}' — skipped",
                    change.EntityType);
                break;
        }
    }

    /// <summary>
    /// Deserialises the incoming entity from <see cref="SyncChange.SerializedData"/> and either:
    /// <list type="bullet">
    ///   <item>Adds it to <paramref name="dbSet"/> when it does not exist locally.</item>
    ///   <item>Overwrites all scalar properties on the existing tracked entry via
    ///         <c>CurrentValues.SetValues</c> when it does exist.</item>
    /// </list>
    /// Navigation properties are excluded because EF's <c>SetValues</c> only copies
    /// scalar columns, and cross-device foreign keys may not be valid on the receiving side.
    /// </summary>
    private async Task UpsertEntityAsync<TEntity>(SyncChange change, DbSet<TEntity> dbSet)
        where TEntity : class
    {
        var incoming = JsonSerializer.Deserialize<TEntity>(change.SerializedData!, JsonOptions);

        if (incoming is null)
        {
            _log.Warning(
                "SyncService.UpsertEntityAsync: could not deserialise {EntityType} Id={EntityId}",
                change.EntityType, change.EntityId);
            return;
        }

        var existing = await dbSet.FindAsync(change.EntityId).ConfigureAwait(false);

        if (existing is null)
        {
            // Detach to prevent EF from tracking the deserialised instance as Modified.
            _db.Entry(incoming).State = EntityState.Detached;
            dbSet.Add(incoming);

            _log.Debug(
                "SyncService.UpsertEntityAsync: adding {EntityType} Id={EntityId}",
                change.EntityType, change.EntityId);
        }
        else
        {
            // Overwrite all scalar columns, leaving navigation properties intact.
            _db.Entry(existing).CurrentValues.SetValues(incoming);

            _log.Debug(
                "SyncService.UpsertEntityAsync: updating {EntityType} Id={EntityId}",
                change.EntityType, change.EntityId);
        }
    }

    /// <summary>
    /// Removes the entity referenced by <paramref name="change"/> from the database
    /// if it exists locally, staging the deletion on the EF change tracker.
    /// </summary>
    private async Task ApplyDeletionAsync(SyncChange change)
    {
        switch (change.EntityType)
        {
            case nameof(DocumentEntity):
                await RemoveByIdAsync(_db.Documents, change.EntityId).ConfigureAwait(false);
                break;
            case nameof(CollectionEntity):
                await RemoveByIdAsync(_db.Collections, change.EntityId).ConfigureAwait(false);
                break;
            case nameof(TagEntity):
                await RemoveByIdAsync(_db.Tags, change.EntityId).ConfigureAwait(false);
                break;
            case nameof(ConversationEntity):
                await RemoveByIdAsync(_db.Conversations, change.EntityId).ConfigureAwait(false);
                break;
            case nameof(AnnotationEntity):
                await RemoveByIdAsync(_db.Annotations, change.EntityId).ConfigureAwait(false);
                break;
            case nameof(SystemPromptEntity):
                await RemoveByIdAsync(_db.SystemPrompts, change.EntityId).ConfigureAwait(false);
                break;
            default:
                _log.Warning(
                    "SyncService.ApplyDeletionAsync: unrecognised entity type '{EntityType}' — skipped",
                    change.EntityType);
                break;
        }
    }

    private async Task RemoveByIdAsync<TEntity>(DbSet<TEntity> dbSet, long id)
        where TEntity : class
    {
        var entity = await dbSet.FindAsync(id).ConfigureAwait(false);

        if (entity is not null)
        {
            dbSet.Remove(entity);
            _log.Debug(
                "SyncService.RemoveByIdAsync: staged deletion of {EntityType} Id={Id}",
                typeof(TEntity).Name, id);
        }
    }

    // ── Private: local timestamp resolution ──────────────────────────────────

    /// <summary>
    /// Returns the modification timestamp for <paramref name="entityId"/> in the local
    /// database, or <see langword="null"/> if the entity does not exist.
    /// Each entity type uses its canonical "last changed" column.
    /// </summary>
    private async Task<DateTime?> GetLocalModifiedAtAsync(string entityType, long entityId)
    {
        return entityType switch
        {
            nameof(DocumentEntity) => await _db.Documents
                .Where(d => d.Id == entityId)
                .Select(d => (DateTime?)d.ImportedAt)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false),

            nameof(CollectionEntity) => await _db.Collections
                .Where(c => c.Id == entityId)
                .Select(c => (DateTime?)c.UpdatedAt)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false),

            nameof(TagEntity) => await _db.Tags
                .Where(t => t.Id == entityId)
                .Select(t => (DateTime?)t.CreatedAt)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false),

            nameof(ConversationEntity) => await _db.Conversations
                .Where(c => c.Id == entityId)
                .Select(c => (DateTime?)c.UpdatedAt)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false),

            nameof(AnnotationEntity) => await _db.Annotations
                .Where(a => a.Id == entityId)
                .Select(a => (DateTime?)a.UpdatedAt)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false),

            nameof(SystemPromptEntity) => await _db.SystemPrompts
                .Where(sp => sp.Id == entityId)
                .Select(sp => (DateTime?)sp.UpdatedAt)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false),

            _ => null,
        };
    }

    // ── Private: device ID ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the stable UUID for this installation.  On first call the ID is
    /// generated, persisted in <c>user_settings</c> under <c>SyncDeviceId</c>, and
    /// cached in-process for the lifetime of this service instance.
    /// </summary>
    private async Task<string> GetOrCreateDeviceIdAsync()
    {
        if (_cachedDeviceId is not null)
            return _cachedDeviceId;

        var stored = await GetSettingAsync(DeviceIdKey).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stored))
        {
            _cachedDeviceId = stored;
            return _cachedDeviceId;
        }

        // First run on this installation — generate a new stable ID.
        var newId = Guid.NewGuid().ToString("N"); // 32-char hex, no hyphens
        await UpsertSettingAsync(DeviceIdKey, newId).ConfigureAwait(false);

        _cachedDeviceId = newId;

        _log.Information(
            "SyncService.GetOrCreateDeviceIdAsync: generated new device ID {DeviceId}",
            newId);

        return _cachedDeviceId;
    }

    // ── Private: UserSettings key-value helpers ───────────────────────────────

    /// <summary>Reads a single value from <c>user_settings</c> by key.</summary>
    private async Task<string?> GetSettingAsync(string key)
    {
        var entity = await _db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key)
            .ConfigureAwait(false);

        return entity?.Value;
    }

    /// <summary>
    /// Inserts or updates a row in <c>user_settings</c> for <paramref name="key"/>
    /// and immediately flushes to the database.
    /// </summary>
    private async Task UpsertSettingAsync(string key, string value)
    {
        var entity = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.Key == key)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _db.UserSettings.Add(new UserSettingsEntity
            {
                Key       = key,
                Value     = value,
                ValueType = "json",
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            entity.Value     = value;
            entity.ValueType = "json";
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    // ── Private: configuration guard ─────────────────────────────────────────

    /// <summary>
    /// Loads and validates the sync configuration, throwing
    /// <see cref="InvalidOperationException"/> if it is absent or incomplete.
    /// </summary>
    private async Task<SyncConfiguration> GetRequiredConfigurationAsync()
    {
        var config = await GetConfigurationAsync().ConfigureAwait(false);

        if (config is null)
            throw new InvalidOperationException(
                "Collaborative Sync has not been configured. " +
                "Call ConfigureAsync before performing sync operations.");

        if (string.IsNullOrWhiteSpace(config.SyncFolderPath))
            throw new InvalidOperationException(
                "SyncFolderPath is not set in the sync configuration.");

        if (string.IsNullOrWhiteSpace(config.EncryptionKey))
            throw new InvalidOperationException(
                "EncryptionKey is not set in the sync configuration.");

        return config;
    }

    // ── Private: sync log persistence ────────────────────────────────────────

    /// <summary>
    /// Persists a <see cref="SyncLogEntity"/> audit record.
    /// Failures are swallowed and logged so they never surface to the caller.
    /// </summary>
    private async Task PersistLogAsync(SyncLogEntity entry, CancellationToken ct)
    {
        try
        {
            _db.SyncLogs.Add(entry);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SyncService.PersistLogAsync: could not persist sync log entry");
        }
    }

    // ── Private: status mutation ──────────────────────────────────────────────

    /// <summary>
    /// Atomically applies <paramref name="mutate"/> to <see cref="_status"/> under
    /// <see cref="_statusLock"/>, then raises <see cref="StatusChanged"/> with a
    /// shallow copy of the updated status on the calling thread.
    /// Exceptions thrown by subscribers are caught and logged.
    /// </summary>
    private void SetStatus(Action<SyncStatus> mutate)
    {
        SyncStatus snapshot;

        lock (_statusLock)
        {
            mutate(_status);

            // Produce an immutable snapshot for the event payload so subscribers
            // cannot modify the internal status object.
            snapshot = new SyncStatus
            {
                LastSyncAt         = _status.LastSyncAt,
                SyncState          = _status.SyncState,
                ErrorMessage       = _status.ErrorMessage,
                PendingChanges     = _status.PendingChanges,
                LastSyncDurationMs = _status.LastSyncDurationMs,
            };
        }

        try
        {
            StatusChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SyncService.SetStatus: exception in StatusChanged subscriber");
        }
    }

    // ── Private: file-system helpers ──────────────────────────────────────────

    private static void EnsureSyncFolderExists(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot create or access the sync folder at '{path}'.", ex);
        }
    }

    /// <summary>
    /// Builds the canonical sync file name:
    /// <c>agentx-sync-{deviceId}-{exportedAt:yyyyMMddHHmmssffff}.axs</c>
    /// The sub-second component avoids collisions when multiple exports occur within
    /// the same second (e.g. during rapid testing).
    /// </summary>
    private static string BuildSyncFileName(string deviceId, DateTime exportedAt) =>
        $"{SyncFilePrefix}{deviceId}-{exportedAt:yyyyMMddHHmmssffff}{SyncFileExtension}";

    // ── Private: AES-256-GCM encryption ──────────────────────────────────────

    // .axs file byte layout (54-byte header + variable ciphertext):
    //
    //   Offset  Length  Field
    //   ------  ------  ---------------------------------------------------
    //    0       8      Magic: "AXSYNC\0\0"
    //    8       2      Format version (uint16 little-endian)
    //   10      16      PBKDF2 salt     (fresh random bytes per file)
    //   26      12      AES-GCM nonce   (fresh random bytes per file)
    //   38      16      AES-GCM authentication tag
    //   54       *      AES-256-GCM ciphertext (UTF-8 JSON of SyncChangeSet)

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM using a key derived
    /// from <paramref name="passphrase"/> via PBKDF2-SHA256.  Returns the full
    /// header + ciphertext byte array.
    /// </summary>
    private static byte[] EncryptGcm(byte[] plaintext, string passphrase)
    {
        var salt  = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceBytes);
        var key   = DeriveKey(passphrase, salt);

        var ciphertext = new byte[plaintext.Length];
        var tag        = new byte[GcmTagBytes];

        using var gcm = new AesGcm(key, GcmTagBytes);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: magic(8) + version(2) + salt(16) + nonce(12) + tag(16) + ciphertext
        var result = new byte[HeaderLen + ciphertext.Length];
        var span   = result.AsSpan();
        var offset = 0;

        SyncMagic.CopyTo(span[offset..]);
        offset += MagicLen;

        // Format version as uint16 LE
        BitConverter.TryWriteBytes(span[offset..], FormatVersion);
        offset += VersionLen;

        salt.CopyTo(span[offset..]);
        offset += SaltBytes;

        nonce.CopyTo(span[offset..]);
        offset += GcmNonceBytes;

        tag.CopyTo(span[offset..]);
        offset += GcmTagBytes;

        ciphertext.CopyTo(span[offset..]);

        return result;
    }

    /// <summary>
    /// Decrypts an AES-256-GCM encrypted .axs file produced by <see cref="EncryptGcm"/>.
    /// Throws <see cref="CryptographicException"/> if the authentication tag does not
    /// match (wrong key, corrupted data, or tampered file).
    /// </summary>
    private static byte[] DecryptGcm(byte[] cipherData, string passphrase)
    {
        if (cipherData.Length < HeaderLen)
            throw new InvalidOperationException(
                "Data is too short to be a valid .axs sync file.");

        // Parse header fields using index ranges for zero-copy slicing.
        var offset    = MagicLen + VersionLen; // skip magic and version — already validated
        var salt      = cipherData[offset..(offset + SaltBytes)];
        offset       += SaltBytes;
        var nonce     = cipherData[offset..(offset + GcmNonceBytes)];
        offset       += GcmNonceBytes;
        var tag       = cipherData[offset..(offset + GcmTagBytes)];
        offset       += GcmTagBytes;
        var ciphertext = cipherData[offset..];

        var key       = DeriveKey(passphrase, salt);
        var plaintext = new byte[ciphertext.Length];

        using var gcm = new AesGcm(key, GcmTagBytes);

        // AesGcm.Decrypt throws CryptographicException on tag mismatch.
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Derives a 256-bit AES key from <paramref name="passphrase"/> and
    /// <paramref name="salt"/> using PBKDF2-HMAC-SHA256 with
    /// <see cref="Pbkdf2Iterations"/> iterations.
    /// </summary>
    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(
            passphrase,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return kdf.GetBytes(AesKeyBytes); // 32 bytes = 256 bits
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="data"/> begins with the
    /// expected magic bytes and is long enough to contain a complete header.
    /// </summary>
    private static bool IsValidSyncFileHeader(byte[] data)
    {
        if (data.Length < HeaderLen)
            return false;

        return data[..MagicLen].SequenceEqual(SyncMagic);
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Sync.Codec;
using AgentX.Core.Services.Sync.ConflictResolution;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Sync.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Serilog;

namespace AgentX.Core.Services.Sync;

/// <summary>
/// Thin orchestrator implementation of <see cref="ISyncService"/>.
/// Delegates file I/O to <see cref="ISyncTransport"/>, serialisation and
/// encryption to <see cref="ISyncPackageCodec"/>, and the last-writer-wins decision
/// to <see cref="ISyncConflictResolver"/>.
///
/// Sync file layout on disk:
///   {SyncFolder}/agentx-sync-{deviceId}-{timestamp:yyyyMMddHHmmssffff}.axs
///
/// State is stored in the <c>user_settings</c> table as key-value rows:
///   Key = "SyncConfiguration"  JSON-serialised <see cref="SyncConfiguration"/>
///   Key = "SyncDeviceId"       stable UUID string for this installation
///   Key = "SyncState"          export watermark, last sync times and per-peer watermarks
///
/// Identity: rows are matched across installations by natural key (see
/// <see cref="SyncNaturalKeys"/>), never by the local auto-increment id, and an
/// unmatched change is inserted with a new local id.
///
/// Failure isolation: every incoming change is saved on its own. A change that fails to
/// save has its tracked entries rolled back so it cannot poison later saves on the shared
/// context, and the peer file stays in the folder to be retried.
///
/// Thread-safety contract:
///   <see cref="Status"/> and <see cref="StatusChanged"/> are safe to call from any
///   thread. The auto-sync loop is guarded by <see cref="_loopLock"/> so that
///   <see cref="StartAutoSyncAsync"/> and <see cref="StopAutoSyncAsync"/> can be
///   called concurrently without data races, and complete passes are serialised by
///   <see cref="_passGate"/> so a manual pass never overlaps a scheduled one.
/// </summary>
public sealed class SyncService : ISyncService
{
    // ---- Constants ----

    private const string SyncConfigKey = "SyncConfiguration";
    private const string DeviceIdKey = "SyncDeviceId";
    private const string SyncStateKey = "SyncState";

    private const string DocumentReference = nameof(AnnotationEntity.DocumentId);
    private const string ParentCollectionReference = nameof(CollectionEntity.ParentCollectionId);
    private const string ParentConversationReference = nameof(ConversationEntity.ParentConversationId);

    /// <summary>
    /// Indexing error recorded on a synced document whose source file does not exist on this
    /// machine. The document is never marked completed, because its chunks are not synced.
    /// </summary>
    internal const string MissingFileIndexingError =
        "Synced from another device. The source file was not found at this path on this machine; " +
        "re-import it here to index its content.";

    /// <summary>
    /// First-cycle delay of a loop started by <see cref="ResumeAutoSyncAsync"/>: long enough to
    /// stay out of the way of startup work, short enough to pick up peer files soon after launch.
    /// </summary>
    private static readonly TimeSpan ResumeFirstCycleDelay = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- Fields ----

    private readonly AgentXDbContext _db;
    private readonly ILogger _log;
    private readonly ISyncTransport _transport;
    private readonly ISyncPackageCodec _codec;
    private readonly ISyncConflictResolver _conflictResolver;

    /// <summary>Current sync status, mutated only through <see cref="SetStatus"/>.</summary>
    private SyncStatus _status = new();

    /// <summary>Guards <see cref="_status"/> for thread-safe reads and atomic mutations.</summary>
    private readonly object _statusLock = new();

    /// <summary>Guards the auto-sync loop start/stop path to prevent races.</summary>
    // Wave 4b: migrated from `lock (object)` to SemaphoreSlim so cancellation of the
    // auto-sync CTS can be awaited. The semaphore is *not* reentrant; each critical
    // section is straight-line and does not reacquire the lock.
    private readonly SemaphoreSlim _loopLock = new(1, 1);

    /// <summary>Serialises complete sync passes (manual and scheduled).</summary>
    private readonly SemaphoreSlim _passGate = new(1, 1);

    /// <summary>Cancels the currently-running auto-sync loop when not null.</summary>
    private CancellationTokenSource? _autoSyncCts;

    /// <summary>The running auto-sync loop, awaited on stop so no cycle outlives it.</summary>
    private Task? _autoSyncTask;

    /// <summary>
    /// In-process cache of the stable device ID so DB round-trips are avoided on
    /// every export cycle.
    /// </summary>
    private string? _cachedDeviceId;

    /// <summary>1 once the persisted last-sync time has been loaded into <see cref="Status"/>.</summary>
    private int _persistedStatusLoaded;

    // ---- Constructor ----

    /// <summary>
    /// Initialises a new <see cref="SyncService"/> backed by the given database context
    /// and composed sub-services.
    /// </summary>
    /// <param name="dbContext">The EF Core context for the AgentX SQLite database.</param>
    /// <param name="logger">Root Serilog logger.</param>
    /// <param name="transport">File-system transport for .axs files.</param>
    /// <param name="codec">Serialisation and encryption codec.</param>
    /// <param name="conflictResolver">Last-writer-wins conflict resolution engine.</param>
    public SyncService(
        AgentXDbContext dbContext,
        ILogger logger,
        ISyncTransport transport,
        ISyncPackageCodec codec,
        ISyncConflictResolver conflictResolver)
    {
        _db = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger)))
                           .ForContext<SyncService>();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));

        _log.Information("SyncService initialised");
    }

    // ---- ISyncService: Status ----

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

    /// <inheritdoc />
    public bool IsAutoSyncRunning => Volatile.Read(ref _autoSyncCts) is { IsCancellationRequested: false };

    // ---- ISyncService: ConfigureAsync ----

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

    // ---- ISyncService: GetConfigurationAsync ----

    /// <inheritdoc />
    public async Task<SyncConfiguration?> GetConfigurationAsync()
    {
        _log.Debug("SyncService.GetConfigurationAsync: loading configuration");

        await EnsurePersistedStatusLoadedAsync().ConfigureAwait(false);

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
                "SyncService.GetConfigurationAsync: failed to deserialise stored configuration, returning null");
            return null;
        }
    }

    // ---- ISyncService: ExportChangesAsync ----

    /// <inheritdoc />
    public Task<SyncChangeSet> ExportChangesAsync(
        DateTime? since = null,
        CancellationToken ct = default)
        => ExportCoreAsync(since, writeEmptyFile: true, ct);

    private async Task<SyncChangeSet> ExportCoreAsync(
        DateTime? since,
        bool writeEmptyFile,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        _log.Information(
            "SyncService.ExportChangesAsync: starting export. Since={Since}",
            since?.ToString("O") ?? "<full>");

        SetStatus(s =>
        {
            s.SyncState = SyncState.Syncing;
            s.ErrorMessage = null;
        });

        try
        {
            var config = await GetRequiredConfigurationAsync().ConfigureAwait(false);
            var deviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

            // The watermark is captured BEFORE collecting, so an edit made while the export
            // runs has a later timestamp and is picked up by the next export instead of lost.
            var watermark = DateTime.UtcNow;

            var changes = await CollectChangesAsync(since, config, ct).ConfigureAwait(false);

            var changeSet = new SyncChangeSet
            {
                DeviceId = deviceId,
                ExportedAt = DateTime.UtcNow,
                Changes = changes,
                Version = SyncChangeSet.CurrentVersion,
            };

            _log.Information(
                "SyncService.ExportChangesAsync: collected {Count} change(s)",
                changes.Count);

            string? fileName = null;
            if (changes.Count > 0 || writeEmptyFile)
            {
                _transport.EnsureFolderExists(config.SyncFolderPath);

                var plaintext = _codec.Serialise(changeSet);
                var encrypted = _codec.Encrypt(plaintext, config.EncryptionKey);

                var filePath = await _transport.WriteSyncFileAsync(
                    config.SyncFolderPath, deviceId, changeSet.ExportedAt, encrypted, ct)
                    .ConfigureAwait(false);
                fileName = Path.GetFileName(filePath);
            }

            sw.Stop();

            _log.Information(
                "SyncService.ExportChangesAsync: complete. File={Path} Changes={Count} Duration={DurationMs:F1} ms",
                fileName ?? "<none, nothing changed>", changes.Count, sw.Elapsed.TotalMilliseconds);

            await RecordExportAsync(since, watermark).ConfigureAwait(false);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt = DateTime.UtcNow,
                Direction = "export",
                ChangesApplied = changes.Count,
                ConflictsDetected = 0,
                ConflictsResolved = 0,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                IsSuccess = true,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState = SyncState.Idle;
                s.LastSyncAt = DateTime.UtcNow;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
                s.PendingChanges = 0;
                s.ErrorMessage = null;
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
                SyncedAt = DateTime.UtcNow,
                Direction = "export",
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = "Export was cancelled.",
                IsSuccess = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState = SyncState.Idle;
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
                SyncedAt = DateTime.UtcNow,
                Direction = "export",
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message,
                IsSuccess = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState = SyncState.Error;
                s.ErrorMessage = ex.Message;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
            });

            throw;
        }
    }

    // ---- ISyncService: ImportChangesAsync ----

    /// <inheritdoc />
    public async Task<int> ImportChangesAsync(
        SyncChangeSet changeSet,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        var result = await ImportChangeSetAsync(changeSet, ct).ConfigureAwait(false);
        return result.Applied;
    }

    /// <summary>
    /// Applies one change set, records a sync log row and updates <see cref="Status"/>.
    /// Throws only when the pass as a whole cannot run (cancellation, database unavailable);
    /// individual change failures are counted in the returned result.
    /// </summary>
    private async Task<SyncImportResult> ImportChangeSetAsync(
        SyncChangeSet changeSet,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        _log.Information(
            "SyncService.ImportChangesAsync: starting import. " +
            "DeviceId={DeviceId} Version={Version} Changes={Count} ExportedAt={ExportedAt}",
            changeSet.DeviceId,
            changeSet.Version,
            changeSet.Changes.Count,
            changeSet.ExportedAt.ToString("O"));

        SetStatus(s =>
        {
            s.SyncState = SyncState.Syncing;
            s.ErrorMessage = null;
        });

        try
        {
            var context = new ImportContext
            {
                LocalDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false),
                RemoteDeviceId = changeSet.DeviceId ?? string.Empty,
            };

            var result = await ApplyChangesAsync(changeSet.Changes, context, ct).ConfigureAwait(false);

            sw.Stop();

            _log.Information(
                "SyncService.ImportChangesAsync: complete. Applied={Applied} Unchanged={Unchanged} " +
                "ConflictsResolved={Conflicts} Rejected={Rejected} Failed={Failed} Duration={DurationMs:F1} ms",
                result.Applied, result.Unchanged, result.ConflictsResolved, result.Rejected, result.Failed,
                sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt = DateTime.UtcNow,
                Direction = "import",
                ChangesApplied = result.Applied,
                ConflictsDetected = result.ConflictsResolved,
                ConflictsResolved = result.ConflictsResolved,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                IsSuccess = result.Failed == 0 && result.Rejected == 0,
                ErrorMessage = DescribeProblems(result),
            }, CancellationToken.None).ConfigureAwait(false);

            if (result.Failed == 0)
                await RecordImportAsync().ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState = result.Failed > 0 ? SyncState.Error : SyncState.Idle;
                if (result.Failed == 0)
                    s.LastSyncAt = DateTime.UtcNow;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
                s.PendingChanges = result.Failed;
                s.ErrorMessage = DescribeProblems(result);
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _log.Warning(
                "SyncService.ImportChangesAsync: cancelled after {DurationMs:F1} ms",
                sw.Elapsed.TotalMilliseconds);

            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt = DateTime.UtcNow,
                Direction = "import",
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = "Import was cancelled.",
                IsSuccess = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState = SyncState.Idle;
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
                SyncedAt = DateTime.UtcNow,
                Direction = "import",
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message,
                IsSuccess = false,
            }, CancellationToken.None).ConfigureAwait(false);

            SetStatus(s =>
            {
                s.SyncState = SyncState.Error;
                s.ErrorMessage = ex.Message;
                s.LastSyncDurationMs = sw.Elapsed.TotalMilliseconds;
            });

            throw;
        }
    }

    // ---- ISyncService: SyncNowAsync / ImportNowAsync ----

    /// <inheritdoc />
    public async Task<SyncRunResult> SyncNowAsync(CancellationToken ct = default)
    {
        await _passGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RunPassAsync(export: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _passGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SyncRunResult> ImportNowAsync(CancellationToken ct = default)
    {
        await _passGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RunPassAsync(export: false, ct).ConfigureAwait(false);
        }
        finally
        {
            _passGate.Release();
        }
    }

    /// <summary>One sync pass. Callers hold <see cref="_passGate"/>.</summary>
    private async Task<SyncRunResult> RunPassAsync(bool export, CancellationToken ct)
    {
        var result = new SyncRunResult();

        if (export)
        {
            var state = await LoadSyncStateAsync().ConfigureAwait(false);
            var changeSet = await ExportCoreAsync(state.ExportWatermarkUtc, writeEmptyFile: false, ct)
                .ConfigureAwait(false);
            result.ExportedChanges = changeSet.Changes.Count;
        }

        await ImportPeerFilesAsync(result, ct).ConfigureAwait(false);

        _log.Information(
            "SyncService: pass complete. Exported={Exported} PeerFiles={Found} Imported={Imported} " +
            "PendingRetry={Retry} Unreadable={Unreadable} Applied={Applied} Conflicts={Conflicts} " +
            "Rejected={Rejected} Failed={Failed}",
            result.ExportedChanges, result.PeerFilesFound, result.PeerFilesImported,
            result.PeerFilesPendingRetry, result.PeerFilesUnreadable, result.ChangesApplied,
            result.ConflictsResolved, result.ChangesRejected, result.ChangesFailed);

        return result;
    }

    // ---- ISyncService: DetectConflictsAsync ----

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(SyncChangeSet incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var context = new ImportContext
        {
            LocalDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false),
            RemoteDeviceId = incoming.DeviceId ?? string.Empty,
        };

        return await _conflictResolver.DetectConflictsAsync(
            incoming,
            context.LocalDeviceId,
            change => TryLocateLocalVersionAsync(change, context, CancellationToken.None)).ConfigureAwait(false);
    }

    // ---- ISyncService: ResolveConflictAsync ----

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

        var changeToApply = _conflictResolver.ResolveConflict(conflict, resolution);

        if (changeToApply is not null)
        {
            // An explicit choice overrides last writer wins: the remote version is applied even
            // though the local copy is newer. Matching is still by natural key.
            var context = new ImportContext
            {
                LocalDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false),
                RemoteDeviceId = string.Empty,
                Force = true,
            };

            try
            {
                await ProcessChangeAsync(changeToApply, context, CancellationToken.None).ConfigureAwait(false);
            }
            catch (RejectedChangeException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
        }

        // Decrement the pending-changes counter and clear the Conflict state once
        // all outstanding conflicts have been resolved.
        SetStatus(s =>
        {
            s.PendingChanges = Math.Max(0, s.PendingChanges - 1);

            if (s.PendingChanges == 0 && s.SyncState == SyncState.Conflict)
            {
                s.SyncState = SyncState.Idle;
                s.ErrorMessage = null;
            }
        });
    }

    // ---- ISyncService: GetSyncHistoryAsync ----

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

    // ---- ISyncService: auto-sync lifecycle ----

    /// <inheritdoc />
    public Task StartAutoSyncAsync(CancellationToken ct = default) =>
        StartLoopAsync(firstDelayOverride: null, ct);

    /// <inheritdoc />
    public async Task ResumeAutoSyncAsync(CancellationToken ct = default)
    {
        await EnsurePersistedStatusLoadedAsync().ConfigureAwait(false);
        await StartLoopAsync(ResumeFirstCycleDelay, ct).ConfigureAwait(false);
    }

    private async Task StartLoopAsync(TimeSpan? firstDelayOverride, CancellationToken ct)
    {
        var config = await GetConfigurationAsync().ConfigureAwait(false);

        // Stop and start under one lock acquisition, so two concurrent starts cannot both
        // launch a loop and leak the first one.
        await _loopLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopLoopWhileLockedAsync().ConfigureAwait(false);

            if (config is null)
            {
                _log.Warning(
                    "SyncService.StartAutoSyncAsync: no configuration found, auto-sync not started");
                return;
            }

            if (!config.AutoSyncEnabled)
            {
                _log.Information(
                    "SyncService.StartAutoSyncAsync: auto-sync is disabled in configuration, not starting");
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, config.SyncIntervalMinutes));
            var firstDelay = firstDelayOverride is { } requested && requested < interval ? requested : interval;

            _autoSyncCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var loopCt = _autoSyncCts.Token;

            // The loop always starts and observes its own token, so a stop can await it to
            // completion.
            _autoSyncTask = Task.Run(() => RunAutoSyncLoopAsync(firstDelay, interval, loopCt), CancellationToken.None);

            _log.Information(
                "SyncService.StartAutoSyncAsync: auto-sync loop started. Interval={Interval} min FirstCycleIn={FirstDelay}",
                interval.TotalMinutes, firstDelay);
        }
        finally
        {
            _loopLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAutoSyncAsync()
    {
        await _loopLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopLoopWhileLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _loopLock.Release();
        }
    }

    /// <summary>Cancels the loop and waits for it to exit. Callers hold <see cref="_loopLock"/>.</summary>
    private async Task StopLoopWhileLockedAsync()
    {
        if (_autoSyncCts is null)
            return;

        _log.Information("SyncService.StopAutoSyncAsync: cancelling auto-sync loop");

        await _autoSyncCts.CancelAsync().ConfigureAwait(false);

        if (_autoSyncTask is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the loop is torn down mid-wait.
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "SyncService.StopAutoSyncAsync: auto-sync loop ended with an error");
            }
        }

        _autoSyncCts.Dispose();
        _autoSyncCts = null;
        _autoSyncTask = null;
    }

    // ---- Private: auto-sync loop ----

    private async Task RunAutoSyncLoopAsync(TimeSpan firstDelay, TimeSpan interval, CancellationToken ct)
    {
        _log.Debug(
            "SyncService: auto-sync loop running. First cycle in {FirstDelay}, then every {Interval}",
            firstDelay, interval);

        try
        {
            await Task.Delay(firstDelay, ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(interval);

            do
            {
                ct.ThrowIfCancellationRequested();

                _log.Information("SyncService: auto-sync cycle triggered");

                try
                {
                    await _passGate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await RunPassAsync(export: true, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        _passGate.Release();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Error(ex,
                        "SyncService: unhandled error in auto-sync cycle, loop continues");
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            _log.Debug("SyncService: auto-sync loop exiting due to cancellation");
        }
    }

    // ---- Private: peer file import ----

    private async Task ImportPeerFilesAsync(SyncRunResult result, CancellationToken ct)
    {
        var config = await GetRequiredConfigurationAsync().ConfigureAwait(false);
        var localDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

        var peerFiles = await _transport.ReadPeerFilesAsync(
            config.SyncFolderPath, localDeviceId, ct).ConfigureAwait(false);

        result.PeerFilesFound = peerFiles.Count;
        var importedPeers = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var peer in peerFiles)
        {
            ct.ThrowIfCancellationRequested();

            _log.Information(
                "SyncService.ImportPeerFilesAsync: processing peer file {FileName}",
                peer.FileName);

            if (!_codec.IsValidHeader(peer.Data))
            {
                _log.Warning(
                    "SyncService.ImportPeerFilesAsync: {FileName} has an invalid header, skipping",
                    peer.FileName);
                result.PeerFilesUnreadable++;
                result.Errors.Add($"{peer.FileName}: not a valid sync file.");
                continue;
            }

            SyncChangeSet changeSet;
            try
            {
                var plaintext = _codec.Decrypt(peer.Data, config.EncryptionKey);
                changeSet = _codec.Deserialise(plaintext);
            }
            catch (CryptographicException ex)
            {
                _log.Warning(ex,
                    "SyncService.ImportPeerFilesAsync: decryption failed for {FileName}, " +
                    "wrong passphrase or corrupted file",
                    peer.FileName);
                result.PeerFilesUnreadable++;
                result.Errors.Add($"{peer.FileName}: could not be decrypted (wrong passphrase or corrupted file).");
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex,
                    "SyncService.ImportPeerFilesAsync: could not read {FileName}",
                    peer.FileName);
                result.PeerFilesUnreadable++;
                result.Errors.Add($"{peer.FileName}: could not be read ({ex.Message}).");
                continue;
            }

            SyncImportResult imported;
            try
            {
                imported = await ImportChangeSetAsync(changeSet, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex,
                    "SyncService.ImportPeerFilesAsync: import of {FileName} failed, it will be retried",
                    peer.FileName);
                result.PeerFilesPendingRetry++;
                result.Errors.Add($"{peer.FileName}: import failed ({ex.Message}); it will be retried.");
                continue;
            }

            result.ChangesApplied += imported.Applied;
            result.ConflictsResolved += imported.ConflictsResolved;
            result.ChangesRejected += imported.Rejected;
            result.ChangesFailed += imported.Failed;
            result.Errors.AddRange(imported.Errors.Select(error => $"{peer.FileName}: {error}"));

            if (!imported.IsComplete)
            {
                // Leave the file in place: the failed changes are retried next cycle and the
                // ones already applied are recognised as current by last writer wins.
                result.PeerFilesPendingRetry++;
                _log.Warning(
                    "SyncService.ImportPeerFilesAsync: {Failed} change(s) in {FileName} failed; " +
                    "file left in place for retry",
                    imported.Failed, peer.FileName);
                continue;
            }

            try
            {
                await _transport.MarkFileImportedAsync(peer.FilePath).ConfigureAwait(false);
                result.PeerFilesImported++;

                if (!string.IsNullOrWhiteSpace(changeSet.DeviceId)
                    && (!importedPeers.TryGetValue(changeSet.DeviceId, out var newest) || changeSet.ExportedAt > newest))
                {
                    importedPeers[changeSet.DeviceId] = changeSet.ExportedAt;
                }

                _log.Information(
                    "SyncService.ImportPeerFilesAsync: {FileName} processed, renamed to .imported",
                    peer.FileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Every change was applied; re-reading the file next cycle is harmless because
                // its changes will all be recognised as current.
                _log.Warning(ex,
                    "SyncService.ImportPeerFilesAsync: {FileName} was imported but could not be renamed",
                    peer.FileName);
                result.PeerFilesImported++;
                result.Errors.Add($"{peer.FileName}: imported, but could not be marked as imported ({ex.Message}).");
            }
        }

        if (importedPeers.Count > 0)
            await RecordPeerImportsAsync(importedPeers).ConfigureAwait(false);
    }

    // ---- Private: change collection ----

    private async Task<List<SyncChange>> CollectChangesAsync(
        DateTime? since,
        SyncConfiguration config,
        CancellationToken ct)
    {
        var changes = new List<SyncChange>();
        var cutoff = since ?? DateTime.MinValue;

        HashSet<long>? selectedCollectionIds = null;

        if (config.SyncScope == SyncScope.SelectedCollections
            && !string.IsNullOrWhiteSpace(config.SelectedCollectionIds))
        {
            selectedCollectionIds = config.SelectedCollectionIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var id) ? id : -1L)
                .Where(id => id > 0)
                .ToHashSet();
        }

        var scoped = selectedCollectionIds is not null;

        // Every collection's natural key is its name path, so keys need the whole tree.
        var collectionRows = await _db.Collections
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ParentCollectionId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var collectionKeys = SyncNaturalKeys.CollectionKeysById(
            collectionRows.Select(c => (c.Id, c.Name, ParentId: c.ParentCollectionId)).ToList());

        // In the selected scope the ancestors of each chosen collection travel too, so the
        // receiving side can rebuild the same hierarchy (and the same keys).
        HashSet<long>? exportedCollectionIds = null;
        if (selectedCollectionIds is not null)
        {
            var parents = collectionRows.ToDictionary(c => c.Id, c => c.ParentCollectionId);
            exportedCollectionIds = new HashSet<long>();
            foreach (var id in selectedCollectionIds)
            {
                var current = (long?)id;
                while (current is { } node && parents.ContainsKey(node) && exportedCollectionIds.Add(node))
                    current = parents[node];
            }
        }

        // ---- Documents ----
        var docsQuery = _db.Documents.AsNoTracking().Where(d => d.ImportedAt >= cutoff);

        if (selectedCollectionIds is not null)
            docsQuery = docsQuery.Where(d => d.DocumentCollections.Any(dc => selectedCollectionIds.Contains(dc.CollectionId)));

        foreach (var doc in await docsQuery.ToListAsync(ct).ConfigureAwait(false))
        {
            changes.Add(new SyncChange
            {
                EntityType = nameof(DocumentEntity),
                EntityId = doc.Id,
                ChangeType = cutoff == DateTime.MinValue ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp = doc.ImportedAt,
                NaturalKey = SyncNaturalKeys.ForDocument(doc.ContentHash, doc.FilePath, doc.FileName),
                SerializedData = JsonSerializer.Serialize(doc, JsonOptions),
            });
        }

        // ---- Collections ----
        var colQuery = _db.Collections.AsNoTracking().Where(c => c.UpdatedAt >= cutoff);
        if (exportedCollectionIds is not null)
            colQuery = colQuery.Where(c => exportedCollectionIds.Contains(c.Id));

        foreach (var col in await colQuery.ToListAsync(ct).ConfigureAwait(false))
        {
            var key = collectionKeys.TryGetValue(col.Id, out var pathKey) ? pathKey : col.Name;
            var parentKey = col.ParentCollectionId is { } parentId && collectionKeys.TryGetValue(parentId, out var pk)
                ? pk
                : null;

            changes.Add(new SyncChange
            {
                EntityType = nameof(CollectionEntity),
                EntityId = col.Id,
                ChangeType = col.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp = col.UpdatedAt,
                NaturalKey = key,
                References = parentKey is null ? null : new Dictionary<string, string> { [ParentCollectionReference] = parentKey },
                SerializedData = JsonSerializer.Serialize(col, JsonOptions),
            });
        }

        // Tags, conversations and system prompts belong to no collection, so the selected
        // scope (which promises to sync only the chosen collections) leaves them out.
        if (!scoped)
        {
            // ---- Tags ----
            foreach (var tag in await _db.Tags.AsNoTracking().Where(t => t.CreatedAt >= cutoff).ToListAsync(ct).ConfigureAwait(false))
            {
                changes.Add(new SyncChange
                {
                    EntityType = nameof(TagEntity),
                    EntityId = tag.Id,
                    ChangeType = SyncChangeType.Created,
                    Timestamp = tag.CreatedAt,
                    NaturalKey = tag.Name,
                    SerializedData = JsonSerializer.Serialize(tag, JsonOptions),
                });
            }

            // ---- Conversations ----
            var conversations = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.UpdatedAt >= cutoff)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var parentConversationIds = conversations
                .Where(c => c.ParentConversationId.HasValue)
                .Select(c => c.ParentConversationId!.Value)
                .Distinct()
                .ToList();

            var parentConversationKeys = parentConversationIds.Count == 0
                ? new Dictionary<long, string>()
                : (await _db.Conversations
                    .AsNoTracking()
                    .Where(c => parentConversationIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.CreatedAt, c.Title })
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                    .ToDictionary(c => c.Id, c => SyncNaturalKeys.ForConversation(c.CreatedAt, c.Title));

            foreach (var conv in conversations)
            {
                var parentKey = conv.ParentConversationId is { } parentId && parentConversationKeys.TryGetValue(parentId, out var pk)
                    ? pk
                    : null;

                changes.Add(new SyncChange
                {
                    EntityType = nameof(ConversationEntity),
                    EntityId = conv.Id,
                    ChangeType = conv.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                    Timestamp = conv.UpdatedAt,
                    NaturalKey = SyncNaturalKeys.ForConversation(conv.CreatedAt, conv.Title),
                    References = parentKey is null ? null : new Dictionary<string, string> { [ParentConversationReference] = parentKey },
                    SerializedData = JsonSerializer.Serialize(conv, JsonOptions),
                });
            }
        }

        // ---- Annotations ----
        // Only annotations on documents that are in scope; in the selected scope that is every
        // document in a chosen collection, whether or not the document itself changed.
        var annotationQuery =
            from a in _db.Annotations.AsNoTracking()
            join d in _db.Documents.AsNoTracking() on a.DocumentId equals d.Id
            where a.UpdatedAt >= cutoff
            select new { Annotation = a, Document = d };

        if (selectedCollectionIds is not null)
        {
            annotationQuery = annotationQuery.Where(row =>
                _db.DocumentCollections.Any(dc =>
                    dc.DocumentId == row.Document.Id && selectedCollectionIds.Contains(dc.CollectionId)));
        }

        var annotationRows = await annotationQuery
            .Select(row => new
            {
                row.Annotation,
                row.Document.ContentHash,
                row.Document.FilePath,
                row.Document.FileName,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in annotationRows)
        {
            var ann = row.Annotation;
            var documentKey = SyncNaturalKeys.ForDocument(row.ContentHash, row.FilePath, row.FileName);

            changes.Add(new SyncChange
            {
                EntityType = nameof(AnnotationEntity),
                EntityId = ann.Id,
                ChangeType = ann.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                Timestamp = ann.UpdatedAt,
                NaturalKey = SyncNaturalKeys.ForAnnotation(documentKey, ann.StartOffset, ann.EndOffset, ann.HighlightedText),
                References = new Dictionary<string, string> { [DocumentReference] = documentKey },
                SerializedData = JsonSerializer.Serialize(ann, JsonOptions),
            });
        }

        // ---- System prompts ----
        if (!scoped)
        {
            foreach (var prompt in await _db.SystemPrompts.AsNoTracking().Where(sp => sp.UpdatedAt >= cutoff).ToListAsync(ct).ConfigureAwait(false))
            {
                changes.Add(new SyncChange
                {
                    EntityType = nameof(SystemPromptEntity),
                    EntityId = prompt.Id,
                    ChangeType = prompt.CreatedAt > cutoff ? SyncChangeType.Created : SyncChangeType.Updated,
                    Timestamp = prompt.UpdatedAt,
                    NaturalKey = prompt.Name,
                    SerializedData = JsonSerializer.Serialize(prompt, JsonOptions),
                });
            }
        }

        _log.Debug("SyncService.CollectChangesAsync: collected {Count} change(s)", changes.Count);

        return changes;
    }

    // ---- Private: change application ----

    /// <summary>Per-import state shared across the changes of one change set.</summary>
    private sealed class ImportContext
    {
        public required string LocalDeviceId { get; init; }

        public required string RemoteDeviceId { get; init; }

        /// <summary>Apply regardless of last writer wins (explicit conflict resolution).</summary>
        public bool Force { get; init; }

        /// <summary>Local collection id by natural key; built on first use, kept current on insert.</summary>
        public Dictionary<string, long>? CollectionIdsByKey { get; set; }
    }

    private enum ChangeOutcome
    {
        Applied,
        Unchanged,
        KeptLocal,
    }

    /// <summary>A change that can never be applied; counted as rejected, not retried.</summary>
    private sealed class RejectedChangeException(string message) : Exception(message);

    private async Task<SyncImportResult> ApplyChangesAsync(
        IReadOnlyList<SyncChange> changes,
        ImportContext context,
        CancellationToken ct)
    {
        var result = new SyncImportResult();

        foreach (var change in OrderForApply(changes))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (await ProcessChangeAsync(change, context, ct).ConfigureAwait(false))
                {
                    case ChangeOutcome.Applied:
                        result.Applied++;
                        break;
                    case ChangeOutcome.Unchanged:
                        result.Unchanged++;
                        break;
                    case ChangeOutcome.KeptLocal:
                        result.ConflictsResolved++;
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (RejectedChangeException ex)
            {
                result.Rejected++;
                result.Errors.Add($"{Describe(change)} was skipped: {ex.Message}");
                _log.Warning(
                    "SyncService.ImportChangesAsync: rejected {EntityType} (remote Id={EntityId}): {Reason}",
                    change.EntityType, change.EntityId, ex.Message);
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{Describe(change)} could not be saved: {ex.GetBaseException().Message}");
                _log.Warning(ex,
                    "SyncService.ImportChangesAsync: failed to apply {EntityType} (remote Id={EntityId}); " +
                    "rolled back, will be retried",
                    change.EntityType, change.EntityId);
            }
        }

        return result;
    }

    /// <summary>
    /// Upserts go parents-first (tags and collections before documents, documents before
    /// annotations, root collections and conversations before their children); deletions run
    /// after every upsert, children first.
    /// </summary>
    private static IEnumerable<SyncChange> OrderForApply(IReadOnlyList<SyncChange> changes)
    {
        static int Rank(string entityType) => entityType switch
        {
            nameof(TagEntity) => 0,
            nameof(CollectionEntity) => 1,
            nameof(DocumentEntity) => 2,
            nameof(AnnotationEntity) => 3,
            nameof(ConversationEntity) => 4,
            nameof(SystemPromptEntity) => 5,
            _ => 6,
        };

        static int Depth(SyncChange change) => change.EntityType switch
        {
            nameof(CollectionEntity) => SyncNaturalKeys.CollectionDepth(change.NaturalKey),
            nameof(ConversationEntity) => change.References?.ContainsKey(ParentConversationReference) == true ? 1 : 0,
            _ => 0,
        };

        var upserts = changes
            .Where(c => c.ChangeType != SyncChangeType.Deleted)
            .OrderBy(c => Rank(c.EntityType))
            .ThenBy(Depth);

        var deletions = changes
            .Where(c => c.ChangeType == SyncChangeType.Deleted)
            .OrderByDescending(c => Rank(c.EntityType))
            .ThenByDescending(Depth);

        return upserts.Concat(deletions);
    }

    private async Task<ChangeOutcome> ProcessChangeAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        if (change.ChangeType == SyncChangeType.Deleted)
            return await ApplyDeletionAsync(change, context, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(change.SerializedData))
            throw new RejectedChangeException("the change carries no data");

        return change.EntityType switch
        {
            nameof(DocumentEntity) => await UpsertDocumentAsync(change, ct).ConfigureAwait(false),
            nameof(CollectionEntity) => await UpsertCollectionAsync(change, context, ct).ConfigureAwait(false),
            nameof(TagEntity) => await UpsertTagAsync(change, ct).ConfigureAwait(false),
            nameof(ConversationEntity) => await UpsertConversationAsync(change, context, ct).ConfigureAwait(false),
            nameof(AnnotationEntity) => await UpsertAnnotationAsync(change, context, ct).ConfigureAwait(false),
            nameof(SystemPromptEntity) => await UpsertSystemPromptAsync(change, context, ct).ConfigureAwait(false),
            _ => throw new RejectedChangeException($"unrecognised entity type '{change.EntityType}'"),
        };
    }

    // Documents carry no modification time and their path, index state and chunks belong to
    // the machine that indexed them, so a matching local document is kept and only descriptive
    // fields it lacks are filled in. A new document is inserted as not yet indexed: pending when
    // the file exists at the same path here, failed with an explanation otherwise.
    private async Task<ChangeOutcome> UpsertDocumentAsync(SyncChange change, CancellationToken ct)
    {
        var incoming = Deserialize<DocumentEntity>(change);
        var key = change.NaturalKey ?? SyncNaturalKeys.ForDocument(incoming.ContentHash, incoming.FilePath, incoming.FileName);
        var localId = await FindDocumentIdAsync(key, ct).ConfigureAwait(false);

        if (localId is null)
        {
            var fileAvailable = IsFileAvailableLocally(incoming.FilePath);
            var entity = new DocumentEntity
            {
                FileName = incoming.FileName,
                FilePath = incoming.FilePath,
                FileType = incoming.FileType,
                MimeType = incoming.MimeType,
                FileSizeBytes = incoming.FileSizeBytes,
                ContentHash = incoming.ContentHash,
                ImportedAt = incoming.ImportedAt,
                FileModifiedAt = incoming.FileModifiedAt,
                LastIndexedAt = null,
                IndexingStatus = fileAvailable ? "pending" : "failed",
                IndexingError = fileAvailable ? null : MissingFileIndexingError,
                ChunkCount = 0,
                PageCount = incoming.PageCount,
                WordCount = incoming.WordCount,
                Summary = incoming.Summary,
                ExtractedTitle = incoming.ExtractedTitle,
                Language = incoming.Language,
                ThumbnailPath = null,
                MetadataJson = incoming.MetadataJson,
            };

            var work = new TrackedWork();
            work.Track(_db.Documents.Add(entity), release: true);
            await SaveAsync(work, ct).ConfigureAwait(false);
            return ChangeOutcome.Applied;
        }

        var current = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == localId.Value)
            .Select(d => new { d.Summary, d.ExtractedTitle, d.Language })
            .FirstAsync(ct)
            .ConfigureAwait(false);

        var fillSummary = string.IsNullOrWhiteSpace(current.Summary) && !string.IsNullOrWhiteSpace(incoming.Summary);
        var fillTitle = string.IsNullOrWhiteSpace(current.ExtractedTitle) && !string.IsNullOrWhiteSpace(incoming.ExtractedTitle);
        var fillLanguage = string.IsNullOrWhiteSpace(current.Language) && !string.IsNullOrWhiteSpace(incoming.Language);

        if (!fillSummary && !fillTitle && !fillLanguage)
            return ChangeOutcome.Unchanged;

        var (local, wasTracked) = await LoadForUpdateAsync<DocumentEntity>(localId.Value, ct).ConfigureAwait(false);
        if (fillSummary) local.Summary = incoming.Summary;
        if (fillTitle) local.ExtractedTitle = incoming.ExtractedTitle;
        if (fillLanguage) local.Language = incoming.Language;

        var update = new TrackedWork();
        update.Track(_db.Entry(local), release: !wasTracked);
        await SaveAsync(update, ct).ConfigureAwait(false);
        return ChangeOutcome.Applied;
    }

    private async Task<ChangeOutcome> UpsertCollectionAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        var incoming = Deserialize<CollectionEntity>(change);
        var key = change.NaturalKey ?? SyncNaturalKeys.ForCollectionPath([incoming.Name]);
        var index = await GetCollectionIndexAsync(context, ct).ConfigureAwait(false);

        if (!index.TryGetValue(key, out var localId))
        {
            var work = new TrackedWork();
            var parent = ResolveOrCreateCollectionParent(key, incoming, index, work);

            var entity = new CollectionEntity
            {
                Name = incoming.Name,
                Description = incoming.Description,
                IconGlyph = incoming.IconGlyph,
                ColorHex = incoming.ColorHex,
                ParentCollectionId = parent.ParentId,
                ParentCollection = parent.Placeholder,
                CreatedAt = incoming.CreatedAt,
                UpdatedAt = incoming.UpdatedAt,
                DocumentCount = 0, // memberships are not synced; the local count starts empty
                SortOrder = incoming.SortOrder,
            };

            work.Track(_db.Collections.Add(entity), release: true);
            await SaveAsync(work, ct).ConfigureAwait(false);

            foreach (var (placeholderKey, placeholder) in parent.CreatedAncestors)
                index[placeholderKey] = placeholder.Id;
            index[key] = entity.Id;
            return ChangeOutcome.Applied;
        }

        var localUpdatedAt = await _db.Collections
            .AsNoTracking()
            .Where(c => c.Id == localId)
            .Select(c => (DateTime?)c.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var decision = Decide(change, context, localUpdatedAt);
        if (decision != SyncDecision.ApplyRemote)
            return Outcome(decision);

        var (local, wasTracked) = await LoadForUpdateAsync<CollectionEntity>(localId, ct).ConfigureAwait(false);
        local.Description = incoming.Description;
        local.IconGlyph = incoming.IconGlyph;
        local.ColorHex = incoming.ColorHex;
        local.SortOrder = incoming.SortOrder;
        local.UpdatedAt = incoming.UpdatedAt;

        var update = new TrackedWork();
        update.Track(_db.Entry(local), release: !wasTracked);
        await SaveAsync(update, ct).ConfigureAwait(false);
        return ChangeOutcome.Applied;
    }

    /// <summary>Parent of a collection being inserted, plus any ancestors created for it.</summary>
    private sealed record CollectionParent(
        long? ParentId,
        CollectionEntity? Placeholder,
        List<(string Key, CollectionEntity Entity)> CreatedAncestors);

    /// <summary>
    /// Finds the local parent of a collection by its key path. Ancestors missing here (deleted
    /// locally, or outside the sender's scope) are recreated by name so the collection keeps its
    /// place in the hierarchy and its key, which later updates rely on.
    /// </summary>
    private CollectionParent ResolveOrCreateCollectionParent(
        string key,
        CollectionEntity incoming,
        Dictionary<string, long> index,
        TrackedWork work)
    {
        var created = new List<(string Key, CollectionEntity Entity)>();
        var parentKey = SyncNaturalKeys.ParentCollectionKey(key);
        if (parentKey is null)
            return new CollectionParent(null, null, created);

        if (index.TryGetValue(parentKey, out var parentId))
            return new CollectionParent(parentId, null, created);

        // Walk up to the nearest ancestor that exists, then create the chain below it.
        var missing = new Stack<string>();
        var cursor = parentKey;
        long? existingAncestorId = null;
        while (cursor is not null)
        {
            if (index.TryGetValue(cursor, out var id))
            {
                existingAncestorId = id;
                break;
            }

            missing.Push(cursor);
            cursor = SyncNaturalKeys.ParentCollectionKey(cursor);
        }

        CollectionEntity? previous = null;
        while (missing.Count > 0)
        {
            var ancestorKey = missing.Pop();
            var name = ancestorKey[(ancestorKey.LastIndexOf(SyncNaturalKeys.Separator) + 1)..];
            var placeholder = new CollectionEntity
            {
                Name = name,
                ParentCollectionId = previous is null ? existingAncestorId : null,
                ParentCollection = previous,
                // Stamped with the child's creation time, so a real update of the ancestor
                // arriving later is normally newer and replaces these defaults.
                CreatedAt = incoming.CreatedAt,
                UpdatedAt = incoming.CreatedAt,
            };

            work.Track(_db.Collections.Add(placeholder), release: true);
            created.Add((ancestorKey, placeholder));
            previous = placeholder;
        }

        _log.Information(
            "SyncService: recreated {Count} missing ancestor collection(s) for '{Collection}'",
            created.Count, incoming.Name);

        return new CollectionParent(null, previous, created);
    }

    private async Task<ChangeOutcome> UpsertTagAsync(SyncChange change, CancellationToken ct)
    {
        var incoming = Deserialize<TagEntity>(change);
        var name = change.NaturalKey ?? incoming.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new RejectedChangeException("the tag has no name");

        var local = await _db.Tags
            .AsNoTracking()
            .Where(t => t.Name == name)
            .Select(t => new { t.Id, t.ColorHex })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (local is null)
        {
            var work = new TrackedWork();
            work.Track(_db.Tags.Add(new TagEntity
            {
                Name = name,
                ColorHex = incoming.ColorHex,
                IsAutoGenerated = incoming.IsAutoGenerated,
                CreatedAt = incoming.CreatedAt,
            }), release: true);
            await SaveAsync(work, ct).ConfigureAwait(false);
            return ChangeOutcome.Applied;
        }

        // Tags carry no modification time; the local tag is kept and only a missing colour
        // is filled in.
        if (!string.IsNullOrWhiteSpace(local.ColorHex) || string.IsNullOrWhiteSpace(incoming.ColorHex))
            return ChangeOutcome.Unchanged;

        var (tag, wasTracked) = await LoadForUpdateAsync<TagEntity>(local.Id, ct).ConfigureAwait(false);
        tag.ColorHex = incoming.ColorHex;

        var update = new TrackedWork();
        update.Track(_db.Entry(tag), release: !wasTracked);
        await SaveAsync(update, ct).ConfigureAwait(false);
        return ChangeOutcome.Applied;
    }

    // Messages are not part of the sync payload, so a conversation created here starts with no
    // messages and a zero count; an existing conversation keeps its own messages, counts and
    // branch links, and only its descriptive fields follow the newer side.
    private async Task<ChangeOutcome> UpsertConversationAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        var incoming = Deserialize<ConversationEntity>(change);
        var key = change.NaturalKey ?? SyncNaturalKeys.ForConversation(incoming.CreatedAt, incoming.Title);
        var local = await FindConversationAsync(key, ct).ConfigureAwait(false);

        if (local is null)
        {
            long? parentId = null;
            if (change.References is { } references
                && references.TryGetValue(ParentConversationReference, out var parentKey)
                && !string.IsNullOrWhiteSpace(parentKey))
            {
                parentId = (await FindConversationAsync(parentKey, ct).ConfigureAwait(false))?.EntityId;
            }

            var work = new TrackedWork();
            work.Track(_db.Conversations.Add(new ConversationEntity
            {
                Title = incoming.Title,
                SystemPrompt = incoming.SystemPrompt,
                ModelId = incoming.ModelId ?? string.Empty,
                CreatedAt = incoming.CreatedAt,
                UpdatedAt = incoming.UpdatedAt,
                IsPinned = incoming.IsPinned,
                IsArchived = incoming.IsArchived,
                MessageCount = 0,
                TokensUsed = 0,
                FolderName = incoming.FolderName,
                ParentConversationId = parentId,
                BranchPointMessageId = null, // message ids are local to each installation
                BranchLabel = incoming.BranchLabel,
            }), release: true);
            await SaveAsync(work, ct).ConfigureAwait(false);
            return ChangeOutcome.Applied;
        }

        var decision = Decide(change, context, local.ModifiedAt);
        if (decision != SyncDecision.ApplyRemote)
            return Outcome(decision);

        var (conversation, wasTracked) = await LoadForUpdateAsync<ConversationEntity>(local.EntityId, ct).ConfigureAwait(false);
        conversation.Title = incoming.Title;
        conversation.SystemPrompt = incoming.SystemPrompt;
        conversation.ModelId = incoming.ModelId ?? string.Empty;
        conversation.IsPinned = incoming.IsPinned;
        conversation.IsArchived = incoming.IsArchived;
        conversation.FolderName = incoming.FolderName;
        conversation.BranchLabel = incoming.BranchLabel;
        conversation.UpdatedAt = incoming.UpdatedAt;

        var update = new TrackedWork();
        update.Track(_db.Entry(conversation), release: !wasTracked);
        await SaveAsync(update, ct).ConfigureAwait(false);
        return ChangeOutcome.Applied;
    }

    private async Task<ChangeOutcome> UpsertAnnotationAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        var incoming = Deserialize<AnnotationEntity>(change);

        // The sender's document id means nothing here; only its natural key can be translated.
        string? documentKey = null;
        change.References?.TryGetValue(DocumentReference, out documentKey);
        if (string.IsNullOrWhiteSpace(documentKey))
            throw new RejectedChangeException("the annotation does not identify its document (file from an older version)");

        var documentId = await FindDocumentIdAsync(documentKey, ct).ConfigureAwait(false)
            ?? throw new RejectedChangeException("its document is not present on this machine");

        var local = await FindAnnotationAsync(documentId, incoming.StartOffset, incoming.EndOffset, incoming.HighlightedText, ct)
            .ConfigureAwait(false);

        if (local is null)
        {
            var work = new TrackedWork();
            work.Track(_db.Annotations.Add(new AnnotationEntity
            {
                DocumentId = documentId,
                ChunkId = null, // chunk ids are local to each installation
                StartOffset = incoming.StartOffset,
                EndOffset = incoming.EndOffset,
                HighlightedText = incoming.HighlightedText,
                NoteText = incoming.NoteText,
                Color = string.IsNullOrWhiteSpace(incoming.Color) ? "yellow" : incoming.Color,
                CreatedAt = incoming.CreatedAt,
                UpdatedAt = incoming.UpdatedAt,
            }), release: true);
            await SaveAsync(work, ct).ConfigureAwait(false);
            return ChangeOutcome.Applied;
        }

        var decision = Decide(change, context, local.ModifiedAt);
        if (decision != SyncDecision.ApplyRemote)
            return Outcome(decision);

        var (annotation, wasTracked) = await LoadForUpdateAsync<AnnotationEntity>(local.EntityId, ct).ConfigureAwait(false);
        annotation.NoteText = incoming.NoteText;
        annotation.Color = string.IsNullOrWhiteSpace(incoming.Color) ? annotation.Color : incoming.Color;
        annotation.UpdatedAt = incoming.UpdatedAt;

        var update = new TrackedWork();
        update.Track(_db.Entry(annotation), release: !wasTracked);
        await SaveAsync(update, ct).ConfigureAwait(false);
        return ChangeOutcome.Applied;
    }

    private async Task<ChangeOutcome> UpsertSystemPromptAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        var incoming = Deserialize<SystemPromptEntity>(change);
        var name = change.NaturalKey ?? incoming.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new RejectedChangeException("the system prompt has no name");

        var local = await FindSystemPromptAsync(name, ct).ConfigureAwait(false);

        if (local is null)
        {
            var work = new TrackedWork();
            work.Track(_db.SystemPrompts.Add(new SystemPromptEntity
            {
                Name = name,
                Content = incoming.Content,
                Category = string.IsNullOrWhiteSpace(incoming.Category) ? "General" : incoming.Category,
                IsBuiltIn = incoming.IsBuiltIn,
                IsFavorite = incoming.IsFavorite,
                CreatedAt = incoming.CreatedAt,
                UpdatedAt = incoming.UpdatedAt,
                UsageCount = 0, // usage statistics are local
            }), release: true);
            await SaveAsync(work, ct).ConfigureAwait(false);
            return ChangeOutcome.Applied;
        }

        var decision = Decide(change, context, local.ModifiedAt);
        if (decision != SyncDecision.ApplyRemote)
            return Outcome(decision);

        var (prompt, wasTracked) = await LoadForUpdateAsync<SystemPromptEntity>(local.EntityId, ct).ConfigureAwait(false);
        prompt.Content = incoming.Content;
        prompt.Category = string.IsNullOrWhiteSpace(incoming.Category) ? prompt.Category : incoming.Category;
        prompt.IsFavorite = incoming.IsFavorite;
        prompt.UpdatedAt = incoming.UpdatedAt;

        var update = new TrackedWork();
        update.Track(_db.Entry(prompt), release: !wasTracked);
        await SaveAsync(update, ct).ConfigureAwait(false);
        return ChangeOutcome.Applied;
    }

    /// <summary>
    /// Deletes the local entity the change's natural key identifies. A deletion without a
    /// natural key (older file format) is rejected rather than applied by numeric id.
    /// </summary>
    private async Task<ChangeOutcome> ApplyDeletionAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(change.NaturalKey))
            throw new RejectedChangeException("the deletion does not identify the entity by natural key");

        var local = await TryLocateLocalVersionAsync(change, context, ct).ConfigureAwait(false);
        if (local is null)
            return ChangeOutcome.Unchanged;

        // Types without a modification time (documents, tags) have nothing to weigh the
        // deletion against, so it is applied.
        if (local.ModifiedAt is not null)
        {
            var decision = Decide(change, context, local.ModifiedAt);
            if (decision != SyncDecision.ApplyRemote)
                return Outcome(decision);
        }

        var work = new TrackedWork();
        switch (change.EntityType)
        {
            case nameof(DocumentEntity): await StageRemovalAsync<DocumentEntity>(local.EntityId, work, ct).ConfigureAwait(false); break;
            case nameof(CollectionEntity): await StageRemovalAsync<CollectionEntity>(local.EntityId, work, ct).ConfigureAwait(false); break;
            case nameof(TagEntity): await StageRemovalAsync<TagEntity>(local.EntityId, work, ct).ConfigureAwait(false); break;
            case nameof(ConversationEntity): await StageRemovalAsync<ConversationEntity>(local.EntityId, work, ct).ConfigureAwait(false); break;
            case nameof(AnnotationEntity): await StageRemovalAsync<AnnotationEntity>(local.EntityId, work, ct).ConfigureAwait(false); break;
            case nameof(SystemPromptEntity): await StageRemovalAsync<SystemPromptEntity>(local.EntityId, work, ct).ConfigureAwait(false); break;
            default:
                throw new RejectedChangeException($"unrecognised entity type '{change.EntityType}'");
        }

        await SaveAsync(work, ct).ConfigureAwait(false);

        if (change.EntityType == nameof(CollectionEntity) && context.CollectionIdsByKey is { } index)
            index.Remove(change.NaturalKey);

        return ChangeOutcome.Applied;
    }

    private async Task StageRemovalAsync<TEntity>(long id, TrackedWork work, CancellationToken ct)
        where TEntity : class
    {
        var (entity, wasTracked) = await LoadForUpdateAsync<TEntity>(id, ct).ConfigureAwait(false);
        work.Track(_db.Set<TEntity>().Remove(entity), release: !wasTracked);
    }

    // ---- Private: local matching by natural key ----

    /// <summary>
    /// Finds the local copy of the entity a change refers to, for conflict detection and
    /// deletions. Returns null when no local entity matches or the change cannot be keyed.
    /// </summary>
    private async Task<SyncLocalVersion?> TryLocateLocalVersionAsync(SyncChange change, ImportContext context, CancellationToken ct)
    {
        try
        {
            switch (change.EntityType)
            {
                case nameof(DocumentEntity):
                    {
                        var key = change.NaturalKey ?? KeyFromPayload<DocumentEntity>(change, d => SyncNaturalKeys.ForDocument(d.ContentHash, d.FilePath, d.FileName));
                        var id = key is null ? null : await FindDocumentIdAsync(key, ct).ConfigureAwait(false);
                        return id is null ? null : new SyncLocalVersion(id.Value, null);
                    }
                case nameof(CollectionEntity):
                    {
                        var key = change.NaturalKey ?? KeyFromPayload<CollectionEntity>(change, c => SyncNaturalKeys.ForCollectionPath([c.Name]));
                        var index = await GetCollectionIndexAsync(context, ct).ConfigureAwait(false);
                        if (key is null || !index.TryGetValue(key, out var id))
                            return null;
                        var updatedAt = await _db.Collections.AsNoTracking().Where(c => c.Id == id)
                            .Select(c => (DateTime?)c.UpdatedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                        return updatedAt is null ? null : new SyncLocalVersion(id, updatedAt);
                    }
                case nameof(TagEntity):
                    {
                        var name = change.NaturalKey ?? KeyFromPayload<TagEntity>(change, t => t.Name);
                        var id = name is null ? null : await _db.Tags.AsNoTracking().Where(t => t.Name == name)
                            .Select(t => (long?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                        return id is null ? null : new SyncLocalVersion(id.Value, null);
                    }
                case nameof(ConversationEntity):
                    {
                        var key = change.NaturalKey ?? KeyFromPayload<ConversationEntity>(change, c => SyncNaturalKeys.ForConversation(c.CreatedAt, c.Title));
                        return key is null ? null : await FindConversationAsync(key, ct).ConfigureAwait(false);
                    }
                case nameof(AnnotationEntity):
                    {
                        if (SyncNaturalKeys.TryParseAnnotation(change.NaturalKey, out var documentKey, out var start, out var end, out var text))
                        {
                            var documentId = await FindDocumentIdAsync(documentKey, ct).ConfigureAwait(false);
                            return documentId is null ? null : await FindAnnotationAsync(documentId.Value, start, end, text, ct).ConfigureAwait(false);
                        }

                        return null;
                    }
                case nameof(SystemPromptEntity):
                    {
                        var name = change.NaturalKey ?? KeyFromPayload<SystemPromptEntity>(change, p => p.Name);
                        return name is null ? null : await FindSystemPromptAsync(name, ct).ConfigureAwait(false);
                    }
                default:
                    return null;
            }
        }
        catch (RejectedChangeException)
        {
            return null;
        }
    }

    private async Task<long?> FindDocumentIdAsync(string key, CancellationToken ct)
    {
        if (!SyncNaturalKeys.TryParseDocument(key, out var contentHash, out var filePath, out var fileName))
            return null;

        var query = _db.Documents.AsNoTracking();
        query = contentHash is not null
            ? query.Where(d => d.ContentHash == contentHash)
            : query.Where(d => d.FilePath == filePath && d.FileName == fileName);

        return await query
            .OrderBy(d => d.Id)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Matches a conversation by creation time (exact to the tick) and, when several local
    /// conversations share that instant, by title. An ambiguous match counts as no match, so
    /// the incoming conversation is inserted rather than overwriting the wrong one.
    /// </summary>
    private async Task<SyncLocalVersion?> FindConversationAsync(string key, CancellationToken ct)
    {
        if (!SyncNaturalKeys.TryParseConversation(key, out var createdAt, out var title))
            return null;

        // A small window keeps the query on the CreatedAt index; exact ticks are compared here.
        var from = createdAt.AddMilliseconds(-1);
        var to = createdAt.AddMilliseconds(1);
        var candidates = (await _db.Conversations
                .AsNoTracking()
                .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
                .Select(c => new { c.Id, c.CreatedAt, c.Title, c.UpdatedAt })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(c => c.CreatedAt.Ticks == createdAt.Ticks)
            .OrderBy(c => c.Id)
            .ToList();

        var match = candidates.Count == 1
            ? candidates[0]
            : candidates.FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.Ordinal));

        return match is null ? null : new SyncLocalVersion(match.Id, match.UpdatedAt);
    }

    private async Task<SyncLocalVersion?> FindAnnotationAsync(
        long documentId,
        int startOffset,
        int endOffset,
        string? highlightedText,
        CancellationToken ct)
    {
        var text = highlightedText ?? string.Empty;
        var match = await _db.Annotations
            .AsNoTracking()
            .Where(a => a.DocumentId == documentId
                        && a.StartOffset == startOffset
                        && a.EndOffset == endOffset
                        && a.HighlightedText == text)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.UpdatedAt })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return match is null ? null : new SyncLocalVersion(match.Id, match.UpdatedAt);
    }

    private async Task<SyncLocalVersion?> FindSystemPromptAsync(string name, CancellationToken ct)
    {
        var match = await _db.SystemPrompts
            .AsNoTracking()
            .Where(p => p.Name == name)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.UpdatedAt })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return match is null ? null : new SyncLocalVersion(match.Id, match.UpdatedAt);
    }

    /// <summary>Local collection ids by name-path key (lowest id wins when two share a path).</summary>
    private async Task<Dictionary<string, long>> GetCollectionIndexAsync(ImportContext context, CancellationToken ct)
    {
        if (context.CollectionIdsByKey is { } cached)
            return cached;

        var rows = await _db.Collections
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ParentCollectionId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var keysById = SyncNaturalKeys.CollectionKeysById(
            rows.Select(c => (c.Id, c.Name, ParentId: c.ParentCollectionId)).ToList());

        var index = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (id, key) in keysById.OrderBy(pair => pair.Key))
            index.TryAdd(key, id);

        context.CollectionIdsByKey = index;
        return index;
    }

    // ---- Private: change helpers ----

    private SyncDecision Decide(SyncChange change, ImportContext context, DateTime? localModifiedAt) =>
        context.Force
            ? SyncDecision.ApplyRemote
            : _conflictResolver.Decide(change, context.RemoteDeviceId, localModifiedAt, context.LocalDeviceId);

    private static ChangeOutcome Outcome(SyncDecision decision) => decision switch
    {
        SyncDecision.ApplyRemote => ChangeOutcome.Applied,
        SyncDecision.KeepLocal => ChangeOutcome.KeptLocal,
        _ => ChangeOutcome.Unchanged,
    };

    private static TEntity Deserialize<TEntity>(SyncChange change)
        where TEntity : class
    {
        try
        {
            return JsonSerializer.Deserialize<TEntity>(change.SerializedData!, JsonOptions)
                   ?? throw new RejectedChangeException("the change data is empty");
        }
        catch (JsonException ex)
        {
            throw new RejectedChangeException($"the change data could not be read ({ex.Message})");
        }
    }

    private static string? KeyFromPayload<TEntity>(SyncChange change, Func<TEntity, string?> keyOf)
        where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(change.SerializedData))
            return null;

        var entity = Deserialize<TEntity>(change);
        var key = keyOf(entity);
        return string.IsNullOrEmpty(key) ? null : key;
    }

    private static string Describe(SyncChange change) =>
        $"{change.EntityType.Replace("Entity", string.Empty, StringComparison.Ordinal)} (remote id {change.EntityId})";

    private static string? DescribeProblems(SyncImportResult result)
    {
        if (result.Failed == 0 && result.Rejected == 0)
            return null;

        var parts = new List<string>();
        if (result.Failed > 0)
            parts.Add($"{result.Failed} change(s) could not be saved and will be retried on the next sync");
        if (result.Rejected > 0)
            parts.Add($"{result.Rejected} change(s) could not be applied and were skipped");

        return string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// A synced document is only queued for indexing when its file exists at the same path
    /// on this machine. Network paths are not probed during import (an unreachable share could
    /// stall the pass); they are left for the indexer to try.
    /// </summary>
    private static bool IsFileAvailableLocally(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (filePath.StartsWith(@"\\", StringComparison.Ordinal) || filePath.StartsWith("//", StringComparison.Ordinal))
            return true;

        try
        {
            return File.Exists(filePath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---- Private: tracked work on the shared context ----

    /// <summary>
    /// The entries one unit of work touched on the shared context, so a failed save can be
    /// undone (instead of leaving invalid entries that make every later save fail) and a
    /// successful one releases what it loaded or created (so the context does not grow with
    /// every synced row).
    /// </summary>
    private sealed class TrackedWork
    {
        private readonly List<(EntityEntry Entry, bool Release)> _entries = [];

        public void Track(EntityEntry entry, bool release) => _entries.Add((entry, release));

        public void RollBack()
        {
            foreach (var (entry, release) in _entries)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.State = EntityState.Detached;
                        continue;

                    case EntityState.Modified:
                    case EntityState.Deleted:
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                        break;
                }

                if (release && entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;
            }
        }

        public void Release()
        {
            foreach (var (entry, release) in _entries)
            {
                if (release && entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;
            }
        }
    }

    private async Task SaveAsync(TrackedWork work, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            work.RollBack();
            throw;
        }

        work.Release();
    }

    /// <summary>
    /// Loads an entity for modification, reusing the tracked instance when another part of the
    /// app already tracks it (so it is not detached from under that owner afterwards).
    /// </summary>
    private async Task<(TEntity Entity, bool WasTracked)> LoadForUpdateAsync<TEntity>(long id, CancellationToken ct)
        where TEntity : class
    {
        var tracked = _db.ChangeTracker.Entries<TEntity>()
            .FirstOrDefault(e => Equals(e.Property("Id").CurrentValue, id));

        if (tracked is not null)
            return (tracked.Entity, true);

        var entity = await _db.Set<TEntity>()
            .FirstOrDefaultAsync(e => EF.Property<long>(e, "Id") == id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} {id} was removed while it was being synced.");

        return (entity, false);
    }

    // ---- Private: device id, settings and persisted sync state ----

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

        var newId = Guid.NewGuid().ToString("N");
        await UpsertSettingAsync(DeviceIdKey, newId).ConfigureAwait(false);
        _cachedDeviceId = newId;

        _log.Information("SyncService.GetOrCreateDeviceIdAsync: generated new device ID {DeviceId}", newId);

        return _cachedDeviceId;
    }

    private async Task<string?> GetSettingAsync(string key)
    {
        var entity = await _db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key)
            .ConfigureAwait(false);

        return entity?.Value;
    }

    private async Task UpsertSettingAsync(string key, string value)
    {
        var entity = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.Key == key)
            .ConfigureAwait(false);

        var work = new TrackedWork();

        if (entity is null)
        {
            work.Track(_db.UserSettings.Add(new UserSettingsEntity
            {
                Key = key,
                Value = value,
                ValueType = "json",
                UpdatedAt = DateTime.UtcNow,
            }), release: false);
        }
        else
        {
            entity.Value = value;
            entity.ValueType = "json";
            entity.UpdatedAt = DateTime.UtcNow;
            work.Track(_db.Entry(entity), release: false);
        }

        await SaveAsync(work, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Watermarks and timestamps persisted across restarts. The export watermark is the time
    /// captured just before the last successful export collected its changes; peer entries
    /// record the newest change set fully imported from each device.
    /// </summary>
    private sealed class PersistedSyncState
    {
        public DateTime? ExportWatermarkUtc { get; set; }

        public DateTime? LastExportAtUtc { get; set; }

        public DateTime? LastImportAtUtc { get; set; }

        public Dictionary<string, PersistedPeerState> Peers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PersistedPeerState
    {
        public DateTime? LastExportedAtUtc { get; set; }

        public DateTime? LastImportedAtUtc { get; set; }
    }

    private async Task<PersistedSyncState> LoadSyncStateAsync()
    {
        var json = await GetSettingAsync(SyncStateKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return new PersistedSyncState();

        try
        {
            var state = JsonSerializer.Deserialize<PersistedSyncState>(json, JsonOptions) ?? new PersistedSyncState();
            state.Peers = new Dictionary<string, PersistedPeerState>(state.Peers ?? [], StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "SyncService: stored sync state is unreadable; starting from a full export");
            return new PersistedSyncState();
        }
    }

    private Task RecordExportAsync(DateTime? since, DateTime watermark) =>
        UpdateSyncStateAsync(state =>
        {
            // Advance only when this export covered everything since the stored watermark; an
            // explicit later 'since' skipped changes that still need to go out.
            if (since is null || state.ExportWatermarkUtc is null || since.Value <= state.ExportWatermarkUtc.Value)
                state.ExportWatermarkUtc = watermark;

            state.LastExportAtUtc = DateTime.UtcNow;
        });

    private Task RecordImportAsync() =>
        UpdateSyncStateAsync(state => state.LastImportAtUtc = DateTime.UtcNow);

    private Task RecordPeerImportsAsync(Dictionary<string, DateTime> newestByDevice) =>
        UpdateSyncStateAsync(state =>
        {
            var now = DateTime.UtcNow;

            foreach (var (deviceId, exportedAt) in newestByDevice)
            {
                if (!state.Peers.TryGetValue(deviceId, out var peer))
                {
                    peer = new PersistedPeerState();
                    state.Peers[deviceId] = peer;
                }

                if (peer.LastExportedAtUtc is null || exportedAt > peer.LastExportedAtUtc)
                    peer.LastExportedAtUtc = exportedAt;
                peer.LastImportedAtUtc = now;
            }
        });

    /// <summary>
    /// Read-modify-write of the persisted sync state. Not fatal on failure: the next export
    /// re-sends from the older watermark, which last writer wins makes harmless on the
    /// receiving side.
    /// </summary>
    private async Task UpdateSyncStateAsync(Action<PersistedSyncState> mutate)
    {
        try
        {
            var state = await LoadSyncStateAsync().ConfigureAwait(false);
            mutate(state);
            await UpsertSettingAsync(SyncStateKey, JsonSerializer.Serialize(state, JsonOptions)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SyncService: could not persist sync state");
        }
    }

    /// <summary>
    /// Restores <see cref="SyncStatus.LastSyncAt"/> from persisted state once per process, so
    /// the status reads correctly after a restart instead of "never".
    /// </summary>
    private async Task EnsurePersistedStatusLoadedAsync()
    {
        if (Interlocked.CompareExchange(ref _persistedStatusLoaded, 1, 0) != 0)
            return;

        try
        {
            var state = await LoadSyncStateAsync().ConfigureAwait(false);
            DateTime? last = state.LastExportAtUtc > state.LastImportAtUtc || state.LastImportAtUtc is null
                ? state.LastExportAtUtc
                : state.LastImportAtUtc;

            if (last is not null)
                SetStatus(s => s.LastSyncAt ??= last);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SyncService: could not load persisted sync status");
        }
    }

    private async Task<SyncConfiguration> GetRequiredConfigurationAsync()
    {
        var config = await GetConfigurationAsync().ConfigureAwait(false);

        if (config is null)
            throw new InvalidOperationException(
                "Collaborative Sync has not been configured. " +
                "Call ConfigureAsync before performing sync operations.");

        if (string.IsNullOrWhiteSpace(config.SyncFolderPath))
            throw new InvalidOperationException("SyncFolderPath is not set in the sync configuration.");

        if (string.IsNullOrWhiteSpace(config.EncryptionKey))
            throw new InvalidOperationException("EncryptionKey is not set in the sync configuration.");

        return config;
    }

    // ---- Private: sync log and status ----

    private async Task PersistLogAsync(SyncLogEntity entry, CancellationToken ct)
    {
        var tracked = _db.SyncLogs.Add(entry);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SyncService.PersistLogAsync: could not persist sync log entry");
        }
        finally
        {
            // Never leave the log row tracked: a row that failed to insert would otherwise be
            // retried (and fail) inside every later SaveChanges on the shared context.
            tracked.State = EntityState.Detached;
        }
    }

    private void SetStatus(Action<SyncStatus> mutate)
    {
        SyncStatus snapshot;

        lock (_statusLock)
        {
            mutate(_status);

            snapshot = new SyncStatus
            {
                LastSyncAt = _status.LastSyncAt,
                SyncState = _status.SyncState,
                ErrorMessage = _status.ErrorMessage,
                PendingChanges = _status.PendingChanges,
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
}

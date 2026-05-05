using System.Diagnostics;
using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Sync.Codec;
using AgentX.Core.Services.Sync.ConflictResolution;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Sync.Transport;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Sync;

/// <summary>
/// Thin orchestrator implementation of <see cref="ISyncService"/>.
/// Delegates file I/O to <see cref="ISyncTransport"/>, serialisation and
/// encryption to <see cref="ISyncPackageCodec"/>, and conflict detection
/// and resolution to <see cref="ISyncConflictResolver"/>.
///
/// Sync file layout on disk:
///   {SyncFolder}/agentx-sync-{deviceId}-{timestamp:yyyyMMddHHmmssffff}.axs
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly AgentXDbContext       _db;
    private readonly ILogger               _log;
    private readonly ISyncTransport        _transport;
    private readonly ISyncPackageCodec     _codec;
    private readonly ISyncConflictResolver _conflictResolver;

    /// <summary>Current sync status — mutated only through <see cref="SetStatus"/>.</summary>
    private SyncStatus _status = new();

    /// <summary>Guards <see cref="_status"/> for thread-safe reads and atomic mutations.</summary>
    private readonly object _statusLock = new();

    /// <summary>Guards the auto-sync loop start/stop path to prevent races.</summary>
    // Wave 4b: migrated from `lock (object)` to SemaphoreSlim so cancellation of the
    // auto-sync CTS can be awaited (CancellationTokenSource.CancelAsync awaits any
    // registered cancellation callbacks). The semaphore is *not* reentrant — each
    // critical section is straight-line and does not reacquire the lock.
    private readonly SemaphoreSlim _loopLock = new(1, 1);

    /// <summary>Cancels the currently-running auto-sync loop when not null.</summary>
    private CancellationTokenSource? _autoSyncCts;

    /// <summary>
    /// In-process cache of the stable device ID so DB round-trips are avoided on
    /// every export cycle.
    /// </summary>
    private string? _cachedDeviceId;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="SyncService"/> backed by the given database context
    /// and composed sub-services.
    /// </summary>
    /// <param name="dbContext">The EF Core context for the AgentX SQLite database.</param>
    /// <param name="logger">Root Serilog logger.</param>
    /// <param name="transport">File-system transport for .axs files.</param>
    /// <param name="codec">Serialisation and encryption codec.</param>
    /// <param name="conflictResolver">Conflict detection and resolution engine.</param>
    public SyncService(
        AgentXDbContext dbContext,
        ILogger logger,
        ISyncTransport transport,
        ISyncPackageCodec codec,
        ISyncConflictResolver conflictResolver)
    {
        _db               = dbContext    ?? throw new ArgumentNullException(nameof(dbContext));
        _log              = (logger      ?? throw new ArgumentNullException(nameof(logger)))
                           .ForContext<SyncService>();
        _transport        = transport        ?? throw new ArgumentNullException(nameof(transport));
        _codec            = codec            ?? throw new ArgumentNullException(nameof(codec));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));

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
                Version    = 1,
            };

            _log.Information(
                "SyncService.ExportChangesAsync: collected {Count} change(s)",
                changes.Count);

            // ── Serialise → encrypt → write .axs file via codec + transport ──
            _transport.EnsureFolderExists(config.SyncFolderPath);

            var plaintext = _codec.Serialise(changeSet);
            var encrypted = _codec.Encrypt(plaintext, config.EncryptionKey);

            var filePath = await _transport.WriteSyncFileAsync(
                config.SyncFolderPath, deviceId, changeSet.ExportedAt, encrypted, ct)
                .ConfigureAwait(false);

            sw.Stop();

            _log.Information(
                "SyncService.ExportChangesAsync: complete. File={Path} Changes={Count} Duration={DurationMs:F1} ms",
                Path.GetFileName(filePath), changes.Count, sw.Elapsed.TotalMilliseconds);

            // ── Persist audit log ─────────────────────────────────────────────
            await PersistLogAsync(new SyncLogEntity
            {
                SyncedAt          = DateTime.UtcNow,
                Direction         = "export",
                ChangesApplied    = changes.Count,
                ConflictsDetected = 0,
                ConflictsResolved = 0,
                DurationMs        = sw.Elapsed.TotalMilliseconds,
                IsSuccess         = true,
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
                SyncedAt     = DateTime.UtcNow,
                Direction    = "export",
                DurationMs   = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = "Export was cancelled.",
                IsSuccess    = false,
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
                SyncedAt     = DateTime.UtcNow,
                Direction    = "export",
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
            var localDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);
            var conflicts     = await _conflictResolver.DetectConflictsAsync(
                changeSet,
                Status.LastSyncAt,
                localDeviceId,
                GetLocalModifiedAtAsync).ConfigureAwait(false);

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

        var localDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

        return await _conflictResolver.DetectConflictsAsync(
            incoming,
            Status.LastSyncAt,
            localDeviceId,
            GetLocalModifiedAtAsync).ConfigureAwait(false);
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

        var changeToApply = _conflictResolver.ResolveConflict(conflict, resolution);

        if (changeToApply is not null)
        {
            await ApplyChangeAsync(changeToApply, CancellationToken.None).ConfigureAwait(false);
            await _db.SaveChangesAsync().ConfigureAwait(false);
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

        await _loopLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _autoSyncCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var loopCt = _autoSyncCts.Token;
            _ = Task.Run(() => RunAutoSyncLoopAsync(intervalMinutes, loopCt), loopCt);
        }
        finally
        {
            _loopLock.Release();
        }

        _log.Information(
            "SyncService.StartAutoSyncAsync: auto-sync loop started. Interval={Interval} min",
            intervalMinutes);
    }

    // ── ISyncService: StopAutoSyncAsync ──────────────────────────────────────

    /// <inheritdoc />
    public async Task StopAutoSyncAsync()
    {
        await _loopLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_autoSyncCts is null)
                return;

            _log.Information("SyncService.StopAutoSyncAsync: cancelling auto-sync loop");

            await _autoSyncCts.CancelAsync().ConfigureAwait(false);
            _autoSyncCts.Dispose();
            _autoSyncCts = null;
        }
        finally
        {
            _loopLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PRIVATE: AUTO-SYNC LOOP
    // ═══════════════════════════════════════════════════════════════════════════

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
                    await ExportChangesAsync(lastSync, ct).ConfigureAwait(false);
                    await ImportPeerFilesAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
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

    // ═══════════════════════════════════════════════════════════════════════════
    //  PRIVATE: PEER FILE IMPORT (uses Transport + Codec)
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ImportPeerFilesAsync(CancellationToken ct)
    {
        var config        = await GetRequiredConfigurationAsync().ConfigureAwait(false);
        var localDeviceId = await GetOrCreateDeviceIdAsync().ConfigureAwait(false);

        var peerFiles = await _transport.ReadPeerFilesAsync(
            config.SyncFolderPath, localDeviceId, ct).ConfigureAwait(false);

        foreach (var peer in peerFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _log.Information(
                    "SyncService.ImportPeerFilesAsync: processing peer file {FileName}",
                    peer.FileName);

                if (!_codec.IsValidHeader(peer.Data))
                {
                    _log.Warning(
                        "SyncService.ImportPeerFilesAsync: {FileName} has an invalid header — skipping",
                        peer.FileName);
                    continue;
                }

                var plaintext = _codec.Decrypt(peer.Data, config.EncryptionKey);
                var changeSet = _codec.Deserialise(plaintext);

                await ImportChangesAsync(changeSet, ct).ConfigureAwait(false);

                await _transport.MarkFileImportedAsync(peer.FilePath).ConfigureAwait(false);

                _log.Information(
                    "SyncService.ImportPeerFilesAsync: {FileName} processed → renamed to .imported",
                    peer.FileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                _log.Warning(ex,
                    "SyncService.ImportPeerFilesAsync: decryption failed for {FileName} " +
                    "— wrong passphrase or corrupted file",
                    peer.FileName);
            }
            catch (Exception ex)
            {
                _log.Error(ex,
                    "SyncService.ImportPeerFilesAsync: unexpected error processing {FileName}",
                    peer.FileName);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PRIVATE: CHANGE COLLECTION & APPLICATION
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<List<SyncChange>> CollectChangesAsync(
        DateTime? since,
        SyncConfiguration config,
        CancellationToken ct)
    {
        var changes = new List<SyncChange>();
        var cutoff  = since ?? DateTime.MinValue;

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
        var docsQuery = _db.Documents.AsNoTracking().Where(d => d.ImportedAt > cutoff);

        if (allowedCollectionIds is not null)
            docsQuery = docsQuery.Where(d => d.DocumentCollections.Any(dc => allowedCollectionIds.Contains(dc.CollectionId)));

        foreach (var doc in await docsQuery.ToListAsync(ct).ConfigureAwait(false))
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
        var colQuery = _db.Collections.AsNoTracking().Where(c => c.UpdatedAt > cutoff);
        if (allowedCollectionIds is not null)
            colQuery = colQuery.Where(c => allowedCollectionIds.Contains(c.Id));

        foreach (var col in await colQuery.ToListAsync(ct).ConfigureAwait(false))
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
        foreach (var tag in await _db.Tags.AsNoTracking().Where(t => t.CreatedAt > cutoff).ToListAsync(ct).ConfigureAwait(false))
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
        foreach (var conv in await _db.Conversations.AsNoTracking().Where(c => c.UpdatedAt > cutoff).ToListAsync(ct).ConfigureAwait(false))
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
        foreach (var ann in await _db.Annotations.AsNoTracking().Where(a => a.UpdatedAt > cutoff).ToListAsync(ct).ConfigureAwait(false))
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
        foreach (var prompt in await _db.SystemPrompts.AsNoTracking().Where(sp => sp.UpdatedAt > cutoff).ToListAsync(ct).ConfigureAwait(false))
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

        _log.Debug("SyncService.CollectChangesAsync: collected {Count} change(s)", changes.Count);

        return changes;
    }

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
            _db.Entry(incoming).State = EntityState.Detached;
            dbSet.Add(incoming);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(incoming);
        }
    }

    private async Task ApplyDeletionAsync(SyncChange change)
    {
        switch (change.EntityType)
        {
            case nameof(DocumentEntity):     await RemoveByIdAsync(_db.Documents, change.EntityId).ConfigureAwait(false); break;
            case nameof(CollectionEntity):   await RemoveByIdAsync(_db.Collections, change.EntityId).ConfigureAwait(false); break;
            case nameof(TagEntity):          await RemoveByIdAsync(_db.Tags, change.EntityId).ConfigureAwait(false); break;
            case nameof(ConversationEntity): await RemoveByIdAsync(_db.Conversations, change.EntityId).ConfigureAwait(false); break;
            case nameof(AnnotationEntity):   await RemoveByIdAsync(_db.Annotations, change.EntityId).ConfigureAwait(false); break;
            case nameof(SystemPromptEntity): await RemoveByIdAsync(_db.SystemPrompts, change.EntityId).ConfigureAwait(false); break;
            default:
                _log.Warning("SyncService.ApplyDeletionAsync: unrecognised entity type '{EntityType}' — skipped", change.EntityType);
                break;
        }
    }

    private async Task RemoveByIdAsync<TEntity>(DbSet<TEntity> dbSet, long id)
        where TEntity : class
    {
        var entity = await dbSet.FindAsync(id).ConfigureAwait(false);
        if (entity is not null)
            dbSet.Remove(entity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PRIVATE: LOCAL TIMESTAMP RESOLUTION
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<DateTime?> GetLocalModifiedAtAsync(string entityType, long entityId)
    {
        return entityType switch
        {
            nameof(DocumentEntity) => await _db.Documents.Where(d => d.Id == entityId).Select(d => (DateTime?)d.ImportedAt).FirstOrDefaultAsync().ConfigureAwait(false),
            nameof(CollectionEntity) => await _db.Collections.Where(c => c.Id == entityId).Select(c => (DateTime?)c.UpdatedAt).FirstOrDefaultAsync().ConfigureAwait(false),
            nameof(TagEntity) => await _db.Tags.Where(t => t.Id == entityId).Select(t => (DateTime?)t.CreatedAt).FirstOrDefaultAsync().ConfigureAwait(false),
            nameof(ConversationEntity) => await _db.Conversations.Where(c => c.Id == entityId).Select(c => (DateTime?)c.UpdatedAt).FirstOrDefaultAsync().ConfigureAwait(false),
            nameof(AnnotationEntity) => await _db.Annotations.Where(a => a.Id == entityId).Select(a => (DateTime?)a.UpdatedAt).FirstOrDefaultAsync().ConfigureAwait(false),
            nameof(SystemPromptEntity) => await _db.SystemPrompts.Where(sp => sp.Id == entityId).Select(sp => (DateTime?)sp.UpdatedAt).FirstOrDefaultAsync().ConfigureAwait(false),
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PRIVATE: DEVICE ID & SETTINGS
    // ═══════════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════════
    //  PRIVATE: SYNC LOG & STATUS
    // ═══════════════════════════════════════════════════════════════════════════

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

    private void SetStatus(Action<SyncStatus> mutate)
    {
        SyncStatus snapshot;

        lock (_statusLock)
        {
            mutate(_status);

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
}

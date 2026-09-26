using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AgentX.Core.Constants;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Backup;

/// <summary>
/// Full implementation of <see cref="IBackupService"/>.
///
/// Backup format: a ZIP archive with the extension .agentxbak containing:
///   database/agentx.db  - SQLite Online Backup API copy (no lock contention)
///   manifest.json       - metadata (version, counts, timestamps, app version)
///   documents/          - optional copy of the document folders inside the storage path
///                         (<see cref="DocumentFolders"/>, the files web import writes)
///
/// Settings, secrets, the encryption marker, logs, models, plugins, caches and derived indexes
/// are never included: they are machine- or account-bound, can be rebuilt or downloaded again,
/// and restoring them under running services broke the app. Documents imported from other
/// folders are indexed where they are and are not copied.
///
/// The archive is streamed to disk, never buffered in memory. With a password it is encrypted
/// on the way (see <see cref="BackupArchiveCrypto"/>: streamed AES-256-GCM, PBKDF2 iteration
/// count recorded in the header); V1 (AES-256-CBC) and V2 (one-shot AES-256-GCM) archives remain
/// restorable.
///
/// Restore stages the archive's database next to the live file, verifies it with the current
/// database key (re-encrypting a plaintext backup when encryption is on), releases the shared
/// connection, swaps the file in with <see cref="File.Replace(string, string, string?)"/> and keeps
/// the replaced file until the swapped-in database passes verification.
/// </summary>
public sealed class BackupService : IBackupService
{
    // ── Constants ──────────────────────────────────────────────────────────

    // Single source (assembly version) so backup manifests record the real build (AX-QA-014).
    private static readonly string AppVersion = AppVersionInfo.Display;
    private const string DbEntryName = "database/agentx.db";
    private const string ManifestEntryName = "manifest.json";
    private const string DocumentsEntryPrefix = "documents/";
    private const string BackupExtension = ".agentxbak";

    /// <summary>
    /// Folders under the storage path that hold user documents: files Agent-X wrote from imported
    /// content and that document records point to. Everything else there is configuration,
    /// secrets, logs, models, plugins, caches or derived indexes.
    /// </summary>
    private static readonly string[] DocumentFolders = { "WebImports" };

    // Restore safety limits: defense-in-depth against archive ("zip") bombs. Generous
    // enough to accommodate real document libraries while blocking pathological expansion.
    private const long MaxRestoredEntryBytes = 2L * 1024 * 1024 * 1024;   // 2 GB per file
    private const long MaxRestoredTotalBytes = 50L * 1024 * 1024 * 1024;  // 50 GB total

    // SQLite result code for "file is not a database": the file is encrypted with another key.
    private const int SqliteNotADatabase = 26;

    // ── Fields ─────────────────────────────────────────────────────────────

    private readonly AgentXDbContext _db;
    private readonly ISettingsService _settingsService;
    private readonly IEncryptedConnectionFactory _connectionFactory;
    private readonly string? _databasePathOverride;

    /// <summary>
    /// CancellationTokenSource used to stop the scheduled backup loop from <see cref="StopScheduledBackups"/>.
    /// Replaced each time <see cref="StartScheduledBackupsAsync"/> is called.
    /// </summary>
    private CancellationTokenSource? _scheduledCts;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Constructor ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="BackupService"/>. The <paramref name="connectionFactory"/> is a
    /// required dependency: PRAGMA key is applied through it to the source and destination
    /// connections of the SQLite Online Backup API and to every staged restore.
    /// </summary>
    public BackupService(
        AgentXDbContext dbContext,
        ISettingsService settingsService,
        IEncryptedConnectionFactory connectionFactory)
        : this(dbContext, settingsService, connectionFactory, databasePath: null)
    {
    }

    /// <summary>Test seam: pins the database file instead of reading it from the context.</summary>
    internal BackupService(
        AgentXDbContext dbContext,
        ISettingsService settingsService,
        IEncryptedConnectionFactory connectionFactory,
        string? databasePath)
    {
        _db = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _databasePathOverride = databasePath;
        Log.Information("BackupService initialized");
    }

    // ── IBackupService: CreateBackupAsync ──────────────────────────────────

    /// <inheritdoc />
    public async Task<BackupResult> CreateBackupAsync(
        BackupOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        Log.Information("Starting backup. Type={BackupType} Destination={Destination}",
            options.BackupType, options.DestinationPath);

        string? dbTempPath = null;
        string? partialPath = null;

        try
        {
            // Resolve destination directory
            var destinationDir = ResolveDestinationDirectory(options.DestinationPath);
            Directory.CreateDirectory(destinationDir);

            // Build archive file name. Invariant culture: a machine-readable name must not carry a
            // Hijri or Buddhist year under ar-SA or th-TH.
            var timestamp = DateTime.UtcNow;
            var archivePath = UniqueFilePath(Path.Combine(
                destinationDir,
                $"agentx-backup-{timestamp.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}{BackupExtension}"));
            var fileName = Path.GetFileName(archivePath);

            // ── Phase 1: gather counts for the manifest ────────────────────
            Report(progress, "Gathering statistics", 5);
            ct.ThrowIfCancellationRequested();

            var docCount = await _db.Documents.CountAsync(ct).ConfigureAwait(false);
            var convCount = await _db.Conversations.CountAsync(ct).ConfigureAwait(false);
            var workflowCount = await _db.Workflows.CountAsync(ct).ConfigureAwait(false);

            // ── Phase 2: copy database via SQLite Online Backup API ─────────
            Report(progress, "Copying database", 15);
            ct.ThrowIfCancellationRequested();

            dbTempPath = Path.Combine(Path.GetTempPath(), $"agentx-dbcopy-{Guid.NewGuid():N}.tmp");
            await CreateSqliteBackupCopyAsync(dbTempPath, ct).ConfigureAwait(false);

            // ── Phase 3: stream the ZIP to a partial file ──────────────────
            Report(progress, "Writing archive", 35);
            ct.ThrowIfCancellationRequested();

            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            var storagePath = settings.StoragePath;

            partialPath = archivePath + ".partial";
            await using (var file = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                // With a password the ZIP is encrypted on its way to disk, so an encrypted backup
                // never exists as plaintext and its size is not limited by memory.
                var target = string.IsNullOrEmpty(options.EncryptionPassword)
                    ? file
                    : BackupArchiveCrypto.CreateEncryptingStream(file, options.EncryptionPassword);
                try
                {
                    await BuildZipArchiveAsync(
                        target, dbTempPath, storagePath, timestamp, docCount, convCount,
                        workflowCount, options, warnings, progress, ct).ConfigureAwait(false);
                }
                finally
                {
                    if (!ReferenceEquals(target, file))
                        await target.DisposeAsync().ConfigureAwait(false);
                }

                await file.FlushAsync(ct).ConfigureAwait(false);
            }

            // ── Phase 4: publish the finished archive ──────────────────────
            Report(progress, "Finalising archive", 90);
            File.Move(partialPath, archivePath);
            partialPath = null;

            // ── Phase 5: record in database ────────────────────────────────
            Report(progress, "Saving history record", 95);

            var fileInfo = new FileInfo(archivePath);
            var sizeMb = fileInfo.Exists ? fileInfo.Length / (1024.0 * 1024.0) : 0;

            var entity = new BackupEntity
            {
                FileName = fileName,
                FilePath = archivePath,
                BackupType = options.BackupType,
                SizeMB = Math.Round(sizeMb, 3),
                CreatedAt = timestamp,
                Notes = options.Notes,
                IsValid = true,
            };

            _db.Backups.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            sw.Stop();
            Report(progress, "Complete", 100);

            Log.Information(
                "Backup completed in {DurationMs:F0} ms. File={FileName} Size={SizeMB:F2} MB Id={Id} Warnings={WarningCount}",
                sw.Elapsed.TotalMilliseconds, fileName, sizeMb, entity.Id, warnings.Count);

            return new BackupResult
            {
                Success = true,
                BackupFilePath = archivePath,
                SizeMB = sizeMb,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                BackupId = entity.Id,
                WarningMessages = warnings,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Warning("Backup operation was cancelled after {DurationMs:F0} ms", sw.Elapsed.TotalMilliseconds);
            return new BackupResult
            {
                Success = false,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = "Backup was cancelled.",
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Backup failed after {DurationMs:F0} ms", sw.Elapsed.TotalMilliseconds);
            return new BackupResult
            {
                Success = false,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            // Always clean up the temp DB copy and any unfinished archive.
            DeleteFileQuietly(dbTempPath);
            DeleteFileQuietly(partialPath);
        }
    }

    // ── IBackupService: RestoreFromBackupAsync ─────────────────────────────

    /// <inheritdoc />
    public Task<RestoreResult> RestoreFromBackupAsync(
        string backupFilePath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
        => RestoreFromBackupAsync(backupFilePath, password: null, progress, ct);

    /// <inheritdoc />
    public async Task<RestoreResult> RestoreFromBackupAsync(
        string backupFilePath,
        string? password,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        RestorePaths? paths = null;
        string? decryptedArchive = null;

        Log.Information("Starting restore from {BackupFilePath}", backupFilePath);

        try
        {
            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("Backup file not found.", backupFilePath);

            // ── Phase 1: decrypt when needed, then validate the archive ─────
            Report(progress, "Validating archive", 5);
            ct.ThrowIfCancellationRequested();

            var encrypted = BackupArchiveCrypto.IsEncryptedFile(backupFilePath);
            if (encrypted && string.IsNullOrEmpty(password))
                throw new InvalidOperationException("This backup is encrypted. Enter its password to restore it.");

            var zipPath = backupFilePath;
            if (encrypted)
            {
                // Decrypted before validation: the archive structure is only visible after it.
                Report(progress, "Decrypting archive", 10);
                decryptedArchive = Path.Combine(Path.GetTempPath(), $"agentx-restore-{Guid.NewGuid():N}.tmp");
                await Task.Run(() => DecryptArchiveToFile(backupFilePath, decryptedArchive, password!, ct), ct)
                    .ConfigureAwait(false);
                zipPath = decryptedArchive;
            }

            using var archive = OpenArchiveForRestore(zipPath);

            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            paths = new RestorePaths(ResolveLiveDatabaseFile(), settings.StoragePath);
            paths.PrepareWorkspace();

            // ── Phase 2: stage the database and verify it with the current key ──
            Report(progress, "Extracting database", 25);
            ct.ThrowIfCancellationRequested();

            await ExtractEntryAsync(archive.GetEntry(DbEntryName)!, paths.StagedDatabase, MaxRestoredTotalBytes, ct)
                .ConfigureAwait(false);

            Report(progress, "Verifying database", 40);
            var restoredDatabase = await Task.Run(() => PrepareStagedDatabase(paths), ct).ConfigureAwait(false);

            // ── Phase 3: stage document files ─────────────────────────────
            Report(progress, "Staging document files", 55);
            var stagedDocuments = await StageDocumentsAsync(archive, paths, warnings, ct).ConfigureAwait(false);

            // Last point where cancelling leaves nothing to undo.
            ct.ThrowIfCancellationRequested();

            // ── Phase 4: swap the database in, verify it, install documents ──
            Report(progress, "Replacing database", 70);
            SwapInDatabase(restoredDatabase, paths);

            int restoredDocs, restoredConvs, restoredWorkflows;
            var installed = new List<InstalledDocument>();
            try
            {
                Report(progress, "Verifying restored data", 80);
                restoredDocs = await _db.Documents.CountAsync(CancellationToken.None).ConfigureAwait(false);
                restoredConvs = await _db.Conversations.CountAsync(CancellationToken.None).ConfigureAwait(false);
                restoredWorkflows = await _db.Workflows.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Report(progress, "Restoring document files", 90);
                InstallDocuments(stagedDocuments, paths, installed);
            }
            catch (Exception ex)
            {
                if (RollBackDocuments(installed))
                    paths.DiscardDocumentRollback();

                var previousKeptAt = RollBackDatabase(paths);
                if (previousKeptAt is not null)
                {
                    throw new InvalidOperationException(
                        $"{ex.Message} The previous database could not be put back automatically; it is kept at {previousKeptAt}.", ex);
                }

                throw;
            }

            // The swapped-in database passed verification: the replaced files are no longer needed.
            paths.DiscardSafetyCopy();
            paths.DiscardDocumentRollback();

            if (stagedDocuments.Count == 0)
                warnings.Add("No document files were found in the archive. The archive may not have included documents.");

            sw.Stop();
            Report(progress, "Complete", 100);

            Log.Information(
                "Restore completed in {DurationMs:F0} ms. Docs={Docs} Convs={Convs} Workflows={Workflows} Files={Files}",
                sw.Elapsed.TotalMilliseconds, restoredDocs, restoredConvs, restoredWorkflows, stagedDocuments.Count);

            return new RestoreResult
            {
                Success = true,
                RestoredDocumentCount = restoredDocs,
                RestoredConversationCount = restoredConvs,
                RestoredWorkflowCount = restoredWorkflows,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                WarningMessages = warnings,
                RequiresRestart = true,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Warning("Restore operation was cancelled after {DurationMs:F0} ms", sw.Elapsed.TotalMilliseconds);
            return new RestoreResult
            {
                Success = false,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = "Restore was cancelled.",
                WarningMessages = warnings,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Restore failed after {DurationMs:F0} ms", sw.Elapsed.TotalMilliseconds);
            return new RestoreResult
            {
                Success = false,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message,
                WarningMessages = warnings,
            };
        }
        finally
        {
            paths?.RemoveStaging();
            DeleteFileQuietly(decryptedArchive);
        }
    }

    // ── IBackupService: GetBackupHistoryAsync ──────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupEntity>> GetBackupHistoryAsync()
    {
        Log.Debug("Retrieving backup history");

        var history = await _db.Backups
            .OrderByDescending(b => b.CreatedAt)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);

        Log.Debug("Backup history contains {Count} record(s)", history.Count);
        return history;
    }

    // ── IBackupService: DeleteBackupAsync ──────────────────────────────────

    /// <inheritdoc />
    public async Task DeleteBackupAsync(long backupId)
    {
        Log.Information("Deleting backup record Id={BackupId}", backupId);

        var entity = await _db.Backups
            .FirstOrDefaultAsync(b => b.Id == backupId)
            .ConfigureAwait(false);

        if (entity is null)
        {
            Log.Warning("Backup record {BackupId} not found; nothing to delete", backupId);
            return;
        }

        // Remove the archive file from disk if it still exists
        if (!string.IsNullOrEmpty(entity.FilePath) && File.Exists(entity.FilePath))
        {
            try
            {
                File.Delete(entity.FilePath);
                Log.Debug("Deleted backup archive file {FilePath}", entity.FilePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not delete backup archive file {FilePath}; removing history record anyway", entity.FilePath);
            }
        }

        _db.Backups.Remove(entity);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        Log.Information("Backup record {BackupId} deleted", backupId);
    }

    // ── IBackupService: EstimateBackupSizeAsync ────────────────────────────

    /// <inheritdoc />
    public async Task<BackupSizeEstimate> EstimateBackupSizeAsync()
    {
        Log.Debug("Estimating backup size");

        var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
        var dbPath = ResolveDatabasePathForCopy();

        var dbSizeBytes = File.Exists(dbPath)
            ? new FileInfo(dbPath).Length
            : 0L;

        // Same selection the backup archives: only the document folders.
        var docsFolderBytes = EnumerateDocumentFiles(settings.StoragePath)
            .Sum(f =>
            {
                try { return new FileInfo(f.FullPath).Length; }
                catch (IOException) { return 0L; }
                catch (UnauthorizedAccessException) { return 0L; }
            });

        var docCount = await _db.Documents.CountAsync().ConfigureAwait(false);

        const double BytesPerMb = 1024.0 * 1024.0;
        var dbMb = Math.Round(dbSizeBytes / BytesPerMb, 3);
        var docsMb = Math.Round(docsFolderBytes / BytesPerMb, 3);

        var estimate = new BackupSizeEstimate
        {
            DatabaseSizeMB = dbMb,
            DocumentsSizeMB = docsMb,
            TotalEstimatedMB = Math.Round(dbMb + docsMb, 3),
            DocumentCount = docCount,
        };

        Log.Debug(
            "Size estimate: DB {DbMb:F2} MB, Docs {DocsMb:F2} MB, Total {TotalMb:F2} MB, Documents {DocCount}",
            estimate.DatabaseSizeMB, estimate.DocumentsSizeMB, estimate.TotalEstimatedMB, estimate.DocumentCount);

        return estimate;
    }

    // ── IBackupService: ValidateBackupAsync ───────────────────────────────

    /// <inheritdoc />
    public Task<bool> ValidateBackupAsync(string backupFilePath)
        => ValidateBackupAsync(backupFilePath, password: null);

    /// <inheritdoc />
    public async Task<bool> ValidateBackupAsync(string backupFilePath, string? password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        Log.Debug("Validating backup archive {BackupFilePath}", backupFilePath);

        string? decryptedPath = null;
        try
        {
            if (!File.Exists(backupFilePath))
            {
                Log.Warning("Validation failed: file does not exist: {BackupFilePath}", backupFilePath);
                return false;
            }

            if (new FileInfo(backupFilePath).Length == 0)
            {
                Log.Warning("Validation failed: file is empty: {BackupFilePath}", backupFilePath);
                return false;
            }

            var zipPath = backupFilePath;
            if (BackupArchiveCrypto.IsEncryptedFile(backupFilePath))
            {
                // Without the password an encrypted archive can only be checked structurally.
                if (string.IsNullOrEmpty(password))
                {
                    Log.Debug("Archive is encrypted: structural validation only (no password provided)");
                    return true;
                }

                decryptedPath = Path.Combine(Path.GetTempPath(), $"agentx-validate-{Guid.NewGuid():N}.tmp");
                await Task.Run(() => DecryptArchiveToFile(backupFilePath, decryptedPath, password, CancellationToken.None))
                    .ConfigureAwait(false);
                zipPath = decryptedPath;
            }

            using var archive = ZipFile.OpenRead(zipPath);
            if (!TryValidateArchive(archive, out var reason))
            {
                Log.Warning("Validation failed: {Reason}", reason);
                return false;
            }

            Log.Debug("Archive validation passed");
            return true;
        }
        catch (InvalidDataException ex)
        {
            Log.Warning(ex, "Validation failed: archive is not a valid ZIP: {BackupFilePath}", backupFilePath);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Validation failed for {BackupFilePath}", backupFilePath);
            return false;
        }
        finally
        {
            DeleteFileQuietly(decryptedPath);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsEncryptedBackupAsync(string backupFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        return Task.FromResult(File.Exists(backupFilePath) && BackupArchiveCrypto.IsEncryptedFile(backupFilePath));
    }

    /// <summary>
    /// Validates that every <c>documents/</c> entry in the archive uses a safe, contained
    /// relative path (no <c>..</c> traversal, no rooted paths) and does not exceed the
    /// per-entry / total restore size limits. Pure and side-effect free so it guards both
    /// <see cref="ValidateBackupAsync(string)"/> and is directly unit-testable.
    /// </summary>
    /// <param name="archive">An archive opened in <see cref="ZipArchiveMode.Read"/> mode.</param>
    /// <param name="failureReason">Human-readable reason when validation fails; null on success.</param>
    /// <returns><c>true</c> when all document entries are safe; otherwise <c>false</c>.</returns>
    public static bool TryValidateDocumentEntries(ZipArchive archive, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(archive);

        long totalUncompressed = 0;

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(DocumentsEntryPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.FullName.EndsWith('/'))
                continue; // directory entry

            var relativePath = entry.FullName[DocumentsEntryPrefix.Length..];

            if (!PathHelper.IsSafeRelativeEntry(relativePath))
            {
                failureReason = $"unsafe document entry path '{entry.FullName}'";
                return false;
            }

            if (entry.Length > MaxRestoredEntryBytes)
            {
                failureReason = $"document entry '{entry.FullName}' exceeds the per-file size limit";
                return false;
            }

            totalUncompressed += entry.Length;
            if (totalUncompressed > MaxRestoredTotalBytes)
            {
                failureReason = "total expanded document size exceeds the restore safety limit";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    private static bool TryValidateArchive(ZipArchive archive, out string? failureReason)
    {
        var hasDb = archive.GetEntry(DbEntryName) is not null;
        var hasManifest = archive.GetEntry(ManifestEntryName) is not null;
        if (!hasDb || !hasManifest)
        {
            failureReason = $"archive missing required entries (database: {hasDb}, manifest: {hasManifest})";
            return false;
        }

        // Reject archives whose document entries use traversal/rooted paths or exceed the
        // restore size limits, before any extraction touches the filesystem.
        return TryValidateDocumentEntries(archive, out failureReason);
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> while enforcing a hard
    /// byte cap, aborting with an <see cref="InvalidOperationException"/> if the cap is exceeded.
    /// Guards against forged ZIP entry lengths during restore.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    private static async Task<long> CopyEntryWithLimitAsync(
        Stream source, Stream destination, long perEntryLimit, string entryName, CancellationToken ct)
    {
        var buffer = new byte[AppConstants.FileStreamBufferSize];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > perEntryLimit)
                throw new InvalidOperationException(
                    $"Archive entry '{entryName}' exceeds the restore size limit of " +
                    $"{perEntryLimit / (1024L * 1024 * 1024)} GB.");

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return written;
    }

    // ── IBackupService: StartScheduledBackupsAsync ─────────────────────────

    /// <inheritdoc />
    public async Task StartScheduledBackupsAsync(CancellationToken ct = default)
    {
        // Cancel any already-running loop
        StopScheduledBackups();

        var config = await LoadScheduleConfigAsync().ConfigureAwait(false);

        if (!config.Enabled)
        {
            Log.Information("Scheduled backups are disabled; loop not started");
            return;
        }

        _scheduledCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loopCt = _scheduledCts.Token;

        // Fire and forget: the loop runs on the thread pool
        _ = Task.Run(() => RunScheduledLoopAsync(config, loopCt), loopCt);

        Log.Information(
            "Scheduled backup loop started. Interval={IntervalHours}h MaxKeep={MaxKeep}",
            config.IntervalHours, config.MaxBackupsToKeep);
    }

    // ── IBackupService: StopScheduledBackups ──────────────────────────────

    /// <inheritdoc />
    public void StopScheduledBackups()
    {
        if (_scheduledCts is null)
            return;

        _scheduledCts.Cancel();
        _scheduledCts.Dispose();
        _scheduledCts = null;

        Log.Information("Scheduled backup loop stopped");
    }

    // ── Private: Scheduled loop ────────────────────────────────────────────

    private async Task RunScheduledLoopAsync(BackupScheduleConfig config, CancellationToken ct)
    {
        var interval = TimeSpan.FromHours(config.IntervalHours);

        using var timer = new PeriodicTimer(interval);

        Log.Debug("Scheduled backup loop running. First backup in {Hours}h", config.IntervalHours);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                Log.Information("Scheduled backup triggered");

                try
                {
                    var destination = string.IsNullOrWhiteSpace(config.DestinationPath)
                        ? GetDefaultStoragePath()
                        : config.DestinationPath;

                    var options = new BackupOptions
                    {
                        DestinationPath = destination,
                        EncryptionPassword = config.EncryptionPassword,
                        IncludeDocuments = true,
                        BackupType = "scheduled",
                        Notes = $"Automatic scheduled backup, {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC",
                    };

                    var result = await CreateBackupAsync(options, progress: null, ct).ConfigureAwait(false);

                    if (result.Success)
                    {
                        Log.Information(
                            "Scheduled backup succeeded: {FilePath} ({SizeMB:F2} MB)",
                            result.BackupFilePath, result.SizeMB);

                        // Enforce retention limit: delete the oldest scheduled backups beyond the cap
                        await EnforceRetentionPolicyAsync(config.MaxBackupsToKeep, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Error("Scheduled backup failed: {ErrorMessage}", result.ErrorMessage);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate to outer loop
                }
                catch (Exception ex)
                {
                    // Never let a single backup failure crash the loop
                    Log.Error(ex, "Unhandled error during scheduled backup cycle; loop continues");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Scheduled backup loop exiting due to cancellation");
        }
    }

    // ── Private: ZIP archive builder ───────────────────────────────────────

    private static async Task BuildZipArchiveAsync(
        Stream outputStream,
        string dbTempPath,
        string storagePath,
        DateTime timestamp,
        int docCount,
        int convCount,
        int workflowCount,
        BackupOptions options,
        List<string> warnings,
        IProgress<BackupProgress>? progress,
        CancellationToken ct)
    {
        using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

        // ── 1. Database file ───────────────────────────────────────────────
        Report(progress, "Adding database to archive", 40);
        ct.ThrowIfCancellationRequested();

        var dbEntry = archive.CreateEntry(DbEntryName, CompressionLevel.Optimal);
        await using (var entryStream = dbEntry.Open())
        await using (var dbStream = OpenForBackupRead(dbTempPath))
        {
            await dbStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
        }

        // ── 2. Manifest ────────────────────────────────────────────────────
        Report(progress, "Writing manifest", 55);
        ct.ThrowIfCancellationRequested();

        var manifest = new
        {
            version = 1,
            appVersion = AppVersion,
            createdAt = timestamp.ToString("O", CultureInfo.InvariantCulture),
            backupType = options.BackupType,
            documentCount = docCount,
            conversationCount = convCount,
            workflowCount = workflowCount,
            includesDocuments = options.IncludeDocuments,
            documentFolders = DocumentFolders,
            notes = options.Notes,
        };

        var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
        await using (var entryStream = manifestEntry.Open())
        {
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            await entryStream.WriteAsync(manifestBytes, ct).ConfigureAwait(false);
        }

        // ── 3. Document files ──────────────────────────────────────────────
        if (!options.IncludeDocuments)
            return;

        Report(progress, "Adding document files", 65);

        var documentFiles = EnumerateDocumentFiles(storagePath).ToList();
        var added = 0;
        for (var i = 0; i < documentFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (filePath, relativePath) = documentFiles[i];

            // Open first: a file that cannot be read is skipped and reported instead of failing
            // the whole backup (or leaving an empty entry behind).
            FileStream fileStream;
            try
            {
                fileStream = OpenForBackupRead(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Skipped '{relativePath}': {ex.Message}");
                Log.Warning(ex, "Skipped unreadable document file {FilePath}", filePath);
                continue;
            }

            await using (fileStream)
            {
                var fileEntry = archive.CreateEntry(DocumentsEntryPrefix + relativePath, CompressionLevel.Optimal);
                await using var entryStream = fileEntry.Open();
                await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
            }

            added++;

            // Report per-file progress within the 65-82% band
            var pct = 65 + (int)((i + 1) / (double)documentFiles.Count * 17);
            Report(progress, "Adding document files", pct, Path.GetFileName(filePath));
        }

        Log.Debug("Added {Count} of {Total} document files to archive", added, documentFiles.Count);
    }

    /// <summary>
    /// ReadWrite | Delete sharing: another process (or a log writer) may hold the file open for
    /// writing; a narrower share mode made the open fail with a sharing violation on Windows.
    /// </summary>
    private static FileStream OpenForBackupRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true);

    /// <summary>The document files a backup includes, with archive-relative paths using '/'.</summary>
    private static IEnumerable<(string FullPath, string RelativePath)> EnumerateDocumentFiles(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || !Directory.Exists(storagePath))
            yield break;

        foreach (var folder in DocumentFolders)
        {
            var root = Path.Combine(storagePath, folder);
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                yield return (file, Path.GetRelativePath(storagePath, file).Replace('\\', '/'));
        }
    }

    /// <summary>True when an archive-relative path lies inside one of the document folders.</summary>
    private static bool IsDocumentPath(string relativePath)
    {
        var separator = relativePath.IndexOfAny(new[] { '/', '\\' });
        if (separator <= 0 || separator == relativePath.Length - 1)
            return false;

        var firstSegment = relativePath[..separator];
        return DocumentFolders.Any(f => string.Equals(f, firstSegment, StringComparison.OrdinalIgnoreCase));
    }

    // ── Private: SQLite Online Backup ──────────────────────────────────────

    /// <summary>
    /// Uses the SQLite Online Backup API (<c>SqliteConnection.BackupDatabase</c>) to obtain
    /// a consistent, lock-safe snapshot of the live database into <paramref name="tempPath"/>.
    /// The caller deletes the copy when done.
    /// </summary>
    private async Task CreateSqliteBackupCopyAsync(string tempPath, CancellationToken ct)
    {
        var dbPath = ResolveDatabasePathForCopy();

        Log.Debug("Creating SQLite backup copy at {TempPath}", tempPath);

        // The Online Backup API must be called synchronously on the same connection;
        // wrap in Task.Run to avoid blocking the calling thread.
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Backups preserve the source's key: a backup of an encrypted DB stays encrypted.
            // Users restoring on a different machine need the matching key material (passphrase
            // for UserPassphrase mode, or a matching Windows user profile for DpapiWrapped).
            using var source = _connectionFactory.OpenKeyed(dbPath);
            using var destination = _connectionFactory.OpenKeyed(tempPath);
            try
            {
                source.BackupDatabase(destination);
            }
            finally
            {
                // OpenKeyed connections are pooled. Clearing the pools makes the disposes below
                // really close the files: a pooled handle blocked reading and deleting the copy
                // on Windows (leaving a database copy in %TEMP%) and kept the live file open.
                SqliteConnection.ClearPool(source);
                SqliteConnection.ClearPool(destination);
            }
        }, ct).ConfigureAwait(false);

        Log.Debug("SQLite backup copy created ({Bytes} bytes)", new FileInfo(tempPath).Length);
    }

    // ── Private: restore steps ─────────────────────────────────────────────

    private static ZipArchive OpenArchiveForRestore(string zipPath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                "The backup archive failed validation. It may be corrupt or in an unrecognised format.", ex);
        }

        if (!TryValidateArchive(archive, out var reason))
        {
            archive.Dispose();
            Log.Warning("Restore validation failed: {Reason}", reason);
            throw new InvalidOperationException(
                "The backup archive failed validation. It may be corrupt or in an unrecognised format.");
        }

        return archive;
    }

    private static void DecryptArchiveToFile(string sourcePath, string targetPath, string password, CancellationToken ct)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        BackupArchiveCrypto.DecryptToStream(input, output, password, ct);
    }

    private static async Task<long> ExtractEntryAsync(ZipArchiveEntry entry, string targetPath, long limit, CancellationToken ct)
    {
        await using var entryStream = entry.Open();
        await using var fileStream = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None, AppConstants.FileStreamBufferSize, useAsync: true);
        return await CopyEntryWithLimitAsync(entryStream, fileStream, limit, entry.FullName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the staged database match this installation's encryption state and verifies it.
    /// A plaintext backup restored while encryption is on is re-encrypted with the current key,
    /// so the restored vault stays protected. An encrypted backup can only be restored where the
    /// same key is in use. Returns the file to swap in.
    /// </summary>
    private string PrepareStagedDatabase(RestorePaths paths)
    {
        var staged = paths.StagedDatabase;
        var stagedPlaintext = SqliteFileInspector.HasPlaintextHeader(staged);
        if (!stagedPlaintext && !SqliteFileInspector.LooksEncrypted(staged))
            throw new InvalidOperationException("The backup's database is damaged: it is not a SQLite database.");

        var installationEncrypted = IsInstallationEncrypted(paths.Database);
        if (installationEncrypted && stagedPlaintext)
        {
            ConvertPlaintextToCurrentKey(staged, paths.ConvertedDatabase);
            staged = paths.ConvertedDatabase;
        }
        else if (!installationEncrypted && !stagedPlaintext)
        {
            throw new InvalidOperationException(
                "This backup's database is encrypted, but database encryption is not turned on for this installation, " +
                "so its key is not available. Restore it with the Agent-X installation and Windows account that created it.");
        }

        VerifyDatabaseFile(staged);
        return staged;
    }

    /// <summary>
    /// The live database file tells whether this installation is encrypted: the connection
    /// factory applies the key that opens it. An empty or missing file follows the factory.
    /// </summary>
    private static bool IsInstallationEncrypted(string liveDatabase)
    {
        var info = new FileInfo(liveDatabase);
        if (info.Exists && info.Length > 0)
            return !SqliteFileInspector.HasPlaintextHeader(liveDatabase);

        return false;
    }

    private void ConvertPlaintextToCurrentKey(string plaintextPath, string targetPath)
    {
        DeleteSidecarsAndFile(targetPath);

        // OpenKeyed applies the current key to the new, empty file; sqlcipher_export then copies
        // schema and data from the attached plaintext copy (KEY '' attaches it unencrypted).
        using var connection = _connectionFactory.OpenKeyed(targetPath);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"ATTACH DATABASE '{plaintextPath.Replace("'", "''")}' AS backup KEY ''; " +
                "SELECT sqlcipher_export('main', 'backup'); " +
                "DETACH DATABASE backup;";
            command.ExecuteNonQuery();
        }
        finally
        {
            SqliteConnection.ClearPool(connection);
        }
    }

    private void VerifyDatabaseFile(string path)
    {
        using var connection = _connectionFactory.OpenKeyed(path);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var check = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The backup's database is damaged (integrity check: {check}).");

            command.CommandText =
                "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('documents', 'conversations', 'workflows');";
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) < 3)
                throw new InvalidOperationException("The backup does not contain an Agent-X database.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNotADatabase)
        {
            throw new InvalidOperationException(
                "The backup's database cannot be opened with this installation's database key. " +
                "It was made on another installation or Windows account.", ex);
        }
        finally
        {
            SqliteConnection.ClearPool(connection);
        }
    }

    private async Task<List<StagedDocument>> StageDocumentsAsync(
        ZipArchive archive, RestorePaths paths, List<string> warnings, CancellationToken ct)
    {
        var staged = new List<StagedDocument>();
        long totalBytes = 0;
        var skipped = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (!entry.FullName.StartsWith(DocumentsEntryPrefix, StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith('/'))
                continue;

            var relativePath = entry.FullName[DocumentsEntryPrefix.Length..];

            // Older backups copied the whole storage folder, including settings.json and the
            // encryption marker. Restoring those under running services broke the app, so only
            // document folders come back.
            if (!IsDocumentPath(relativePath) || paths.StoragePath is null)
            {
                skipped++;
                continue;
            }

            // Containment guard: never write outside the configured storage directory, even if
            // the archive was crafted with "../" traversal or rooted entry names. Validation
            // rejects such archives up front; this is defense in depth.
            var targetPath = PathHelper.ResolveContainedPath(paths.StoragePath, relativePath);
            var stagedPath = PathHelper.ResolveContainedPath(paths.DocumentStaging!, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);

            totalBytes += await ExtractEntryAsync(entry, stagedPath, MaxRestoredEntryBytes, ct).ConfigureAwait(false);
            if (totalBytes > MaxRestoredTotalBytes)
                throw new InvalidOperationException(
                    $"Restore aborted: total expanded document size exceeded the " +
                    $"{MaxRestoredTotalBytes / (1024L * 1024 * 1024)} GB safety limit.");

            staged.Add(new StagedDocument(stagedPath, targetPath, relativePath));
        }

        if (skipped > 0)
        {
            warnings.Add(
                $"{skipped} file(s) in the archive were not restored because they are not documents " +
                "(settings, keys, logs, models or caches saved by an older version).");
        }

        Log.Debug("Staged {Count} document files; skipped {Skipped}", staged.Count, skipped);
        return staged;
    }

    /// <summary>
    /// Replaces the live database with the verified staged file. The shared connection is
    /// released first (SQLite opens files without FILE_SHARE_DELETE, so any open handle makes
    /// the replace fail on Windows) and reopened with the current key afterwards. The replaced
    /// file is kept as the safety copy.
    /// </summary>
    private void SwapInDatabase(string restoredDatabase, RestorePaths paths)
    {
        var live = paths.Database;

        SharedDatabaseConnection.Release(_db);
        _db.ChangeTracker.Clear();

        try
        {
            // The sidecars belong to the database being replaced; replaying them against the
            // restored file would corrupt it. After a clean close they only remain when another
            // connection still holds the file, and then the swap cannot succeed on Windows.
            DeleteIfExists(live + "-wal");
            DeleteIfExists(live + "-shm");
            DeleteIfExists(live + "-journal");

            if (File.Exists(live))
                File.Replace(restoredDatabase, live, paths.SafetyCopy, ignoreMetadataErrors: true);
            else
                File.Move(restoredDatabase, live);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A replace that stopped halfway can leave the current database only at the backup name.
            if (!File.Exists(live) && File.Exists(paths.SafetyCopy))
                File.Move(paths.SafetyCopy, live);

            SharedDatabaseConnection.Reacquire(_db);
            throw new InvalidOperationException(
                "The current database could not be replaced because it is in use. Close other Agent-X windows " +
                "and wait for background work to finish, or restart Agent-X, then restore again. Nothing was changed.", ex);
        }

        SharedDatabaseConnection.Reacquire(_db);
        Log.Information("Restored database swapped in; previous database kept at {SafetyCopy} until verified", paths.SafetyCopy);
    }

    /// <summary>
    /// Puts the pre-restore database back. Returns null on success, or the path where the
    /// previous database is kept when it could not be moved back.
    /// </summary>
    private string? RollBackDatabase(RestorePaths paths)
    {
        try
        {
            SharedDatabaseConnection.Release(_db);
            _db.ChangeTracker.Clear();

            if (File.Exists(paths.SafetyCopy))
            {
                DeleteIfExists(paths.Database + "-wal");
                DeleteIfExists(paths.Database + "-shm");
                DeleteIfExists(paths.Database + "-journal");
                File.Move(paths.SafetyCopy, paths.Database, overwrite: true);
            }

            Log.Warning("Restore failed after the database was replaced; the previous database was put back");
            return null;
        }
        catch (Exception ex)
        {
            paths.KeepSafetyCopy = true;
            Log.Fatal(ex, "Could not put the previous database back; it is kept at {SafetyCopy}", paths.SafetyCopy);
            return paths.SafetyCopy;
        }
        finally
        {
            try
            {
                SharedDatabaseConnection.Reacquire(_db);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not reopen the database after rolling back a restore");
            }
        }
    }

    private static void InstallDocuments(
        IReadOnlyList<StagedDocument> staged, RestorePaths paths, List<InstalledDocument> installed)
    {
        foreach (var document in staged)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(document.TargetPath)!);

            // An existing file is moved aside first so a failure later can put it back.
            string? displaced = null;
            if (File.Exists(document.TargetPath))
            {
                displaced = PathHelper.ResolveContainedPath(paths.DocumentRollback!, document.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(displaced)!);
                File.Move(document.TargetPath, displaced, overwrite: true);
            }

            installed.Add(new InstalledDocument(document.TargetPath, displaced));
            File.Move(document.StagedPath, document.TargetPath);
        }
    }

    /// <summary>
    /// Undoes <see cref="InstallDocuments"/> in reverse order. Returns false when a displaced
    /// original could not be moved back; it then stays in the rollback folder.
    /// </summary>
    private static bool RollBackDocuments(List<InstalledDocument> installed)
    {
        var allRestored = true;
        for (var i = installed.Count - 1; i >= 0; i--)
        {
            var document = installed[i];
            try
            {
                if (document.DisplacedPath is not null)
                {
                    if (File.Exists(document.DisplacedPath))
                        File.Move(document.DisplacedPath, document.TargetPath, overwrite: true);
                }
                else if (File.Exists(document.TargetPath))
                {
                    File.Delete(document.TargetPath);
                }
            }
            catch (Exception ex)
            {
                allRestored = false;
                Log.Error(ex, "Could not roll back restored document {TargetPath}", document.TargetPath);
            }
        }

        return allRestored;
    }

    // ── Private: AES-256 encryption/decryption ─────────────────────────────

    /// <summary>
    /// Encrypts backup bytes in the current archive format (V3, see
    /// <see cref="BackupArchiveCrypto"/>). Public to mirror <see cref="DecryptBytes"/> and to
    /// support round-trip testing; backups themselves are encrypted while they stream to disk.
    /// </summary>
    public static byte[] EncryptBytes(byte[] plaintext, string password)
        => BackupArchiveCrypto.Encrypt(plaintext, password);

    /// <summary>
    /// Decrypts a backup archive in any supported format: V3 (streamed AES-256-GCM), V2
    /// (one-shot AES-256-GCM) or legacy V1 (AES-256-CBC). The format is selected by the magic
    /// header so existing backups remain restorable. Throws <see cref="InvalidOperationException"/>
    /// when the password is wrong or the archive fails authentication (tamper detected).
    /// </summary>
    public static byte[] DecryptBytes(byte[] cipherData, string password)
        => BackupArchiveCrypto.Decrypt(cipherData, password);

    // ── Private: scheduled backup retention ───────────────────────────────

    private async Task EnforceRetentionPolicyAsync(int maxToKeep, CancellationToken ct)
    {
        if (maxToKeep <= 0)
            return;

        var scheduledBackups = await _db.Backups
            .Where(b => b.BackupType == "scheduled")
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (scheduledBackups.Count <= maxToKeep)
            return;

        var toDelete = scheduledBackups.Skip(maxToKeep).ToList();

        Log.Information(
            "Retention policy: removing {Count} oldest scheduled backup(s) (limit={Limit})",
            toDelete.Count, maxToKeep);

        foreach (var record in toDelete)
        {
            await DeleteBackupAsync(record.Id).ConfigureAwait(false);
        }
    }

    // ── Private: schedule config helpers ──────────────────────────────────

    private async Task<BackupScheduleConfig> LoadScheduleConfigAsync()
    {
        try
        {
            var json = await _settingsService.GetValueAsync<string>("BackupScheduleConfig")
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(json))
            {
                var config = JsonSerializer.Deserialize<BackupScheduleConfig>(json, ManifestJsonOptions);
                if (config is not null)
                    return config;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load backup schedule config; using defaults");
        }

        return new BackupScheduleConfig
        {
            Enabled = false,
            IntervalHours = 168,
            MaxBackupsToKeep = 5,
            DestinationPath = GetDefaultStoragePath(),
        };
    }

    // ── Private: path helpers ──────────────────────────────────────────────

    private static string GetDefaultStoragePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentX");

    /// <summary>
    /// The database to copy. The shared context's own file when it is file-backed; the default
    /// app database otherwise (the connection factory decides what is opened).
    /// </summary>
    private string ResolveDatabasePathForCopy()
        => _databasePathOverride
           ?? SharedDatabaseConnection.TryGetDatabasePath(_db)
           ?? PathHelper.GetDatabasePath();

    /// <summary>
    /// The live database file a restore replaces. Never falls back to a default path: replacing
    /// a file the context does not use would swap the wrong database.
    /// </summary>
    private string ResolveLiveDatabaseFile()
        => _databasePathOverride
           ?? SharedDatabaseConnection.TryGetDatabasePath(_db)
           ?? throw new InvalidOperationException("Restore needs the file-backed application database.");

    private string ResolveDestinationDirectory(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath;

        return GetDefaultStoragePath();
    }

    /// <summary>Two backups in the same second must not overwrite each other.</summary>
    private static string UniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    // ── Private: utility helpers ───────────────────────────────────────────

    private static void Report(
        IProgress<BackupProgress>? progress,
        string phase,
        int percent,
        string? currentItem = null)
    {
        progress?.Report(new BackupProgress
        {
            Phase = phase,
            PercentComplete = Math.Clamp(percent, 0, 100),
            CurrentItem = currentItem,
        });
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteSidecarsAndFile(string path)
    {
        DeleteFileQuietly(path + "-wal");
        DeleteFileQuietly(path + "-shm");
        DeleteFileQuietly(path + "-journal");
        DeleteFileQuietly(path);
    }

    private static void DeleteFileQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete temporary file {Path}", path);
        }
    }

    private static void DeleteDirectoryQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete temporary folder {Path}", path);
        }
    }

    // ── Private: restore bookkeeping ───────────────────────────────────────

    private sealed record StagedDocument(string StagedPath, string TargetPath, string RelativePath);

    private sealed record InstalledDocument(string TargetPath, string? DisplacedPath);

    /// <summary>
    /// Every file a restore creates. The staged database and the decrypted archive sit next to
    /// the live database (same volume, so the swap is a rename); document staging sits inside
    /// the storage folder for the same reason.
    /// </summary>
    private sealed class RestorePaths
    {
        public RestorePaths(string database, string? storagePath)
        {
            Database = database;
            StagedDatabase = database + ".restore.tmp";
            ConvertedDatabase = database + ".restore-keyed.tmp";
            SafetyCopy = database + ".pre-restore";

            if (!string.IsNullOrWhiteSpace(storagePath))
            {
                StoragePath = Path.GetFullPath(storagePath);
                DocumentStaging = Path.Combine(StoragePath, ".restore-staging");
                DocumentRollback = Path.Combine(StoragePath, ".restore-rollback");
            }
        }

        public string Database { get; }
        public string StagedDatabase { get; }
        public string ConvertedDatabase { get; }
        public string SafetyCopy { get; }
        public string? StoragePath { get; }
        public string? DocumentStaging { get; }
        public string? DocumentRollback { get; }

        /// <summary>Set when the previous database could not be put back; it must then survive.</summary>
        public bool KeepSafetyCopy { get; set; }

        public void PrepareWorkspace()
        {
            RemoveStaging();

            // Leftovers of an interrupted restore may hold someone's only copy of their previous
            // database or documents: keep them under a unique name rather than overwriting them.
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(SafetyCopy))
            {
                var kept = $"{SafetyCopy}-{stamp}";
                File.Move(SafetyCopy, kept);
                Log.Warning("Kept an earlier pre-restore database at {Path}", kept);
            }

            if (DocumentRollback is not null && Directory.Exists(DocumentRollback))
            {
                var kept = $"{DocumentRollback}-{stamp}";
                Directory.Move(DocumentRollback, kept);
                Log.Warning("Kept documents displaced by an earlier restore at {Path}", kept);
            }
        }

        public void DiscardSafetyCopy()
        {
            if (!KeepSafetyCopy)
                DeleteFileQuietly(SafetyCopy);
        }

        /// <summary>Removes the originals displaced by restored documents once they are not needed.</summary>
        public void DiscardDocumentRollback() => DeleteDirectoryQuietly(DocumentRollback);

        public void RemoveStaging()
        {
            DeleteSidecarsAndFile(StagedDatabase);
            DeleteSidecarsAndFile(ConvertedDatabase);
            DeleteDirectoryQuietly(DocumentStaging);
        }
    }
}

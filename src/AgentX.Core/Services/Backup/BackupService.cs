using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentX.Core.Constants;
using AgentX.Core.Data;
using AgentX.Core.Helpers;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Backup;

/// <summary>
/// Full implementation of <see cref="IBackupService"/>.
///
/// Backup format: a ZIP archive with the extension .agentxbak containing:
///   database/agentx.db  — SQLite Online Backup API copy (no lock contention)
///   manifest.json       — metadata (version, counts, timestamps, app version)
///   documents/          — optional copy of files in the configured storage path
///
/// Encryption: when a password is supplied the raw ZIP bytes are encrypted with
/// AES-256-GCM authenticated encryption (V2 format) using a PBKDF2-derived key
/// (100,000 iterations, SHA-256), a random salt, and a random 96-bit nonce. The GCM
/// authentication tag lets restore detect a wrong password or a tampered archive before
/// it is opened. Legacy AES-256-CBC archives (V1) remain restorable for backward
/// compatibility; see <see cref="DecryptBytes"/>.
/// </summary>
public sealed class BackupService : IBackupService
{
    // ── Constants ──────────────────────────────────────────────────────────

    private const string AppVersion = "1.2.0";
    private const string DbEntryName = "database/agentx.db";
    private const string ManifestEntryName = "manifest.json";
    private const string DocumentsEntryPrefix = "documents/";
    private const string BackupExtension = ".agentxbak";
    private const int AesKeySize = AppConstants.AesKeySizeBits;         // bits
    private const int AesBlockSize = AppConstants.AesBlockSizeBits;     // bits
    private const int Pbkdf2Iterations = AppConstants.Pbkdf2Iterations;
    private const int SaltSize = AppConstants.PbkdfSaltBytes;          // bytes
    private const int IvSize = AppConstants.IvSizeBytes;               // bytes (legacy CBC)
    private const int GcmNonceSize = AppConstants.GcmNonceBytes;       // bytes (96-bit)
    private const int GcmTagSize = AppConstants.GcmTagBytes;           // bytes (128-bit)

    // Restore safety limits — defense-in-depth against archive ("zip") bombs. Generous
    // enough to accommodate real document libraries while blocking pathological expansion.
    private const long MaxRestoredEntryBytes = 2L * 1024 * 1024 * 1024;   // 2 GB per file
    private const long MaxRestoredTotalBytes = 50L * 1024 * 1024 * 1024;  // 50 GB total

    // ── Fields ─────────────────────────────────────────────────────────────

    private readonly AgentXDbContext _db;
    private readonly ISettingsService _settingsService;
    private readonly IEncryptedConnectionFactory _connectionFactory;

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
    /// required dependency — PRAGMA key is applied through it to the source and destination
    /// connections of the SQLite Online Backup API.
    /// </summary>
    public BackupService(
        AgentXDbContext dbContext,
        ISettingsService settingsService,
        IEncryptedConnectionFactory connectionFactory)
    {
        _db = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
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
        Log.Information("Starting backup. Type={BackupType} Destination={Destination}",
            options.BackupType, options.DestinationPath);

        try
        {
            // Resolve destination directory
            var destinationDir = ResolveDestinationDirectory(options.DestinationPath);
            Directory.CreateDirectory(destinationDir);

            // Build archive file name
            var timestamp = DateTime.UtcNow;
            var fileName = $"agentx-backup-{timestamp:yyyy-MM-dd-HHmmss}{BackupExtension}";
            var archivePath = Path.Combine(destinationDir, fileName);

            // ── Phase 1: gather counts for the manifest ────────────────────
            Report(progress, "Gathering statistics", 5);
            ct.ThrowIfCancellationRequested();

            var docCount = await _db.Documents.CountAsync(ct).ConfigureAwait(false);
            var convCount = await _db.Conversations.CountAsync(ct).ConfigureAwait(false);
            var workflowCount = await _db.Workflows.CountAsync(ct).ConfigureAwait(false);

            // ── Phase 2: copy database via SQLite Online Backup API ─────────
            Report(progress, "Copying database", 15);
            ct.ThrowIfCancellationRequested();

            var dbTempPath = await CreateSqliteBackupCopyAsync(ct).ConfigureAwait(false);

            try
            {
                // ── Phase 3: build the ZIP in-memory then write to disk ─────
                Report(progress, "Writing archive", 35);
                ct.ThrowIfCancellationRequested();

                var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
                var storagePath = settings.StoragePath;

                // Write the raw ZIP to a MemoryStream first so we can optionally encrypt it.
                using var zipBuffer = new MemoryStream();
                await BuildZipArchiveAsync(
                    zipBuffer, dbTempPath, storagePath, timestamp, docCount, convCount,
                    workflowCount, options, progress, ct).ConfigureAwait(false);

                // ── Phase 4: encrypt or write raw ──────────────────────────
                Report(progress, "Finalising archive", 85);
                ct.ThrowIfCancellationRequested();

                var zipBytes = zipBuffer.ToArray();

                if (!string.IsNullOrEmpty(options.EncryptionPassword))
                {
                    Report(progress, "Encrypting archive", 90);
                    var encryptedBytes = EncryptBytes(zipBytes, options.EncryptionPassword);
                    await File.WriteAllBytesAsync(archivePath, encryptedBytes, ct).ConfigureAwait(false);
                    Log.Debug("Backup archive encrypted ({Bytes} bytes)", encryptedBytes.Length);
                }
                else
                {
                    await File.WriteAllBytesAsync(archivePath, zipBytes, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                // Always clean up the temp DB copy
                DeleteFileQuietly(dbTempPath);
            }

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
                "Backup completed in {DurationMs:F0} ms. File={FileName} Size={SizeMB:F2} MB Id={Id}",
                sw.Elapsed.TotalMilliseconds, fileName, sizeMb, entity.Id);

            return new BackupResult
            {
                Success = true,
                BackupFilePath = archivePath,
                SizeMB = sizeMb,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                BackupId = entity.Id,
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
    }

    // ── IBackupService: RestoreFromBackupAsync ─────────────────────────────

    /// <inheritdoc />
    public async Task<RestoreResult> RestoreFromBackupAsync(
        string backupFilePath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        Log.Information("Starting restore from {BackupFilePath}", backupFilePath);

        try
        {
            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("Backup file not found.", backupFilePath);

            // ── Phase 1: validate the archive first ────────────────────────
            Report(progress, "Validating archive", 5);
            ct.ThrowIfCancellationRequested();

            var isValid = await ValidateBackupAsync(backupFilePath).ConfigureAwait(false);
            if (!isValid)
                throw new InvalidOperationException("The backup archive failed validation. It may be corrupt or in an unrecognised format.");

            // ── Phase 2: read the archive bytes ────────────────────────────
            Report(progress, "Reading archive", 15);
            ct.ThrowIfCancellationRequested();

            var archiveBytes = await File.ReadAllBytesAsync(backupFilePath, ct).ConfigureAwait(false);

            // ── Phase 3: determine if the archive is encrypted ─────────────
            // Encrypted archives begin with our magic header "AGXENC\0"
            byte[] zipBytes;
            if (IsEncryptedArchive(archiveBytes))
            {
                // We cannot decrypt without a password here; the caller must supply it.
                // A separate overload or UI flow handles encrypted restores. For the base
                // interface we treat this as an error and guide the caller appropriately.
                throw new InvalidOperationException(
                    "The backup archive is encrypted. Use RestoreFromEncryptedBackupAsync and supply the encryption password.");
            }
            else
            {
                zipBytes = archiveBytes;
            }

            // ── Phase 4: extract database from ZIP ─────────────────────────
            Report(progress, "Extracting database", 35);
            ct.ThrowIfCancellationRequested();

            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            var dbPath = GetDatabasePath();
            var dbBackupPath = dbPath + ".restore-backup";

            // Keep a safety copy of the current database in case extraction fails
            if (File.Exists(dbPath))
                File.Copy(dbPath, dbBackupPath, overwrite: true);

            try
            {
                using var zipStream = new MemoryStream(zipBytes);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

                var dbEntry = archive.GetEntry(DbEntryName)
                    ?? throw new InvalidOperationException($"Archive is missing the required entry '{DbEntryName}'.");

                // Close all EF connections before replacing the file
                await _db.Database.CloseConnectionAsync().ConfigureAwait(false);

                Report(progress, "Replacing database", 55);
                ct.ThrowIfCancellationRequested();

                using (var entryStream = dbEntry.Open())
                using (var fileStream = new FileStream(dbPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
                }

                // ── Phase 5: optionally restore document files ─────────────
                Report(progress, "Restoring document files", 70);
                ct.ThrowIfCancellationRequested();

                var storagePath = settings.StoragePath;
                var docEntriesRestored = 0;
                long totalRestoredBytes = 0;

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!entry.FullName.StartsWith(DocumentsEntryPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (entry.FullName.EndsWith('/'))
                        continue; // directory entry

                    var relativePath = entry.FullName[DocumentsEntryPrefix.Length..];

                    // Containment guard: never write outside the configured storage directory,
                    // even if the archive was crafted with "../" traversal or rooted entry names.
                    // ValidateBackupAsync rejects such archives up front; this is defense in depth
                    // and throws (rolling back the restore) if a malicious entry slips through.
                    var targetPath = PathHelper.ResolveContainedPath(storagePath, relativePath);
                    var targetDir = Path.GetDirectoryName(targetPath)!;

                    Directory.CreateDirectory(targetDir);

                    using (var entryStream = entry.Open())
                    using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, AppConstants.FileStreamBufferSize, useAsync: true))
                    {
                        // Enforce a per-file byte cap while copying so a forged entry length
                        // cannot drive unbounded writes.
                        var written = await CopyEntryWithLimitAsync(entryStream, fileStream, MaxRestoredEntryBytes, ct)
                            .ConfigureAwait(false);

                        totalRestoredBytes += written;
                        if (totalRestoredBytes > MaxRestoredTotalBytes)
                            throw new InvalidOperationException(
                                $"Restore aborted — total expanded document size exceeded the " +
                                $"{MaxRestoredTotalBytes / (1024L * 1024 * 1024)} GB safety limit.");
                    }

                    docEntriesRestored++;
                }

                if (docEntriesRestored == 0)
                    warnings.Add("No document files were found in the archive. The archive may not have included documents.");

                Log.Debug("Restored {Count} document files from archive", docEntriesRestored);
            }
            catch
            {
                // Restore the pre-restore database copy so the application is not left in a broken state
                if (File.Exists(dbBackupPath))
                {
                    try
                    {
                        File.Copy(dbBackupPath, dbPath, overwrite: true);
                        Log.Warning("Restore failed — original database was recovered from safety copy");
                    }
                    catch (Exception rollbackEx)
                    {
                        Log.Error(rollbackEx, "Failed to roll back database to pre-restore state");
                    }
                }

                throw;
            }
            finally
            {
                DeleteFileQuietly(dbBackupPath);
            }

            // ── Phase 6: reopen EF connection and count restored records ────
            Report(progress, "Verifying restored data", 88);
            ct.ThrowIfCancellationRequested();

            // Force EF to reconnect to the newly swapped-in database
            await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

            var restoredDocs = await _db.Documents.CountAsync(ct).ConfigureAwait(false);
            var restoredConvs = await _db.Conversations.CountAsync(ct).ConfigureAwait(false);
            var restoredWorkflows = await _db.Workflows.CountAsync(ct).ConfigureAwait(false);

            sw.Stop();
            Report(progress, "Complete", 100);

            Log.Information(
                "Restore completed in {DurationMs:F0} ms. Docs={Docs} Convs={Convs} Workflows={Workflows}",
                sw.Elapsed.TotalMilliseconds, restoredDocs, restoredConvs, restoredWorkflows);

            return new RestoreResult
            {
                Success = true,
                RestoredDocumentCount = restoredDocs,
                RestoredConversationCount = restoredConvs,
                RestoredWorkflowCount = restoredWorkflows,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                WarningMessages = warnings,
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
            Log.Warning("Backup record {BackupId} not found — nothing to delete", backupId);
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
                Log.Warning(ex, "Could not delete backup archive file {FilePath} — removing history record anyway", entity.FilePath);
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
        var dbPath = GetDatabasePath();
        var storagePath = settings.StoragePath;

        var dbSizeBytes = File.Exists(dbPath)
            ? new FileInfo(dbPath).Length
            : 0L;

        var docsFolderBytes = 0L;
        if (Directory.Exists(storagePath))
        {
            docsFolderBytes = Directory
                .EnumerateFiles(storagePath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase))
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }

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
            "Size estimate — DB: {DbMb:F2} MB, Docs: {DocsMb:F2} MB, Total: {TotalMb:F2} MB, Documents: {DocCount}",
            estimate.DatabaseSizeMB, estimate.DocumentsSizeMB, estimate.TotalEstimatedMB, estimate.DocumentCount);

        return estimate;
    }

    // ── IBackupService: ValidateBackupAsync ───────────────────────────────

    /// <inheritdoc />
    public async Task<bool> ValidateBackupAsync(string backupFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        Log.Debug("Validating backup archive {BackupFilePath}", backupFilePath);

        try
        {
            if (!File.Exists(backupFilePath))
            {
                Log.Warning("Validation failed — file does not exist: {BackupFilePath}", backupFilePath);
                return false;
            }

            var archiveBytes = await File.ReadAllBytesAsync(backupFilePath).ConfigureAwait(false);

            if (archiveBytes.Length == 0)
            {
                Log.Warning("Validation failed — file is empty: {BackupFilePath}", backupFilePath);
                return false;
            }

            // Encrypted archives cannot be validated further without a password.
            // Report them as structurally valid (they exist and have content).
            if (IsEncryptedArchive(archiveBytes))
            {
                Log.Debug("Archive is encrypted — structural validation only (no password provided)");
                return true;
            }

            // For plain archives open the ZIP and verify the required entries are present.
            using var zipStream = new MemoryStream(archiveBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

            var hasDb = archive.GetEntry(DbEntryName) is not null;
            var hasManifest = archive.GetEntry(ManifestEntryName) is not null;

            if (!hasDb || !hasManifest)
            {
                Log.Warning(
                    "Validation failed — archive missing required entries. HasDb={HasDb} HasManifest={HasManifest}",
                    hasDb, hasManifest);
                return false;
            }

            // Reject archives whose document entries use traversal/rooted paths or exceed
            // the restore size limits — before any extraction touches the filesystem.
            if (!TryValidateDocumentEntries(archive, out var entryReason))
            {
                Log.Warning("Validation failed — {Reason}", entryReason);
                return false;
            }

            Log.Debug("Archive validation passed");
            return true;
        }
        catch (InvalidDataException ex)
        {
            Log.Warning(ex, "Validation failed — archive is not a valid ZIP: {BackupFilePath}", backupFilePath);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Validation failed unexpectedly for {BackupFilePath}", backupFilePath);
            return false;
        }
    }

    /// <summary>
    /// Validates that every <c>documents/</c> entry in the archive uses a safe, contained
    /// relative path (no <c>..</c> traversal, no rooted paths) and does not exceed the
    /// per-entry / total restore size limits. Pure and side-effect free so it guards both
    /// <see cref="ValidateBackupAsync"/> and is directly unit-testable.
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

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> while enforcing a hard
    /// byte cap, aborting with an <see cref="InvalidOperationException"/> if the cap is exceeded.
    /// Guards against forged ZIP entry lengths during restore.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    private static async Task<long> CopyEntryWithLimitAsync(
        Stream source, Stream destination, long perEntryLimit, CancellationToken ct)
    {
        var buffer = new byte[AppConstants.FileStreamBufferSize];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > perEntryLimit)
                throw new InvalidOperationException(
                    $"Document entry exceeds the per-file restore limit of " +
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
            Log.Information("Scheduled backups are disabled — loop not started");
            return;
        }

        _scheduledCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loopCt = _scheduledCts.Token;

        // Fire and forget — the loop runs on the thread pool
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
                        Notes = $"Automatic scheduled backup — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
                    };

                    var result = await CreateBackupAsync(options, progress: null, ct).ConfigureAwait(false);

                    if (result.Success)
                    {
                        Log.Information(
                            "Scheduled backup succeeded: {FilePath} ({SizeMB:F2} MB)",
                            result.BackupFilePath, result.SizeMB);

                        // Enforce retention limit — delete the oldest scheduled backups beyond the cap
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
                    Log.Error(ex, "Unhandled error during scheduled backup cycle — loop continues");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Scheduled backup loop exiting due to cancellation");
        }
    }

    // ── Private: ZIP archive builder ───────────────────────────────────────

    private async Task BuildZipArchiveAsync(
        Stream outputStream,
        string dbTempPath,
        string storagePath,
        DateTime timestamp,
        int docCount,
        int convCount,
        int workflowCount,
        BackupOptions options,
        IProgress<BackupProgress>? progress,
        CancellationToken ct)
    {
        using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

        // ── 1. Database file ───────────────────────────────────────────────
        Report(progress, "Adding database to archive", 40);
        ct.ThrowIfCancellationRequested();

        var dbEntry = archive.CreateEntry(DbEntryName, CompressionLevel.Optimal);
        using (var entryStream = dbEntry.Open())
        using (var dbStream = new FileStream(dbTempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
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
            createdAt = timestamp.ToString("O"),
            backupType = options.BackupType,
            documentCount = docCount,
            conversationCount = convCount,
            workflowCount = workflowCount,
            includesDocuments = options.IncludeDocuments,
            notes = options.Notes,
        };

        var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
        using (var entryStream = manifestEntry.Open())
        {
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            await entryStream.WriteAsync(manifestBytes, ct).ConfigureAwait(false);
        }

        // ── 3. Document files ──────────────────────────────────────────────
        if (options.IncludeDocuments && Directory.Exists(storagePath))
        {
            Report(progress, "Adding document files", 65);

            var documentFiles = Directory
                .EnumerateFiles(storagePath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (var i = 0; i < documentFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var filePath = documentFiles[i];
                var relativePath = Path.GetRelativePath(storagePath, filePath)
                    .Replace('\\', '/');

                var entryName = DocumentsEntryPrefix + relativePath;
                var fileEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                using var entryStream = fileEntry.Open();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);

                // Report per-file progress within the 65–82% band
                var pct = 65 + (int)((i + 1) / (double)documentFiles.Count * 17);
                Report(progress, "Adding document files", pct, Path.GetFileName(filePath));
            }

            Log.Debug("Added {Count} document files to archive", documentFiles.Count);
        }
    }

    // ── Private: SQLite Online Backup ──────────────────────────────────────

    /// <summary>
    /// Uses the SQLite Online Backup API (<c>SqliteConnection.BackupDatabase</c>) to obtain
    /// a consistent, lock-safe snapshot of the live database. The copy is written to a
    /// temporary file in the system temp directory and the caller is responsible for
    /// deleting it when done.
    /// </summary>
    private async Task<string> CreateSqliteBackupCopyAsync(CancellationToken ct)
    {
        var dbPath = GetDatabasePath();
        var tempPath = Path.Combine(Path.GetTempPath(), $"agentx-dbcopy-{Guid.NewGuid():N}.tmp");

        Log.Debug("Creating SQLite backup copy at {TempPath}", tempPath);

        // The Online Backup API must be called synchronously on the same connection;
        // wrap in Task.Run to avoid blocking the calling thread.
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Backups preserve the source's key — a backup of an encrypted DB stays encrypted.
            // Users restoring on a different machine need the matching key material (passphrase
            // for UserPassphrase mode, or a matching Windows user profile for DpapiWrapped).
            using var source = _connectionFactory.OpenKeyed(dbPath);
            using var destination = _connectionFactory.OpenKeyed(tempPath);

            // BackupDatabase is synchronous but very fast for local files
            source.BackupDatabase(destination);
        }, ct).ConfigureAwait(false);

        Log.Debug("SQLite backup copy created ({Bytes} bytes)", new FileInfo(tempPath).Length);
        return tempPath;
    }

    // ── Private: AES-256 encryption/decryption ─────────────────────────────

    // Encrypted archive byte layouts:
    //   V2 (current, authenticated): magic "AGXENC2\0" (8) + salt(16) + nonce(12) + tag(16) + AES-256-GCM ciphertext
    //   V1 (legacy, restore-only):   magic "AGXENC\0\0" (8) + salt(16) + iv(16)    + AES-256-CBC ciphertext

    private static readonly byte[] EncryptionMagic = Encoding.ASCII.GetBytes("AGXENC\0\0");   // V1 legacy CBC
    private static readonly byte[] EncryptionMagicV2 = Encoding.ASCII.GetBytes("AGXENC2\0");  // V2 authenticated GCM

    /// <summary>
    /// Encrypts backup bytes using AES-256-GCM authenticated encryption (V2 format). The GCM
    /// tag binds the ciphertext so a wrong password or a tampered archive is detected on restore
    /// (see <see cref="DecryptBytes"/>) rather than yielding corrupt plaintext. Public to mirror
    /// <see cref="DecryptBytes"/> and to support round-trip testing.
    /// </summary>
    public static byte[] EncryptBytes(byte[] plaintext, string password)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var key = DeriveKey(password, salt);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[GcmTagSize];

        using (var gcm = new AesGcm(key, GcmTagSize))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        // Layout: magic(8) + salt(16) + nonce(12) + tag(16) + ciphertext
        var result = new byte[EncryptionMagicV2.Length + SaltSize + GcmNonceSize + GcmTagSize + ciphertext.Length];
        var span = result.AsSpan();
        var offset = 0;
        EncryptionMagicV2.CopyTo(span[offset..]); offset += EncryptionMagicV2.Length;
        salt.CopyTo(span[offset..]); offset += SaltSize;
        nonce.CopyTo(span[offset..]); offset += GcmNonceSize;
        tag.CopyTo(span[offset..]); offset += GcmTagSize;
        ciphertext.CopyTo(span[offset..]);

        return result;
    }

    /// <summary>
    /// Decrypts a backup archive produced by <see cref="EncryptBytes"/> (AES-256-GCM, V2) or a
    /// legacy AES-256-CBC archive (V1). The format is selected by the archive's magic header so
    /// existing backups remain restorable. Throws <see cref="InvalidOperationException"/> when the
    /// password is wrong or — for V2 — the archive fails authentication (tamper detected).
    /// </summary>
    public static byte[] DecryptBytes(byte[] cipherData, string password)
    {
        ArgumentNullException.ThrowIfNull(cipherData);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (StartsWith(cipherData, EncryptionMagicV2))
            return DecryptGcm(cipherData, password);

        if (StartsWith(cipherData, EncryptionMagic))
            return DecryptLegacyCbc(cipherData, password);

        throw new InvalidOperationException("Data is not a recognised encrypted Agent-X backup archive.");
    }

    private static byte[] DecryptGcm(byte[] cipherData, string password)
    {
        var headerLen = EncryptionMagicV2.Length + SaltSize + GcmNonceSize + GcmTagSize;
        if (cipherData.Length < headerLen)
            throw new InvalidOperationException("Data is too short to be a valid encrypted archive.");

        var offset = EncryptionMagicV2.Length;
        var salt = cipherData[offset..(offset + SaltSize)]; offset += SaltSize;
        var nonce = cipherData[offset..(offset + GcmNonceSize)]; offset += GcmNonceSize;
        var tag = cipherData[offset..(offset + GcmTagSize)]; offset += GcmTagSize;
        var ciphertext = cipherData[offset..];

        var key = DeriveKey(password, salt);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var gcm = new AesGcm(key, GcmTagSize);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Backup decryption failed — the password is incorrect or the archive has been tampered with.", ex);
        }

        return plaintext;
    }

    private static byte[] DecryptLegacyCbc(byte[] cipherData, string password)
    {
        var headerLen = EncryptionMagic.Length + SaltSize + IvSize; // 40 bytes
        if (cipherData.Length < headerLen)
            throw new InvalidOperationException("Data is too short to be a valid encrypted archive.");

        var salt = cipherData[EncryptionMagic.Length..(EncryptionMagic.Length + SaltSize)];
        var iv = cipherData[(EncryptionMagic.Length + SaltSize)..headerLen];
        var ciphertext = cipherData[headerLen..];
        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.KeySize = AesKeySize;
        aes.BlockSize = AesBlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return kdf.GetBytes(AesKeySize / 8); // 32 bytes for AES-256
    }

    private static bool IsEncryptedArchive(byte[] data)
        => StartsWith(data, EncryptionMagicV2) || StartsWith(data, EncryptionMagic);

    private static bool StartsWith(byte[] data, byte[] prefix)
        => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

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
            Log.Warning(ex, "Failed to load backup schedule config — using defaults");
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

    private static string GetDatabasePath() =>
        Path.Combine(GetDefaultStoragePath(), "agentx.db");

    private string ResolveDestinationDirectory(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath;

        return GetDefaultStoragePath();
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
}

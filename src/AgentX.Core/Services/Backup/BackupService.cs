using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentX.Core.Constants;
using AgentX.Core.Data;
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
/// Encryption: when a password is supplied the raw ZIP bytes are AES-256-CBC encrypted
/// with a PBKDF2-derived key (100,000 iterations, SHA-256) and a random 16-byte IV.
/// The IV is prepended to the cipher stream so the restore path can locate it.
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
    private const int IvSize = AppConstants.IvSizeBytes;               // bytes

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

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!entry.FullName.StartsWith(DocumentsEntryPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (entry.FullName.EndsWith('/'))
                        continue; // directory entry

                    var relativePath = entry.FullName[DocumentsEntryPrefix.Length..];
                    var targetPath = Path.Combine(storagePath, relativePath);
                    var targetDir = Path.GetDirectoryName(targetPath)!;

                    Directory.CreateDirectory(targetDir);

                    using var entryStream = entry.Open();
                    using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                    await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

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

    // Encrypted archive byte layout:
    //   [0..7]   magic header: "AGXENC\0\0" (8 bytes)
    //   [8..23]  PBKDF2 salt  (16 bytes)
    //   [24..39] AES IV       (16 bytes)
    //   [40..]   AES-256-CBC cipher text

    private static readonly byte[] EncryptionMagic = Encoding.ASCII.GetBytes("AGXENC\0\0");

    private static byte[] EncryptBytes(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.KeySize = AesKeySize;
        aes.BlockSize = AesBlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        // Prefix: magic(8) + salt(16) + iv(16) = 40 bytes header
        var result = new byte[EncryptionMagic.Length + SaltSize + IvSize + ciphertext.Length];
        EncryptionMagic.CopyTo(result, 0);
        salt.CopyTo(result, EncryptionMagic.Length);
        iv.CopyTo(result, EncryptionMagic.Length + SaltSize);
        ciphertext.CopyTo(result, EncryptionMagic.Length + SaltSize + IvSize);

        return result;
    }

    /// <summary>
    /// Decrypts an AES-256-CBC encrypted backup produced by <see cref="EncryptBytes"/>.
    /// </summary>
    public static byte[] DecryptBytes(byte[] cipherData, string password)
    {
        ArgumentNullException.ThrowIfNull(cipherData);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

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
    {
        if (data.Length < EncryptionMagic.Length)
            return false;

        return data[..EncryptionMagic.Length].SequenceEqual(EncryptionMagic);
    }

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

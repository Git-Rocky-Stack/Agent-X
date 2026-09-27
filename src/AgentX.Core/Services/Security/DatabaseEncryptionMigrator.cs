using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AgentX.Core.Services.Security;

public sealed class DatabaseEncryptionMigrator : IDatabaseEncryptionMigrator
{
    private readonly IEncryptionStateFile? _stateFile;

    /// <summary>
    /// File-level migrator without marker awareness. <see cref="RecoverIfNeeded"/> then only
    /// performs the recoveries that do not depend on the encryption marker.
    /// </summary>
    public DatabaseEncryptionMigrator()
        : this(null)
    {
    }

    /// <summary>
    /// DI constructor. With the marker available, <see cref="RecoverIfNeeded"/> can also finish or
    /// roll back an interrupted migration and retire a marker whose database is plaintext.
    /// </summary>
    public DatabaseEncryptionMigrator(IEncryptionStateFile? stateFile)
    {
        _stateFile = stateFile;
    }

    public void RecoverIfNeeded(string dbPath)
    {
        var backupPath = dbPath + ".plain.bak";
        var tempPath = dbPath + ".enc.tmp";

        if (!File.Exists(dbPath) && File.Exists(backupPath))
        {
            // Kill-window recovery: main DB missing, backup exists -> restore.
            File.Move(backupPath, dbPath);
            Log.Warning("Recovered the plaintext database from an interrupted encryption migration");
        }
        else if (File.Exists(dbPath) && File.Exists(backupPath) && _stateFile is not null)
        {
            ResolveCompletedSwap(dbPath, backupPath);
        }

        // Clean up orphaned temp from any prior interrupted attempt.
        SafeDelete(tempPath);

        RetireStaleMarker(dbPath);
    }

    public Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key)
        => MigrateToEncryptedAsync(dbPath, key, commitAsync: null);

    public async Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key, Func<Task>? commitAsync)
    {
        if (string.IsNullOrWhiteSpace(key.HexKey))
            throw new ArgumentException("Key material is empty.", nameof(key));
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Plaintext database not found.", dbPath);
        if (new FileInfo(dbPath).Length > 0 && !SqliteFileInspector.HasPlaintextHeader(dbPath))
            throw new InvalidOperationException("The database is not a plaintext SQLite file; it may already be encrypted.");

        var tempEncryptedPath = dbPath + ".enc.tmp";
        var backupPath = dbPath + ".plain.bak";

        // Clean up any leftover temps from a prior interrupted migration. The backup path must be
        // free: a stale copy there would be mistaken for this run's rollback source.
        SafeDelete(tempEncryptedPath);
        DeleteOrThrow(backupPath);

        var movedAside = false;
        try
        {
            // Open plaintext source (no key), ATTACH an empty encrypted DB with a raw-bytes KEY,
            // invoke sqlcipher_export to copy schema + data, DETACH.
            using (var source = new SqliteConnection($"Data Source={dbPath}"))
            {
                await source.OpenAsync().ConfigureAwait(false);

                // Ensure WAL is flushed to the main DB file before export.
                // Without this, uncommitted WAL pages could be lost during encryption migration.
                using var checkpointCmd = source.CreateCommand();
                checkpointCmd.CommandText = "PRAGMA wal_checkpoint(FULL)";
                await checkpointCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                using var cmd = source.CreateCommand();
                cmd.CommandText = $@"
                    ATTACH DATABASE '{EscapeSingleQuotes(tempEncryptedPath)}' AS encrypted KEY ""x'{key.HexKey}'"";
                    SELECT sqlcipher_export('encrypted');
                    DETACH DATABASE encrypted;";
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Release file handles held by the Microsoft.Data.Sqlite connection pool.
            // Without this, File.Move below throws IOException on Windows.
            SqliteConnection.ClearAllPools();

            // Atomic swap: move plaintext aside, install encrypted, then verify. The plaintext
            // WAL sidecars belong to the file just moved aside; the export already read through
            // them, and they must not be replayed against the encrypted file.
            File.Move(dbPath, backupPath);
            movedAside = true;
            DeleteOrThrow(dbPath + "-wal");
            DeleteOrThrow(dbPath + "-shm");
            File.Move(tempEncryptedPath, dbPath);

            // Verification open: use PRAGMA key (NOT Password=), matches Correction #1.
            using (var verify = new SqliteConnection($"Data Source={dbPath}"))
            {
                await verify.OpenAsync().ConfigureAwait(false);

                using var keyCmd = verify.CreateCommand();
                keyCmd.CommandText = $@"PRAGMA key = ""x'{key.HexKey}'"";";
                await keyCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                using var probeCmd = verify.CreateCommand();
                probeCmd.CommandText = "SELECT count(*) FROM sqlite_master";
                await probeCmd.ExecuteScalarAsync().ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();

            // Commit point (normally: write the encryption marker). It runs while the plaintext
            // backup still exists, so a failed commit rolls the database back and the marker and
            // the file on disk never disagree.
            if (commitAsync is not null)
                await commitAsync().ConfigureAwait(false);
        }
        catch
        {
            // Rollback: if the plaintext backup exists but the DB path was replaced with an
            // incomplete encrypted file, restore the plaintext backup.
            SqliteConnection.ClearAllPools();

            if (movedAside && File.Exists(backupPath))
            {
                // If dbPath currently holds a half-written encrypted file, replace it.
                File.Move(backupPath, dbPath, overwrite: true);
            }

            SafeDelete(tempEncryptedPath);
            throw;
        }

        // Committed. From here on nothing may roll back, so cleanup is best effort. A backup that
        // survives here is removed by RecoverIfNeeded on the next start.
        SafeDelete(backupPath);
    }

    /// <summary>
    /// Both the database and the plaintext backup exist, so the swap finished. The marker decides
    /// which copy is authoritative: with a committed marker the encrypted file wins and the
    /// plaintext copy must not linger; without one the migration stopped before its commit and the
    /// plaintext copy still holds the data.
    /// </summary>
    private void ResolveCompletedSwap(string dbPath, string backupPath)
    {
        var markerCommitted = _stateFile!.Exists();
        var databaseEncrypted = SqliteFileInspector.LooksEncrypted(dbPath);
        var backupPlaintext = SqliteFileInspector.HasPlaintextHeader(backupPath);

        if (markerCommitted && databaseEncrypted)
        {
            SafeDelete(backupPath);
            Log.Information("Removed the plaintext copy left behind by a completed encryption migration");
        }
        else if (!markerCommitted && !SqliteFileInspector.HasPlaintextHeader(dbPath) && backupPlaintext)
        {
            File.Move(backupPath, dbPath, overwrite: true);
            Log.Warning("Rolled back an encryption migration that stopped before it was committed");
        }
        else
        {
            // Neither rule applies (for example both files are plaintext). Keep both: deleting
            // either could lose data, and the database itself still opens normally.
            Log.Warning("Found {BackupPath} next to the database but could not tell which copy is current; leaving both in place", backupPath);
        }
    }

    /// <summary>
    /// A marker that claims encryption for a database whose header is plaintext would make every
    /// start apply a key to a plaintext file and fail with SQLite error 26. Such a marker is stale
    /// (for example from a migration that failed after the marker was written), so move it aside
    /// and let the plaintext database open normally.
    /// </summary>
    private void RetireStaleMarker(string dbPath)
    {
        if (_stateFile is null || !_stateFile.Exists() || !SqliteFileInspector.HasPlaintextHeader(dbPath))
            return;

        var retiredTo = _stateFile.MoveAside();
        Log.Warning(
            "The encryption marker claimed an encrypted database but {DbPath} is plaintext; moved the stale marker to {RetiredTo}",
            dbPath, retiredTo);
    }

    private static void DeleteOrThrow(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup. If a file is still locked we will surface the issue on next run.
        }
    }

    private static string EscapeSingleQuotes(string s) => s.Replace("'", "''");
}

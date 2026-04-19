using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace AgentX.Core.Services.Security;

public sealed class DatabaseEncryptionMigrator : IDatabaseEncryptionMigrator
{
    public void RecoverIfNeeded(string dbPath)
    {
        var backupPath = dbPath + ".plain.bak";
        var tempPath = dbPath + ".enc.tmp";

        // Kill-window recovery: main DB missing, backup exists → restore.
        if (!File.Exists(dbPath) && File.Exists(backupPath))
        {
            File.Move(backupPath, dbPath);
        }

        // Clean up orphaned temp from any prior interrupted attempt.
        SafeDelete(tempPath);
    }

    public async Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key)
    {
        if (string.IsNullOrWhiteSpace(key.HexKey))
            throw new ArgumentException("Key material is empty.", nameof(key));
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Plaintext database not found.", dbPath);

        var tempEncryptedPath = dbPath + ".enc.tmp";
        var backupPath = dbPath + ".plain.bak";

        // Clean up any leftover temps from a prior interrupted migration.
        SafeDelete(tempEncryptedPath);
        SafeDelete(backupPath);

        try
        {
            // Open plaintext source (no key), ATTACH an empty encrypted DB with a raw-bytes KEY,
            // invoke sqlcipher_export to copy schema + data, DETACH.
            using (var source = new SqliteConnection($"Data Source={dbPath}"))
            {
                await source.OpenAsync();

                // Ensure WAL is flushed to the main DB file before export.
                // Without this, uncommitted WAL pages could be lost during encryption migration.
                using var checkpointCmd = source.CreateCommand();
                checkpointCmd.CommandText = "PRAGMA wal_checkpoint(FULL)";
                await checkpointCmd.ExecuteNonQueryAsync();

                using var cmd = source.CreateCommand();
                cmd.CommandText = $@"
                    ATTACH DATABASE '{EscapeSingleQuotes(tempEncryptedPath)}' AS encrypted KEY ""x'{key.HexKey}'"";
                    SELECT sqlcipher_export('encrypted');
                    DETACH DATABASE encrypted;";
                await cmd.ExecuteNonQueryAsync();
            }

            // Release file handles held by the Microsoft.Data.Sqlite connection pool.
            // Without this, File.Move below throws IOException on Windows.
            SqliteConnection.ClearAllPools();

            // Atomic swap: move plaintext aside, install encrypted, then verify.
            File.Move(dbPath, backupPath);
            File.Move(tempEncryptedPath, dbPath);

            // Verification open — use PRAGMA key (NOT Password=), matches Correction #1.
            using (var verify = new SqliteConnection($"Data Source={dbPath}"))
            {
                await verify.OpenAsync();

                using var keyCmd = verify.CreateCommand();
                keyCmd.CommandText = $@"PRAGMA key = ""x'{key.HexKey}'"";";
                await keyCmd.ExecuteNonQueryAsync();

                using var probeCmd = verify.CreateCommand();
                probeCmd.CommandText = "SELECT count(*) FROM sqlite_master";
                await probeCmd.ExecuteScalarAsync();
            }

            SqliteConnection.ClearAllPools();

            // Success — remove the backup.
            SafeDelete(backupPath);
        }
        catch
        {
            // Rollback: if the plaintext backup exists but the DB path was replaced with an
            // incomplete encrypted file, restore the plaintext backup.
            SqliteConnection.ClearAllPools();

            if (File.Exists(backupPath))
            {
                // If dbPath currently holds a half-written encrypted file, remove it first.
                SafeDelete(dbPath);
                File.Move(backupPath, dbPath);
            }

            SafeDelete(tempEncryptedPath);
            throw;
        }
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

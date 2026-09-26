using System;
using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IDatabaseEncryptionMigrator
{
    /// <summary>
    /// Converts a plaintext SQLite database at <paramref name="dbPath"/> into an encrypted copy
    /// using the given key, then atomically replaces the original file. Safe against interruption:
    /// leaves the original file intact if any step fails.
    ///
    /// Uses SQLCipher's sqlcipher_export() via ATTACH with the raw-bytes KEY "x'&lt;hex&gt;'" literal.
    /// Key delivery is explicitly NOT through SqliteConnectionStringBuilder.Password (which runs
    /// values through PBKDF2 KDF and would produce a different derived key).
    ///
    /// No connection may hold the file open while this runs: on Windows the swap fails for an
    /// open file. The caller closes the shared application connection first.
    /// </summary>
    Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key);

    /// <summary>
    /// Same as <see cref="MigrateToEncryptedAsync(string, DatabaseKeyMaterial)"/>, with a commit
    /// step. <paramref name="commitAsync"/> runs after the encrypted file is installed and verified
    /// but while the plaintext backup still exists; if it throws, the plaintext database is put
    /// back and the exception propagates. The enable-encryption flow writes the encryption marker
    /// here, so the marker exists exactly when the database on disk is encrypted.
    /// </summary>
    Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key, Func<Task>? commitAsync);

    /// <summary>
    /// Detects and recovers from an interrupted encryption migration.
    /// If a plaintext backup exists but the main DB is missing, restores the backup.
    /// When both exist, the encryption marker decides: a committed marker keeps the encrypted
    /// database and removes the plaintext copy; no marker means the migration stopped before its
    /// commit, so the plaintext copy is put back.
    /// Retires a marker whose database is plaintext (which would otherwise make every start fail).
    /// Always cleans up orphaned .enc.tmp files.
    /// Call this at app startup before any DB access and before reading the marker.
    /// </summary>
    void RecoverIfNeeded(string dbPath);
}

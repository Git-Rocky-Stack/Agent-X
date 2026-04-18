using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IDatabaseEncryptionMigrator
{
    /// <summary>
    /// Converts a plaintext SQLite database at <paramref name="dbPath"/> into an encrypted copy
    /// using the given key, then atomically replaces the original file. Safe against interruption:
    /// leaves the original file intact if any step fails.
    ///
    /// Uses SQLCipher's sqlcipher_export() via ATTACH with the raw-bytes KEY "x'<hex>'" literal.
    /// Key delivery is explicitly NOT through SqliteConnectionStringBuilder.Password (which runs
    /// values through PBKDF2 KDF and would produce a different derived key).
    /// </summary>
    Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key);
}

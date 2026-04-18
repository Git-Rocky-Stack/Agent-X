using Microsoft.Data.Sqlite;

namespace AgentX.Core.Data;

public interface IEncryptedConnectionFactory
{
    /// <summary>
    /// Opens a connection AND applies the current PRAGMA key if one is loaded.
    /// If no key is loaded (encryption disabled), returns a plaintext-opened connection.
    /// Caller disposes the connection.
    /// </summary>
    SqliteConnection OpenKeyed(string dbPath);

    /// <summary>
    /// Applies PRAGMA key to an already-opened connection. No-op if no key is loaded.
    /// Use this when a caller (e.g. an ATTACH-based workflow, or an EF Core connection
    /// that is managed by the framework) opens its own connection.
    /// </summary>
    void ApplyKey(SqliteConnection openConnection);
}

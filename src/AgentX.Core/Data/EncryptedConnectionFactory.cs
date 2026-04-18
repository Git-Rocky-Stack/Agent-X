using System.Data;
using AgentX.Core.Services.Security;
using Microsoft.Data.Sqlite;

namespace AgentX.Core.Data;

public sealed class EncryptedConnectionFactory : IEncryptedConnectionFactory
{
    private readonly IDatabaseKeyProvider _keyProvider;

    public EncryptedConnectionFactory(IDatabaseKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public SqliteConnection OpenKeyed(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        ApplyKey(conn);
        return conn;
    }

    public void ApplyKey(SqliteConnection openConnection)
    {
        var key = _keyProvider.Current;
        if (key is null) return;
        if (openConnection.State != ConnectionState.Open)
            throw new System.InvalidOperationException("ApplyKey requires an already-open connection.");

        using var cmd = openConnection.CreateCommand();
        // key.HexKey is 64 uppercase hex chars. SQLite BLOB literal x'<hex>' is the raw-bytes form
        // that SQLCipher treats as a 32-byte key (no KDF). This is NOT interchangeable with
        // SqliteConnectionStringBuilder.Password (which runs values through PBKDF2).
        cmd.CommandText = $@"PRAGMA key = ""x'{key.HexKey}'"";";
        cmd.ExecuteNonQuery();
    }
}

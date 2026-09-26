using System.Data;
using System.Data.Common;
using AgentX.Core.Data;
using AgentX.Core.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Releases and re-acquires the long-lived connection of the shared <see cref="AgentXDbContext"/>
/// around operations that move or replace the database file (enabling encryption, restoring a
/// backup).
/// <para>
/// <see cref="AgentXDbContext.EnsureKeyApplied"/> opens that connection itself so it can apply
/// PRAGMA key, which means EF Core treats it as externally opened and
/// <c>Database.CloseConnection()</c> leaves it open. SQLite opens files without
/// FILE_SHARE_DELETE, so on Windows any open handle makes the move or replace fail. Closing the
/// DbConnection directly and clearing the SQLite pools releases every handle this process holds
/// through Microsoft.Data.Sqlite (connections held open elsewhere, such as a vector store's own
/// connection, are not affected and still block the swap).
/// </para>
/// </summary>
internal static class SharedDatabaseConnection
{
    /// <summary>The database file the shared context is configured for.</summary>
    public static string GetDatabasePath(AgentXDbContext db)
    {
        var dataSource = db.Database.GetDbConnection().DataSource;
        return string.IsNullOrWhiteSpace(dataSource) ? PathHelper.GetDatabasePath() : dataSource;
    }

    /// <summary>
    /// Checkpoints the WAL into the main file (so the file on disk is complete on its own), closes
    /// the shared connection and clears the connection pools.
    /// </summary>
    public static void Release(AgentXDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Closed)
        {
            try
            {
                using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }
            catch (DbException ex)
            {
                Log.Warning(ex, "WAL checkpoint before releasing the database connection failed");
            }

            connection.Close();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Reopens the shared connection and applies whatever key is current. A no-op for contexts
    /// built without the encrypted connection factory; EF Core then opens on demand.
    /// </summary>
    public static void Reacquire(AgentXDbContext db) => db.EnsureKeyApplied();
}

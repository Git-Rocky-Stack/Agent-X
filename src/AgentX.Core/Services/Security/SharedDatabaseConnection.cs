using System.Data;
using System.Data.Common;
using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
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
/// DbConnection directly and clearing the SQLite pools releases every idle handle this process
/// holds through Microsoft.Data.Sqlite. A connection that is open elsewhere is not affected: the
/// vector store keeps its own, so callers suspend it first (<see cref="TrySuspendVectorStoreAsync"/>).
/// </para>
/// <para>
/// Callers hold <see cref="AgentXDbContext.EnterDatabaseGate"/> from the release to the reacquire,
/// so EF work from other flows waits instead of reopening the connection on a file in mid-swap.
/// </para>
/// </summary>
internal static class SharedDatabaseConnection
{
    /// <summary>
    /// The database file the shared context is configured for. Throws for a context that is not
    /// file-backed (for example in-memory), so file operations can never be aimed at a different
    /// database than the one the context uses.
    /// </summary>
    public static string GetDatabasePath(AgentXDbContext db)
    {
        var dataSource = TryGetDatabasePath(db);
        return dataSource ?? throw new InvalidOperationException("The application database is not file-backed.");
    }

    /// <summary>The database file the shared context is configured for, or null when it is not file-backed.</summary>
    public static string? TryGetDatabasePath(AgentXDbContext db)
    {
        var dataSource = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
            return null;

        return Path.GetFullPath(dataSource);
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

    /// <summary>
    /// Suspends the vector store, which keeps its own connection to the database file, before the
    /// file is moved or replaced. Returns true when the store is suspended and must be resumed
    /// with <see cref="ResumeVectorStoreAsync"/>. Returns false when there is no store or it could
    /// not be suspended; the file operation then fails as "in use" on Windows and rolls back.
    /// A cancelled wait propagates, so the caller can stop before changing anything.
    /// </summary>
    public static async Task<bool> TrySuspendVectorStoreAsync(IVectorStore? store, CancellationToken ct)
    {
        if (store is null)
            return false;

        try
        {
            await store.SuspendAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not suspend the vector store before changing the database file");
            return false;
        }
    }

    /// <summary>
    /// Resumes a vector store suspended by <see cref="TrySuspendVectorStoreAsync"/>. Returns false
    /// when it could not reopen; that is logged, not thrown, because the file operation itself is
    /// complete, and the store's operations report that it is not initialized until Agent-X
    /// restarts.
    /// </summary>
    public static async Task<bool> ResumeVectorStoreAsync(IVectorStore store, bool reloadFromDatabase)
    {
        try
        {
            await store.ResumeAsync(reloadFromDatabase, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not reopen the vector store after changing the database file; semantic search is unavailable until Agent-X restarts");
            return false;
        }
    }
}

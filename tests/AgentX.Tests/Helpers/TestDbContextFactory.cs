using AgentX.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgentX.Tests.Helpers;

/// <summary>
/// Factory that produces in-memory SQLite <see cref="AgentXDbContext"/> instances
/// suitable for unit testing. Each factory instance owns a dedicated in-memory
/// database that persists for the lifetime of the factory (because the underlying
/// <see cref="SqliteConnection"/> stays open).
///
/// Usage:
///   using var factory = new TestDbContextFactory();
///   var db = factory.CreateContext();
///   // ... use db ...
///
/// Disposing the factory closes the connection and destroys the in-memory database.
/// </summary>
public sealed class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AgentXDbContext> _options;
    private bool _disposed;

    /// <summary>
    /// Initializes a new in-memory SQLite database, opens the connection,
    /// and creates all tables defined in <see cref="AgentXDbContext.OnModelCreating"/>.
    /// </summary>
    public TestDbContextFactory()
    {
        // Use a shared in-memory database that stays alive as long as the connection is open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create the schema by running EnsureCreated against the open connection.
        using var context = new AgentXDbContext(_options);
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Creates a new <see cref="AgentXDbContext"/> instance backed by the same
    /// in-memory database. Each call returns a fresh context that shares the
    /// underlying connection (and therefore the same data).
    /// </summary>
    public AgentXDbContext CreateContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AgentXDbContext(_options);
    }

    /// <summary>
    /// Closes the in-memory SQLite connection, destroying all data.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Close();
        _connection.Dispose();
    }
}

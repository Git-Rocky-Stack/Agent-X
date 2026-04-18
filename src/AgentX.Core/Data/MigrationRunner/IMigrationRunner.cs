using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentX.Core.Data.MigrationRunner;

public interface IMigrationRunner
{
    /// <summary>
    /// Applies all pending migrations to the database. Creates the database if it does not exist.
    /// Idempotent — if no migrations are pending, returns a MigrationResult with empty AppliedMigrations.
    /// </summary>
    Task<MigrationResult> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns pending migration names without applying them.
    /// </summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);
}

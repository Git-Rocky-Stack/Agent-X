using System;
using System.Collections.Generic;

namespace AgentX.Core.Data.MigrationRunner;

/// <summary>
/// Thrown when migrations are pending but the runner was invoked in a read-only
/// mode (e.g., a dry-run startup check). Carries the pending migration names so
/// the caller can surface them in the UI.
/// </summary>
public sealed class PendingMigrationsException : Exception
{
    public IReadOnlyList<string> PendingMigrations { get; }

    public PendingMigrationsException(IReadOnlyList<string> pendingMigrations)
        : base($"{pendingMigrations.Count} migration(s) pending: {string.Join(", ", pendingMigrations)}")
    {
        PendingMigrations = pendingMigrations;
    }
}

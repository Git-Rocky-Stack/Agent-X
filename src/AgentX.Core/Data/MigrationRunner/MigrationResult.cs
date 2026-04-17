using System.Collections.Generic;

namespace AgentX.Core.Data.MigrationRunner;

/// <summary>
/// Outcome of a migration runner invocation.
/// </summary>
public sealed record MigrationResult(
    bool DatabaseCreated,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> AlreadyApplied,
    string DatabasePath);

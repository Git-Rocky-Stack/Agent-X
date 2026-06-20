using System;
using System.Collections.Generic;

namespace AgentX.Core.Data.MigrationRunner;

/// <summary>
/// Thrown by baseline adoption when a pre-migration database carries application tables but is
/// MISSING one or more of the tables created by <c>_InitialBaseline</c>, and those missing objects
/// could not be created (or the schema could not be re-validated) before stamping the baseline.
///
/// This is the fail-closed backstop for AX-QA-002: rather than stamp <c>_InitialBaseline</c> as
/// applied against an incomplete schema — which leaves later migrations to crash with
/// "no such table: …" — the runner aborts so startup can enter a recovery state instead of
/// silently corrupting the database. <see cref="MissingTables"/> names the baseline tables that
/// were still absent.
/// </summary>
public sealed class BaselineSchemaIncompleteException : Exception
{
    public IReadOnlyList<string> MissingTables { get; }

    public BaselineSchemaIncompleteException(IReadOnlyList<string> missingTables)
        : base(
            $"The _InitialBaseline schema is incomplete and could not be self-healed. " +
            $"{missingTables.Count} baseline table(s) still missing: {string.Join(", ", missingTables)}. " +
            "Refusing to stamp _InitialBaseline as applied to avoid leaving the database in a " +
            "partially-migrated state.")
    {
        MissingTables = missingTables;
    }
}

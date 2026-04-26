using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AgentX.Core.Data.MigrationRunner;

public sealed class MigrationRunner : IMigrationRunner
{
    private static readonly string[] OperationsSchemaSql =
    [
        """
        CREATE TABLE IF NOT EXISTS "plugins" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_plugins" PRIMARY KEY AUTOINCREMENT,
            "PluginId" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Version" TEXT NOT NULL,
            "Author" TEXT NOT NULL,
            "Description" TEXT NOT NULL,
            "PluginType" TEXT NOT NULL DEFAULT 'Custom',
            "InstallPath" TEXT NOT NULL,
            "IsEnabled" INTEGER NOT NULL DEFAULT 0,
            "InstalledAt" TEXT NOT NULL,
            "LastActivatedAt" TEXT NULL,
            "SettingsJson" TEXT NULL,
            "ReadmeContent" TEXT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "sync_logs" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_sync_logs" PRIMARY KEY AUTOINCREMENT,
            "SyncedAt" TEXT NOT NULL,
            "Direction" TEXT NOT NULL,
            "ChangesApplied" INTEGER NOT NULL,
            "ConflictsDetected" INTEGER NOT NULL,
            "ConflictsResolved" INTEGER NOT NULL,
            "DurationMs" REAL NOT NULL,
            "ErrorMessage" TEXT NULL,
            "IsSuccess" INTEGER NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "workflows" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_workflows" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL,
            "Description" TEXT NULL,
            "Icon" TEXT NULL,
            "Category" TEXT NOT NULL DEFAULT 'Custom',
            "IsBuiltIn" INTEGER NOT NULL,
            "IsEnabled" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            "RunCount" INTEGER NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "workflow_runs" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_workflow_runs" PRIMARY KEY AUTOINCREMENT,
            "WorkflowId" INTEGER NOT NULL,
            "Status" TEXT NOT NULL DEFAULT 'pending',
            "InitialInput" TEXT NULL,
            "FinalOutput" TEXT NULL,
            "ErrorMessage" TEXT NULL,
            "StartedAt" TEXT NOT NULL,
            "CompletedAt" TEXT NULL,
            "StepsCompleted" INTEGER NOT NULL,
            "TotalSteps" INTEGER NOT NULL,
            "StepOutputsJson" TEXT NULL,
            "TotalTokensUsed" INTEGER NOT NULL,
            CONSTRAINT "FK_workflow_runs_workflows_WorkflowId" FOREIGN KEY ("WorkflowId") REFERENCES "workflows" ("Id") ON DELETE CASCADE
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "workflow_steps" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_workflow_steps" PRIMARY KEY AUTOINCREMENT,
            "WorkflowId" INTEGER NOT NULL,
            "StepOrder" INTEGER NOT NULL,
            "Name" TEXT NOT NULL,
            "StepType" TEXT NOT NULL DEFAULT 'AiPrompt',
            "PromptTemplate" TEXT NOT NULL,
            "ModelOverride" TEXT NULL,
            "TemperatureOverride" REAL NULL,
            "MaxTokensOverride" INTEGER NULL,
            "ConfigJson" TEXT NULL,
            CONSTRAINT "FK_workflow_steps_workflows_WorkflowId" FOREIGN KEY ("WorkflowId") REFERENCES "workflows" ("Id") ON DELETE CASCADE
        );
        """,
        """CREATE INDEX IF NOT EXISTS "IX_plugins_InstalledAt" ON "plugins" ("InstalledAt");""",
        """CREATE INDEX IF NOT EXISTS "IX_plugins_IsEnabled" ON "plugins" ("IsEnabled");""",
        """CREATE INDEX IF NOT EXISTS "IX_plugins_Name" ON "plugins" ("Name");""",
        """CREATE UNIQUE INDEX IF NOT EXISTS "IX_plugins_PluginId" ON "plugins" ("PluginId");""",
        """CREATE INDEX IF NOT EXISTS "IX_plugins_PluginType" ON "plugins" ("PluginType");""",
        """CREATE INDEX IF NOT EXISTS "IX_sync_logs_Direction" ON "sync_logs" ("Direction");""",
        """CREATE INDEX IF NOT EXISTS "IX_sync_logs_IsSuccess" ON "sync_logs" ("IsSuccess");""",
        """CREATE INDEX IF NOT EXISTS "IX_sync_logs_SyncedAt" ON "sync_logs" ("SyncedAt");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflows_Category" ON "workflows" ("Category");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflows_IsBuiltIn" ON "workflows" ("IsBuiltIn");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflows_IsEnabled" ON "workflows" ("IsEnabled");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflow_runs_StartedAt" ON "workflow_runs" ("StartedAt");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflow_runs_Status" ON "workflow_runs" ("Status");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflow_runs_WorkflowId" ON "workflow_runs" ("WorkflowId");""",
        """CREATE INDEX IF NOT EXISTS "IX_workflow_steps_WorkflowId_StepOrder" ON "workflow_steps" ("WorkflowId", "StepOrder");"""
    ];

    private readonly AgentXDbContext _context;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public MigrationRunner(AgentXDbContext context)
    {
        _context = context;
    }

    public async Task<MigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<MigrationResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var dbPath = ExtractDbPath(_context);
        var databaseExistedBefore = await _context.Database.CanConnectAsync(cancellationToken);

        string? adoptedBaseline = null;
        if (databaseExistedBefore && !await HasMigrationsHistoryTableAsync(cancellationToken))
        {
            adoptedBaseline = await AdoptBaselineAsync(cancellationToken);
        }

        var appliedBeforeMigrate = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        // For reporting, "already applied" reflects state BEFORE this runner invocation —
        // if we adopted a baseline this run, that baseline is treated as newly applied, not already applied.
        var alreadyAppliedForResult = adoptedBaseline is null
            ? appliedBeforeMigrate
            : appliedBeforeMigrate.Where(m => m != adoptedBaseline).ToList();

        var pendingBefore = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pendingBefore.Count == 0)
        {
            await EnsureOperationsTablesAsync(cancellationToken);

            var appliedFromAdoption = adoptedBaseline is null
                ? (IReadOnlyList<string>)System.Array.Empty<string>()
                : new[] { adoptedBaseline };

            return new MigrationResult(
                DatabaseCreated: !databaseExistedBefore,
                AppliedMigrations: appliedFromAdoption,
                AlreadyApplied: alreadyAppliedForResult,
                DatabasePath: dbPath);
        }

        await _context.Database.MigrateAsync(cancellationToken);
        await EnsureOperationsTablesAsync(cancellationToken);

        var appliedAfter = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var newlyAppliedFromMigrate = appliedAfter.Except(appliedBeforeMigrate).ToList();

        var allNewlyApplied = adoptedBaseline is null
            ? (IReadOnlyList<string>)newlyAppliedFromMigrate
            : new[] { adoptedBaseline }.Concat(newlyAppliedFromMigrate).ToList();

        return new MigrationResult(
            DatabaseCreated: !databaseExistedBefore,
            AppliedMigrations: allNewlyApplied,
            AlreadyApplied: alreadyAppliedForResult,
            DatabasePath: dbPath);
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
        return pending.ToList();
    }

    private async Task EnsureOperationsTablesAsync(CancellationToken cancellationToken)
    {
        foreach (var sql in OperationsSchemaSql)
        {
            await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private async Task<bool> HasMigrationsHistoryTableAsync(CancellationToken cancellationToken)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result is not null;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private async Task<string?> AdoptBaselineAsync(CancellationToken cancellationToken)
    {
        // Create the EF migrations history table and insert a row marking every
        // migration up through InitialBaseline as already applied, so MigrateAsync
        // only applies migrations that ship AFTER the baseline. Returns the
        // baseline migration name so the caller can report it as "applied" in
        // the MigrationResult.
        var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
        var allMigrations = migrationsAssembly.Migrations.Keys.ToList();
        var baseline = allMigrations.FirstOrDefault(m => m.EndsWith("_InitialBaseline"));
        if (baseline is null) return null;

        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" TEXT NOT NULL);",
            cancellationToken);

        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});",
            new object[] { baseline, "8.0.11" },
            cancellationToken);

        return baseline;
    }

    private static string ExtractDbPath(AgentXDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        // ConnectionString format: "Data Source=/some/path.db"
        var cs = connection.ConnectionString ?? string.Empty;
        const string token = "Data Source=";
        var idx = cs.IndexOf(token, System.StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "<unknown>";
        var tail = cs[(idx + token.Length)..];
        var semi = tail.IndexOf(';');
        return semi < 0 ? tail : tail[..semi];
    }
}

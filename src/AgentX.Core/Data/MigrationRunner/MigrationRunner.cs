using System;
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

    private static readonly string[] CompatibilitySchemaSql =
    [
        """
        CREATE TABLE IF NOT EXISTS "inbox_items" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_inbox_items" PRIMARY KEY AUTOINCREMENT,
            "FilePath" TEXT NOT NULL,
            "FileName" TEXT NOT NULL,
            "FileType" TEXT NOT NULL,
            "FileSizeBytes" INTEGER NOT NULL,
            "Status" TEXT NOT NULL DEFAULT 'pending',
            "Preview" TEXT NULL,
            "SuggestedCollectionId" INTEGER NULL,
            "SuggestedCollectionName" TEXT NULL,
            "SuggestedTags" TEXT NULL,
            "AddedAt" TEXT NOT NULL,
            "ProcessedAt" TEXT NULL,
            "WatchFolderId" INTEGER NULL,
            "SourceType" TEXT NULL,
            "SourceUrl" TEXT NULL,
            "SourcePluginId" TEXT NULL,
            "SourceCategory" TEXT NULL,
            "ExternalId" TEXT NULL,
            "DocumentId" INTEGER NULL
        );
        """,
        """CREATE INDEX IF NOT EXISTS "IX_inbox_items_AddedAt" ON "inbox_items" ("AddedAt");""",
        """CREATE INDEX IF NOT EXISTS "IX_inbox_items_DocumentId" ON "inbox_items" ("DocumentId");""",
        """CREATE INDEX IF NOT EXISTS "IX_inbox_items_ExternalId_SourcePluginId" ON "inbox_items" ("ExternalId", "SourcePluginId");""",
        """CREATE INDEX IF NOT EXISTS "IX_inbox_items_Status" ON "inbox_items" ("Status");""",
        """CREATE INDEX IF NOT EXISTS "IX_inbox_items_WatchFolderId" ON "inbox_items" ("WatchFolderId");""",
        """
        CREATE TABLE IF NOT EXISTS "belief_conflicts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_belief_conflicts" PRIMARY KEY AUTOINCREMENT,
            "DetectedAt" TEXT NOT NULL,
            "BeliefId" INTEGER NOT NULL DEFAULT 0,
            "Topic" TEXT NOT NULL,
            "PreviousStance" TEXT NOT NULL,
            "CurrentStance" TEXT NOT NULL,
            "PreviousStancePeriod" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
            "StanceChangedAt" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
            "ConflictMagnitude" REAL NOT NULL,
            "HasBeenAcknowledged" INTEGER NOT NULL,
            "AcknowledgedAt" TEXT NULL,
            "ContextJson" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL
        );
        """,
        """CREATE INDEX IF NOT EXISTS "IX_belief_conflicts_BeliefId" ON "belief_conflicts" ("BeliefId");""",
        """CREATE INDEX IF NOT EXISTS "IX_belief_conflicts_ConflictMagnitude" ON "belief_conflicts" ("ConflictMagnitude");""",
        """CREATE INDEX IF NOT EXISTS "IX_belief_conflicts_HasBeenAcknowledged" ON "belief_conflicts" ("HasBeenAcknowledged");""",
        """CREATE INDEX IF NOT EXISTS "IX_belief_conflicts_Topic" ON "belief_conflicts" ("Topic");"""
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

        List<string> adoptedMigrations = [];
        if (databaseExistedBefore && !await HasMigrationsHistoryTableAsync(cancellationToken))
        {
            adoptedMigrations = (await AdoptBaselineAsync(cancellationToken)).ToList();
        }

        var appliedBeforeMigrate = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        // For reporting, "already applied" reflects state BEFORE this runner invocation -
        // if we adopted migrations this run, those rows are treated as newly applied, not already applied.
        var adoptedSet = adoptedMigrations.ToHashSet(StringComparer.Ordinal);
        var alreadyAppliedForResult = adoptedSet.Count == 0
            ? appliedBeforeMigrate
            : appliedBeforeMigrate.Where(m => !adoptedSet.Contains(m)).ToList();

        var pendingBefore = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pendingBefore.Count == 0)
        {
            await EnsureOperationsTablesAsync(cancellationToken);

            var appliedFromAdoption = adoptedMigrations.Count == 0
                ? (IReadOnlyList<string>)System.Array.Empty<string>()
                : adoptedMigrations;

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

        var allNewlyApplied = adoptedMigrations.Count == 0
            ? (IReadOnlyList<string>)newlyAppliedFromMigrate
            : adoptedMigrations.Concat(newlyAppliedFromMigrate).ToList();

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

        foreach (var sql in CompatibilitySchemaSql)
        {
            await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        await EnsureColumnAsync(
            "belief_conflicts",
            "BeliefId",
            """ALTER TABLE "belief_conflicts" ADD COLUMN "BeliefId" INTEGER NOT NULL DEFAULT 0;""",
            cancellationToken);
        await EnsureColumnAsync(
            "belief_conflicts",
            "PreviousStancePeriod",
            """ALTER TABLE "belief_conflicts" ADD COLUMN "PreviousStancePeriod" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';""",
            cancellationToken);
        await EnsureColumnAsync(
            "belief_conflicts",
            "StanceChangedAt",
            """ALTER TABLE "belief_conflicts" ADD COLUMN "StanceChangedAt" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';""",
            cancellationToken);
        await EnsureColumnAsync(
            "conversations",
            "FolderName",
            """ALTER TABLE "conversations" ADD COLUMN "FolderName" TEXT NULL;""",
            cancellationToken);
        await EnsureColumnAsync(
            "conversations",
            "ParentConversationId",
            """ALTER TABLE "conversations" ADD COLUMN "ParentConversationId" INTEGER NULL;""",
            cancellationToken);
        await EnsureColumnAsync(
            "conversations",
            "BranchPointMessageId",
            """ALTER TABLE "conversations" ADD COLUMN "BranchPointMessageId" INTEGER NULL;""",
            cancellationToken);
        await EnsureColumnAsync(
            "conversations",
            "BranchLabel",
            """ALTER TABLE "conversations" ADD COLUMN "BranchLabel" TEXT NULL;""",
            cancellationToken);
        await _context.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_conversations_ParentConversationId" ON "conversations" ("ParentConversationId");""",
            cancellationToken);
    }

    private async Task EnsureColumnAsync(
        string tableName,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }

        await _context.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
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

    private async Task<IReadOnlyList<string>> AdoptBaselineAsync(CancellationToken cancellationToken)
    {
        // Create the EF migrations history table and insert rows for migrations
        // whose schema is already present. Existing installs created before the
        // EF runner may have no history table, but they can still contain tables
        // from newer EnsureCreated-based builds. Stamping only the baseline would
        // make EF attempt duplicate CREATE TABLE / ADD COLUMN operations.
        var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
        var allMigrations = migrationsAssembly.Migrations.Keys
            .OrderBy(migration => migration, StringComparer.Ordinal)
            .ToList();
        var baseline = FindMigration(allMigrations, "_InitialBaseline");
        if (baseline is null) return System.Array.Empty<string>();

        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" TEXT NOT NULL);",
            cancellationToken);

        var adopted = new List<string>();
        await StampMigrationAsync(baseline, adopted, cancellationToken);

        var addEncryption = FindMigration(allMigrations, "_AddEncryptionColumns");
        var removeEncryption = FindMigration(allMigrations, "_RemoveEncryptionColumns");
        if (addEncryption is not null && removeEncryption is not null)
        {
            var encryptionColumns = new[]
            {
                "DpapiWrappedKey",
                "EncryptionEnabled",
                "EncryptionKeyStorageMode",
                "EncryptionSaltBase64"
            };
            var allEncryptionColumnsExist = await AllColumnsExistAsync("user_settings", encryptionColumns, cancellationToken);
            await StampMigrationAsync(addEncryption, adopted, cancellationToken);
            if (!allEncryptionColumnsExist)
            {
                await StampMigrationAsync(removeEncryption, adopted, cancellationToken);
            }
        }

        if (await TableExistsAsync("conversation_summary_snapshots", cancellationToken)
            && await TableExistsAsync("conversation_summary_states", cancellationToken)
            && FindMigration(allMigrations, "_AddConversationSummaryPersistence") is { } summaryMigration)
        {
            await StampMigrationAsync(summaryMigration, adopted, cancellationToken);
        }

        if (await AllColumnsExistAsync(
                "memories",
                ["Embedding", "LinkedMemoryId", "DecayRate", "Confidence", "Tags"],
                cancellationToken)
            && FindMigration(allMigrations, "_AddSemanticMemoryColumns") is { } semanticMemoryMigration)
        {
            await StampMigrationAsync(semanticMemoryMigration, adopted, cancellationToken);
        }

        if (await AllColumnsExistAsync(
                "messages",
                ["Embedding", "EmbeddedAt", "EmbeddingModel"],
                cancellationToken)
            && FindMigration(allMigrations, "_AddMessageRecallEmbeddings") is { } recallMigration)
        {
            await StampMigrationAsync(recallMigration, adopted, cancellationToken);
        }

        // Embedding-model versioning added EmbeddingModelVersion / EmbeddingDimensions /
        // EmbeddedAt to document_chunks and memories (plus EmbeddingDimensions on messages).
        // A pre-migration install created from the full model via EnsureCreated already has
        // these columns, so the migration must be stamped as applied rather than replayed -
        // otherwise EF re-runs ADD COLUMN "EmbeddingModelVersion" against document_chunks
        // and SQLite throws "duplicate column name". document_chunks uniquely identifies this
        // migration's schema (no earlier migration touches it).
        if (await AllColumnsExistAsync(
                "document_chunks",
                ["EmbeddingModelVersion", "EmbeddingDimensions", "EmbeddedAt"],
                cancellationToken)
            && await AllColumnsExistAsync(
                "memories",
                ["EmbeddingModelVersion", "EmbeddingDimensions", "EmbeddedAt"],
                cancellationToken)
            && FindMigration(allMigrations, "_AddEmbeddingModelVersioning") is { } embeddingVersioningMigration)
        {
            await StampMigrationAsync(embeddingVersioningMigration, adopted, cancellationToken);
        }

        if (await TableExistsAsync("conversation_theme_clusters", cancellationToken)
            && await TableExistsAsync("conversation_theme_memberships", cancellationToken)
            && await AllColumnsExistAsync(
                "conversation_summary_snapshots",
                ["Embedding", "EmbeddedAt", "EmbeddingModel"],
                cancellationToken)
            && FindMigration(allMigrations, "_AddConversationThemeClustering") is { } themeMigration)
        {
            await StampMigrationAsync(themeMigration, adopted, cancellationToken);
        }

        if (await TableExistsAsync("conversation_theme_daily_metrics", cancellationToken)
            && FindMigration(allMigrations, "_AddConversationThemeDailyMetrics") is { } themeDailyMigration)
        {
            await StampMigrationAsync(themeDailyMigration, adopted, cancellationToken);
        }

        if (await TableExistsAsync("temporal_beliefs", cancellationToken)
            && await TableExistsAsync("insight_moments", cancellationToken)
            && await TableExistsAsync("engagement_metrics", cancellationToken)
            && await TableExistsAsync("belief_conflicts", cancellationToken)
            && await TableExistsAsync("voice_profiles", cancellationToken)
            && FindMigration(allMigrations, "_AddTemporalIdentity") is { } temporalMigration)
        {
            await StampMigrationAsync(temporalMigration, adopted, cancellationToken);
        }

        // DropLicensesTable removed the "licenses" table when Agent-X became fully free.
        // A pre-migration install created from the current model (via EnsureCreated) never
        // had the table to begin with, and _InitialBaseline is always stamped above as if it
        // created it. If "licenses" is already absent, the drop has effectively been applied,
        // so stamp it as applied; replaying DROP TABLE "licenses" would otherwise throw
        // "no such table: licenses". A legacy install that still has the table is left
        // unstamped so EF performs the drop normally.
        if (!await TableExistsAsync("licenses", cancellationToken)
            && FindMigration(allMigrations, "_DropLicensesTable") is { } dropLicensesMigration)
        {
            await StampMigrationAsync(dropLicensesMigration, adopted, cancellationToken);
        }

        return adopted;
    }

    private async Task StampMigrationAsync(
        string migrationId,
        List<string> adopted,
        CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});",
            new object[] { migrationId, "8.0.11" },
            cancellationToken);
        adopted.Add(migrationId);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$tableName;";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$tableName";
            parameter.Value = tableName;
            cmd.Parameters.Add(parameter);
            return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> AllColumnsExistAsync(
        string tableName,
        IReadOnlyCollection<string> columnNames,
        CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }

        return columnNames.All(existingColumns.Contains);
    }

    private static string? FindMigration(IEnumerable<string> migrations, string suffix)
    {
        return migrations.FirstOrDefault(migration => migration.EndsWith(suffix, StringComparison.Ordinal));
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgentX.Core.Data.MigrationRunner;

public sealed class MigrationRunner : IMigrationRunner
{
    /// <summary>
    /// Every table created by <c>_InitialBaseline</c> (see
    /// 20260417011607_InitialBaseline.cs). Baseline adoption may only treat that migration as
    /// already-applied when ALL of these tables are genuinely present. When some are missing on a
    /// pre-migration database, they are created from the migration's own operations (via EF's SQL
    /// generator) before the baseline is stamped — so later migrations never run against a
    /// half-built schema. Keep in lockstep with the migration's <c>Up</c> method.
    /// </summary>
    private static readonly string[] BaselineTables =
    [
        "annotations",
        "backups",
        "collections",
        "conversation_tags",
        "conversations",
        "digest_reports",
        "document_chunks",
        "document_collections",
        "document_tags",
        "documents",
        "feedback",
        "inbox_items",
        "indexing_jobs",
        "licenses",
        "memories",
        "messages",
        "oauth_credentials",
        "plugins",
        "search_history",
        "sync_logs",
        "system_prompts",
        "tags",
        "user_settings",
        "watch_folders",
        "workflow_runs",
        "workflow_steps",
        "workflows",
        "workspace_profiles"
    ];

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

        // Determine the database's prior state from actual SCHEMA presence, not from
        // file/connection existence. App startup opens the SQLite connection
        // (EnsureKeyApplied applies the SQLCipher PRAGMA) BEFORE this runner, which creates
        // an empty .db file on disk — so CanConnectAsync() reports "true" even on a genuinely
        // fresh install. A database "existed" for our purposes only when it already carries a
        // real schema: an EF history table, or at least one application table. Deriving the
        // signal this way keeps MigrationResult.DatabaseCreated accurate (true on a fresh
        // install, where this run creates the full schema) and is exactly the condition the
        // baseline-adoption gate below needs.
        var hadHistoryTable = await HasMigrationsHistoryTableAsync(cancellationToken);
        var hadApplicationTables = await HasApplicationTablesAsync(cancellationToken);
        var databaseExistedBefore = hadHistoryTable || hadApplicationTables;

        List<string> adoptedMigrations = [];
        // Baseline adoption is ONLY for a genuine pre-migration install: a database that
        // already contains application tables (from an old EnsureCreated build) but has no
        // __EFMigrationsHistory. A brand-new, empty .db file (no history, no app tables) — the
        // one EnsureKeyApplied leaves behind — must NOT adopt the baseline: doing so stamps
        // migrations as applied WITHOUT creating any tables, so MigrateAsync skips the
        // table-creating baseline and the app fails every query with "no such table". Such an
        // empty database instead falls through to MigrateAsync and receives the full schema.
        if (hadApplicationTables && !hadHistoryTable)
        {
            adoptedMigrations = (await AdoptBaselineAsync(cancellationToken)).ToList();
        }

        // Reconcile migrations whose recognized id changed or whose schema is already
        // present, so MigrateAsync below does not replay them against an existing DB.
        // Runs regardless of whether a history table existed (i.e. after any baseline
        // adoption above) and is a no-op on fresh databases. See the method for details.
        await ReconcilePendingMigrationsAsync(cancellationToken);

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

    /// <summary>
    /// True when the database contains at least one application table (a real schema from a
    /// prior install), ignoring SQLite-internal tables and the EF history table. Used to
    /// distinguish a genuine pre-migration database from a brand-new, empty .db file that
    /// exists only because the connection was opened before the runner ran.
    /// </summary>
    private async Task<bool> HasApplicationTablesAsync(CancellationToken cancellationToken)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
                "AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return System.Convert.ToInt64(result) > 0;
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

        // AX-QA-002: _InitialBaseline may only be stamped as already-applied when its FULL schema is
        // genuinely present. A legacy/partial database can carry baseline + later-migration tables
        // yet be MISSING some baseline tables (and have no history table). Stamping the baseline in
        // that state means EF never creates the missing tables, and a later migration's
        // "ALTER TABLE <missing> …" crashes with "no such table". Self-heal first: create exactly
        // the missing baseline objects from the migration's own operations, then re-verify the full
        // 28-table baseline before stamping. If healing leaves any table absent, fail closed.
        await EnsureBaselineSchemaCompleteAsync(allMigrations, cancellationToken);

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

    /// <summary>
    /// Ensures every table created by <c>_InitialBaseline</c> exists before that migration is
    /// stamped as applied. On a pre-migration database whose baseline schema is incomplete, this
    /// creates ONLY the missing baseline objects — sourced from the migration's own
    /// <see cref="Migration.UpOperations"/> and turned into SQL by EF's
    /// <see cref="IMigrationsSqlGenerator"/> (never hand-duplicated DDL) — preserving EF's operation
    /// ordering so inter-baseline foreign keys resolve. After healing it re-verifies the full
    /// baseline and throws <see cref="BaselineSchemaIncompleteException"/> if any table is still
    /// absent (fail-closed). When the baseline is already complete this is a no-op.
    /// </summary>
    private async Task EnsureBaselineSchemaCompleteAsync(
        IReadOnlyList<string> allMigrations,
        CancellationToken cancellationToken)
    {
        var missing = await GetMissingBaselineTablesAsync(cancellationToken);
        if (missing.Count == 0)
        {
            // Complete-baseline path: nothing to heal, behavior unchanged.
            return;
        }

        var missingSet = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
        var sqlGenerator = _context.GetService<IMigrationsSqlGenerator>();

        var baselineMigration = InstantiateMigration(allMigrations, "_InitialBaseline");
        if (baselineMigration is not null)
        {
            // Select only the operations that build the MISSING baseline tables — their
            // CreateTableOperation (FKs are inline columns on the operation) plus the
            // CreateIndexOperations targeting those tables. Iterate UpOperations in EF's original
            // order so dependency ordering (parents before children) is preserved.
            var operationsToApply = new List<MigrationOperation>();
            foreach (var operation in baselineMigration.UpOperations)
            {
                switch (operation)
                {
                    case CreateTableOperation createTable when missingSet.Contains(createTable.Name):
                        operationsToApply.Add(createTable);
                        break;
                    case CreateIndexOperation createIndex when missingSet.Contains(createIndex.Table):
                        operationsToApply.Add(createIndex);
                        break;
                }
            }

            if (operationsToApply.Count > 0)
            {
                var commands = sqlGenerator.Generate(operationsToApply, _context.Model);
                foreach (var command in commands)
                {
                    await _context.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
                }
            }
        }

        // A healed table is reborn at BASELINE shape, but the surrounding database is at HEAD
        // (it already carries later-migration tables/columns). Bring each healed table forward by
        // replaying — idempotently — the post-baseline AddColumn/CreateIndex operations that target
        // it, in migration order. Without this, the per-migration stamp guards below would see a
        // healed table that is missing later columns, decline to stamp the corresponding migration,
        // and let MigrateAsync replay it — re-running its OTHER (already-applied) operations against
        // sibling HEAD tables and crashing with "duplicate column name". (We touch ONLY healed
        // tables, so genuinely-pending migrations on untouched tables still apply normally.)
        await HealPostBaselineColumnsForTablesAsync(allMigrations, missingSet, sqlGenerator, cancellationToken);

        // Re-verify: the heal must have produced a complete baseline. If not, refuse to stamp
        // _InitialBaseline so startup can enter a recovery state instead of crashing later.
        var stillMissing = await GetMissingBaselineTablesAsync(cancellationToken);
        if (stillMissing.Count > 0)
        {
            throw new BaselineSchemaIncompleteException(stillMissing);
        }
    }

    /// <summary>
    /// Replays, idempotently, the post-baseline column and index additions that target the given
    /// healed tables — sourced from each later migration's own <see cref="Migration.UpOperations"/>
    /// (via EF's SQL generator, never hand-written DDL) and applied in migration order. Columns that
    /// already exist are skipped; indexes are created with IF NOT EXISTS semantics. Operations for
    /// tables NOT in <paramref name="healedTables"/> are ignored, so this never disturbs the rest of
    /// the schema. This makes a reborn baseline table consistent with the surrounding HEAD database
    /// so the downstream per-migration stamp guards fire correctly.
    /// </summary>
    private async Task HealPostBaselineColumnsForTablesAsync(
        IReadOnlyList<string> allMigrations,
        HashSet<string> healedTables,
        IMigrationsSqlGenerator sqlGenerator,
        CancellationToken cancellationToken)
    {
        if (healedTables.Count == 0) return;

        foreach (var migrationId in allMigrations)
        {
            if (migrationId.EndsWith("_InitialBaseline", StringComparison.Ordinal)) continue;

            var migration = InstantiateMigrationById(migrationId);
            if (migration is null) continue;

            foreach (var operation in migration.UpOperations)
            {
                switch (operation)
                {
                    case AddColumnOperation addColumn when healedTables.Contains(addColumn.Table):
                        if (!await ColumnExistsAsync(addColumn.Table, addColumn.Name, cancellationToken))
                        {
                            foreach (var command in sqlGenerator.Generate(new[] { addColumn }, _context.Model))
                            {
                                await _context.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
                            }
                        }
                        break;

                    case CreateIndexOperation createIndex when healedTables.Contains(createIndex.Table):
                        // EF's SQLite generator emits CREATE INDEX without IF NOT EXISTS, so guard
                        // on prior existence to stay idempotent across repeated runs.
                        if (!await IndexExistsAsync(createIndex.Name, cancellationToken))
                        {
                            foreach (var command in sqlGenerator.Generate(new[] { createIndex }, _context.Model))
                            {
                                await _context.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
                            }
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Returns the subset of <see cref="BaselineTables"/> that are not present in the database,
    /// in the same order, using a single sqlite_master read.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetMissingBaselineTablesAsync(CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(0));
            }
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }

        return BaselineTables.Where(table => !existing.Contains(table)).ToList();
    }

    /// <summary>
    /// Materializes the <see cref="Migration"/> instance whose id ends with <paramref name="suffix"/>
    /// so its <see cref="Migration.UpOperations"/> can be read. Returns null when not found.
    /// </summary>
    private Migration? InstantiateMigration(IEnumerable<string> allMigrations, string suffix)
    {
        var migrationId = FindMigration(allMigrations, suffix);
        return migrationId is null ? null : InstantiateMigrationById(migrationId);
    }

    /// <summary>
    /// Materializes the <see cref="Migration"/> instance for an exact migration id so its
    /// <see cref="Migration.UpOperations"/> can be read. Returns null when not found.
    /// </summary>
    private Migration? InstantiateMigrationById(string migrationId)
    {
        var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
        if (!migrationsAssembly.Migrations.TryGetValue(migrationId, out var migrationType)) return null;

        var activeProvider = _context.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseProvider>().Name;
        return migrationsAssembly.CreateMigration(migrationType, activeProvider);
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName, CancellationToken cancellationToken)
        => await AllColumnsExistAsync(tableName, new[] { columnName }, cancellationToken);

    private async Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name=$indexName;";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$indexName";
            parameter.Value = indexName;
            cmd.Parameters.Add(parameter);
            return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    // Legacy migration id that AddTemporalIdentity shipped with before its [Migration]
    // attribute was corrected. A developer's pre-existing agentx.db may have this exact
    // string stamped in __EFMigrationsHistory; it must be migrated to the corrected id
    // so EF treats AddTemporalIdentity as applied instead of replaying its CREATE TABLEs.
    private const string LegacyTemporalIdentityMigrationId = "20260430XXXXXX_AddTemporalIdentity";

    /// <summary>
    /// Reconciles the migrations history for two id/recognition changes so that
    /// <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync"/>
    /// does not replay migrations whose schema is already present on a pre-existing database.
    /// This runs on every invocation (after any baseline adoption, before MigrateAsync) and is a
    /// no-op on fresh databases:
    /// <list type="number">
    /// <item>
    /// AddTemporalIdentity's recognized id changed from a placeholder to a real timestamp.
    /// If the legacy id is stamped in __EFMigrationsHistory, migrate that row to the new id
    /// (the corresponding tables — temporal_beliefs etc. — already exist, so replaying the
    /// migration would throw "table temporal_beliefs already exists"). Guarded so it only
    /// touches the history table when it exists; affects 0 rows on a fresh database.
    /// </item>
    /// <item>
    /// AddSemanticMemoryColumns was previously unrecognized (no [Migration] attribute) and is
    /// now a recognized, pending migration. A pre-existing database already has the memories
    /// columns it adds (Embedding, LinkedMemoryId, DecayRate, Confidence, Tags), so stamp it as
    /// applied to avoid a duplicate ADD COLUMN. On a fresh database the memories table/columns do
    /// not exist yet, so the column check fails and MigrateAsync applies the migration normally.
    /// </item>
    /// </list>
    /// </summary>
    private async Task ReconcilePendingMigrationsAsync(CancellationToken cancellationToken)
    {
        var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
        var allMigrations = migrationsAssembly.Migrations.Keys
            .OrderBy(migration => migration, StringComparer.Ordinal)
            .ToList();

        // (1) Rename-stamp for AddTemporalIdentity. Only meaningful once a history table exists.
        if (await HasMigrationsHistoryTableAsync(cancellationToken)
            && FindMigration(allMigrations, "_AddTemporalIdentity") is { } temporalMigrationId
            && !string.Equals(temporalMigrationId, LegacyTemporalIdentityMigrationId, StringComparison.Ordinal)
            && await MigrationStampedAsync(LegacyTemporalIdentityMigrationId, cancellationToken))
        {
            // Carry the legacy row's ProductVersion forward where present; INSERT OR IGNORE keeps
            // an already-correct row intact, then the legacy row is removed. Idempotent across runs.
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                "SELECT {0}, \"ProductVersion\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {1};",
                new object[] { temporalMigrationId, LegacyTemporalIdentityMigrationId },
                cancellationToken);
            await _context.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {0};",
                new object[] { LegacyTemporalIdentityMigrationId },
                cancellationToken);
        }

        // (2) Schema-exists stamp for AddSemanticMemoryColumns. Only stamp when the migration is
        // recognized, still pending, and its columns are already present on the memories table.
        if (FindMigration(allMigrations, "_AddSemanticMemoryColumns") is { } semanticMemoryMigrationId)
        {
            var pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Contains(semanticMemoryMigrationId, StringComparer.Ordinal)
                && await AllColumnsExistAsync(
                    "memories",
                    ["Embedding", "LinkedMemoryId", "DecayRate", "Confidence", "Tags"],
                    cancellationToken))
            {
                // The history table is guaranteed to exist here: AdoptBaselineAsync creates it when
                // it ran, and any DB that already has these columns also has an EF history table.
                // StampMigrationAsync uses INSERT OR IGNORE, so this never double-stamps.
                await StampMigrationAsync(semanticMemoryMigrationId, [], cancellationToken);
            }
        }
    }

    private async Task<bool> MigrationStampedAsync(string migrationId, CancellationToken cancellationToken)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            cmd.CommandText = "SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $migrationId;";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$migrationId";
            parameter.Value = migrationId;
            cmd.Parameters.Add(parameter);
            return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
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

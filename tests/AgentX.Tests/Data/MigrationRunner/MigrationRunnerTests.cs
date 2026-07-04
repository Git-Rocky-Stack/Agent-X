using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentX.Core.Data;
using AgentX.Core.Data.MigrationRunner;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentX.Tests.Data.MigrationRunner;

public class MigrationRunnerTests
{
    private static (AgentXDbContext ctx, string dbPath) CreateContextAtTempPath()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-migtest-{System.Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return (new AgentXDbContext(options), dbPath);
    }

    [Fact]
    public async Task RunAsync_on_fresh_database_applies_all_migrations()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var result = await runner.RunAsync();

            result.DatabaseCreated.Should().BeTrue();
            result.AppliedMigrations.Should().NotBeEmpty();
            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_InitialBaseline"));
            File.Exists(dbPath).Should().BeTrue();

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            await ctx.Database.OpenConnectionAsync();
            try
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('conversation_theme_clusters','conversation_theme_memberships','conversation_theme_daily_metrics');";
                var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                tableCount.Should().Be(3);
            }
            finally
            {
                await ctx.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_freshly_created_empty_database_file_creates_full_schema()
    {
        // Regression for the fresh-install defect: the real app opens the SQLite connection
        // (EnsureKeyApplied applies the SQLCipher PRAGMA) BEFORE the migration runner, which
        // creates an empty .db file on disk. The runner must NOT mistake that empty file for a
        // pre-migration install and adopt the baseline -- doing so stamps migrations as applied
        // without creating any tables, so MigrateAsync skips the table-creating baseline and the
        // app fails every query with "no such table: memories/documents/...". An empty database
        // must flow through MigrateAsync and receive the full schema.
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            // Simulate EnsureKeyApplied: open + close so the empty .db file exists before the run.
            await ctx.Database.OpenConnectionAsync();
            await ctx.Database.CloseConnectionAsync();
            File.Exists(dbPath).Should().BeTrue("opening the connection creates the empty db file");

            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var result = await runner.RunAsync();

            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_InitialBaseline"));

            // The core application tables that failed on a real install must all exist.
            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            await ctx.Database.OpenConnectionAsync();
            try
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
                    "AND name IN ('documents','memories','user_settings','oauth_credentials');";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                count.Should().Be(4);
            }
            finally
            {
                await ctx.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_reports_DatabaseCreated_true_when_schema_absent_despite_preopened_file()
    {
        // Regression for KNOWN-ISSUE #7: app startup opens the connection (EnsureKeyApplied
        // applies the SQLCipher PRAGMA) BEFORE the runner, creating an empty .db file. The
        // previous implementation derived DatabaseCreated from CanConnectAsync(), which is true
        // for that empty file, so a genuinely fresh install was mis-reported as created=false.
        // DatabaseCreated must reflect whether this run created the SCHEMA, derived from real
        // table presence — a freshly-opened empty file still means "created on this run".
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            await ctx.Database.OpenConnectionAsync();
            await ctx.Database.CloseConnectionAsync();
            File.Exists(dbPath).Should().BeTrue("opening the connection creates the empty db file");

            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            var result = await runner.RunAsync();

            result.DatabaseCreated.Should().BeTrue(
                "an empty pre-created file carries no schema, so this run created the database");
            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_InitialBaseline"));
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_fresh_database_does_not_leave_the_licenses_table()
    {
        // Regression for KNOWN-ISSUE #6: Agent-X is fully free, so 20260528120000_DropLicensesTable
        // removes the licenses table. InitialBaseline still CREATES it (it must — so the drop has a
        // target on every historical install and so the migration stays reversible), but a fresh
        // install applies all migrations in order and the trailing DropLicensesTable removes it
        // again. The resulting schema must therefore contain NO licenses table.
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            await runner.RunAsync();

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            await ctx.Database.OpenConnectionAsync();
            try
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='licenses';";
                var licensesTables = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                licensesTables.Should().Be(0, "DropLicensesTable must leave no licenses table on a fresh install");
            }
            finally
            {
                await ctx.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_up_to_date_database_applies_nothing()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            await runner.RunAsync();

            var secondResult = await runner.RunAsync();

            secondResult.DatabaseCreated.Should().BeFalse();
            secondResult.AppliedMigrations.Should().BeEmpty();
            secondResult.AlreadyApplied.Should().NotBeEmpty();
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_up_to_date_database_repairs_missing_operations_tables()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            await runner.RunAsync();

            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS workflow_runs;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS workflow_steps;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS workflows;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS sync_logs;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS plugins;");

            var secondResult = await runner.RunAsync();

            secondResult.DatabaseCreated.Should().BeFalse();
            await AssertOperationsTablesExistAsync(ctx);
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task GetPendingMigrationsAsync_returns_empty_when_up_to_date()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            await runner.RunAsync();

            var pending = await runner.GetPendingMigrationsAsync();

            pending.Should().BeEmpty();
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task GetPendingMigrationsAsync_on_empty_database_returns_all_migrations()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var pending = await runner.GetPendingMigrationsAsync();

            pending.Should().NotBeEmpty();
            pending.Should().Contain(m => m.EndsWith("_InitialBaseline"));
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_preexisting_database_without_history_table_adopts_baseline()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            // Simulate pre-migration install: schema present, no __EFMigrationsHistory.
            // Baseline adoption must handle this case by stamping the initial baseline
            // migration as applied without re-running its SQL. The fixture starts from
            // the current model via EnsureCreated(), so we drop any tables introduced
            // after the initial baseline to mimic an older pre-migration install before
            // invoking the runner.
            await ctx.Database.EnsureCreatedAsync();
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS __EFMigrationsHistory;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS conversation_summary_states;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS conversation_summary_snapshots;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS conversation_theme_memberships;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS conversation_theme_clusters;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS conversation_theme_daily_metrics;");
            await ctx.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_messages_EmbeddedAt;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE messages DROP COLUMN Embedding;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE messages DROP COLUMN EmbeddedAt;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE messages DROP COLUMN EmbeddingModel;");

            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var result = await runner.RunAsync();

            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_InitialBaseline"));
            // Data tables must still exist after adoption (query sqlite_master via raw ADO.NET).
            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            await ctx.Database.OpenConnectionAsync();
            try
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='conversations';";
                var tableExists = (await cmd.ExecuteScalarAsync()) is not null;
                tableExists.Should().BeTrue();
            }
            finally
            {
                await ctx.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_preexisting_database_with_incomplete_baseline_creates_missing_baseline_tables()
    {
        // Regression for AX-QA-002 (data integrity): a legacy/partial install carries baseline +
        // later-migration tables (38 application tables) but is MISSING baseline tables and has no
        // __EFMigrationsHistory. The old behavior stamped _InitialBaseline as applied because at
        // least one application table existed — so the missing baseline tables were never created,
        // and a later migration's "ALTER TABLE memories ..." failed with
        // "SQLite Error 1: 'no such table: memories'". Baseline adoption must self-heal: create the
        // missing baseline objects (via EF's own SQL generator, not hand-duplicated DDL), re-verify
        // the full 28-table baseline is present, THEN stamp _InitialBaseline so MigrateAsync applies
        // only the genuinely-pending later migrations against a complete schema.
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            // Start from the current model (all tables), then mutate it to mimic the reproduced DB:
            // remove the history table and two baseline tables (memories + oauth_credentials).
            // Nothing FK-references these two baseline tables, so they drop cleanly.
            await ctx.Database.EnsureCreatedAsync();
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS __EFMigrationsHistory;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS oauth_credentials;");
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS memories;");

            // Sanity: the two baseline tables are genuinely absent before the run.
            (await TableExistsAsync(ctx, "memories")).Should().BeFalse();
            (await TableExistsAsync(ctx, "oauth_credentials")).Should().BeFalse();

            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            // The previously-failing "ALTER TABLE memories ..." path must now succeed.
            var act = async () => await runner.RunAsync();
            await act.Should().NotThrowAsync();

            // The missing baseline tables must have been created during adoption.
            (await TableExistsAsync(ctx, "memories")).Should().BeTrue(
                "the incomplete baseline must be healed before _InitialBaseline is stamped");
            (await TableExistsAsync(ctx, "oauth_credentials")).Should().BeTrue(
                "the incomplete baseline must be healed before _InitialBaseline is stamped");

            // _InitialBaseline must be stamped so EF treats the baseline as applied.
            var stamped = await GetStampedMigrationsAsync(ctx);
            stamped.Should().Contain(m => m.EndsWith("_InitialBaseline"));

            // The later semantic-memory migration's columns must exist on the now-present memories
            // table — proof the ALTER TABLE path that used to crash on "no such table" now runs.
            var memoryColumns = await GetTableColumnsAsync(ctx, "memories");
            memoryColumns.Should().Contain(new[] { "Embedding", "DecayRate", "LinkedMemoryId", "Confidence", "Tags" });
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_fresh_database_creates_semantic_memory_columns()
    {
        // Regression for the latent fresh-install defect: AddSemanticMemoryColumns was a
        // Migration subclass with no [Migration]/[DbContext] attributes, so EF never applied
        // it. A fresh install (MigrationRunner -> MigrateAsync) was therefore missing these
        // memories columns. With the attributes restored, the migration must run on a fresh DB.
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var result = await runner.RunAsync();

            result.DatabaseCreated.Should().BeTrue();
            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_AddSemanticMemoryColumns"));

            var memoryColumns = await GetTableColumnsAsync(ctx, "memories");
            memoryColumns.Should().Contain(new[] { "Embedding", "DecayRate", "LinkedMemoryId", "Confidence", "Tags" });
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_on_existing_database_stamps_target_migrations_without_replaying()
    {
        // Existing-DB safety: a developer's agentx.db already has the semantic memory columns
        // and a __EFMigrationsHistory table, but the two now-recognized migrations are not yet
        // stamped. MigrateAsync would replay AddSemanticMemoryColumns (duplicate ADD COLUMN) and
        // halt startup. The reconciliation step must stamp it instead, and AddTemporalIdentity
        // (whose tables are absent here) must be applied normally. Neither must throw.
        var (seedCtx, dbPath) = CreateContextAtTempPath();
        try
        {
            // Bring the DB fully up to date (schema + all migrations stamped), then rewind the
            // history to simulate the pre-existing install: drop the two target rows and remove
            // the temporal tables so AddTemporalIdentity can be applied cleanly by MigrateAsync.
            IMigrationRunner seedRunner = new Core.Data.MigrationRunner.MigrationRunner(seedCtx);
            await seedRunner.RunAsync();

            await seedCtx.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" IN ('20260422120000_AddSemanticMemoryColumns', '20260430000000_AddTemporalIdentity');");
            await seedCtx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS belief_conflicts;");
            await seedCtx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS voice_profiles;");
            await seedCtx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS engagement_metrics;");
            await seedCtx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS insight_moments;");
            await seedCtx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS temporal_beliefs;");

            await seedCtx.DisposeAsync();
            SqliteConnection.ClearAllPools();

            // Reopen against the same file as a brand-new context (no in-memory carry-over) and run.
            var options = new DbContextOptionsBuilder<AgentXDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using var ctx = new AgentXDbContext(options);
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var act = async () => await runner.RunAsync();
            await act.Should().NotThrowAsync();

            var stamped = await GetStampedMigrationsAsync(ctx);
            stamped.Should().Contain("20260422120000_AddSemanticMemoryColumns");
            stamped.Should().Contain("20260430000000_AddTemporalIdentity");

            // The semantic columns must remain intact (they were never dropped / re-added).
            var memoryColumns = await GetTableColumnsAsync(ctx, "memories");
            memoryColumns.Should().Contain(new[] { "Embedding", "DecayRate", "LinkedMemoryId", "Confidence", "Tags" });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_migrates_legacy_temporal_identity_history_id()
    {
        // A pre-existing DB created before AddTemporalIdentity's placeholder id was corrected has
        // the legacy "20260430XXXXXX_AddTemporalIdentity" stamped and the temporal tables present.
        // Under the corrected id that migration would be pending; replaying it would throw
        // "table temporal_beliefs already exists". The reconciliation must migrate the legacy
        // history row to the corrected id so the migration is treated as applied.
        var (seedCtx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner seedRunner = new Core.Data.MigrationRunner.MigrationRunner(seedCtx);
            await seedRunner.RunAsync();

            // Rewrite history to the legacy id (tables stay in place — mimicking the old install).
            await seedCtx.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260430000000_AddTemporalIdentity';");
            await seedCtx.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260430XXXXXX_AddTemporalIdentity', '8.0.11');");

            await seedCtx.DisposeAsync();
            SqliteConnection.ClearAllPools();

            var options = new DbContextOptionsBuilder<AgentXDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using var ctx = new AgentXDbContext(options);
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var act = async () => await runner.RunAsync();
            await act.Should().NotThrowAsync();

            var stamped = await GetStampedMigrationsAsync(ctx);
            stamped.Should().NotContain("20260430XXXXXX_AddTemporalIdentity");
            stamped.Should().Contain("20260430000000_AddTemporalIdentity");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private static async Task<bool> TableExistsAsync(AgentXDbContext ctx, string tableName)
    {
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        await ctx.Database.OpenConnectionAsync();
        try
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$tableName;";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$tableName";
            parameter.Value = tableName;
            cmd.Parameters.Add(parameter);
            return (await cmd.ExecuteScalarAsync()) is not null;
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private static async Task<System.Collections.Generic.List<string>> GetTableColumnsAsync(
        AgentXDbContext ctx, string tableName)
    {
        var columns = new System.Collections.Generic.List<string>();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        await ctx.Database.OpenConnectionAsync();
        try
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }

        return columns;
    }

    private static async Task<System.Collections.Generic.List<string>> GetStampedMigrationsAsync(AgentXDbContext ctx)
    {
        var ids = new System.Collections.Generic.List<string>();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        await ctx.Database.OpenConnectionAsync();
        try
        {
            cmd.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\";";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetString(0));
            }
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }

        return ids;
    }

    private static async Task AssertOperationsTablesExistAsync(AgentXDbContext ctx)
    {
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        await ctx.Database.OpenConnectionAsync();
        try
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master " +
                "WHERE type='table' AND name IN ('plugins','sync_logs','workflows','workflow_runs','workflow_steps');";
            var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            tableCount.Should().Be(5);
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private static async Task<System.Collections.Generic.HashSet<string>> GetColumnsAsync(
        AgentXDbContext ctx, string table)
    {
        var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        await ctx.Database.OpenConnectionAsync();
        try
        {
            cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                cols.Add(reader.GetString(1));
            }
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
        return cols;
    }

    [Fact]
    public async Task RunAsync_heals_stamped_baseline_missing_memories_table_before_pending_replay()
    {
        // Regression for the stamped-baseline brick: a database stamped by the pre-AX-QA-002
        // adopter carries _InitialBaseline in __EFMigrationsHistory but is MISSING the memories
        // table (the old adopter stamped without verifying schema). AddSemanticMemoryColumns is
        // pending, so MigrateAsync replays "ALTER TABLE memories ..." against the missing table,
        // throws "no such table: memories", and the fail-closed startup gate bricks the app on
        // every launch. The runner must heal the missing baseline table first, fast-forward it
        // through the ALREADY-STAMPED migrations only, then let the pending replay proceed.
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            await runner.RunAsync();

            // Sabotage into the pre-heal-adopter state: memories gone, AddSemanticMemoryColumns
            // un-stamped (pending), everything else still stamped as applied.
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS memories;");
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_AddSemanticMemoryColumns';");

            var result = await runner.RunAsync();

            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_AddSemanticMemoryColumns"));

            var cols = await GetColumnsAsync(ctx, "memories");
            // Baseline shape + stamped fast-forward (versioning) + pending replay (semantic memory).
            cols.Should().Contain(["Embedding", "LinkedMemoryId", "DecayRate", "Confidence", "Tags"]);
            cols.Should().Contain(["EmbeddingModelVersion", "EmbeddingDimensions", "EmbeddedAt"]);

            // Converged: a further run applies nothing.
            var thirdResult = await runner.RunAsync();
            thirdResult.AppliedMigrations.Should().BeEmpty();
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RunAsync_heals_stamped_baseline_when_multiple_memories_migrations_are_pending()
    {
        // Same brick, deeper history hole — the exact topology observed on a real dev database:
        // memories missing AND both AddSemanticMemoryColumns and AddEmbeddingModelVersioning
        // absent from __EFMigrationsHistory (pending), with document_chunks/messages still at
        // their pre-versioning shape. The heal must recreate memories at BASELINE shape only
        // (no stamped migration touches it), so both pending migrations replay cleanly across
        // memories, document_chunks, and messages without "duplicate column name".
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);
            await runner.RunAsync();

            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS memories;");
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_AddSemanticMemoryColumns';");
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_AddEmbeddingModelVersioning';");
            // Rewind document_chunks/messages to their pre-versioning shape so the pending
            // versioning migration has real work to do (indexes first — SQLite cannot drop an
            // indexed column, and EF's replayed CREATE INDEX has no IF NOT EXISTS).
            await ctx.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_document_chunks_EmbeddingModelVersion;");
            await ctx.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_messages_EmbeddingModel;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE document_chunks DROP COLUMN EmbeddingModelVersion;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE document_chunks DROP COLUMN EmbeddingDimensions;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE document_chunks DROP COLUMN EmbeddedAt;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE messages DROP COLUMN EmbeddingDimensions;");

            var result = await runner.RunAsync();

            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_AddSemanticMemoryColumns"));
            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_AddEmbeddingModelVersioning"));

            var memoryCols = await GetColumnsAsync(ctx, "memories");
            memoryCols.Should().Contain(["Embedding", "LinkedMemoryId", "DecayRate", "Confidence", "Tags"]);
            memoryCols.Should().Contain(["EmbeddingModelVersion", "EmbeddingDimensions", "EmbeddedAt"]);

            var chunkCols = await GetColumnsAsync(ctx, "document_chunks");
            chunkCols.Should().Contain(["EmbeddingModelVersion", "EmbeddingDimensions", "EmbeddedAt"]);

            var messageCols = await GetColumnsAsync(ctx, "messages");
            messageCols.Should().Contain("EmbeddingDimensions");

            var thirdResult = await runner.RunAsync();
            thirdResult.AppliedMigrations.Should().BeEmpty();
        }
        finally
        {
            await ctx.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}

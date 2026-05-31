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
}

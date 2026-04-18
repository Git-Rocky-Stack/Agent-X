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
            // EnsureCreated creates the CURRENT model's schema which includes columns added
            // by post-baseline migrations (AddEncryptionColumns). To faithfully simulate a
            // pre-migration-era install (DB from before AddEncryptionColumns existed), drop
            // those columns so that after adoption the AddEncryptionColumns migration has
            // real work to do and does not fail with "duplicate column".
            await ctx.Database.EnsureCreatedAsync();
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS __EFMigrationsHistory;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE user_settings DROP COLUMN DpapiWrappedKey;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE user_settings DROP COLUMN EncryptionSaltBase64;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE user_settings DROP COLUMN EncryptionKeyStorageMode;");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE user_settings DROP COLUMN EncryptionEnabled;");

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
}

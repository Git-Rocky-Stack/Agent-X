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
    private readonly AgentXDbContext _context;

    public MigrationRunner(AgentXDbContext context)
    {
        _context = context;
    }

    public async Task<MigrationResult> RunAsync(CancellationToken cancellationToken = default)
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

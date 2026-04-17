using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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

        var pendingBefore = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var alreadyAppliedBefore = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

        if (pendingBefore.Count == 0)
        {
            return new MigrationResult(
                DatabaseCreated: !databaseExistedBefore,
                AppliedMigrations: System.Array.Empty<string>(),
                AlreadyApplied: alreadyAppliedBefore,
                DatabasePath: dbPath);
        }

        await _context.Database.MigrateAsync(cancellationToken);

        var appliedAfter = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var newlyApplied = appliedAfter.Except(alreadyAppliedBefore).ToList();

        return new MigrationResult(
            DatabaseCreated: !databaseExistedBefore,
            AppliedMigrations: newlyApplied,
            AlreadyApplied: alreadyAppliedBefore,
            DatabasePath: dbPath);
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
        return pending.ToList();
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

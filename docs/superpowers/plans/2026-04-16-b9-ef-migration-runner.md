# B9 — EF Migration Runner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `EnsureCreatedAsync()` with a proper EF Core migrations runner so schema changes are tracked, applied deterministically on startup, and auditable from a new `IMigrationRunner` service.

**Architecture:** Introduce a design-time `DbContextFactory`, generate a baseline migration that matches the current live schema (so existing installs don't lose data), then ship a `MigrationRunner` that applies pending migrations on app launch before any DI service touches the DB. Failures throw `PendingMigrationsException` with remediation data, and a startup check exposes pending state for UI surfacing in future phases.

**Tech Stack:** .NET 8, EF Core 8.0.11 (already in project), `Microsoft.EntityFrameworkCore.Design`, xUnit + FluentAssertions + Moq (test project), `dotnet ef` CLI tool.

---

## File Structure

**Create:**
- `src/AgentX.Core/Data/AgentXDbContextFactory.cs` — design-time factory for `dotnet ef` tooling
- `src/AgentX.Core/Data/MigrationRunner/IMigrationRunner.cs` — runner interface
- `src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs` — implementation
- `src/AgentX.Core/Data/MigrationRunner/MigrationResult.cs` — result DTO
- `src/AgentX.Core/Data/MigrationRunner/PendingMigrationsException.cs` — diagnostic exception
- `src/AgentX.Core/Data/Migrations/<timestamp>_InitialBaseline.cs` — generated baseline (via `dotnet ef`)
- `src/AgentX.Core/Data/Migrations/AgentXDbContextModelSnapshot.cs` — generated
- `tests/AgentX.Tests/Data/MigrationRunner/MigrationRunnerTests.cs`

**Modify:**
- `src/AgentX.Core/AgentX.Core.csproj` — add `Microsoft.EntityFrameworkCore.Design` and `Microsoft.EntityFrameworkCore.Tools`
- `src/AgentX.App/App.xaml.cs` lines 89–100 — swap `EnsureCreatedAsync` for `IMigrationRunner.RunAsync`
- `src/AgentX.App/Services/ServiceCollectionExtensions.cs` — register `IMigrationRunner` in DI (exact file path confirmed at Task 6)

---

### Task 1: Add EF Core design-time tooling packages

**Files:**
- Modify: `src/AgentX.Core/AgentX.Core.csproj`

- [ ] **Step 1: Inspect current package refs**

Run: `grep EntityFrameworkCore src/AgentX.Core/AgentX.Core.csproj`
Expected output confirms `Microsoft.EntityFrameworkCore.Sqlite 8.0.11` present; `Microsoft.EntityFrameworkCore.Design` already present (flagged as `OutputItemType="Analyzer"`).

- [ ] **Step 2: Verify Design package is usable at runtime**

Open `src/AgentX.Core/AgentX.Core.csproj`. If the existing `<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">` has `<PrivateAssets>all</PrivateAssets>` and `<IncludeAssets>runtime; build; ...</IncludeAssets>`, it's design-time only — that is fine for tooling. We do **not** need it at runtime for the runner (uses `Microsoft.EntityFrameworkCore.Migrate()` extension method from the `Sqlite` package, which is already present).

- [ ] **Step 3: Install `dotnet ef` tool globally (dev machine only)**

Run: `dotnet tool install --global dotnet-ef --version 8.0.11`
Expected: "Tool 'dotnet-ef' was successfully installed." (If already installed, upgrade with `dotnet tool update --global dotnet-ef --version 8.0.11`.)

- [ ] **Step 4: Verify tool**

Run: `dotnet ef --version`
Expected: `Entity Framework Core .NET Command-line Tools` followed by `8.0.11`.

- [ ] **Step 5: Commit if csproj changed**

Only commit if the csproj was modified in Step 2. Otherwise skip.

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X"
git add src/AgentX.Core/AgentX.Core.csproj
git commit -m "chore(ef): ensure EF Core Design package available for migrations tooling"
```

---

### Task 2: Create `AgentXDbContextFactory` for design-time tooling

**Files:**
- Create: `src/AgentX.Core/Data/AgentXDbContextFactory.cs`

- [ ] **Step 1: Write the factory**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentX.Core.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` tooling to build a DbContext
/// without the full DI container. Points at a neutral temp SQLite path so
/// tooling never writes into the user's real AgentX database.
/// </summary>
public class AgentXDbContextFactory : IDesignTimeDbContextFactory<AgentXDbContext>
{
    public AgentXDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite("Data Source=agentx.design.db")
            .Options;

        return new AgentXDbContext(options);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/AgentX.Core/AgentX.Core.csproj -c Debug`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add src/AgentX.Core/Data/AgentXDbContextFactory.cs
git commit -m "feat(ef): add design-time DbContext factory for migrations tooling"
```

---

### Task 3: Generate the `InitialBaseline` migration against the **current live schema**

This task captures the existing schema as migration #1. Any existing install will have this migration marked "applied" on first runner pass (via Task 6), so no data loss.

**Files:**
- Create (generated): `src/AgentX.Core/Data/Migrations/<timestamp>_InitialBaseline.cs`
- Create (generated): `src/AgentX.Core/Data/Migrations/<timestamp>_InitialBaseline.Designer.cs`
- Create (generated): `src/AgentX.Core/Data/Migrations/AgentXDbContextModelSnapshot.cs`

- [ ] **Step 1: Generate the migration**

Run from repo root:
```bash
dotnet ef migrations add InitialBaseline \
  --project src/AgentX.Core/AgentX.Core.csproj \
  --startup-project src/AgentX.Core/AgentX.Core.csproj \
  --output-dir Data/Migrations \
  --context AgentXDbContext
```
Expected: "Done. To undo this action, use 'ef migrations remove'" and three files created under `src/AgentX.Core/Data/Migrations/`.

- [ ] **Step 2: Open the generated migration and verify every existing table is present**

Open `src/AgentX.Core/Data/Migrations/<timestamp>_InitialBaseline.cs` in an editor. Confirm the `Up` method creates tables for every DbSet on `AgentXDbContext` (conversations, messages, documents, document_chunks, collections, document_collections, tags, document_tags, search_history, system_prompts, user_settings, watch_folders, indexing_jobs, licenses, memories, digest_reports, sync_logs, plugins, feedbacks, oauth_credentials, inbox_items, calendar_events, email_messages — whatever the current DbContext defines). If any is missing, delete the generated files and re-run Step 1 after confirming the DbContext compiles with all entities.

- [ ] **Step 3: Build and confirm**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Data/Migrations/
git commit -m "feat(ef): add InitialBaseline migration capturing current schema"
```

---

### Task 4: Create `MigrationResult` and `PendingMigrationsException`

**Files:**
- Create: `src/AgentX.Core/Data/MigrationRunner/MigrationResult.cs`
- Create: `src/AgentX.Core/Data/MigrationRunner/PendingMigrationsException.cs`

- [ ] **Step 1: Write `MigrationResult.cs`**

```csharp
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
```

- [ ] **Step 2: Write `PendingMigrationsException.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace AgentX.Core.Data.MigrationRunner;

/// <summary>
/// Thrown when migrations are pending but the runner was invoked in a read-only
/// mode (e.g., a dry-run startup check). Carries the pending migration names so
/// the caller can surface them in the UI.
/// </summary>
public sealed class PendingMigrationsException : Exception
{
    public IReadOnlyList<string> PendingMigrations { get; }

    public PendingMigrationsException(IReadOnlyList<string> pendingMigrations)
        : base($"{pendingMigrations.Count} migration(s) pending: {string.Join(", ", pendingMigrations)}")
    {
        PendingMigrations = pendingMigrations;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/AgentX.Core`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Data/MigrationRunner/
git commit -m "feat(migrations): add MigrationResult and PendingMigrationsException"
```

---

### Task 5: Write failing tests for `IMigrationRunner`

**Files:**
- Create: `src/AgentX.Core/Data/MigrationRunner/IMigrationRunner.cs` (stub only — implementation in Task 6)
- Create: `tests/AgentX.Tests/Data/MigrationRunner/MigrationRunnerTests.cs`

- [ ] **Step 1: Write the interface stub**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentX.Core.Data.MigrationRunner;

public interface IMigrationRunner
{
    /// <summary>
    /// Applies all pending migrations to the database. Creates the database if it does not exist.
    /// Idempotent — if no migrations are pending, returns a MigrationResult with empty AppliedMigrations.
    /// </summary>
    Task<MigrationResult> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns pending migration names without applying them.
    /// </summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the test file**

```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentX.Core.Data;
using AgentX.Core.Data.MigrationRunner;
using FluentAssertions;
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
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure**

Run: `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --filter "FullyQualifiedName~MigrationRunnerTests"`
Expected: Build fails with "The type or namespace name 'MigrationRunner' does not exist" — this is the failing-red state we want before implementing.

---

### Task 6: Implement `MigrationRunner`

**Files:**
- Create: `src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs`

- [ ] **Step 1: Write the implementation**

```csharp
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
```

- [ ] **Step 2: Run tests — expect pass**

Run: `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --filter "FullyQualifiedName~MigrationRunnerTests"`
Expected: All 4 tests pass.

- [ ] **Step 3: Run full test suite to verify no regressions**

Run: `dotnet test`
Expected: 673 tests pass (669 original + 4 new). No failures.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Data/MigrationRunner/ tests/AgentX.Tests/Data/MigrationRunner/
git commit -m "feat(migrations): add IMigrationRunner + MigrationRunner with pending-migration API"
```

---

### Task 7: Handle existing installs — baseline adoption test

When a user upgrades from a pre-migration install, their DB exists but has no `__EFMigrationsHistory` table. `MigrateAsync` against such a DB would try to recreate the `InitialBaseline` tables and fail. We need a baseline-adoption path.

**Files:**
- Modify: `src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs`
- Modify: `tests/AgentX.Tests/Data/MigrationRunner/MigrationRunnerTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `MigrationRunnerTests.cs` (inside the existing class):

```csharp
    [Fact]
    public async Task RunAsync_on_preexisting_database_without_history_table_adopts_baseline()
    {
        var (ctx, dbPath) = CreateContextAtTempPath();
        try
        {
            // Simulate pre-migration install: schema present, no __EFMigrationsHistory.
            await ctx.Database.EnsureCreatedAsync();
            // Drop history table if EnsureCreated created one (it doesn't, but be safe).
            await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS __EFMigrationsHistory;");

            IMigrationRunner runner = new Core.Data.MigrationRunner.MigrationRunner(ctx);

            var result = await runner.RunAsync();

            result.AppliedMigrations.Should().Contain(m => m.EndsWith("_InitialBaseline"));
            // Data tables must still exist after adoption.
            var tableExists = await ctx.Database.ExecuteSqlRawAsync(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='conversations';") >= 0;
            tableExists.Should().BeTrue();
        }
        finally
        {
            await ctx.DisposeAsync();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
```

- [ ] **Step 2: Run the new test — expect failure**

Run: `dotnet test --filter "FullyQualifiedName~RunAsync_on_preexisting_database_without_history_table_adopts_baseline"`
Expected: FAIL with SQLite error "table conversations already exists".

- [ ] **Step 3: Update `MigrationRunner.RunAsync` to handle baseline adoption**

Replace the body of `RunAsync` in `MigrationRunner.cs` with:

```csharp
    public async Task<MigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var dbPath = ExtractDbPath(_context);
        var databaseExistedBefore = await _context.Database.CanConnectAsync(cancellationToken);

        if (databaseExistedBefore && !await HasMigrationsHistoryTableAsync(cancellationToken))
        {
            await AdoptBaselineAsync(cancellationToken);
        }

        var alreadyAppliedBefore = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pendingBefore = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

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

    private async Task AdoptBaselineAsync(CancellationToken cancellationToken)
    {
        // Create the EF migrations history table and insert a row marking every
        // migration up through InitialBaseline as already applied, so MigrateAsync
        // only applies migrations that ship AFTER the baseline.
        var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
        var allMigrations = migrationsAssembly.Migrations.Keys.ToList();
        var baseline = allMigrations.FirstOrDefault(m => m.EndsWith("_InitialBaseline"));
        if (baseline is null) return;

        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" TEXT NOT NULL);",
            cancellationToken);

        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});",
            new object[] { baseline, "8.0.11" },
            cancellationToken);
    }
```

Add these `using` statements at the top of the file if not already present:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
```

- [ ] **Step 4: Run all migration tests — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~MigrationRunnerTests"`
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs tests/AgentX.Tests/Data/MigrationRunner/MigrationRunnerTests.cs
git commit -m "feat(migrations): adopt baseline for pre-migration installs"
```

---

### Task 8: Wire `MigrationRunner` into DI

**Files:**
- Modify: `src/AgentX.App/Services/ServiceCollectionExtensions.cs` (or whichever file registers services — locate at Step 1)

- [ ] **Step 1: Locate the DI registration site**

Run: `grep -rn "AddDbContext\|AddSingleton<I" src/AgentX.App --include="*.cs" | head -20`

Find the file that registers `AgentXDbContext` or any `I*Service`. Likely `src/AgentX.App/Services/ServiceCollectionExtensions.cs` or `src/AgentX.App/App.xaml.cs`.

- [ ] **Step 2: Register `IMigrationRunner`**

Add next to the existing `AgentXDbContext` registration:

```csharp
services.AddScoped<AgentX.Core.Data.MigrationRunner.IMigrationRunner,
                   AgentX.Core.Data.MigrationRunner.MigrationRunner>();
```

Scoped (not singleton) because it holds a DbContext, which is scoped in EF's default lifetime model.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add -u
git commit -m "feat(di): register IMigrationRunner as scoped service"
```

---

### Task 9: Replace `EnsureCreatedAsync` in app startup

**Files:**
- Modify: `src/AgentX.App/App.xaml.cs` (around lines 89–100)

- [ ] **Step 1: Read current implementation**

Open `src/AgentX.App/App.xaml.cs` and find `InitializeCoreServicesAsync`. Current body near line 95:

```csharp
await dbContext.Database.EnsureCreatedAsync();
```

- [ ] **Step 2: Replace with MigrationRunner call**

Replace the `EnsureCreatedAsync()` line with:

```csharp
using (var scope = Host.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<AgentX.Core.Data.MigrationRunner.IMigrationRunner>();
    var result = await runner.RunAsync();
    Serilog.Log.Information(
        "Migration runner: db={DbPath} created={Created} applied={Applied} alreadyApplied={AlreadyApplied}",
        result.DatabasePath,
        result.DatabaseCreated,
        string.Join(",", result.AppliedMigrations),
        string.Join(",", result.AlreadyApplied));
}
```

Ensure `using Microsoft.Extensions.DependencyInjection;` is at the top of the file (it likely already is).

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Smoke test — launch app**

Run the app from IDE or:
```bash
dotnet run --project src/AgentX.App/AgentX.App.csproj
```

Expected: App launches normally. Check Serilog log file at `%LOCALAPPDATA%\AgentX\logs\` for a "Migration runner" entry with `created=False` and `alreadyApplied` listing `<timestamp>_InitialBaseline`. The existing DB was adopted at baseline.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.App/App.xaml.cs
git commit -m "feat(startup): replace EnsureCreatedAsync with IMigrationRunner.RunAsync"
```

---

### Task 10: Full regression gate

**Files:** none

- [ ] **Step 1: Run full test suite**

Run: `dotnet test`
Expected: All tests pass. Count is `669 original + 5 new MigrationRunnerTests = 674 passing`.

- [ ] **Step 2: Run the app one more time to sanity-check schema**

Launch app. Verify:
- Existing chat history visible (pre-upgrade data preserved)
- Documents list loads
- Settings load
- Open a new chat, send a message — persists across restart

- [ ] **Step 3: Confirm `__EFMigrationsHistory` table exists with baseline row**

Use any SQLite browser (or `sqlite3 "%LOCALAPPDATA%\AgentX\agentx.db" ".schema __EFMigrationsHistory"` then `SELECT * FROM __EFMigrationsHistory;`).
Expected: one row, `MigrationId` ends with `_InitialBaseline`, `ProductVersion` = `8.0.11`.

- [ ] **Step 4: Update docs**

Edit `docs/ARCHITECTURE.md` and append a short section under the data-access description:

```markdown
### Migrations

Schema changes ship via EF Core migrations under `src/AgentX.Core/Data/Migrations/`. `IMigrationRunner` is invoked during `App.InitializeCoreServicesAsync` to apply any pending migrations at launch. Pre-migration installs are automatically adopted at the `InitialBaseline` migration so existing user data is preserved on first run after upgrade.
```

- [ ] **Step 5: Final commit**

```bash
git add docs/ARCHITECTURE.md
git commit -m "docs(architecture): document migration runner and baseline adoption"
```

---

## Self-Review Summary

- **Spec coverage:** B9 scope items addressed — `IMigrationRunner` exists (Task 5–6), pending-migration startup check (Task 5/6 GetPendingMigrationsAsync), rollback discipline enforced by EF migrations (built-in `Remove-Migration` command documented as next-step below), ad-hoc `ALTER TABLE` pattern eliminated (Task 9 replaces EnsureCreatedAsync).
- **Placeholder scan:** every code step contains complete code, every command has expected output, no "TODO" / "TBD" / "similar to".
- **Type consistency:** `MigrationResult`, `IMigrationRunner`, `PendingMigrationsException` consistent across Tasks 4, 5, 6, 7, 8, 9.

## Follow-up (not in this plan)

1. Document the migration authoring workflow in `docs/DEVELOPER-GUIDE.md` — command to add a migration: `dotnet ef migrations add <Name> --project src/AgentX.Core --output-dir Data/Migrations`.
2. Add a UI surface for pending-migrations state in Phase 1 closeout (Settings page banner).
3. C13 SQLCipher plan will consume `IMigrationRunner` to flip the DB encryption atomically.

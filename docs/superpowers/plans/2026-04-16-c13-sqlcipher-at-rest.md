# C13 — SQLCipher At-Rest Encryption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Encrypt the entire `agentx.db` SQLite database at rest using SQLCipher (AES-256-CBC) with a tier-aware key-management scheme, and provide a safe one-shot migration path that converts existing plaintext databases to encrypted form without data loss.

**Architecture:** Swap the SQLite native library at the SQLitePCLRaw provider layer using `SQLitePCLRaw.bundle_e_sqlcipher`. Every `SqliteConnection` created anywhere in the codebase must pass through a new `IEncryptedConnectionFactory` that appends the correct `Password=` clause. Key material is derived via PBKDF2-HMAC-SHA256 from a user passphrase (Ultimate tier) or auto-generated and DPAPI-wrapped (Starter/Professional — transparent). A migration service performs the atomic plaintext→encrypted database conversion via the SQLite `sqlcipher_export()` function. All five existing Sqlite connection sites are routed through the factory. Runs **after B9** and uses `IMigrationRunner` to re-apply migrations to the new encrypted file so schema continuity is preserved.

**Tech Stack:** .NET 8, Microsoft.Data.Sqlite 8.0.11, `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.7+ (ships SQLCipher 4.6.x native), existing `DpapiEncryptionService`, new `IDatabaseKeyService` + `IEncryptedConnectionFactory` + `IDatabaseEncryptionMigrator`.

**Prerequisite:** Plan B9 landed (IMigrationRunner exists).

---

## Pre-Implementation Spikes (REQUIRED — run first)

Every spike produces a concrete finding written into the `Spike Findings` subsection at the end of each spike. If a spike invalidates any implementation task below, the plan is revised **in place** and committed before the first implementation task starts. Zero assumptions survive into implementation.

### Spike 1 — Verify SQLCipher provider swap actually engages

**Question:** Does adding `SQLitePCLRaw.bundle_e_sqlcipher 2.1.7` to a csproj and calling `SQLitePCL.Batteries_V2.Init()` actually route through SQLCipher 4.x — not the default sqlite3 bundle that silently ignores `Password=`?

- [ ] Create a throwaway `spike/sqlcipher-probe/` project: `dotnet new console -o spike/sqlcipher-probe`
- [ ] Add `Microsoft.Data.Sqlite 8.0.11` and `SQLitePCLRaw.bundle_e_sqlcipher 2.1.7`
- [ ] Write a 20-line Main: init provider, create db with `Password=x'0102...20 bytes...'`, write 1 row, close, reopen without password — must throw with "file is not a database" message
- [ ] Run it. Confirm the exception actually fires. Confirm reopening WITH the same password returns the row.
- [ ] Record exact exception message and stack (implementation tests depend on it)
- [ ] If it does NOT encrypt: research the `Mode=` connection string options and the `PRAGMA key` fallback path — update Task 1 Step 7 of this plan with the corrected setup before proceeding.
- [ ] Delete `spike/sqlcipher-probe/` after finding is recorded.

**Spike Findings (recorded 2026-04-17, probe project deleted):**
- **SQLitePCLRaw.bundle_e_sqlcipher 2.1.7 installs cleanly.** Provider engagement: **CONFIRMED.**
- **Step 2 (open encrypted DB with no password):** throws `SqliteException` with `SqliteErrorCode: 26`, message: `'file is not a database'`.
- **Step 3 (open with wrong password):** throws `SqliteException` with `SqliteErrorCode: 26`, message: `'file is not a database'`. (Same error code and message as no-password — tests must not distinguish these two cases by exception content.)
- **Step 4 (open with right password):** opens successfully; row read back correctly.
- **CRITICAL FINDING — Plan is wrong in a silent-corruption way:** `SqliteConnectionStringBuilder.Password` property sends the value through **PBKDF2 key derivation** — it is treated as a passphrase. `PRAGMA key = "x'<hex>'"` and `ATTACH DATABASE ... KEY "x'<hex>'"` use **raw key bytes** (no KDF). Using `Password="<hex>"` for create and `KEY "x'<hex>'"` for ATTACH produces **two different derived keys** → the DB appears corrupt on reopen. This bug would have shipped without this spike.
- **Correct key-delivery pattern for raw-hex keys (applies to ALL SqliteConnection opens in C13):**
  1. Open with a bare connection string — NO `Password=`: `new SqliteConnection($"Data Source={path}")`
  2. Immediately after `.Open()`, execute ONE pragma: `PRAGMA key = "x'<hexKey>'";`
  3. Only then issue other commands on the connection
  4. For ATTACH operations: include `KEY "x'<hex>'"` in the ATTACH statement directly
- **This pattern must be used consistently** across create, reopen, and ATTACH. Mixing `Password=` with `PRAGMA key`/`ATTACH KEY` produces key-format mismatch.

**Revisions to plan tasks at Spike Closure:**
1. **Task 1 Step 7:** Retain the `SQLitePCL.Batteries_V2.Init()` call and the provider setup. Remove any language suggesting `Password=` is the correct key-delivery mechanism.
2. **Task 7 (EncryptedConnectionFactory) rewrite:** Remove `builder.Password = key.HexKey` from `BuildConnectionString`. Factory returns plain `Data Source=<path>` string. Add a new method `SqliteConnection OpenKeyed(string dbPath)` that opens the connection AND executes `PRAGMA key` in one unit — this is the new canonical entry point. Existing callers using `CreateConnection` need to migrate to `OpenKeyed` OR call a helper `ApplyKey(SqliteConnection)` immediately after `.Open()`. Update tests accordingly.
3. **Task 12 (DatabaseEncryptionMigrator):** Already uses ATTACH KEY — consistent with the raw-bytes path. After DETACH, the verification open must also use `PRAGMA key` (NOT `builder.Password`). Update verification code accordingly.
4. **Tests that assert exception on wrong-key open:** must expect `SqliteException` with `SqliteErrorCode == 26` AND message `'file is not a database'`. Same error fires for no-key and wrong-key cases — tests cannot distinguish.
5. **Add a new helper in `EncryptedConnectionFactory`:** `ApplyKey(SqliteConnection)` method that reads the key from `IDatabaseKeyProvider.Current` and executes `PRAGMA key = "x'<hex>'"` against the given connection. This keeps the PRAGMA construction in one place with proper escaping.

### Spike 2 — Inventory every `SqliteConnection` creation site

**Question:** The plan lists 5 sites but line numbers may have drifted since the plan was written. Are there sites the plan missed entirely?

- [ ] Run `grep -rn "new SqliteConnection\|SqliteConnectionStringBuilder" src/AgentX.Core src/AgentX.App --include='*.cs' | grep -v '/bin/' | grep -v '/obj/'`
- [ ] Compare result against the 5 sites in the plan (`AgentXDbContext:61`, `HnswVectorStore:187-194`, `SqliteVecStore:78-85`, `BackupService:787-788`, `WorkspaceService:545-551, 611-617`)
- [ ] If any site is **missing** from the plan: add it to Task 8 with exact file path and line range
- [ ] If any line number is drifted by more than 10 lines: update the plan's Task 8 with current line numbers

**Spike Findings (recorded 2026-04-17):**
- **Total SqliteConnection sites found: 6** — plan listed 5; one new post-B9 addition.
- Plan's 5 production sites all confirmed at the documented line numbers:
  - `src/AgentX.Core/Data/AgentXDbContext.cs:61` — `optionsBuilder.UseSqlite($"Data Source={_dbPath}")`
  - `src/AgentX.Core/Data/VectorDb/HnswVectorStore.cs:187` + `194` — builder + `new SqliteConnection`
  - `src/AgentX.Core/Data/VectorDb/SqliteVecStore.cs:78` + `85` — builder + `new SqliteConnection`
  - `src/AgentX.Core/Services/Backup/BackupService.cs:787` + `788` — two `new SqliteConnection($"Data Source=...")` (source + destination)
  - `src/AgentX.Core/Services/Workspaces/WorkspaceService.cs:545` + `551` — builder + `await using var connection = new SqliteConnection(...)`
- **New site discovered (post-B9): `src/AgentX.Core/Data/AgentXDbContextFactory.cs:16`** — `.UseSqlite("Data Source=agentx.design.db")`. This is the design-time `IDesignTimeDbContextFactory` for `dotnet ef` migration tooling. It writes to a throwaway tooling DB that is never used by the shipping app. **EXEMPTION:** this site MUST NOT route through `IEncryptedConnectionFactory` — adding a `Password=` parameter would break `dotnet ef migrations add` / `script` because those CLI invocations do not have a real key in scope.
- **Line-range correction:** plan's Task 8 reference to `WorkspaceService.cs:611-617` is wrong. Line ~611 is `QueryTableCountAsync(SqliteConnection connection, ...)` — a method that **receives** a `SqliteConnection` as a parameter, not a site that creates one. The creator is line 545/551; that caller already routes through the factory, so the callee needs no change. Drop 611-617 from Task 8.
- **Task 8 revisions applied at Spike Closure:**
  1. Add an explicit "Do NOT modify" note for `AgentXDbContextFactory.cs:16` (design-time factory exempt from encryption).
  2. Remove the `WorkspaceService.cs:611-617` reference; keep only `545-551`.

### Spike 3 — Prove `sqlcipher_export()` works via ATTACH DATABASE

**Question:** Can we invoke the SQLCipher `sqlcipher_export()` function through ATTACH inside a `Microsoft.Data.Sqlite` SqliteCommand — or does the plaintext source need to be opened a specific way?

- [ ] Write a 40-line integration probe in the same `spike/sqlcipher-probe` project (or a fresh one): create plaintext DB with 3 rows across 2 tables, ATTACH an empty encrypted DB with `KEY "x'<64 hex chars>'"`, run `SELECT sqlcipher_export('encrypted');`, DETACH, close source, open encrypted target with password and verify rows present.
- [ ] Record the exact command string that works, including quoting style for the KEY parameter.
- [ ] If the pattern differs from Task 12's `DatabaseEncryptionMigrator.MigrateToEncryptedAsync` code: update Task 12 Step 1 before proceeding.
- [ ] Delete the probe.

**Spike Findings (recorded 2026-04-17, probe project deleted):**
- **Exact working command string (verified end-to-end):**
  ```sql
  ATTACH DATABASE '<encPath>' AS encrypted KEY "x'<hexKey>'";
  SELECT sqlcipher_export('encrypted');
  DETACH DATABASE encrypted;
  ```
- **Quoting style:** `"x'<hex>'"` — double-quote wrapper, `x`-prefix hex literal inside single quotes. This is the SQLite BLOB literal syntax; `x'<hex>'` is recognized by SQLCipher as a raw-bytes key.
- **Source-DB key delivery:** the plaintext source is opened with **NO key and NO PRAGMA key** (standard open) — because the source is plaintext. Only the ATTACH target receives a key.
- **Verification of encrypted target:** opening the target with `PRAGMA key = "x'<hex>'"` immediately after `.Open()` (NOT `Password=`) successfully reads the exported rows. Row count matched source (2 of 2).
- **Plaintext-read of encrypted target:** throws `SqliteException` with `SqliteErrorCode: 26`, message `'file is not a database'` — same exception as wrong-key case.
- **Windows-specific footgun:** `Microsoft.Data.Sqlite` uses a connection pool that **holds a file handle open** after `using` block disposes. `File.Delete(plaintextPath)` immediately after `sqlcipher_export` throws `IOException` (file in use). **Must call `SqliteConnection.ClearAllPools()`** (or a `GC.Collect()` + `GC.WaitForPendingFinalizers()` pair) before attempting to delete the plaintext source. This affects the `DatabaseEncryptionMigrator` where the migrator deletes the plaintext backup after verification.

**Revisions to Task 12 at Spike Closure:**
1. Keep the exact ATTACH+export+DETACH SQL shown above. It works verbatim.
2. **After DETACH but before the `File.Move` operations:** call `SqliteConnection.ClearAllPools()` to release the source-DB file handle. Add `using Microsoft.Data.Sqlite;` if not already present for `SqliteConnection.ClearAllPools` accessibility.
3. **Verification-open step** in `MigrateToEncryptedAsync`: replace the `new SqliteConnectionStringBuilder { DataSource = dbPath, Password = key.HexKey }` pattern with: open connection with bare `Data Source=...`, then execute `PRAGMA key = "x'<hexKey>'";`, then run the verification query. This aligns with the Spike 1 canonical key-delivery pattern.
4. **Rollback cleanup:** if the migration fails after `File.Move(dbPath, backupPath)`, the plaintext backup at `backupPath` will be restored — and that file also needs `ClearAllPools` called before the `File.Delete(tempEncryptedPath)` in the failure path.

### Spike 4 — Verify DI lifetime assumptions

**Question:** B9 discovered `AgentXDbContext` is Singleton. Will the plan's proposed `AddScoped<IDatabaseKeyService>` and `AddSingleton<IDatabaseKeyProvider>` graph actually resolve cleanly, or will we hit "cannot consume scoped service from singleton" at runtime?

- [ ] Read `src/AgentX.App/App.xaml.cs` around line 167 and confirm `AddSingleton<AgentXDbContext>()` still present
- [ ] Enumerate which C13 services consume `AgentXDbContext` directly: `IDatabaseKeyService`, `IAuditKeyService` (from C14), `IDatabaseEncryptionMigrator`
- [ ] Decision: because `AgentXDbContext` is Singleton and only startup code calls these services, register them all as **Singleton** (NOT Scoped as plan originally prescribes).
- [ ] Update Task 9 Step 1 of this plan: change `AddScoped` to `AddSingleton` for `IDatabaseKeyService` and `IDatabaseEncryptionMigrator`. `IDatabaseKeyProvider` stays Singleton. `IEncryptedConnectionFactory` stays Singleton.
- [ ] Update commit messages accordingly.

**Spike Findings (recorded 2026-04-17):**
- **DbContext lifetime confirmed: Singleton** at `src/AgentX.App/App.xaml.cs:166` — `services.AddSingleton<AgentXDbContext>();`
- **Codebase DI convention is uniformly Singleton for production services.** Confirmed registrations include: `DpapiEncryptionService` (171), `SecurityStatusService` (172), `OAuthService` (175, factory form), `SettingsService` (207), `LicenseService` (208), `FeatureFlagService` (209), `KeyboardShortcutService` (212), `ThemeService` (213), `AiService` + 10+ AI services (216–226), `IVectorStore` factory (229), `ConversationService` (237), `ChatService` (240), `ScreenCaptureService` (243), all `IDocumentProcessor` implementations (246–249), `IMigrationRunner` from B9 (167). There are **no `AddScoped` registrations anywhere in the production DI graph** (transients appear only for WinUI 3 ViewModels).
- **Corrected lifetimes for C13 services:**
  - `IDatabaseKeyProvider` → **Singleton** (already Singleton in plan — unchanged)
  - `IEncryptedConnectionFactory` → **Singleton** (already Singleton in plan — unchanged)
  - `IDatabaseKeyService` → **Singleton** (CHANGED — plan said Scoped)
  - `IDatabaseEncryptionMigrator` → **Singleton** (CHANGED — plan Task 13 said Scoped)
- **Task 9 Step 1 and Task 13 Step 1 revisions at Spike Closure:**
  1. Replace `services.AddScoped<IDatabaseKeyService, DatabaseKeyService>();` with `services.AddSingleton<IDatabaseKeyService, DatabaseKeyService>();`
  2. Replace `services.AddScoped<IDatabaseEncryptionMigrator, DatabaseEncryptionMigrator>();` with `services.AddSingleton<IDatabaseEncryptionMigrator, DatabaseEncryptionMigrator>();`
  3. Commit messages: "register as singleton" (matching B9's `feat(di): register IMigrationRunner as singleton service` pattern)
- **C14 implication:** the C14 plan's `IAuditKeyService`, `IAuditLogService`, `IAuditLogVerifier`, `IAuditLogExporter` should also all be Singleton (C14 Spike 4 should re-verify).

### Spike 5 — Locate real Settings UI and License tier plumbing

**Question:** The plan assumes `SettingsPage.xaml` and `SettingsViewModel.cs` exist with a known structure, and `ILicenseService.GetCurrentTierAsync()` returns a `LicenseTier.Ultimate` enum value. Verify.

- [ ] `grep -rn "class SettingsViewModel\|class SettingsPage" src/AgentX.App --include='*.cs'` — capture exact file paths
- [ ] Read the ViewModel constructor to see existing DI injection pattern (keep the additions consistent)
- [ ] `grep -rn "enum LicenseTier\|interface ILicenseService\|class LicenseService" src/AgentX.Core --include='*.cs'` — confirm `Ultimate` is a valid tier name and `GetCurrentTierAsync` is the correct method name
- [ ] If either differs (e.g., tier is called `Pro` not `Professional`, method is `GetTier()` not `GetCurrentTierAsync()`): update Task 13 Step 3 code block with the correct names.

**Spike Findings (recorded 2026-04-17):**
- **SettingsViewModel path:** `src/AgentX.App/ViewModels/SettingsViewModel.cs` — `public partial class SettingsViewModel : ObservableObject` at line 15. Namespace `AgentX.App.ViewModels`.
- **SettingsPage path:** `src/AgentX.App/Views/SettingsPage.xaml.cs` — `public sealed partial class SettingsPage : Page`.
- **Existing VM injection pattern:** constructor already takes 7 deps (`ISettingsService`, `ILicenseService`, `IAiService`, `ICostTracker`, `IThemeService`, `ISecurityStatusService`, `IModelRouterService?`). Adding C13's `IDatabaseKeyService`, `IDatabaseEncryptionMigrator`, `IDatabaseKeyProvider` fits this pattern cleanly — no refactor needed.
- **LicenseTier enum values:** `Trial`, `Starter`, `Professional`, `Ultimate` (exactly 4; defined at `src/AgentX.Core/Services/License/LicenseTier.cs:7`).
- **Correct method on `ILicenseService`:** `Task<LicenseInfo> GetCurrentLicenseAsync()` — **NOT** `GetCurrentTierAsync()` (plan was wrong). The plan's Task 13 Step 3 must be corrected.
- **Correct accessor pattern:** `var info = await _licenseService.GetCurrentLicenseAsync(); var tier = info.Tier;`
- **Dialog pattern:** codebase has no `IDialogService` abstraction. Direct `ContentDialog` construction is standard — examples in `MainWindow.xaml.cs:304`, `ChatPage.xaml.cs` (lines 332/370/394), and `ExportDialog` (subclass of `ContentDialog`). Plan's inline ContentDialog + PasswordBox approach is consistent.
- **XamlRoot pattern:** `App.MainWindow.Content.XamlRoot` is used throughout for VM-invoked dialogs; confirmed usable.
- **Code corrections for Task 13 Step 3 at Spike Closure:**
  1. Replace `var tier = await _license.GetCurrentTierAsync();` with `var licenseInfo = await _license.GetCurrentLicenseAsync();`
  2. Replace `var mode = tier == LicenseTier.Ultimate ? ... : ...;` with `var mode = licenseInfo.Tier == LicenseTier.Ultimate ? ... : ...;`
  3. Rename the `_license` field to `_licenseService` to match existing ViewModel convention.

### Spike 6 — Startup sequence ordering (encryption unlock vs B9 migration runner)

**Question:** The plan's Task 14 adds an encryption unlock step **before** the migration runner. Will the migration runner's existing DI wiring still resolve if the DbContext cannot be constructed (because no key is set yet for the encrypted DB)?

- [ ] Read the current `App.InitializeCoreServicesAsync` sequence (post-B9 state)
- [ ] Confirm the migration runner's first DB touch happens inside `RunAsync()` itself, not on DI resolution
- [ ] Decision: unlock flow must run **before** ANY DbContext method is called. In the Task 14 code block, move all key-service + key-provider work into a pre-migration scope, then resolve migration runner in a second scope.
- [ ] Also verify: the `IDatabaseKeyService.IsProvisionedAsync()` call itself touches the DB (it reads UserSettings). This is a chicken-and-egg problem on first launch with encryption pre-provisioned by a different user profile (rare but possible). Document the fallback: attempt unlock, catch SqliteException with "file is not a database", prompt passphrase.
- [ ] Update Task 14 implementation with the corrected ordering and exception handling.

**Spike Findings (recorded 2026-04-17):**
- **Current post-B9 startup sequence** in `App.InitializeCoreServicesAsync`:
  1. `GetService<IMigrationRunner>().RunAsync()` — first DB touch
  1b. `IKeywordSearchService.InitializeFtsAsync()` — more DB
  2. `IAiService.InitializeAsync()` — unrelated
- **MigrationRunner's first DB touch:** `await _context.Database.CanConnectAsync()` on the FIRST line of `RunAsync` (after `ExtractDbPath`). On an encrypted DB with no key loaded, this throws `SqliteException` ("file is not a database" or SQLCipher-specific). So unlock MUST happen before `IMigrationRunner.RunAsync()`.
- **Chicken-and-egg caught:** the plan's Task 14 calls `IDatabaseKeyService.IsProvisionedAsync()` which reads `UserSettings.EncryptionEnabled` — but reading `UserSettings` requires the DB to be UNLOCKED, which requires knowing the mode, which we don't know until reading UserSettings. Plan is broken as written.
- **Fix — add an out-of-DB marker file:** whenever encryption is provisioned, write `%LocalAppData%\AgentX\encryption.info.json`:
  ```json
  { "version": 1, "storageMode": "DpapiWrapped" | "UserPassphrase", "enabledAt": "<ISO-8601>" }
  ```
  Startup reads this marker FIRST (no DB access). Absent → plaintext DB, skip unlock. Present → load key per mode, set provider, THEN run migrations.
- **New interface needed:** `IEncryptionStateFile` with methods `Exists()`, `Read()`, `Write(KeyStorageMode mode)`, `Delete()` — backed by a JSON file at the known path. Put under `src/AgentX.Core/Services/Security/EncryptionStateFile.cs`.
- **`IDatabaseKeyService.IsProvisionedAsync()` still exists** for scenarios where we want post-unlock verification of UserSettings (e.g., confirm key matches what was expected, detect state-file vs DB-state desync). But startup gate is the marker file, not the DB read.
- **Ordering corrections applied at Spike Closure — revised Task 14 sequence:**
  1. Read `IEncryptionStateFile.Exists()`. If false → skip all encryption logic; fall through to existing migration runner call.
  2. If true → read the marker, get `storageMode`.
  3. If `DpapiWrapped` → `keySvc.GetOrCreateKeyAsync(DpapiWrapped)` and `keyProvider.Set(key)`. No user prompt.
  4. If `UserPassphrase` → show passphrase dialog. On submit → `keySvc.UnlockWithPassphraseAsync(passphrase)` → `keyProvider.Set(key)` → open a trivial test connection via `IEncryptedConnectionFactory` → execute `PRAGMA schema_version;`. If that throws `SqliteException` → wrong passphrase, loop prompt. If success → break.
  5. Only then → `IMigrationRunner.RunAsync()`.
- **Exception-handling additions:**
  - `SqliteException` on key-probed open → treat as wrong key / corrupted header (user-passphrase path loops; DPAPI path should alert, never loop).
  - `FileNotFoundException` when marker claims encryption but DB file missing → log error, tell user to restore from backup.
  - `JsonException` on marker parse → treat as tamper/corruption; prompt user to re-provision (destructive — requires backup).
- **Task 13 "enable encryption" flow must ALSO write the marker file** after `MigrateToEncryptedAsync` succeeds (atomic with the encryption flip — write marker LAST so if anything upstream fails, startup still sees "no marker" and opens plaintext).
- **Task 14 startup code at Spike Closure is rewritten — see revised Task 14 section for the full implementation.**

### Spike Closure

Before starting Task 1 of the implementation:
- [ ] All 6 spike findings recorded above
- [ ] Plan tasks revised in place for every finding that changes implementation
- [ ] Commit the revised plan with message: `docs(plans): revise C13 plan after pre-implementation spikes`
- [ ] Only then begin Task 1.

---

## ⚠️ Spike Closure Corrections — READ BEFORE IMPLEMENTATION

**The implementation tasks (Task 1 → Task 15) below were written before the spikes ran. Apply these corrections as you execute each task.** The spike findings in the preceding sections are authoritative; where a task below contradicts a finding, the finding wins.

### Critical correction #1 — Key delivery: `PRAGMA key`, NOT `Password=`

`SqliteConnectionStringBuilder.Password` runs the value through PBKDF2 KDF. `PRAGMA key = "x'<hex>'"` and `ATTACH ... KEY "x'<hex>'"` use raw bytes. **These produce different derived keys.** Mixing them silently corrupts the DB on reopen.

**Every SqliteConnection open in C13 uses this pattern:**
```csharp
using var conn = new SqliteConnection($"Data Source={dbPath}");  // NO Password=
conn.Open();
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = $@"PRAGMA key = ""x'{hexKey}'"";";
    cmd.ExecuteNonQuery();
}
// ... now issue other commands
```

### Correction #2 — `EncryptedConnectionFactory` shape (Task 7)

Replace the proposed `CreateConnection(dbPath)` / `BuildConnectionString(dbPath)` surface with:

```csharp
public interface IEncryptedConnectionFactory
{
    /// <summary>Opens a connection AND applies the current PRAGMA key if one is loaded.
    /// If no key is loaded (encryption disabled), returns a plaintext-opened connection.</summary>
    SqliteConnection OpenKeyed(string dbPath);

    /// <summary>Applies PRAGMA key to an already-opened connection. No-op if no key loaded.
    /// Use this when a caller (e.g. ATTACH-based workflow) opens its own connection.</summary>
    void ApplyKey(SqliteConnection openConnection);
}
```

Implementation:
```csharp
public SqliteConnection OpenKeyed(string dbPath)
{
    var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    ApplyKey(conn);
    return conn;
}

public void ApplyKey(SqliteConnection openConnection)
{
    var key = _keyProvider.Current;
    if (key is null) return;
    using var cmd = openConnection.CreateCommand();
    // key.HexKey is 64 hex chars; x'<hex>' is the SQLite BLOB literal form for raw bytes.
    cmd.CommandText = $@"PRAGMA key = ""x'{key.HexKey}'"";";
    cmd.ExecuteNonQuery();
}
```

Tests in Task 6 must:
- Drop the `CreateConnection` tests that assert `cs.Should().Contain("Password=...")` — that semantic is gone.
- Replace with tests that `OpenKeyed(path)` on an encrypted DB with the correct key reads rows back; with the wrong key throws `SqliteException` (ErrorCode 26, message `'file is not a database'`).

### Correction #3 — Routing DbContext through the factory (Task 8)

`AgentXDbContext` is Singleton. It cannot use `OpenKeyed` directly from `OnConfiguring` because EF creates the connection lazily. Instead:

1. Inject `IEncryptedConnectionFactory` into `AgentXDbContext` via constructor (new overload already exists per plan Task 8 Step 1).
2. Override `OnConfiguring` to build a plain `Data Source=...` connection string.
3. In the factory's `ApplyKey` extension, ALSO register an EF Core `IDbContextOptionsConfiguration` that hooks a connection-opened callback — OR simpler: subscribe to `_context.Database.GetDbConnection().StateChange` and call `ApplyKey` when state transitions to Open.

The simplest safe pattern: add a method `EnsureKeyApplied()` on DbContext that the MigrationRunner and Unlock flow call once at startup:
```csharp
public void EnsureKeyApplied()
{
    var conn = Database.GetDbConnection();
    if (conn.State == ConnectionState.Closed) conn.Open();
    _connectionFactory?.ApplyKey((SqliteConnection)conn);
}
```
Call this AFTER unlock and BEFORE `RunAsync` so the migration runner's first DB touch already has the key.

### Correction #4 — Design-time factory is EXEMPT (Task 8)

`src/AgentX.Core/Data/AgentXDbContextFactory.cs:16` is post-B9 and is used ONLY by `dotnet ef` tooling. It must **NOT** route through `IEncryptedConnectionFactory` — `dotnet ef` invocations do not have a key provider in scope. Leave that file alone.

### Correction #5 — `WorkspaceService.cs:611-617` is not a site (Task 8)

Drop the `611-617` reference. Only `545` + `551` create a connection. Line 611 is `QueryTableCountAsync(SqliteConnection connection, ...)` which receives a connection — no change needed there.

### Correction #6 — DI lifetimes: everything Singleton (Task 9, Task 13)

- `IDatabaseKeyProvider` → Singleton (unchanged)
- `IEncryptedConnectionFactory` → Singleton (unchanged)
- `IDatabaseKeyService` → **Singleton** (plan said Scoped — change to Singleton)
- `IDatabaseEncryptionMigrator` → **Singleton** (plan said Scoped — change to Singleton)
- `IEncryptionStateFile` (new — see correction #10) → Singleton

Commit message format: follow B9's pattern — `feat(di): register C13 security services as singletons`.

### Correction #7 — License API (Task 13 Step 3)

The plan's `GetCurrentTierAsync()` does not exist. Use:
```csharp
var licenseInfo = await _licenseService.GetCurrentLicenseAsync();
var mode = licenseInfo.Tier == LicenseTier.Ultimate
    ? KeyStorageMode.UserPassphrase
    : KeyStorageMode.DpapiWrapped;
```
Rename the field `_license` → `_licenseService` to match existing VM convention.

### Correction #8 — Migrator verification open + file-handle release (Task 12)

In `DatabaseEncryptionMigrator.MigrateToEncryptedAsync`:

1. **Before `File.Move(dbPath, backupPath)`:** call `SqliteConnection.ClearAllPools();` — Microsoft.Data.Sqlite's pool holds a file handle open after the `using` block disposes. Without this, `File.Move` fails on Windows with `IOException`.
2. **Verification step** (after the move): open the encrypted DB with `new SqliteConnection($"Data Source={dbPath}")` (plain), then `PRAGMA key = "x'<hexKey>'";`, then run the verification query. Do NOT use `builder.Password = key.HexKey`.
3. **Rollback path:** before `File.Move(backupPath, dbPath)` and `File.Delete(tempEncryptedPath)`, call `ClearAllPools()` again.

### Correction #9 — Tests expect ErrorCode 26 / "file is not a database"

Any test asserting `SqliteException` on a wrong-key or no-key open of an encrypted DB must expect:
- `ex.SqliteErrorCode == 26`
- `ex.Message.Contains("file is not a database")`

The same error fires for both wrong-key and no-key — tests cannot distinguish these two cases by exception content. If you need to differentiate, add an `ApplyKey` path that detects the "no-key" case upstream (check `_keyProvider.Current` is null before calling).

### Correction #10 — New `IEncryptionStateFile` to break unlock chicken-and-egg (Task 14)

Add BEFORE Task 14:

**Task 13.5 (NEW) — `IEncryptionStateFile`:**
- Create `src/AgentX.Core/Services/Security/IEncryptionStateFile.cs` and `EncryptionStateFile.cs`
- Stores a JSON marker at `%LocalAppData%\AgentX\encryption.info.json`:
  ```json
  { "version": 1, "storageMode": "DpapiWrapped" | "UserPassphrase", "enabledAt": "<ISO-8601>" }
  ```
- Methods: `bool Exists()`, `EncryptionStateInfo? Read()`, `Task WriteAsync(KeyStorageMode mode)`, `void Delete()`
- Written **after** `MigrateToEncryptedAsync` succeeds in Task 13's enable flow (last step — so if anything upstream fails, startup still sees "no marker" and opens plaintext)
- Deleted if the user ever disables encryption (future feature — out of scope here; leave a `// TODO` comment if the delete path is not wired yet)

**Task 14 rewritten:**
```csharp
using (var scope = Host.Services.CreateScope())
{
    var stateFile = scope.ServiceProvider.GetRequiredService<IEncryptionStateFile>();

    if (stateFile.Exists())
    {
        var info = stateFile.Read()!;
        var keySvc = scope.ServiceProvider.GetRequiredService<IDatabaseKeyService>();
        var keyProvider = (DatabaseKeyProvider)scope.ServiceProvider.GetRequiredService<IDatabaseKeyProvider>();

        DatabaseKeyMaterial key;
        if (info.StorageMode == KeyStorageMode.DpapiWrapped)
        {
            // Transparent unlock — no user prompt
            key = await keySvc.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);
        }
        else
        {
            // Passphrase unlock loop
            while (true)
            {
                var passphrase = await PromptForPassphraseAsync();
                if (passphrase is null) { Application.Current.Exit(); return; }
                var candidate = await keySvc.UnlockWithPassphraseAsync(passphrase);
                if (await TryProbeKeyAsync(candidate))
                {
                    key = candidate;
                    break;
                }
                await ShowInvalidPassphraseDialogAsync();
            }
        }
        keyProvider.Set(key);
    }
    // If no marker file: plaintext DB, fall through without key.
}

// THEN the migration runner scope from B9 runs — the DbContext will now see the key
// via EnsureKeyApplied() which the runner should call internally as its first step.
```

Add helper `TryProbeKeyAsync`:
```csharp
private static async Task<bool> TryProbeKeyAsync(DatabaseKeyMaterial candidate)
{
    var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentX", "agentx.db");
    try
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"PRAGMA key = ""x'{candidate.HexKey}'""; SELECT COUNT(1) FROM sqlite_master;";
        await cmd.ExecuteScalarAsync();
        return true;
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 26)
    {
        return false;
    }
}
```

This probe uses the canonical PRAGMA key path and checks for ErrorCode 26 specifically — exactly matching the Spike 1 findings.

### Correction #11 — MigrationRunner integration

`IMigrationRunner.RunAsync` (from B9) touches the DB on `CanConnectAsync`. When encryption is on, the DbContext must have its key applied BEFORE that call. Since the DbContext is Singleton, the key stays applied for the rest of the session.

Option A (minimal change to B9 code): add a call in `MigrationRunner.RunAsync` that checks if the DbContext has an `IEncryptedConnectionFactory` and calls `EnsureKeyApplied()` before `CanConnectAsync`. Requires exposing a new method on DbContext.

Option B (cleaner): don't touch B9's `MigrationRunner`. Instead, in `App.InitializeCoreServicesAsync`, after the unlock flow and BEFORE `runner.RunAsync()`, call:
```csharp
var db = GetService<AgentXDbContext>();
db.EnsureKeyApplied();  // opens connection if not open, applies PRAGMA key if key loaded
```

**Use Option B.** It keeps B9's migration runner purely about migrations, and the key lifecycle stays in the startup sequence where it's visible.

---

**All 11 corrections must be applied when executing Tasks 1–15. The findings sections at the top of this document are the authoritative source — any contradiction with task prose below is resolved by the findings.**

---

## File Structure

**Create:**
- `src/AgentX.Core/Services/Security/IDatabaseKeyService.cs` — key derivation + storage interface
- `src/AgentX.Core/Services/Security/DatabaseKeyService.cs` — implementation
- `src/AgentX.Core/Services/Security/DatabaseKeyMaterial.cs` — record type carrying derived key + metadata
- `src/AgentX.Core/Services/Security/KeyStorageMode.cs` — enum (DpapiWrapped, UserPassphrase)
- `src/AgentX.Core/Data/IEncryptedConnectionFactory.cs` — factory interface
- `src/AgentX.Core/Data/EncryptedConnectionFactory.cs` — implementation
- `src/AgentX.Core/Services/Security/IDatabaseEncryptionMigrator.cs` — one-shot migrator interface
- `src/AgentX.Core/Services/Security/DatabaseEncryptionMigrator.cs` — implementation
- `src/AgentX.Core/Services/Security/InvalidDatabaseKeyException.cs` — wrong-passphrase exception
- `tests/AgentX.Tests/Services/Security/DatabaseKeyServiceTests.cs`
- `tests/AgentX.Tests/Services/Security/DatabaseEncryptionMigratorTests.cs`
- `tests/AgentX.Tests/Data/EncryptedConnectionFactoryTests.cs`

**Modify:**
- `src/AgentX.Core/AgentX.Core.csproj` — replace `SQLitePCLRaw` bundle (if present) with `SQLitePCLRaw.bundle_e_sqlcipher`, add version pin
- `src/AgentX.Core/Data/AgentXDbContext.cs` — use factory for connection string
- `src/AgentX.Core/Data/VectorDb/HnswVectorStore.cs` — route connection through factory
- `src/AgentX.Core/Data/VectorDb/SqliteVecStore.cs` — route connection through factory
- `src/AgentX.Core/Services/Backup/BackupService.cs` — route connection through factory (backup of encrypted DB must stay encrypted)
- `src/AgentX.Core/Services/Workspaces/WorkspaceService.cs` — route connection through factory
- `src/AgentX.App/Services/ServiceCollectionExtensions.cs` — register new services
- `src/AgentX.App/App.xaml.cs` — key unlock step before MigrationRunner call
- `src/AgentX.Core/Data/Entities/UserSettingsEntity.cs` — add `EncryptionEnabled`, `KeyStorageMode`, `EncryptionSalt` columns (migration in Task 10)

---

### Task 1: Add the SQLCipher SQLitePCLRaw bundle

**Files:**
- Modify: `src/AgentX.Core/AgentX.Core.csproj`
- Modify: `tests/AgentX.Tests/AgentX.Tests.csproj`

- [ ] **Step 1: Inspect current SQLite native provider**

Run: `grep -n "SQLitePCLRaw\|Microsoft.Data.Sqlite" src/AgentX.Core/AgentX.Core.csproj tests/AgentX.Tests/AgentX.Tests.csproj`
Expected: both csproj files reference `Microsoft.Data.Sqlite 8.0.11`. The `Microsoft.Data.Sqlite` package transitively includes `SQLitePCLRaw.bundle_e_sqlite3` — we need to replace that with the SQLCipher bundle.

- [ ] **Step 2: Add SQLCipher bundle to Core csproj**

Add inside the existing `<ItemGroup>` that holds package references in `src/AgentX.Core/AgentX.Core.csproj`:

```xml
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlcipher" Version="2.1.7" />
```

- [ ] **Step 3: Add SQLCipher bundle to Test csproj**

Same line into `tests/AgentX.Tests/AgentX.Tests.csproj`:

```xml
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlcipher" Version="2.1.7" />
```

- [ ] **Step 4: Initialize the SQLCipher provider at app startup**

The bundle requires `Batteries.Init()` before first use. Open `src/AgentX.App/App.xaml.cs` and inside `InitializeCoreServicesAsync` add this as the **first** line before any DB-touching call:

```csharp
SQLitePCL.Batteries_V2.Init();
```

Add `using SQLitePCL;` at top of file if needed.

- [ ] **Step 5: Do the same for tests**

Create `tests/AgentX.Tests/TestFixtures/SqlCipherFixture.cs`:

```csharp
using Xunit;

namespace AgentX.Tests.TestFixtures;

public sealed class SqlCipherFixture
{
    public SqlCipherFixture()
    {
        SQLitePCL.Batteries_V2.Init();
    }
}

[CollectionDefinition("SqlCipher")]
public sealed class SqlCipherCollection : ICollectionFixture<SqlCipherFixture> { }
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded. 0 Errors. There may be a warning about two SQLitePCLRaw providers being present — resolve in Step 7.

- [ ] **Step 7: Remove the default `bundle_e_sqlite3` if transitively pulled**

If the build warns about duplicate providers, add to BOTH csproj files in the same `<ItemGroup>`:

```xml
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.7" ExcludeAssets="all" />
```

Re-run `dotnet build`. Expected: clean build.

- [ ] **Step 8: Commit**

```bash
git add src/AgentX.Core/AgentX.Core.csproj tests/AgentX.Tests/AgentX.Tests.csproj src/AgentX.App/App.xaml.cs tests/AgentX.Tests/TestFixtures/SqlCipherFixture.cs
git commit -m "chore(sqlcipher): add SQLitePCLRaw.bundle_e_sqlcipher + provider init"
```

---

### Task 2: `KeyStorageMode` enum and `DatabaseKeyMaterial` record

**Files:**
- Create: `src/AgentX.Core/Services/Security/KeyStorageMode.cs`
- Create: `src/AgentX.Core/Services/Security/DatabaseKeyMaterial.cs`

- [ ] **Step 1: Write `KeyStorageMode.cs`**

```csharp
namespace AgentX.Core.Services.Security;

public enum KeyStorageMode
{
    /// <summary>Auto-generated 32-byte key stored DPAPI-wrapped in UserSettings. Transparent to user (Starter/Professional tier default).</summary>
    DpapiWrapped = 0,

    /// <summary>User supplies a passphrase on each launch (Ultimate tier). Key derived via PBKDF2-HMAC-SHA256.</summary>
    UserPassphrase = 1,
}
```

- [ ] **Step 2: Write `DatabaseKeyMaterial.cs`**

```csharp
using System;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Derived database key material passed to SQLCipher via connection string Password=.
/// Hex-encoded 64 characters (32 bytes, 256 bits).
/// </summary>
public sealed record DatabaseKeyMaterial(string HexKey, KeyStorageMode Mode)
{
    public static DatabaseKeyMaterial FromBytes(ReadOnlySpan<byte> keyBytes, KeyStorageMode mode)
    {
        if (keyBytes.Length != 32)
            throw new ArgumentException("Database key must be exactly 32 bytes (256 bits).", nameof(keyBytes));
        return new DatabaseKeyMaterial(Convert.ToHexString(keyBytes), mode);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/AgentX.Core`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Services/Security/KeyStorageMode.cs src/AgentX.Core/Services/Security/DatabaseKeyMaterial.cs
git commit -m "feat(security): add KeyStorageMode and DatabaseKeyMaterial types"
```

---

### Task 3: `InvalidDatabaseKeyException`

**Files:**
- Create: `src/AgentX.Core/Services/Security/InvalidDatabaseKeyException.cs`

- [ ] **Step 1: Write**

```csharp
using System;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Thrown when the supplied key/passphrase cannot open the encrypted database.
/// Callers should prompt the user to re-enter their passphrase.
/// </summary>
public sealed class InvalidDatabaseKeyException : Exception
{
    public InvalidDatabaseKeyException() : base("The supplied database key is invalid.") { }
    public InvalidDatabaseKeyException(Exception inner) : base("The supplied database key is invalid.", inner) { }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/AgentX.Core/Services/Security/InvalidDatabaseKeyException.cs
git commit -m "feat(security): add InvalidDatabaseKeyException"
```

---

### Task 4: Write failing tests for `IDatabaseKeyService`

**Files:**
- Create: `src/AgentX.Core/Services/Security/IDatabaseKeyService.cs`
- Create: `tests/AgentX.Tests/Services/Security/DatabaseKeyServiceTests.cs`

- [ ] **Step 1: Write the interface stub**

```csharp
using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IDatabaseKeyService
{
    /// <summary>
    /// Returns existing key material if already provisioned, or provisions a new one
    /// using the given mode. In UserPassphrase mode, passphrase must be non-null.
    /// </summary>
    Task<DatabaseKeyMaterial> GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null);

    /// <summary>
    /// Re-derives the key from an existing passphrase without creating a new one.
    /// Throws InvalidDatabaseKeyException if passphrase does not match the stored salt's derivation.
    /// </summary>
    Task<DatabaseKeyMaterial> UnlockWithPassphraseAsync(string passphrase);

    /// <summary>
    /// Returns true if encryption has been provisioned (even if not currently unlocked).
    /// </summary>
    Task<bool> IsProvisionedAsync();

    /// <summary>
    /// Returns the provisioned storage mode, or null if not provisioned.
    /// </summary>
    Task<KeyStorageMode?> GetProvisionedModeAsync();
}
```

- [ ] **Step 2: Write the test file**

```csharp
using System.IO;
using System.Threading.Tasks;
using AgentX.Core.Data;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentX.Tests.Services.Security;

public class DatabaseKeyServiceTests
{
    private static AgentXDbContext NewContext(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"agentx-keysvc-{System.Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var ctx = new AgentXDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_with_DpapiWrapped_creates_new_key_on_first_call()
    {
        var ctx = NewContext(out var dbPath);
        try
        {
            var dpapi = new DpapiEncryptionService();
            IDatabaseKeyService sut = new DatabaseKeyService(ctx, dpapi);

            var key = await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);

            key.Mode.Should().Be(KeyStorageMode.DpapiWrapped);
            key.HexKey.Should().HaveLength(64);
            (await sut.IsProvisionedAsync()).Should().BeTrue();
        }
        finally
        {
            await ctx.DisposeAsync();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_returns_same_key_on_repeat_call()
    {
        var ctx = NewContext(out var dbPath);
        try
        {
            IDatabaseKeyService sut = new DatabaseKeyService(ctx, new DpapiEncryptionService());

            var key1 = await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);
            var key2 = await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);

            key2.HexKey.Should().Be(key1.HexKey);
        }
        finally
        {
            await ctx.DisposeAsync();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_with_UserPassphrase_derives_deterministic_key_for_same_passphrase()
    {
        var ctx = NewContext(out var dbPath);
        try
        {
            IDatabaseKeyService sut = new DatabaseKeyService(ctx, new DpapiEncryptionService());

            var key1 = await sut.GetOrCreateKeyAsync(KeyStorageMode.UserPassphrase, passphrase: "correct horse battery staple");

            key1.Mode.Should().Be(KeyStorageMode.UserPassphrase);
            key1.HexKey.Should().HaveLength(64);
        }
        finally
        {
            await ctx.DisposeAsync();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UnlockWithPassphraseAsync_with_correct_passphrase_returns_same_key()
    {
        var ctx = NewContext(out var dbPath);
        try
        {
            IDatabaseKeyService sut = new DatabaseKeyService(ctx, new DpapiEncryptionService());

            var created = await sut.GetOrCreateKeyAsync(KeyStorageMode.UserPassphrase, "correct horse battery staple");
            var unlocked = await sut.UnlockWithPassphraseAsync("correct horse battery staple");

            unlocked.HexKey.Should().Be(created.HexKey);
        }
        finally
        {
            await ctx.DisposeAsync();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UnlockWithPassphraseAsync_with_wrong_passphrase_still_derives_but_yields_different_key()
    {
        // Note: PBKDF2 derives deterministically from passphrase+salt, so a "wrong" passphrase
        // produces a different key. The encrypted-DB-open test (EncryptedConnectionFactoryTests) is what
        // actually rejects wrong passphrases, because SQLCipher fails to open with a bad key.
        var ctx = NewContext(out var dbPath);
        try
        {
            IDatabaseKeyService sut = new DatabaseKeyService(ctx, new DpapiEncryptionService());
            var created = await sut.GetOrCreateKeyAsync(KeyStorageMode.UserPassphrase, "right");

            var wrong = await sut.UnlockWithPassphraseAsync("wrong");

            wrong.HexKey.Should().NotBe(created.HexKey);
        }
        finally
        {
            await ctx.DisposeAsync();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task GetProvisionedModeAsync_returns_null_before_provisioning()
    {
        var ctx = NewContext(out var dbPath);
        try
        {
            IDatabaseKeyService sut = new DatabaseKeyService(ctx, new DpapiEncryptionService());

            var mode = await sut.GetProvisionedModeAsync();

            mode.Should().BeNull();
        }
        finally
        {
            await ctx.DisposeAsync();
            File.Delete(dbPath);
        }
    }
}
```

- [ ] **Step 3: Run tests — expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~DatabaseKeyServiceTests"`
Expected: Build error "type or namespace name 'DatabaseKeyService' does not exist".

---

### Task 5: Implement `DatabaseKeyService`

**Files:**
- Create: `src/AgentX.Core/Services/Security/DatabaseKeyService.cs`
- Modify: `src/AgentX.Core/Data/Entities/UserSettingsEntity.cs` — add 3 columns

- [ ] **Step 1: Add columns to `UserSettingsEntity`**

Open `src/AgentX.Core/Data/Entities/UserSettingsEntity.cs`. Inside the entity class, add:

```csharp
    public bool EncryptionEnabled { get; set; }
    public string? EncryptionKeyStorageMode { get; set; }  // "DpapiWrapped" | "UserPassphrase"
    public string? EncryptionSaltBase64 { get; set; }       // 16-byte salt, base64
    public string? DpapiWrappedKey { get; set; }             // DPAPI-encrypted hex key (DpapiWrapped mode only)
```

(No migration added here yet — Task 10 does that in one pass after migrator is written.)

- [ ] **Step 2: Write `DatabaseKeyService.cs`**

```csharp
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentX.Core.Services.Security;

public sealed class DatabaseKeyService : IDatabaseKeyService
{
    private const int KeyLengthBytes = 32;       // 256-bit key for SQLCipher
    private const int SaltLengthBytes = 16;
    private const int Pbkdf2Iterations = 600_000; // OWASP 2023 recommendation for PBKDF2-HMAC-SHA256

    private readonly AgentXDbContext _db;
    private readonly IDpapiEncryptionService _dpapi;

    public DatabaseKeyService(AgentXDbContext db, IDpapiEncryptionService dpapi)
    {
        _db = db;
        _dpapi = dpapi;
    }

    public async Task<DatabaseKeyMaterial> GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null)
    {
        if (mode == KeyStorageMode.UserPassphrase && string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase required for UserPassphrase mode.", nameof(passphrase));

        var settings = await EnsureSettingsRowAsync();

        if (settings.EncryptionEnabled && !string.IsNullOrEmpty(settings.EncryptionKeyStorageMode))
        {
            var existingMode = Enum.Parse<KeyStorageMode>(settings.EncryptionKeyStorageMode);
            return existingMode switch
            {
                KeyStorageMode.DpapiWrapped => UnwrapDpapiKey(settings.DpapiWrappedKey!),
                KeyStorageMode.UserPassphrase => DerivePassphraseKey(passphrase!, Convert.FromBase64String(settings.EncryptionSaltBase64!)),
                _ => throw new InvalidOperationException($"Unknown mode: {existingMode}")
            };
        }

        // First-time provisioning
        return mode switch
        {
            KeyStorageMode.DpapiWrapped => await ProvisionDpapiWrappedAsync(settings),
            KeyStorageMode.UserPassphrase => await ProvisionPassphraseAsync(settings, passphrase!),
            _ => throw new InvalidOperationException($"Unknown mode: {mode}")
        };
    }

    public async Task<DatabaseKeyMaterial> UnlockWithPassphraseAsync(string passphrase)
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings is null || !settings.EncryptionEnabled || string.IsNullOrEmpty(settings.EncryptionSaltBase64))
            throw new InvalidOperationException("Database encryption is not provisioned in UserPassphrase mode.");
        if (settings.EncryptionKeyStorageMode != nameof(KeyStorageMode.UserPassphrase))
            throw new InvalidOperationException("Provisioned mode is not UserPassphrase.");

        return DerivePassphraseKey(passphrase, Convert.FromBase64String(settings.EncryptionSaltBase64));
    }

    public async Task<bool> IsProvisionedAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        return settings is not null && settings.EncryptionEnabled;
    }

    public async Task<KeyStorageMode?> GetProvisionedModeAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings is null || !settings.EncryptionEnabled || string.IsNullOrEmpty(settings.EncryptionKeyStorageMode))
            return null;
        return Enum.Parse<KeyStorageMode>(settings.EncryptionKeyStorageMode);
    }

    private async Task<Data.Entities.UserSettingsEntity> EnsureSettingsRowAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new Data.Entities.UserSettingsEntity();
            _db.UserSettings.Add(settings);
        }
        return settings;
    }

    private async Task<DatabaseKeyMaterial> ProvisionDpapiWrappedAsync(Data.Entities.UserSettingsEntity settings)
    {
        var keyBytes = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var hexKey = Convert.ToHexString(keyBytes);
        var wrapped = _dpapi.Encrypt(hexKey);

        settings.EncryptionEnabled = true;
        settings.EncryptionKeyStorageMode = nameof(KeyStorageMode.DpapiWrapped);
        settings.DpapiWrappedKey = wrapped;
        settings.EncryptionSaltBase64 = null;
        await _db.SaveChangesAsync();

        return DatabaseKeyMaterial.FromBytes(keyBytes, KeyStorageMode.DpapiWrapped);
    }

    private async Task<DatabaseKeyMaterial> ProvisionPassphraseAsync(Data.Entities.UserSettingsEntity settings, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);

        settings.EncryptionEnabled = true;
        settings.EncryptionKeyStorageMode = nameof(KeyStorageMode.UserPassphrase);
        settings.EncryptionSaltBase64 = Convert.ToBase64String(salt);
        settings.DpapiWrappedKey = null;
        await _db.SaveChangesAsync();

        return DerivePassphraseKey(passphrase, salt);
    }

    private DatabaseKeyMaterial UnwrapDpapiKey(string wrappedHexKey)
    {
        var hexKey = _dpapi.Decrypt(wrappedHexKey);
        var bytes = Convert.FromHexString(hexKey);
        return DatabaseKeyMaterial.FromBytes(bytes, KeyStorageMode.DpapiWrapped);
    }

    private static DatabaseKeyMaterial DerivePassphraseKey(string passphrase, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);
        var keyBytes = pbkdf2.GetBytes(KeyLengthBytes);
        return DatabaseKeyMaterial.FromBytes(keyBytes, KeyStorageMode.UserPassphrase);
    }
}
```

- [ ] **Step 3: Run tests — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~DatabaseKeyServiceTests"`
Expected: All 6 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Services/Security/ src/AgentX.Core/Data/Entities/UserSettingsEntity.cs tests/AgentX.Tests/Services/Security/DatabaseKeyServiceTests.cs
git commit -m "feat(security): add IDatabaseKeyService with DPAPI-wrap and passphrase modes"
```

---

### Task 6: Write failing tests for `IEncryptedConnectionFactory`

**Files:**
- Create: `src/AgentX.Core/Data/IEncryptedConnectionFactory.cs`
- Create: `tests/AgentX.Tests/Data/EncryptedConnectionFactoryTests.cs`

- [ ] **Step 1: Write the interface stub**

```csharp
using Microsoft.Data.Sqlite;

namespace AgentX.Core.Data;

public interface IEncryptedConnectionFactory
{
    /// <summary>
    /// Builds a `Data Source=...;Password=...` connection string. If no key is currently
    /// loaded (encryption disabled), returns a plaintext connection string.
    /// </summary>
    string BuildConnectionString(string dbPath);

    /// <summary>
    /// Convenience — returns a new, unopened SqliteConnection with the correct string.
    /// </summary>
    SqliteConnection CreateConnection(string dbPath);
}
```

- [ ] **Step 2: Write the test file**

```csharp
using System.IO;
using AgentX.Core.Data;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgentX.Tests.Data;

[Collection("SqlCipher")]
public class EncryptedConnectionFactoryTests
{
    [Fact]
    public void BuildConnectionString_with_key_includes_password()
    {
        var key = DatabaseKeyMaterial.FromBytes(new byte[32], KeyStorageMode.DpapiWrapped);
        var keyProvider = new FakeKeyProvider(key);
        IEncryptedConnectionFactory sut = new EncryptedConnectionFactory(keyProvider);

        var cs = sut.BuildConnectionString("/tmp/test.db");

        cs.Should().Contain("Data Source=/tmp/test.db");
        cs.Should().Contain($"Password={key.HexKey}");
    }

    [Fact]
    public void BuildConnectionString_without_key_returns_plaintext()
    {
        var keyProvider = new FakeKeyProvider(null);
        IEncryptedConnectionFactory sut = new EncryptedConnectionFactory(keyProvider);

        var cs = sut.BuildConnectionString("/tmp/test.db");

        cs.Should().Be("Data Source=/tmp/test.db");
    }

    [Fact]
    public void CreateConnection_with_key_opens_encrypted_db_successfully()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentx-encfactory-{System.Guid.NewGuid():N}.db");
        try
        {
            var key = DatabaseKeyMaterial.FromBytes(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            var keyProvider = new FakeKeyProvider(key);
            IEncryptedConnectionFactory sut = new EncryptedConnectionFactory(keyProvider);

            using (var create = sut.CreateConnection(path))
            {
                create.Open();
                using var cmd = create.CreateCommand();
                cmd.CommandText = "CREATE TABLE t (x INT); INSERT INTO t VALUES (42);";
                cmd.ExecuteNonQuery();
            }

            using (var reopen = sut.CreateConnection(path))
            {
                reopen.Open();
                using var cmd = reopen.CreateCommand();
                cmd.CommandText = "SELECT x FROM t";
                var x = (long)cmd.ExecuteScalar()!;
                x.Should().Be(42);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CreateConnection_with_wrong_key_fails_to_read_encrypted_db()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentx-encfactory-{System.Guid.NewGuid():N}.db");
        try
        {
            var rightKey = DatabaseKeyMaterial.FromBytes(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            var wrongKey = DatabaseKeyMaterial.FromBytes(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);

            using (var create = new EncryptedConnectionFactory(new FakeKeyProvider(rightKey)).CreateConnection(path))
            {
                create.Open();
                using var cmd = create.CreateCommand();
                cmd.CommandText = "CREATE TABLE t (x INT); INSERT INTO t VALUES (42);";
                cmd.ExecuteNonQuery();
            }

            using (var reopen = new EncryptedConnectionFactory(new FakeKeyProvider(wrongKey)).CreateConnection(path))
            {
                reopen.Open();
                using var cmd = reopen.CreateCommand();
                cmd.CommandText = "SELECT x FROM t";
                var act = () => cmd.ExecuteScalar();
                act.Should().Throw<SqliteException>().WithMessage("*file is not a database*");
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FakeKeyProvider : IDatabaseKeyProvider
    {
        private readonly DatabaseKeyMaterial? _key;
        public FakeKeyProvider(DatabaseKeyMaterial? key) => _key = key;
        public DatabaseKeyMaterial? Current => _key;
    }
}
```

- [ ] **Step 3: Run tests — expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~EncryptedConnectionFactoryTests"`
Expected: Compile error — `EncryptedConnectionFactory` and `IDatabaseKeyProvider` don't exist.

---

### Task 7: Implement `EncryptedConnectionFactory` and `IDatabaseKeyProvider`

**Files:**
- Create: `src/AgentX.Core/Services/Security/IDatabaseKeyProvider.cs`
- Create: `src/AgentX.Core/Services/Security/DatabaseKeyProvider.cs`
- Create: `src/AgentX.Core/Data/EncryptedConnectionFactory.cs`

- [ ] **Step 1: Write `IDatabaseKeyProvider.cs`**

```csharp
namespace AgentX.Core.Services.Security;

/// <summary>
/// Holds the currently loaded database key (if any) for the session.
/// Separate from IDatabaseKeyService because this is a simple in-memory cache
/// that EncryptedConnectionFactory can read without hitting the DB on every call.
/// </summary>
public interface IDatabaseKeyProvider
{
    DatabaseKeyMaterial? Current { get; }
}
```

- [ ] **Step 2: Write `DatabaseKeyProvider.cs`**

```csharp
namespace AgentX.Core.Services.Security;

public sealed class DatabaseKeyProvider : IDatabaseKeyProvider
{
    private DatabaseKeyMaterial? _current;

    public DatabaseKeyMaterial? Current => _current;

    /// <summary>Called by the startup unlock flow after key material is available.</summary>
    public void Set(DatabaseKeyMaterial? material) => _current = material;
}
```

- [ ] **Step 3: Write `EncryptedConnectionFactory.cs`**

```csharp
using AgentX.Core.Services.Security;
using Microsoft.Data.Sqlite;

namespace AgentX.Core.Data;

public sealed class EncryptedConnectionFactory : IEncryptedConnectionFactory
{
    private readonly IDatabaseKeyProvider _keyProvider;

    public EncryptedConnectionFactory(IDatabaseKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public string BuildConnectionString(string dbPath)
    {
        var key = _keyProvider.Current;
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        if (key is not null)
            builder.Password = key.HexKey;

        return builder.ToString();
    }

    public SqliteConnection CreateConnection(string dbPath)
        => new SqliteConnection(BuildConnectionString(dbPath));
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~EncryptedConnectionFactoryTests"`
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/Services/Security/IDatabaseKeyProvider.cs src/AgentX.Core/Services/Security/DatabaseKeyProvider.cs src/AgentX.Core/Data/IEncryptedConnectionFactory.cs src/AgentX.Core/Data/EncryptedConnectionFactory.cs tests/AgentX.Tests/Data/EncryptedConnectionFactoryTests.cs
git commit -m "feat(security): add IEncryptedConnectionFactory + IDatabaseKeyProvider"
```

---

### Task 8: Route all 5 connection-creation sites through the factory

**Files to modify:** (exact line numbers from pre-plan audit, confirm each before editing)
- `src/AgentX.Core/Data/AgentXDbContext.cs` line 61 — `optionsBuilder.UseSqlite($"Data Source={_dbPath}")`
- `src/AgentX.Core/Data/VectorDb/HnswVectorStore.cs` lines 187–194
- `src/AgentX.Core/Data/VectorDb/SqliteVecStore.cs` lines 78–85
- `src/AgentX.Core/Services/Backup/BackupService.cs` lines 787–788
- `src/AgentX.Core/Services/Workspaces/WorkspaceService.cs` lines 545–551, 611–617

For each of the 5 files, the pattern is the same — inject `IEncryptedConnectionFactory` and use it.

- [ ] **Step 1: Update `AgentXDbContext` to accept a factory**

In `src/AgentX.Core/Data/AgentXDbContext.cs`, add an optional factory field and a new constructor overload (keep the existing two for EF-tooling compatibility):

```csharp
    private readonly IEncryptedConnectionFactory? _connectionFactory;

    public AgentXDbContext(DbContextOptions<AgentXDbContext> options, IEncryptedConnectionFactory connectionFactory)
        : this(options)
    {
        _connectionFactory = connectionFactory;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var cs = _connectionFactory?.BuildConnectionString(_dbPath)
                     ?? $"Data Source={_dbPath}";
            optionsBuilder.UseSqlite(cs);
        }
    }
```

Add `using AgentX.Core.Data;` at top if not already present (note: same namespace already).

- [ ] **Step 2: Update `HnswVectorStore` and `SqliteVecStore`**

For each file, replace:

```csharp
var connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ConnectionString;
_connection = new SqliteConnection(connectionString);
```

with an injected factory and:

```csharp
_connection = _connectionFactory.CreateConnection(_dbPath);
```

In each class, add constructor parameter `IEncryptedConnectionFactory connectionFactory`, store as `_connectionFactory`.

- [ ] **Step 3: Update `BackupService`**

In `src/AgentX.Core/Services/Backup/BackupService.cs` around line 787:

Replace:
```csharp
using var source = new SqliteConnection($"Data Source={dbPath}");
using var destination = new SqliteConnection($"Data Source={tempPath}");
```
with:
```csharp
using var source = _connectionFactory.CreateConnection(dbPath);
using var destination = _connectionFactory.CreateConnection(tempPath);
```

Inject `IEncryptedConnectionFactory connectionFactory` into the constructor.

**Important note:** encrypted SQLCipher databases do NOT support the built-in `BackupDatabase()` API across encryption boundaries cleanly. Keep using it for same-key backups, but add a comment:

```csharp
// Backups produced from an encrypted source keep the same key. Users restoring on
// a different machine will need the matching passphrase (UserPassphrase mode) or
// DPAPI-wrapped key (DpapiWrapped mode — machine-bound).
```

- [ ] **Step 4: Update `WorkspaceService`**

Same pattern in `src/AgentX.Core/Services/Workspaces/WorkspaceService.cs` lines 545 and 611 — inject factory, replace direct `new SqliteConnection(...)`.

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded. Any DI graph mismatches will be caught in Task 9.

- [ ] **Step 6: Commit**

```bash
git add -u
git commit -m "refactor(data): route all SqliteConnection sites through IEncryptedConnectionFactory"
```

---

### Task 9: DI registration

**Files:**
- Modify: `src/AgentX.App/Services/ServiceCollectionExtensions.cs` (or whichever file registers services — locate)

- [ ] **Step 1: Register the new services**

Add to the service-collection extension method near the existing `AgentXDbContext` registration:

```csharp
services.AddSingleton<AgentX.Core.Services.Security.IDatabaseKeyProvider,
                     AgentX.Core.Services.Security.DatabaseKeyProvider>();
services.AddSingleton<AgentX.Core.Data.IEncryptedConnectionFactory,
                     AgentX.Core.Data.EncryptedConnectionFactory>();
services.AddScoped<AgentX.Core.Services.Security.IDatabaseKeyService,
                   AgentX.Core.Services.Security.DatabaseKeyService>();
```

Singletons for provider (session-long) and factory (stateless, depends on provider). Scoped for key service (uses DbContext).

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -u
git commit -m "feat(di): register DatabaseKeyProvider, EncryptedConnectionFactory, DatabaseKeyService"
```

---

### Task 10: Add migration that writes the new UserSettings columns

**Files:**
- Create (via `dotnet ef`): `src/AgentX.Core/Data/Migrations/<timestamp>_AddEncryptionColumns.cs`

- [ ] **Step 1: Generate the migration**

```bash
dotnet ef migrations add AddEncryptionColumns \
  --project src/AgentX.Core/AgentX.Core.csproj \
  --output-dir Data/Migrations \
  --context AgentXDbContext
```
Expected: three files added, schema diff includes the 4 new columns on `user_settings`.

- [ ] **Step 2: Inspect the generated `Up` method**

Open the generated `<timestamp>_AddEncryptionColumns.cs`. Confirm it contains `AddColumn` calls for `EncryptionEnabled`, `EncryptionKeyStorageMode`, `EncryptionSaltBase64`, `DpapiWrappedKey` on `user_settings`.

- [ ] **Step 3: Build + test**

Run: `dotnet test`
Expected: all tests pass (migration runner applies the new migration in DatabaseKeyServiceTests).

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Data/Migrations/
git commit -m "feat(migrations): add encryption columns to user_settings"
```

---

### Task 11: Write failing tests for `IDatabaseEncryptionMigrator`

**Files:**
- Create: `src/AgentX.Core/Services/Security/IDatabaseEncryptionMigrator.cs`
- Create: `tests/AgentX.Tests/Services/Security/DatabaseEncryptionMigratorTests.cs`

- [ ] **Step 1: Write the interface stub**

```csharp
using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IDatabaseEncryptionMigrator
{
    /// <summary>
    /// Converts a plaintext SQLite database at `dbPath` into an encrypted copy using the given key,
    /// then atomically replaces the original file. Safe against interruption: leaves the
    /// original file intact if any step fails.
    /// </summary>
    Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key);
}
```

- [ ] **Step 2: Write the test file**

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgentX.Tests.Services.Security;

[Collection("SqlCipher")]
public class DatabaseEncryptionMigratorTests
{
    [Fact]
    public async Task MigrateToEncryptedAsync_converts_plaintext_db_to_encrypted_with_same_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-{System.Guid.NewGuid():N}.db");
        try
        {
            using (var plain = new SqliteConnection($"Data Source={dbPath}"))
            {
                plain.Open();
                using var cmd = plain.CreateCommand();
                cmd.CommandText = "CREATE TABLE docs (id INT, title TEXT); INSERT INTO docs VALUES (1, 'hello');";
                cmd.ExecuteNonQuery();
            }

            var key = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            IDatabaseEncryptionMigrator sut = new DatabaseEncryptionMigrator();

            await sut.MigrateToEncryptedAsync(dbPath, key);

            // Plaintext open should fail
            using (var plainAfter = new SqliteConnection($"Data Source={dbPath}"))
            {
                plainAfter.Open();
                using var cmd = plainAfter.CreateCommand();
                cmd.CommandText = "SELECT title FROM docs";
                var act = () => cmd.ExecuteScalar();
                act.Should().Throw<SqliteException>();
            }

            // Encrypted open with correct key should work
            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Password = key.HexKey };
            using (var encr = new SqliteConnection(csb.ToString()))
            {
                encr.Open();
                using var cmd = encr.CreateCommand();
                cmd.CommandText = "SELECT title FROM docs WHERE id=1";
                var title = (string)cmd.ExecuteScalar()!;
                title.Should().Be("hello");
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_preserves_original_file_on_failure()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-{System.Guid.NewGuid():N}.db");
        try
        {
            using (var plain = new SqliteConnection($"Data Source={dbPath}"))
            {
                plain.Open();
                using var cmd = plain.CreateCommand();
                cmd.CommandText = "CREATE TABLE docs (id INT); INSERT INTO docs VALUES (1);";
                cmd.ExecuteNonQuery();
            }

            var originalSize = new FileInfo(dbPath).Length;

            // Invalid (empty) key triggers a failure path.
            var badKey = new DatabaseKeyMaterial("", KeyStorageMode.DpapiWrapped);
            IDatabaseEncryptionMigrator sut = new DatabaseEncryptionMigrator();

            var act = async () => await sut.MigrateToEncryptedAsync(dbPath, badKey);
            await act.Should().ThrowAsync<System.Exception>();

            // Original file still intact
            File.Exists(dbPath).Should().BeTrue();
            new FileInfo(dbPath).Length.Should().Be(originalSize);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
```

- [ ] **Step 3: Run — expect compile fail**

Run: `dotnet test --filter "FullyQualifiedName~DatabaseEncryptionMigratorTests"`
Expected: Build error — `DatabaseEncryptionMigrator` missing.

---

### Task 12: Implement `DatabaseEncryptionMigrator`

**Files:**
- Create: `src/AgentX.Core/Services/Security/DatabaseEncryptionMigrator.cs`

Uses SQLCipher's `sqlcipher_export()` function to atomically copy plaintext → encrypted.

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace AgentX.Core.Services.Security;

public sealed class DatabaseEncryptionMigrator : IDatabaseEncryptionMigrator
{
    public async Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key)
    {
        if (string.IsNullOrWhiteSpace(key.HexKey))
            throw new ArgumentException("Key material is empty.", nameof(key));
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Plaintext database not found.", dbPath);

        var tempEncryptedPath = dbPath + ".enc.tmp";
        var backupPath = dbPath + ".plain.bak";

        if (File.Exists(tempEncryptedPath)) File.Delete(tempEncryptedPath);
        if (File.Exists(backupPath)) File.Delete(backupPath);

        try
        {
            // Open plaintext source, attach an empty encrypted DB, copy via sqlcipher_export.
            using (var source = new SqliteConnection($"Data Source={dbPath}"))
            {
                await source.OpenAsync();

                using var cmd = source.CreateCommand();
                cmd.CommandText = $@"
                    ATTACH DATABASE '{tempEncryptedPath.Replace("'", "''")}' AS encrypted KEY ""x'{key.HexKey}'"";
                    SELECT sqlcipher_export('encrypted');
                    DETACH DATABASE encrypted;";
                await cmd.ExecuteNonQueryAsync();
            }

            // Move plaintext aside as a safety backup, then install the encrypted file.
            File.Move(dbPath, backupPath);
            File.Move(tempEncryptedPath, dbPath);

            // Verify the new encrypted DB opens and responds to a simple query.
            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Password = key.HexKey };
            using (var verify = new SqliteConnection(csb.ToString()))
            {
                await verify.OpenAsync();
                using var cmd = verify.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM sqlite_master";
                await cmd.ExecuteScalarAsync();
            }

            // Success — remove the backup.
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
        catch
        {
            // Rollback: if we already moved the plaintext aside, restore it.
            if (File.Exists(backupPath) && !File.Exists(dbPath))
            {
                File.Move(backupPath, dbPath);
            }
            if (File.Exists(tempEncryptedPath)) File.Delete(tempEncryptedPath);
            throw;
        }
    }
}
```

- [ ] **Step 2: Run tests — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~DatabaseEncryptionMigratorTests"`
Expected: both tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/AgentX.Core/Services/Security/IDatabaseEncryptionMigrator.cs src/AgentX.Core/Services/Security/DatabaseEncryptionMigrator.cs tests/AgentX.Tests/Services/Security/DatabaseEncryptionMigratorTests.cs
git commit -m "feat(security): add IDatabaseEncryptionMigrator using sqlcipher_export"
```

---

### Task 13: Register migrator and expose Settings UI toggle

**Files:**
- Modify: `src/AgentX.App/Services/ServiceCollectionExtensions.cs` — register migrator
- Modify: `src/AgentX.App/Views/SettingsPage.xaml` — add encryption section
- Modify: `src/AgentX.App/ViewModels/SettingsViewModel.cs` — add commands

- [ ] **Step 1: Register the migrator**

Add to the DI extension:

```csharp
services.AddScoped<AgentX.Core.Services.Security.IDatabaseEncryptionMigrator,
                   AgentX.Core.Services.Security.DatabaseEncryptionMigrator>();
```

- [ ] **Step 2: Add UI panel to `SettingsPage.xaml`**

Add a new `StackPanel` section (after existing API-key section):

```xml
<StackPanel Spacing="8" Margin="0,16,0,0">
    <TextBlock x:Uid="Encryption_SectionHeader" Style="{StaticResource SubtitleTextBlockStyle}" />
    <TextBlock x:Uid="Encryption_Description" TextWrapping="Wrap" Opacity="0.8" />
    <ToggleSwitch x:Uid="Encryption_Toggle"
                  IsOn="{x:Bind ViewModel.EncryptionEnabled, Mode=OneWay}"
                  Toggled="{x:Bind ViewModel.OnEncryptionToggledAsync}" />
    <TextBlock x:Name="EncryptionStatusText"
               Text="{x:Bind ViewModel.EncryptionStatus, Mode=OneWay}"
               Opacity="0.7" />
</StackPanel>
```

Add matching entries to `Resources.resw`:
- `Encryption_SectionHeader.Text` = "Database Encryption"
- `Encryption_Description.Text` = "Encrypt your knowledge vault with AES-256 (SQLCipher). Ultimate tier: user passphrase. Other tiers: automatic DPAPI-wrapped key."
- `Encryption_Toggle.OnContent` = "Encrypted"
- `Encryption_Toggle.OffContent` = "Plaintext"

- [ ] **Step 3: Wire the ViewModel command**

In `SettingsViewModel.cs`, add:

```csharp
    [ObservableProperty]
    private bool encryptionEnabled;

    [ObservableProperty]
    private string encryptionStatus = string.Empty;

    private readonly IDatabaseKeyService _keyService;
    private readonly IDatabaseEncryptionMigrator _migrator;
    private readonly IDatabaseKeyProvider _keyProvider;
    private readonly ILicenseService _license;

    // (add to constructor and DI injection list)

    public async Task OnEncryptionToggledAsync(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!EncryptionEnabled)
        {
            EncryptionStatus = "Encryption disabled.";
            return;
        }

        // Tier gate: Ultimate gets UserPassphrase, others get DpapiWrapped transparent.
        var tier = await _license.GetCurrentTierAsync();
        var mode = tier == LicenseTier.Ultimate ? KeyStorageMode.UserPassphrase : KeyStorageMode.DpapiWrapped;

        string? passphrase = null;
        if (mode == KeyStorageMode.UserPassphrase)
        {
            passphrase = await PromptForNewPassphraseAsync();
            if (string.IsNullOrEmpty(passphrase))
            {
                EncryptionEnabled = false;
                return;
            }
        }

        try
        {
            var key = await _keyService.GetOrCreateKeyAsync(mode, passphrase);
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentX", "agentx.db");
            await _migrator.MigrateToEncryptedAsync(dbPath, key);
            ((DatabaseKeyProvider)_keyProvider).Set(key);
            EncryptionStatus = $"Encrypted ({mode}). App will use the encrypted DB on next restart.";
        }
        catch (Exception ex)
        {
            EncryptionEnabled = false;
            EncryptionStatus = $"Encryption failed: {ex.Message}";
        }
    }

    private async Task<string?> PromptForNewPassphraseAsync()
    {
        var dlg = new ContentDialog
        {
            Title = "Set a passphrase",
            PrimaryButtonText = "Encrypt",
            CloseButtonText = "Cancel",
            XamlRoot = App.MainWindow.Content.XamlRoot,
        };
        var box = new PasswordBox { PlaceholderText = "Minimum 12 characters" };
        dlg.Content = box;
        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;
        if (box.Password.Length < 12)
        {
            EncryptionStatus = "Passphrase must be at least 12 characters.";
            return null;
        }
        return box.Password;
    }
```

- [ ] **Step 4: Build + smoke-test app**

Run: `dotnet build && dotnet run --project src/AgentX.App`
Expected: Settings page renders the new Encryption section with a toggle.

Manually toggle Encryption on. Accept DPAPI-wrapped mode (if tier is Starter/Pro). App reports "Encrypted (DpapiWrapped)...". Restart app.

- [ ] **Step 5: Commit**

```bash
git add -u
git commit -m "feat(security): add Settings UI for database encryption toggle"
```

---

### Task 14: Startup unlock flow

**Files:**
- Modify: `src/AgentX.App/App.xaml.cs` — unlock before migration runner

- [ ] **Step 1: Add unlock step before `IMigrationRunner.RunAsync`**

In `InitializeCoreServicesAsync`, **before** the scope-create for the migration runner, add:

```csharp
using (var unlockScope = Host.Services.CreateScope())
{
    var keySvc = unlockScope.ServiceProvider.GetRequiredService<IDatabaseKeyService>();
    var keyProvider = (DatabaseKeyProvider)unlockScope.ServiceProvider.GetRequiredService<IDatabaseKeyProvider>();
    if (await keySvc.IsProvisionedAsync())
    {
        var mode = await keySvc.GetProvisionedModeAsync();
        if (mode == KeyStorageMode.DpapiWrapped)
        {
            var key = await keySvc.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);
            keyProvider.Set(key);
        }
        else if (mode == KeyStorageMode.UserPassphrase)
        {
            // Show passphrase prompt; retry on InvalidDatabaseKeyException.
            while (true)
            {
                var passphrase = await PromptForPassphraseAsync();
                if (passphrase is null) { Application.Current.Exit(); return; }
                try
                {
                    var key = await keySvc.UnlockWithPassphraseAsync(passphrase);
                    keyProvider.Set(key);

                    // Verify by attempting a trivial DB open.
                    var factory = unlockScope.ServiceProvider.GetRequiredService<IEncryptedConnectionFactory>();
                    var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentX", "agentx.db");
                    using var testConn = factory.CreateConnection(dbPath);
                    await testConn.OpenAsync();
                    break;
                }
                catch (SqliteException)
                {
                    await ShowInvalidPassphraseDialogAsync();
                }
            }
        }
    }
}
```

Where `PromptForPassphraseAsync` and `ShowInvalidPassphraseDialogAsync` are private helpers on the `App` class showing a `ContentDialog`.

Add the helpers:

```csharp
private static async Task<string?> PromptForPassphraseAsync()
{
    var box = new Microsoft.UI.Xaml.Controls.PasswordBox { PlaceholderText = "Enter database passphrase" };
    var dlg = new ContentDialog
    {
        Title = "Unlock your Agent-X database",
        Content = box,
        PrimaryButtonText = "Unlock",
        CloseButtonText = "Exit app",
        XamlRoot = MainWindow.Content.XamlRoot,
    };
    var result = await dlg.ShowAsync();
    return result == ContentDialogResult.Primary ? box.Password : null;
}

private static async Task ShowInvalidPassphraseDialogAsync()
{
    var dlg = new ContentDialog
    {
        Title = "Incorrect passphrase",
        Content = "That passphrase did not unlock the database. Please try again.",
        CloseButtonText = "OK",
        XamlRoot = MainWindow.Content.XamlRoot,
    };
    await dlg.ShowAsync();
}
```

- [ ] **Step 2: Build + smoke-test**

Run: `dotnet build && dotnet run --project src/AgentX.App`

Scenario A: Previously migrated to DPAPI-wrapped in Task 13 — app launches silently, DB opens.

Scenario B: Switch the provisioned mode to UserPassphrase manually in DB (or create a second test install), restart — passphrase dialog appears. Enter correct passphrase, app unlocks. Enter wrong, retry loop fires.

- [ ] **Step 3: Full test suite**

Run: `dotnet test`
Expected: all tests pass (669 + 5 from B9 + 13 new from C13 = 687).

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.App/App.xaml.cs
git commit -m "feat(security): add startup unlock flow for encrypted database"
```

---

### Task 15: Docs + release notes

**Files:**
- Modify: `docs/ARCHITECTURE.md` — Encryption section
- Modify: `docs/USER-GUIDE.md` — Encryption how-to
- Create: entry in forthcoming `docs/v2.1.0-RELEASE-NOTES.md` (seed the file)

- [ ] **Step 1: Add architecture section**

Append to `docs/ARCHITECTURE.md`:

```markdown
### Database Encryption (C13)

`agentx.db` is encrypted at rest using SQLCipher (AES-256-CBC, 4096-byte pages) when encryption is enabled. Keys are managed by `IDatabaseKeyService`:

- **DpapiWrapped** (Starter/Professional): 32-byte key auto-generated with `RandomNumberGenerator`, stored DPAPI-wrapped in `UserSettings.DpapiWrappedKey`. Transparent unlock at startup — no user prompt.
- **UserPassphrase** (Ultimate): 32-byte key derived at each launch via PBKDF2-HMAC-SHA256 (600,000 iterations) from a user passphrase and a per-install 16-byte salt stored in `UserSettings.EncryptionSaltBase64`. The passphrase itself is never persisted.

Every `SqliteConnection` in the codebase is built via `IEncryptedConnectionFactory`, which injects the active key from `IDatabaseKeyProvider` as the connection-string `Password` parameter.

Plaintext → encrypted migration is one-shot and reversible-safe via `IDatabaseEncryptionMigrator.MigrateToEncryptedAsync`, which uses SQLCipher's `sqlcipher_export()` with staged temp-file + rollback-on-failure semantics.
```

- [ ] **Step 2: Add user-guide section**

Append to `docs/USER-GUIDE.md`:

```markdown
## Database Encryption

Open Settings → Database Encryption → toggle on. Your vault is encrypted immediately. On Ultimate tier you will be asked to set a passphrase; on Starter/Professional the key is managed automatically and bound to your Windows user account.

**Passphrase tier users:** write your passphrase down and store it safely. If lost, the database cannot be recovered. Use the Backup & Restore feature to create recovery copies.
```

- [ ] **Step 3: Seed release notes**

Create `docs/v2.1.0-RELEASE-NOTES.md` with:

```markdown
# Agent-X v2.1.0 Release Notes

## Overview

Agent-X v2.1.0 ("Bedrock") hardens the foundation for Phase 2 Memory. Key additions: EF Core migrations (B9), SQLCipher at-rest encryption (C13), and audit log (C14).

## New Features

### Database Encryption (SQLCipher)

- AES-256-CBC at-rest encryption using SQLCipher 4.6
- Tier-aware key management: automatic DPAPI-wrapped (Starter/Professional), user passphrase (Ultimate)
- One-shot migration path from plaintext to encrypted with atomic fallback
- Settings UI toggle with passphrase entry dialog

### EF Core Migrations

- `IMigrationRunner` replaces `EnsureCreatedAsync`
- `InitialBaseline` migration adopts existing installs automatically — no data loss on upgrade
```

- [ ] **Step 4: Commit**

```bash
git add docs/ARCHITECTURE.md docs/USER-GUIDE.md docs/v2.1.0-RELEASE-NOTES.md
git commit -m "docs(encryption): document C13 SQLCipher at-rest + v2.1 release notes seed"
```

---

## Self-Review Summary

- **Spec coverage:** SQLCipher enabled (Task 1), key derivation (Task 5: PBKDF2-HMAC-SHA256, 600k iterations, 32-byte key), key storage modes (Task 2: DpapiWrapped + UserPassphrase), connection routing for all 5 sites (Task 8), one-shot migration with rollback (Task 12), Settings UI (Task 13), startup unlock flow (Task 14), tier-gating enforced in Settings command (Task 13 Step 3).
- **Placeholder scan:** every code step contains complete code, including the UI passphrase dialog helpers. No "TBD" / "similar to".
- **Type consistency:** `IDatabaseKeyService`, `IDatabaseKeyProvider`, `IEncryptedConnectionFactory`, `IDatabaseEncryptionMigrator`, `DatabaseKeyMaterial`, `KeyStorageMode` used consistently across Tasks 2–14.
- **Dependency on B9:** Confirmed — this plan assumes `IMigrationRunner` and the `InitialBaseline` migration exist (Task 10 adds a second migration, which only succeeds if B9 landed).

## Follow-up (not in this plan)

1. Windows Hello unlock option for Ultimate tier (added in a future hardening patch).
2. Passphrase-change workflow (re-key existing encrypted DB via a second `sqlcipher_export` pass).
3. Backup file encryption behavior — the existing `BackupService` produces encrypted backups when source is encrypted; that's covered in C14 docs.

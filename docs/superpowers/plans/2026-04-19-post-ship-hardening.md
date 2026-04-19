# Post-Ship Hardening Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the encryption subsystem against three edge cases: mid-swap crash recovery, keystore file permissions, and WAL consistency before sqlcipher_export.

**Architecture:** Three focused, independent changes touching `DatabaseEncryptionMigrator.cs` and `EncryptionStateFile.cs` plus their tests.

**Tech Stack:** C#, .NET 8, Microsoft.Data.Sqlite, System.IO.FileSystem.AccessControl, xUnit

---

### Task 1: WAL Checkpoint Before sqlcipher_export

**Problem:** If the plaintext source DB has uncommitted WAL content, `sqlcipher_export` may export an inconsistent snapshot because it reads from the main DB file, not the WAL.

**Fix:** Run `PRAGMA wal_checkpoint(FULL)` on the plaintext source connection before the ATTACH/export sequence.

**Files:**
- Modify: `src/AgentX.Core/Services/Security/DatabaseEncryptionMigrator.cs`
- Modify: `tests/AgentX.Tests/Services/Security/DatabaseEncryptionMigratorTests.cs`

- [ ] **Step 1: Add failing test `MigrateToEncryptedAsync_checkpoints_wal_before_export`**

Create a plaintext DB, force WAL mode, insert data (which stays in WAL since no checkpoint yet), then call `MigrateToEncryptedAsync`. Verify the encrypted copy contains all data. Without the fix, data still in WAL would be lost.

```csharp
[Fact]
public async Task MigrateToEncryptedAsync_checkpoints_wal_before_export()
{
    using var dir = new TempDirectory();
    var dbPath = Path.Combine(dir.Path, "test.db");
    var hexKey = new string('A', 64);

    // Create DB in WAL mode, insert a row, deliberately skip checkpoint.
    using (var conn = new SqliteConnection($"Data Source={dbPath}"))
    {
        await conn.OpenAsync();
        using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL";
        await walCmd.ExecuteScalarAsync();

        using var tableCmd = conn.CreateCommand();
        tableCmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT); INSERT INTO t VALUES(1,'wal-data')";
        await tableCmd.ExecuteNonQueryAsync();

        // Do NOT checkpoint — data sits in WAL.
    }

    SqliteConnection.ClearAllPools();

    var key = new DatabaseKeyMaterial(hexKey, KeyStorageMode.DpapiWrapped);
    var migrator = new DatabaseEncryptionMigrator();
    await migrator.MigrateToEncryptedAsync(dbPath, key);

    // Verify encrypted DB has the WAL-originated row.
    using (var verify = new SqliteConnection($"Data Source={dbPath}"))
    {
        await verify.OpenAsync();
        using var k = verify.CreateCommand();
        k.CommandText = $@"PRAGMA key = ""x'{hexKey}'"";";
        await k.ExecuteNonQueryAsync();

        using var probe = verify.CreateCommand();
        probe.CommandText = "SELECT v FROM t WHERE id=1";
        var result = await probe.ExecuteScalarAsync();
        Assert.Equal("wal-data", result);
    }
}
```

- [ ] **Step 2: Add `PRAGMA wal_checkpoint(FULL)` before ATTACH in migrator**

In `DatabaseEncryptionMigrator.MigrateToEncryptedAsync`, after `source.OpenAsync()` and before the ATTACH command, insert:

```csharp
using var checkpointCmd = source.CreateCommand();
checkpointCmd.CommandText = "PRAGMA wal_checkpoint(FULL)";
await checkpointCmd.ExecuteNonQueryAsync();
```

- [ ] **Step 3: Run existing tests to confirm no regressions**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~DatabaseEncryptionMigrator" --blame-hang-timeout 60s
```

All 4 existing tests + 1 new test must pass.

---

### Task 2: Kill-Window Self-Heal for Interrupted Migrations

**Problem:** If the process is killed between `File.Move(dbPath, backupPath)` (line 45) and `File.Move(tempEncryptedPath, dbPath)` (line 46), the app is left with `db.plain.bak` as the only complete database and no file at `dbPath`. On next launch, the app fails because `agentx.db` doesn't exist.

**Fix:** Add a `RecoverIfNeeded(string dbPath)` method that detects and recovers from interrupted migrations. Call it at app startup before any DB access.

**Files:**
- Modify: `src/AgentX.Core/Services/Security/DatabaseEncryptionMigrator.cs`
- Modify: `src/AgentX.Core/Services/Security/IDatabaseEncryptionMigrator.cs`
- Modify: `tests/AgentX.Tests/Services/Security/DatabaseEncryptionMigratorTests.cs`

- [ ] **Step 1: Add failing tests for `RecoverIfNeeded`**

```csharp
[Fact]
public void RecoverIfNeeded_restores_plaintext_backup_when_no_main_db()
{
    using var dir = new TempDirectory();
    var dbPath = Path.Combine(dir.Path, "test.db");
    var backupPath = dbPath + ".plain.bak";

    // Simulate kill-window: only the backup exists, main db is missing.
    File.WriteAllText(dbPath, "fake-sqlite-content");
    File.Move(dbPath, backupPath);

    var migrator = new DatabaseEncryptionMigrator();
    migrator.RecoverIfNeeded(dbPath);

    Assert.True(File.Exists(dbPath));
    Assert.False(File.Exists(backupPath));
    Assert.Equal("fake-sqlite-content", File.ReadAllText(dbPath));
}

[Fact]
public void RecoverIfNeeded_remains_orphaned_temp_when_main_db_intact()
{
    using var dir = new TempDirectory();
    var dbPath = Path.Combine(dir.Path, "test.db");
    var tempPath = dbPath + ".enc.tmp";

    // Main DB intact, orphaned temp from a prior failed attempt.
    File.WriteAllText(dbPath, "real-content");
    File.WriteAllText(tempPath, "stale-temp");

    var migrator = new DatabaseEncryptionMigrator();
    migrator.RecoverIfNeeded(dbPath);

    Assert.True(File.Exists(dbPath));
    Assert.Equal("real-content", File.ReadAllText(dbPath));
    Assert.False(File.Exists(tempPath));
}

[Fact]
public void RecoverIfNeeded_is_noop_when_no_artifacts()
{
    using var dir = new TempDirectory();
    var dbPath = Path.Combine(dir.Path, "test.db");
    File.WriteAllText(dbPath, "normal-content");

    var migrator = new DatabaseEncryptionMigrator();
    migrator.RecoverIfNeeded(dbPath);

    Assert.Equal("normal-content", File.ReadAllText(dbPath));
}
```

- [ ] **Step 2: Add `RecoverIfNeeded` to interface**

Add to `IDatabaseEncryptionMigrator`:

```csharp
/// <summary>
/// Detects and recovers from an interrupted encryption migration.
/// If a plaintext backup exists but the main DB is missing, restores the backup.
/// Always cleans up orphaned .enc.tmp files.
/// Call this at app startup before any DB access.
/// </summary>
void RecoverIfNeeded(string dbPath);
```

- [ ] **Step 3: Implement `RecoverIfNeeded` in `DatabaseEncryptionMigrator`**

```csharp
public void RecoverIfNeeded(string dbPath)
{
    var backupPath = dbPath + ".plain.bak";
    var tempPath = dbPath + ".enc.tmp";

    // Kill-window recovery: main DB missing, backup exists → restore.
    if (!File.Exists(dbPath) && File.Exists(backupPath))
    {
        File.Move(backupPath, dbPath);
    }

    // Clean up orphaned temp from any prior interrupted attempt.
    SafeDelete(tempPath);
}
```

- [ ] **Step 4: Run all migrator tests**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~DatabaseEncryptionMigrator" --blame-hang-timeout 60s
```

All 4 existing + 3 new tests must pass.

---

### Task 3: ACL-Restrict Encryption Keystore File

**Problem:** `encryption.info.json` is written with default file permissions, meaning any process on the machine can read the DPAPI-wrapped key material.

**Fix:** After writing `encryption.info.json`, set the file ACL to restrict access to the current Windows user only (Full Control for owner, no access for others).

**Files:**
- Modify: `src/AgentX.Core/Services/Security/EncryptionStateFile.cs`
- Modify: `src/AgentX.Core/AgentX.Core.csproj` (if `System.IO.FileSystem.AccessControl` not already referenced)
- Modify: `tests/AgentX.Tests/Services/Security/EncryptionStateFileTests.cs`

- [ ] **Step 1: Add failing test for file permissions**

```csharp
[Fact]
public async Task WriteAsync_restricts_file_permissions_to_current_user()
{
    using var dir = new TempDirectory();
    var path = Path.Combine(dir.Path, "encryption.info.json");
    var sut = new EncryptionStateFile(path);

    var info = new EncryptionStateInfo(1, KeyStorageMode.DpapiWrapped, DateTimeOffset.UtcNow, "DPAPI:abc", null);
    await sut.WriteAsync(info);

    // On Windows, verify the file has restricted ACL.
    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
    {
        var acl = File.GetAccessControl(path);
        var rules = acl.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
        Assert.Equal(1, rules.Count); // Only current user should have access
    }
}
```

- [ ] **Step 2: Add `System.IO.FileSystem.AccessControl` NuGet reference if missing**

Check `AgentX.Core.csproj` for existing reference. If missing, add:

```xml
<PackageReference Include="System.IO.FileSystem.AccessControl" Version="*" />
```

- [ ] **Step 3: Add ACL restriction after file write in `EncryptionStateFile.WriteAsync`**

After `await File.WriteAllTextAsync(...)`, add:

```csharp
// Restrict to current user only on Windows.
if (Environment.OSVersion.Platform == PlatformID.Win32NT)
{
    var acl = new FileSecurity();
    var currentUser = WindowsIdentity.GetCurrent().Owner;
    acl.SetOwner(currentUser);
    acl.AddAccessRule(new FileSystemAccessRule(
        currentUser,
        FileSystemRights.FullControl,
        AccessControlType.Allow));
    acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    File.SetAccessControl(_filePath, acl);
}
```

Add required usings: `System.Security.AccessControl`, `System.Security.Principal`.

- [ ] **Step 4: Run all EncryptionStateFile tests**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~EncryptionStateFile" --blame-hang-timeout 60s
```

All 10 existing + 1 new test must pass.

---

## Verification Gate

After all 3 tasks are complete:

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

**Expected:** All 1,055+ tests passing, 0 failing, 2 skipped (H1 hang, pre-existing).

## Commit Strategy

One commit per task:
- `fix(security): PRAGMA wal_checkpoint(FULL) before sqlcipher_export`
- `fix(security): kill-window self-heal for interrupted encryption migrations`
- `fix(security): ACL-restrict encryption keystore to current user only`

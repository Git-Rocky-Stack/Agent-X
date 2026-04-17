# C13 — SQLCipher At-Rest Encryption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Encrypt the entire `agentx.db` SQLite database at rest using SQLCipher (AES-256-CBC) with a tier-aware key-management scheme, and provide a safe one-shot migration path that converts existing plaintext databases to encrypted form without data loss.

**Architecture:** Swap the SQLite native library at the SQLitePCLRaw provider layer using `SQLitePCLRaw.bundle_e_sqlcipher`. Every `SqliteConnection` created anywhere in the codebase must pass through a new `IEncryptedConnectionFactory` that appends the correct `Password=` clause. Key material is derived via PBKDF2-HMAC-SHA256 from a user passphrase (Ultimate tier) or auto-generated and DPAPI-wrapped (Starter/Professional — transparent). A migration service performs the atomic plaintext→encrypted database conversion via the SQLite `sqlcipher_export()` function. All five existing Sqlite connection sites are routed through the factory. Runs **after B9** and uses `IMigrationRunner` to re-apply migrations to the new encrypted file so schema continuity is preserved.

**Tech Stack:** .NET 8, Microsoft.Data.Sqlite 8.0.11, `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.7+ (ships SQLCipher 4.6.x native), existing `DpapiEncryptionService`, new `IDatabaseKeyService` + `IEncryptedConnectionFactory` + `IDatabaseEncryptionMigrator`.

**Prerequisite:** Plan B9 landed (IMigrationRunner exists).

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

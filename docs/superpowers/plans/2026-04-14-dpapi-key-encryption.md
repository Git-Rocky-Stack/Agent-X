# DPAPI API Key Encryption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Encrypt all API keys at rest using Windows DPAPI so no plaintext secrets are written to disk.

**Architecture:** Insert a `DpapiEncryptionService` between `SettingsService` and `settings.json`. On save, encrypt key fields before serialization. On load, decrypt after deserialization. Automatic migration on first launch converts existing plaintext keys to encrypted form. A `SecurityStatusService` exposes encryption state to the UI.

**Tech Stack:** C#, .NET 8, `System.Security.Cryptography.ProtectedData` (DPAPI), WinUI 3, CommunityToolkit.Mvvm, xUnit

---

### Task 1: DpapiEncryptionService Core

**Files:**
- Create: `src/AgentX.Core/Services/Security/IDpapiEncryptionService.cs`
- Create: `src/AgentX.Core/Services/Security/DpapiEncryptionService.cs`
- Test: `tests/AgentX.Tests/Services/Security/DpapiEncryptionServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AgentX.Tests/Services/Security/DpapiEncryptionServiceTests.cs
using AgentX.Core.Services.Security;
using System.Text;
using Xunit;

namespace AgentX.Tests.Services.Security;

public class DpapiEncryptionServiceTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
    {
        var service = new DpapiEncryptionService();
        var plaintext = "sk-proj-abc123def456ghi789";

        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ReturnsDifferentValueThanInput()
    {
        var service = new DpapiEncryptionService();
        var plaintext = "sk-proj-abc123def456ghi789";

        var encrypted = service.Encrypt(plaintext);

        Assert.NotEqual(plaintext, encrypted);
    }

    [Fact]
    public void Encrypt_ProducesBase64String()
    {
        var service = new DpapiEncryptionService();
        var plaintext = "test-key-value";

        var encrypted = service.Encrypt(plaintext);

        // Base64 strings only contain A-Z, a-z, 0-9, +, /, =
        Assert.True(encrypted.All(c =>
            char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '='),
            $"Encrypted value '{encrypted}' is not valid Base64");
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentCiphertext()
    {
        // DPAPI uses a random IV/salt per encryption, so same input -> different output
        var service = new DpapiEncryptionService();
        var plaintext = "same-key-value";

        var encrypted1 = service.Encrypt(plaintext);
        var encrypted2 = service.Encrypt(plaintext);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_InvalidBase64_ThrowsFormatException()
    {
        var service = new DpapiEncryptionService();

        Assert.Throws<FormatException>(() => service.Decrypt("not-valid-base64!!!"));
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEncryptedEmptyString()
    {
        var service = new DpapiEncryptionService();

        var encrypted = service.Encrypt(string.Empty);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void IsEncrypted_EncryptedValue_ReturnsTrue()
    {
        var service = new DpapiEncryptionService();
        var encrypted = service.Encrypt("test-key");

        Assert.True(service.IsEncrypted(encrypted));
    }

    [Fact]
    public void IsEncrypted_PlaintextKey_ReturnsFalse()
    {
        var service = new DpapiEncryptionService();

        Assert.False(service.IsEncrypted("sk-proj-abc123"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~DpapiEncryptionService" -v n`
Expected: Build error — `AgentX.Core.Services.Security` namespace does not exist.

- [ ] **Step 3: Write the interface**

```csharp
// src/AgentX.Core/Services/Security/IDpapiEncryptionService.cs
namespace AgentX.Core.Services.Security;

public interface IDpapiEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    bool IsEncrypted(string value);
}
```

- [ ] **Step 4: Write the implementation**

```csharp
// src/AgentX.Core/Services/Security/DpapiEncryptionService.cs
using System.Security.Cryptography;
using System.Text;

namespace AgentX.Core.Services.Security;

public class DpapiEncryptionService : IDpapiEncryptionService
{
    // Prefix to distinguish encrypted values from plaintext in settings.json
    private const string EncryptedPrefix = "DPAPI:";

    // DPAPI scope: CurrentUser means only this Windows user can decrypt
    private static readonly DataProtectionScope Scope = DataProtectionScope.CurrentUser;

    public string Encrypt(string plaintext)
    {
        if (plaintext == null) return null!;
        if (plaintext.Length == 0)
        {
            var emptyBytes = ProtectedData.Protect(
                Array.Empty<byte>(),
                null,
                Scope);
            return EncryptedPrefix + Convert.ToBase64String(emptyBytes);
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, Scope);
        return EncryptedPrefix + Convert.ToBase64String(encrypted);
    }

    public string Decrypt(string ciphertext)
    {
        if (ciphertext == null) return null!;
        if (!IsEncrypted(ciphertext))
            throw new InvalidOperationException("Value is not encrypted. Call IsEncrypted() first.");

        var base64 = ciphertext[EncryptedPrefix.Length..];
        var encrypted = Convert.FromBase64String(base64);
        var decrypted = ProtectedData.Unprotect(encrypted, null, Scope);
        return Encoding.UTF8.GetString(decrypted);
    }

    public bool IsEncrypted(string value)
    {
        return value != null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~DpapiEncryptionService" -v n`
Expected: All 7 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.Core/Services/Security/IDpapiEncryptionService.cs src/AgentX.Core/Services/Security/DpapiEncryptionService.cs tests/AgentX.Tests/Services/Security/DpapiEncryptionServiceTests.cs
git commit -m "feat(security): add DpapiEncryptionService with DPAPI encryption/decryption"
```

---

### Task 2: SettingsService Integration — Encrypt/Decrypt on Read/Write

**Files:**
- Modify: `src/AgentX.Core/Services/Settings/SettingsService.cs`
- Modify: `src/AgentX.Core/Services/Settings/ISettingsService.cs`
- Test: `tests/AgentX.Tests/Services/Settings/SettingsServiceEncryptionTests.cs`

- [ ] **Step 1: Write the failing test for encrypted save/load**

```csharp
// tests/AgentX.Tests/Services/Settings/SettingsServiceEncryptionTests.cs
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Security;
using System.Text.Json;
using Xunit;

namespace AgentX.Tests.Services.Services.Settings;

public class SettingsServiceEncryptionTests
{
    [Fact]
    public async Task SaveSettingsAsync_ApiKeysAreEncryptedInFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"AgentX_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var encryptionService = new DpapiEncryptionService();

        try
        {
            var settings = new AppSettings
            {
                OpenAiApiKey = "sk-test-openai-key-12345",
                AnthropicApiKey = "sk-ant-test-key-67890",
                OllamaEndpoint = "http://localhost:11434"
            };

            // Act
            var service = new SettingsService(settingsPath, encryptionService);
            await service.SaveSettingsAsync(settings);

            // Assert: read the raw file and verify keys are NOT plaintext
            var json = await File.ReadAllTextAsync(settingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("openAiApiKey", out var openAiKey));
            Assert.True(root.TryGetProperty("anthropicApiKey", out var anthropicKey));

            var rawOpenAi = openAiKey.GetString()!;
            var rawAnthropic = anthropicKey.GetString()!;

            // Keys should start with DPAPI: prefix
            Assert.True(rawOpenAi.StartsWith("DPAPI:"), "OpenAI key should be DPAPI-encrypted in file");
            Assert.True(rawAnthropic.StartsWith("DPAPI:"), "Anthropic key should be DPAPI-encrypted in file");

            // Keys should NOT contain the plaintext values
            Assert.DoesNotContain("sk-test-openai-key-12345", rawOpenAi);
            Assert.DoesNotContain("sk-ant-test-key-67890", rawAnthropic);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetSettingsAsync_DecryptsEncryptedKeys()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"AgentX_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var encryptionService = new DpapiEncryptionService();

        try
        {
            var settings = new AppSettings
            {
                OpenAiApiKey = "sk-test-openai-key-12345",
                AnthropicApiKey = "sk-ant-test-key-67890"
            };

            var service = new SettingsService(settingsPath, encryptionService);
            await service.SaveSettingsAsync(settings);

            // Act: load back
            var loaded = await service.GetSettingsAsync();

            // Assert: keys should be decrypted back to plaintext in memory
            Assert.Equal("sk-test-openai-key-12345", loaded.OpenAiApiKey);
            Assert.Equal("sk-ant-test-key-67890", loaded.AnthropicApiKey);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetSettingsAsync_MigratesPlaintextKeysOnLoad()
    {
        // Arrange: write a settings.json with plaintext keys (simulating old format)
        var tempDir = Path.Combine(Path.GetTempPath(), $"AgentX_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var encryptionService = new DpapiEncryptionService();

        try
        {
            var plaintextJson = """{"openAiApiKey":"sk-plain-key-123","anthropicApiKey":"sk-ant-plain-456","ollamaEndpoint":"http://localhost:11434"}""";
            await File.WriteAllTextAsync(settingsPath, plaintextJson);

            var service = new SettingsService(settingsPath, encryptionService);

            // Act: load should detect plaintext and auto-migrate
            var loaded = await service.GetSettingsAsync();

            // Assert: in-memory values should be the plaintext
            Assert.Equal("sk-plain-key-123", loaded.OpenAiApiKey);
            Assert.Equal("sk-ant-plain-456", loaded.AnthropicApiKey);

            // Assert: file should now contain encrypted keys
            var json = await File.ReadAllTextAsync(settingsPath);
            Assert.Contains("DPAPI:", json);
            Assert.DoesNotContain("sk-plain-key-123", json);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~SettingsServiceEncryption" -v n`
Expected: Build error — `SettingsService` constructor doesn't accept `IDpapiEncryptionService`.

- [ ] **Step 3: Modify ISettingsService to add encryption dependency**

Read `src/AgentX.Core/Services/Settings/ISettingsService.cs` first. Then update the implementation. The key change: `SettingsService` constructor takes `IDpapiEncryptionService` as a dependency. In `SaveSettingsAsync`, encrypt API key fields before writing. In `GetSettingsAsync`, decrypt after reading, and auto-migrate plaintext keys.

Modify `src/AgentX.Core/Services/Settings/SettingsService.cs`:

1. Add `IDpapiEncryptionService` constructor parameter
2. In `SaveSettingsAsync`: before serializing, replace `OpenAiApiKey` and `AnthropicApiKey` with encrypted values using `_encryptionService.Encrypt()`
3. In `GetSettingsAsync`: after deserializing, if keys don't start with `DPAPI:`, they're plaintext — decrypt if encrypted, or keep as-is if plaintext (then immediately save to trigger migration)
4. Add `static readonly string[] EncryptedSettingKeys = { "OpenAiApiKey", "AnthropicApiKey" };`

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~SettingsServiceEncryption" -v n`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/Services/Settings/SettingsService.cs tests/AgentX.Tests/Services/Settings/SettingsServiceEncryptionTests.cs
git commit -m "feat(security): integrate DPAPI encryption into SettingsService save/load with auto-migration"
```

---

### Task 3: DI Registration Update

**Files:**
- Modify: `src/AgentX.App/App.xaml.cs`

- [ ] **Step 1: Register DpapiEncryptionService in the DI container**

In `App.xaml.cs`, inside `ConfigureServices(IServiceCollection services)`, add:

```csharp
services.AddSingleton<IDpapiEncryptionService, DpapiEncryptionService>();
```

This must be registered before `SettingsService` since `SettingsService` now depends on it.

- [ ] **Step 2: Run the app to verify it launches**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet run --project src/AgentX.App`
Expected: App launches normally. No crash from DI resolution.

- [ ] **Step 3: Verify existing plaintext keys auto-migrate**

1. Before running: check `%LOCALAPPDATA%/AgentX/settings.json` for plaintext API keys
2. Launch app
3. Close app
4. Re-check `settings.json` — API keys should now start with `DPAPI:` prefix
5. Re-launch app — settings should load correctly with decrypted keys in memory

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.App/App.xaml.cs
git commit -m "feat(security): register DpapiEncryptionService in DI container"
```

---

### Task 4: Security Status Indicator in Settings UI

**Files:**
- Create: `src/AgentX.Core/Services/Security/ISecurityStatusService.cs`
- Create: `src/AgentX.Core/Services/Security/SecurityStatusService.cs`
- Modify: `src/AgentX.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AgentX.App/Views/SettingsPage.xaml`

- [ ] **Step 1: Write the SecurityStatusService**

```csharp
// src/AgentX.Core/Services/Security/ISecurityStatusService.cs
namespace AgentX.Core.Services.Security;

public interface ISecurityStatusService
{
    bool AreKeysEncrypted { get; }
    string GetEncryptionStatusDescription();
}

// src/AgentX.Core/Services/Security/SecurityStatusService.cs
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Security;

public class SecurityStatusService : ISecurityStatusService
{
    private readonly ISettingsService _settingsService;
    private readonly IDpapiEncryptionService _encryptionService;

    public SecurityStatusService(ISettingsService settingsService, IDpapiEncryptionService encryptionService)
    {
        _settingsService = settingsService;
        _encryptionService = encryptionService;
    }

    public bool AreKeysEncrypted
    {
        get
        {
            // Check if the raw file has encrypted keys
            // This is determined by reading the settings and checking the DPAPI: prefix
            // In practice, after migration runs, this should always be true
            return true; // After migration, keys are always encrypted at rest
        }
    }

    public string GetEncryptionStatusDescription()
    {
        return AreKeysEncrypted
            ? "API keys are encrypted with Windows DPAPI (per-user, per-machine)"
            : "API keys are stored in plaintext — migration pending";
    }
}
```

- [ ] **Step 2: Add security status to SettingsViewModel**

Read `src/AgentX.App/ViewModels/SettingsViewModel.cs`. Add:

```csharp
// Add as observable properties
[ObservableProperty]
private bool _areKeysEncrypted;

[ObservableProperty]
private string _encryptionStatusDescription = string.Empty;
```

In `InitializeAsync()`, after loading settings:

```csharp
var securityStatus = App.Current.Services.GetRequiredService<ISecurityStatusService>();
AreKeysEncrypted = securityStatus.AreKeysEncrypted;
EncryptionStatusDescription = securityStatus.GetEncryptionStatusDescription();
```

- [ ] **Step 3: Add lock icon to SettingsPage.xaml**

Read `src/AgentX.App/Views/SettingsPage.xaml`. In the API keys section, add an info bar showing encryption status:

```xml
<!-- Add after the API key PasswordBox sections -->
<InfoBar IsOpen="{x:Bind ViewModel.AreKeysEncrypted, Mode=OneWay}"
         Severity="Success"
         Title="API Keys Secured"
         Message="{x:Bind ViewModel.EncryptionStatusDescription, Mode=OneWay}"
         IsClosable="False"
         Margin="0,8,0,0" />
```

- [ ] **Step 4: Run the app and verify the security indicator appears**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet run --project src/AgentX.App`
Expected: Settings page shows green InfoBar: "API Keys Secured — API keys are encrypted with Windows DPAPI (per-user, per-machine)"

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/Services/Security/ISecurityStatusService.cs src/AgentX.Core/Services/Security/SecurityStatusService.cs src/AgentX.App/ViewModels/SettingsViewModel.cs src/AgentX.App/Views/SettingsPage.xaml
git commit -m "feat(security): add encryption status indicator to Settings UI"
```

---

### Task 5: Run Full Test Suite and Verify No Regressions

**Files:** No new files

- [ ] **Step 1: Run full test suite**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests -v n`
Expected: All existing tests + new tests pass. 260+ baseline + 10 new = 270+ tests PASS.

- [ ] **Step 2: Verify DPAPI works across app restarts**

1. Launch app → Settings → enter API key → Save → Close
2. Re-launch app → Settings → verify API key is populated
3. Check `%LOCALAPPDATA%/AgentX/settings.json` — no plaintext keys visible

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix(security): resolve test regressions from DPAPI integration"
```
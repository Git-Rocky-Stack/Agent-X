using System.Reflection;
using System.Text.Json;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Settings;

/// <summary>
/// Tests verifying that <see cref="SettingsService"/> encrypts API keys at rest
/// using <see cref="IDpapiEncryptionService"/> and auto-migrates plaintext keys
/// on load.
/// </summary>
public sealed class SettingsServiceEncryptionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DpapiEncryptionService _encryptionService;
    private readonly string _settingsPath;

    public SettingsServiceEncryptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgentXTests_Encryption_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _encryptionService = new DpapiEncryptionService();
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp files will be purged by the OS eventually.
        }
    }

    /// <summary>
    /// Helper to create a <see cref="SettingsService"/> with its _settingsPath
    /// redirected to the test temp directory.
    /// </summary>
    private SettingsService CreateSut()
    {
        var sut = new SettingsService(_encryptionService);
        var field = typeof(SettingsService).GetField("_settingsPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(sut, _settingsPath);
        return sut;
    }

    [Fact]
    public async Task SaveSettingsAsync_ApiKeysAreEncryptedInFile()
    {
        // Arrange
        var sut = CreateSut();
        var settings = new AppSettings
        {
            OpenAiApiKey = "sk-plaintext-openai-key",
            AnthropicApiKey = "sk-ant-plaintext-anthropic-key",
            ActiveProviderId = "openai",
        };

        // Act
        await sut.SaveSettingsAsync(settings);

        // Assert: read the raw file and verify API keys are encrypted
        var rawJson = await File.ReadAllTextAsync(_settingsPath);
        var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var openAiKey = root.GetProperty("openAiApiKey").GetString()!;
        var anthropicKey = root.GetProperty("anthropicApiKey").GetString()!;

        openAiKey.Should().StartWith("DPAPI:",
            "the OpenAI API key should be DPAPI-encrypted on disk");
        openAiKey.Should().NotContain("sk-plaintext-openai-key",
            "the plaintext key must not appear in the file");

        anthropicKey.Should().StartWith("DPAPI:",
            "the Anthropic API key should be DPAPI-encrypted on disk");
        anthropicKey.Should().NotContain("sk-ant-plaintext-anthropic-key",
            "the plaintext key must not appear in the file");
    }

    [Fact]
    public async Task GetSettingsAsync_DecryptsEncryptedKeys()
    {
        // Arrange: save settings with API keys (they get encrypted on disk)
        var sut = CreateSut();
        var originalSettings = new AppSettings
        {
            OpenAiApiKey = "sk-my-secret-openai-key",
            AnthropicApiKey = "sk-ant-my-secret-anthropic-key",
            ActiveProviderId = "anthropic",
        };
        await sut.SaveSettingsAsync(originalSettings);

        // Act: load settings with a fresh service instance (bypasses in-memory cache)
        var freshSut = CreateSut();
        var loaded = await freshSut.GetSettingsAsync();

        // Assert: in-memory values should be the original plaintext
        loaded.OpenAiApiKey.Should().Be("sk-my-secret-openai-key",
            "the in-memory OpenAI API key must be the decrypted plaintext");
        loaded.AnthropicApiKey.Should().Be("sk-ant-my-secret-anthropic-key",
            "the in-memory Anthropic API key must be the decrypted plaintext");
    }

    [Fact]
    public async Task GetSettingsAsync_MigratesPlaintextKeysOnLoad()
    {
        // Arrange: write a raw settings.json with plaintext API keys
        // (simulating a file from a previous version without encryption)
        var plaintextSettings = new
        {
            onboardingCompleted = false,
            activeProviderId = "openai",
            openAiApiKey = "sk-legacy-plaintext-key",
            anthropicApiKey = "sk-ant-legacy-plaintext-key",
            temperature = 0.7,
            maxTokens = 4096,
            chunkSize = 512,
            chunkOverlap = 50,
            topKResults = 5,
            autoIndexWatchFolders = true,
            localModelFileName = "llama-3.2-3b-instruct-q4_k_m.gguf",
            localContextSize = 8192,
            localGpuLayers = 0,
            ollamaEndpoint = "http://localhost:11434",
            defaultModel = "llama3.2",
            embeddingModel = "all-minilm",
            openAiEndpoint = "https://api.openai.com/v1/",
            openAiDefaultModel = "gpt-4o-mini",
            anthropicEndpoint = "https://api.anthropic.com/v1/",
            anthropicDefaultModel = "claude-sonnet-4-20250514",
            contextWindow = 8192,
            storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentX"),
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var rawJson = JsonSerializer.Serialize(plaintextSettings, jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, rawJson);

        // Act: load settings via the service — this should detect plaintext
        // keys, keep them in memory as-is, and trigger a re-save with encryption
        var sut = CreateSut();
        var loaded = await sut.GetSettingsAsync();

        // Assert 1: in-memory values should be the original plaintext
        loaded.OpenAiApiKey.Should().Be("sk-legacy-plaintext-key",
            "plaintext keys should remain as plaintext in memory after migration");
        loaded.AnthropicApiKey.Should().Be("sk-ant-legacy-plaintext-key",
            "plaintext keys should remain as plaintext in memory after migration");

        // Assert 2: the file should now contain encrypted keys (auto-migration)
        var migratedJson = await File.ReadAllTextAsync(_settingsPath);
        var doc = JsonDocument.Parse(migratedJson);
        var root = doc.RootElement;

        var openAiKeyOnDisk = root.GetProperty("openAiApiKey").GetString()!;
        var anthropicKeyOnDisk = root.GetProperty("anthropicApiKey").GetString()!;

        openAiKeyOnDisk.Should().StartWith("DPAPI:",
            "auto-migration should have encrypted the OpenAI key on disk");
        openAiKeyOnDisk.Should().NotContain("sk-legacy-plaintext-key",
            "the plaintext key must not remain on disk after migration");

        anthropicKeyOnDisk.Should().StartWith("DPAPI:",
            "auto-migration should have encrypted the Anthropic key on disk");
        anthropicKeyOnDisk.Should().NotContain("sk-ant-legacy-plaintext-key",
            "the plaintext key must not remain on disk after migration");
    }
}
using System.Reflection;
using System.Text.Json;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SettingsService"/>.
///
/// Because <see cref="SettingsService"/> reads/writes to the filesystem and derives
/// its path from <see cref="Environment.SpecialFolder.LocalApplicationData"/>, we
/// use reflection to redirect the <c>_settingsPath</c> field to a temp directory
/// for each test. This ensures full isolation and prevents tests from interfering
/// with the actual application settings.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _sut;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgentXTests_Settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _sut = new SettingsService();

        // Redirect _settingsPath via reflection so we don't touch real app data.
        var field = typeof(SettingsService).GetField("_settingsPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(_sut, Path.Combine(_tempDir, "settings.json"));
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

    // ── GetSettingsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSettingsAsync_WhenNoFileExists_ReturnsDefaultSettings()
    {
        // Arrange: temp dir is empty, no settings.json exists

        // Act
        var settings = await _sut.GetSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        settings.ActiveProviderId.Should().Be("ollama");
        settings.Temperature.Should().Be(0.7);
        settings.MaxTokens.Should().Be(4096);
        settings.ChunkSize.Should().Be(512);
        settings.OnboardingCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetSettingsAsync_WhenNoFileExists_CreatesDefaultSettingsFile()
    {
        // Arrange
        var settingsPath = Path.Combine(_tempDir, "settings.json");

        // Act
        await _sut.GetSettingsAsync();

        // Assert: the file should have been created by the service
        File.Exists(settingsPath).Should().BeTrue("the service should persist defaults when no file exists");
    }

    // ── SaveSettingsAsync + GetSettingsAsync round-trip ──────────────────

    [Fact]
    public async Task SaveSettingsAsync_AndGetSettingsAsync_RoundTripCorrectly()
    {
        // Arrange
        var original = new AppSettings
        {
            ActiveProviderId = "openai",
            Temperature = 1.2,
            MaxTokens = 8192,
            ChunkSize = 1024,
            ChunkOverlap = 128,
            OnboardingCompleted = true,
            DefaultModel = "gpt-4o",
            OpenAiApiKey = "sk-test-key-12345"
        };

        // Act
        await _sut.SaveSettingsAsync(original);

        // Force a fresh service instance to read from disk (bypasses cache).
        var freshService = new SettingsService();
        var field = typeof(SettingsService).GetField("_settingsPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(freshService, Path.Combine(_tempDir, "settings.json"));

        var loaded = await freshService.GetSettingsAsync();

        // Assert
        loaded.ActiveProviderId.Should().Be("openai");
        loaded.Temperature.Should().Be(1.2);
        loaded.MaxTokens.Should().Be(8192);
        loaded.ChunkSize.Should().Be(1024);
        loaded.ChunkOverlap.Should().Be(128);
        loaded.OnboardingCompleted.Should().BeTrue();
        loaded.DefaultModel.Should().Be("gpt-4o");
        loaded.OpenAiApiKey.Should().Be("sk-test-key-12345");
    }

    // ── GetValueAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetValueAsync_WithKnownProperty_ReturnsCorrectTypedValue()
    {
        // Arrange
        var settings = new AppSettings { Temperature = 1.5 };
        await _sut.SaveSettingsAsync(settings);

        // Act
        var temperature = await _sut.GetValueAsync<double>("Temperature");

        // Assert
        temperature.Should().Be(1.5);
    }

    [Fact]
    public async Task GetValueAsync_WithKnownStringProperty_ReturnsCorrectValue()
    {
        // Arrange
        var settings = new AppSettings { ActiveProviderId = "anthropic" };
        await _sut.SaveSettingsAsync(settings);

        // Act
        var provider = await _sut.GetValueAsync<string>("ActiveProviderId");

        // Assert
        provider.Should().Be("anthropic");
    }

    [Fact]
    public async Task GetValueAsync_WithUnknownProperty_ReturnsDefault()
    {
        // Arrange: ensure settings are loaded (triggers default creation)
        await _sut.GetSettingsAsync();

        // Act
        var result = await _sut.GetValueAsync<string>("NonExistentProperty");

        // Assert
        result.Should().BeNull("unknown properties should return default(T)");
    }

    [Fact]
    public async Task GetValueAsync_WithUnknownProperty_ReturnsDefaultInt()
    {
        // Arrange
        await _sut.GetSettingsAsync();

        // Act
        var result = await _sut.GetValueAsync<int>("CompletelyFakeProperty");

        // Assert
        result.Should().Be(0, "default(int) is 0 for a missing property");
    }

    // ── SetValueAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SetValueAsync_UpdatesPropertyAndPersists()
    {
        // Arrange
        await _sut.GetSettingsAsync(); // Initialize defaults

        // Act
        await _sut.SetValueAsync("MaxTokens", 16384);

        // Assert: verify in-memory cache is updated
        var settings = await _sut.GetSettingsAsync();
        settings.MaxTokens.Should().Be(16384);

        // Assert: verify persistence — read from disk with a fresh instance
        var freshService = new SettingsService();
        var field = typeof(SettingsService).GetField("_settingsPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(freshService, Path.Combine(_tempDir, "settings.json"));

        var persisted = await freshService.GetSettingsAsync();
        persisted.MaxTokens.Should().Be(16384);
    }

    [Fact]
    public async Task SetValueAsync_WithUnknownProperty_DoesNotThrow()
    {
        // Arrange
        await _sut.GetSettingsAsync();

        // Act
        var act = () => _sut.SetValueAsync("UnknownProperty", "value");

        // Assert: should silently do nothing for unknown properties
        await act.Should().NotThrowAsync();
    }

    // ── Caching ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSettingsAsync_ConcurrentCalls_ReturnSameCachedInstance()
    {
        // Arrange & Act: fire multiple concurrent calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _sut.GetSettingsAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert: all returned references should be the same cached object
        var first = results[0];
        foreach (var result in results)
        {
            result.Should().BeSameAs(first,
                "concurrent calls should return the same cached AppSettings instance");
        }
    }

    [Fact]
    public async Task GetSettingsAsync_AfterSave_ReturnsCachedUpdatedInstance()
    {
        // Arrange
        var settings = await _sut.GetSettingsAsync();
        settings.Temperature = 0.3;
        await _sut.SaveSettingsAsync(settings);

        // Act
        var retrieved = await _sut.GetSettingsAsync();

        // Assert
        retrieved.Should().BeSameAs(settings,
            "after saving, GetSettingsAsync should return the cached (same) instance");
        retrieved.Temperature.Should().Be(0.3);
    }
}

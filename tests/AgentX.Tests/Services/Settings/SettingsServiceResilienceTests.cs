using System.Text.Json;
using System.Text.Json.Nodes;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Settings;

/// <summary>
/// ST15: a load problem must never turn into a silent reset of every setting, and a save must
/// never leave a truncated file. Also covers the validation that SaveSettingsAsync now applies.
/// Uses a reversible DPAPI stand-in so the tests run on every platform.
/// </summary>
public sealed class SettingsServiceResilienceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AgentXTests_SettingsResilience_" + Guid.NewGuid().ToString("N"));
    private readonly string _settingsPath;
    private readonly FakeDpapiEncryptionService _dpapi = new();

    public SettingsServiceResilienceTests()
    {
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SettingsService CreateSut() => new(_dpapi, validator: null, _settingsPath);

    private void WriteRawSettings(Action<JsonObject> edit)
    {
        var node = JsonSerializer.SerializeToNode(new AppSettings(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        edit(node);
        File.WriteAllText(_settingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task GetSettingsAsync_when_one_secret_cannot_be_decrypted_keeps_every_other_setting()
    {
        var undecryptable = _dpapi.Encrypt("sk-ant-from-another-account");
        _dpapi.Undecryptable.Add(undecryptable);
        WriteRawSettings(s =>
        {
            s["onboardingCompleted"] = true;
            s["chunkSize"] = 1024;
            s["openAiApiKey"] = _dpapi.Encrypt("sk-openai");
            s["anthropicApiKey"] = undecryptable;
            s["localApiToken"] = _dpapi.Encrypt("api-token");
        });

        var settings = await CreateSut().GetSettingsAsync();

        // Previously one DPAPI failure returned new AppSettings(), and the first save (the API
        // token provisioning at startup) then overwrote every key and the onboarding flag.
        settings.OnboardingCompleted.Should().BeTrue();
        settings.ChunkSize.Should().Be(1024);
        settings.OpenAiApiKey.Should().Be("sk-openai");
        settings.LocalApiToken.Should().Be("api-token");
        settings.AnthropicApiKey.Should().BeNull("an undecryptable value is cleared, not guessed");
        Directory.GetFiles(_dir, "settings.json.undecryptable-*").Should().ContainSingle(
            "the original ciphertext is kept in case it decrypts under the account that wrote it");
    }

    [Fact]
    public async Task GetSettingsAsync_with_a_truncated_file_preserves_it_instead_of_overwriting_it()
    {
        const string truncated = "{\n  \"onboardingCompleted\": true,\n  \"theme\": \"Li";
        File.WriteAllText(_settingsPath, truncated);
        var sut = CreateSut();

        var settings = await sut.GetSettingsAsync();
        settings.OnboardingCompleted.Should().BeFalse("defaults are in use for this session");

        var preserved = Directory.GetFiles(_dir, "settings.json.corrupt-*").Should().ContainSingle().Subject;
        File.ReadAllText(preserved).Should().Be(truncated);

        // A later save (for example the API token provisioning) writes a valid file and the
        // preserved copy survives it.
        settings.LocalApiToken = "fresh-token";
        await sut.SaveSettingsAsync(settings);
        File.ReadAllText(preserved).Should().Be(truncated);
        (await CreateSut().GetSettingsAsync()).LocalApiToken.Should().Be("fresh-token");
    }

    [Fact]
    public async Task GetSettingsAsync_restores_nested_sections_written_as_null()
    {
        WriteRawSettings(s =>
        {
            s["oAuth"] = null;
            s["emailConnector"] = null;
            s["theme"] = "Light";
        });

        var settings = await CreateSut().GetSettingsAsync();

        settings.Theme.Should().Be("Light");
        settings.OAuth.Should().NotBeNull();
        settings.OAuth.Google.Should().NotBeNull();
        settings.EmailConnector.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveSettingsAsync_replaces_the_file_without_leaving_temp_files()
    {
        var sut = CreateSut();
        var settings = await sut.GetSettingsAsync();

        settings.Theme = "Light";
        await sut.SaveSettingsAsync(settings);
        settings.Theme = "Dark";
        await sut.SaveSettingsAsync(settings);

        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().Equal("settings.json");
        (await CreateSut().GetSettingsAsync()).Theme.Should().Be("Dark");
    }

    [Fact]
    public async Task SaveSettingsAsync_encrypts_secrets_on_disk_and_keeps_plaintext_in_memory()
    {
        var sut = CreateSut();
        var settings = await sut.GetSettingsAsync();
        settings.OpenAiApiKey = "sk-plain";

        await sut.SaveSettingsAsync(settings);

        var onDisk = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
        onDisk["openAiApiKey"]!.GetValue<string>().Should().StartWith("DPAPI:");
        settings.OpenAiApiKey.Should().Be("sk-plain");
    }

    [Fact]
    public async Task SaveSettingsAsync_rejects_clearly_invalid_values_and_keeps_the_saved_settings()
    {
        var sut = CreateSut();
        var settings = await sut.GetSettingsAsync();
        settings.ChunkSize = 512;
        settings.ChunkOverlap = 50;
        await sut.SaveSettingsAsync(settings);
        var savedJson = File.ReadAllText(_settingsPath);

        settings.ChunkOverlap = 900; // larger than the chunk size: indexing would reject every document
        var act = () => sut.SaveSettingsAsync(settings);

        var thrown = await act.Should().ThrowAsync<SettingsValidationException>();
        thrown.Which.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ChunkOverlap));
        thrown.Which.Message.Should().Contain("ChunkOverlap");
        File.ReadAllText(_settingsPath).Should().Be(savedJson);
        (await sut.GetSettingsAsync()).ChunkOverlap.Should().Be(50, "the rejected value must not linger in memory");
    }

    [Fact]
    public async Task SaveSettingsAsync_does_not_lock_callers_out_over_a_value_already_invalid_on_disk()
    {
        WriteRawSettings(s =>
        {
            s["chunkSize"] = 512;
            s["chunkOverlap"] = 600;
        });
        var sut = CreateSut();
        var settings = await sut.GetSettingsAsync();

        // An unrelated save (theme, API token, onboarding) must still work.
        settings.Theme = "Light";
        var act = () => sut.SaveSettingsAsync(settings);

        await act.Should().NotThrowAsync();
        (await CreateSut().GetSettingsAsync()).Theme.Should().Be("Light");
    }

    [Fact]
    public async Task SaveSettingsAsync_allows_a_cloud_provider_whose_key_is_not_entered_yet()
    {
        var sut = CreateSut();
        var settings = await sut.GetSettingsAsync();
        settings.ActiveProviderId = "openai";
        settings.OpenAiApiKey = null;

        var act = () => sut.SaveSettingsAsync(settings);

        await act.Should().NotThrowAsync();
    }
}

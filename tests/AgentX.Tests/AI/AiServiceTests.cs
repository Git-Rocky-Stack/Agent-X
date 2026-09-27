using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Providers;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class AiServiceTests : IDisposable
{
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "agentx-aiservice-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings;
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Dictionary<string, Mock<IAiProvider>> _fakes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AiService> _services = new();

    public AiServiceTests()
    {
        Directory.CreateDirectory(_storage);
        _settings = new AppSettings
        {
            StoragePath = _storage,
            ActiveProviderId = "ollama",
            OllamaEndpoint = "http://localhost:11434",
            DefaultModel = "llama3.2",
            OpenAiApiKey = "sk-test",
            OpenAiDefaultModel = "gpt-4o",
            AnthropicApiKey = "sk-ant-test",
            AnthropicDefaultModel = "claude-sonnet-5"
        };
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => _settings);
    }

    public void Dispose()
    {
        foreach (var service in _services) service.Dispose();
        try { Directory.Delete(_storage, recursive: true); } catch { /* best-effort */ }
    }

    private Mock<IAiProvider> Fake(string id, bool connected = true)
    {
        var provider = new Mock<IAiProvider>();
        provider.Setup(p => p.ProviderId).Returns(id);
        provider.Setup(p => p.DisplayName).Returns(id);
        provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connected);
        _fakes[id] = provider;
        return provider;
    }

    /// <summary>Service whose cloud and Ollama providers are fakes; the built-in provider is real.</summary>
    private AiService CreateService(Func<string, AppSettings, IAiProvider?>? factory = null)
    {
        var service = new AiService(_settingsService.Object)
        {
            ProviderFactoryOverride = factory ?? ((id, _) =>
                id == "local" ? null : (_fakes.TryGetValue(id, out var fake) ? fake.Object : Fake(id).Object))
        };
        _services.Add(service);
        return service;
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:11434")]
    [InlineData("not a url")]
    [InlineData("ftp://localhost:11434")]
    public async Task Invalid_Ollama_endpoint_keeps_the_service_usable(string endpoint)
    {
        _settings.OllamaEndpoint = endpoint;
        _settings.ActiveProviderId = "openai";
        var service = CreateService();

        await service.InitializeAsync();

        service.GetProvider("ollama").Should().BeNull();
        service.ActiveProvider.ProviderId.Should().Be("openai");
        service.ActiveModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task Missing_preferred_provider_falls_back_without_leaving_no_active_provider()
    {
        _settings.ActiveProviderId = "anthropic";
        _settings.AnthropicApiKey = null;
        var service = CreateService();

        await service.InitializeAsync();

        service.ActiveProvider.ProviderId.Should().Be("ollama", "the built-in model is not installed here");
        service.ActiveModelId.Should().Be("llama3.2");
    }

    [Fact]
    public async Task Reinitialize_reuses_unchanged_providers_and_disposes_replaced_ones()
    {
        var service = CreateService();
        await service.InitializeAsync();

        var ollama = service.GetProvider("ollama");
        var openAi = _fakes["openai"];
        var local = service.GetProvider("local");

        _fakes.Remove("openai"); // the factory builds a new fake for the changed key
        _settings.OpenAiApiKey = "sk-rotated";
        await service.InitializeAsync();

        service.GetProvider("ollama").Should().BeSameAs(ollama, "an unchanged provider is not rebuilt");
        service.GetProvider("local").Should().BeSameAs(local, "the built-in model is not reloaded on every save");
        service.GetProvider("openai").Should().NotBeSameAs(openAi.Object);
        openAi.Verify(p => p.Dispose(), Times.Once);
        _fakes["ollama"].Verify(p => p.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Switch_to_an_unreachable_provider_keeps_the_current_one()
    {
        Fake("anthropic", connected: false);
        var service = CreateService();
        await service.InitializeAsync();

        var switched = await service.SwitchProviderAsync("anthropic");

        switched.Should().BeFalse();
        service.ActiveProvider.ProviderId.Should().Be("ollama");
        service.ActiveModelId.Should().Be("llama3.2");
    }

    [Fact]
    public async Task Switch_sets_provider_and_its_default_model_together()
    {
        var service = CreateService();
        await service.InitializeAsync();

        (await service.SwitchProviderAsync("openai")).Should().BeTrue();

        service.ActiveProvider.ProviderId.Should().Be("openai");
        service.ActiveModelId.Should().Be("gpt-4o", "the Ollama model id must never be sent to OpenAI");
    }

    [Fact]
    public async Task Connection_checks_are_reused_for_a_short_time()
    {
        var service = CreateService();
        await service.InitializeAsync();

        (await service.IsProviderAvailableAsync("anthropic")).Should().BeTrue();
        (await service.IsProviderAvailableAsync("anthropic")).Should().BeTrue();
        (await service.SwitchProviderAsync("anthropic")).Should().BeTrue();

        _fakes["anthropic"].Verify(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        (await service.IsProviderAvailableAsync("not-registered")).Should().BeFalse();
    }

    [Fact]
    public async Task SetActiveModel_persists_into_the_active_providers_own_setting()
    {
        var service = CreateService();
        await service.InitializeAsync();
        await service.SwitchProviderAsync("openai");

        await service.SetActiveModelAsync("gpt-4.1");

        _settings.OpenAiDefaultModel.Should().Be("gpt-4.1");
        _settings.DefaultModel.Should().Be("llama3.2", "Ollama's model setting must not receive an OpenAI id");
        _settingsService.Verify(s => s.SaveSettingsAsync(_settings), Times.Once);

        // Restart: the pick is restored for OpenAI.
        var restarted = CreateService();
        _settings.ActiveProviderId = "openai";
        await restarted.InitializeAsync();
        restarted.ActiveModelId.Should().Be("gpt-4.1");
    }

    [Fact]
    public async Task SetActiveModel_for_the_built_in_provider_never_changes_the_embedding_model_file()
    {
        _settings.ActiveProviderId = "local";
        var service = CreateService();
        await service.InitializeAsync();

        await service.SetActiveModelAsync("llama-3.2-1b-instruct-q4_k_m.gguf");

        service.ActiveModelId.Should().Be("llama-3.2-1b-instruct-q4_k_m.gguf");
        _settings.LocalModelFileName.Should().Be("llama-3.2-3b-instruct-q4_k_m.gguf");
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task Chat_fills_the_active_model_without_mutating_the_callers_options()
    {
        var ollama = Fake("ollama");
        ChatOptions? seen = null;
        ollama.Setup(p => p.ChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<ChatMessage> _, ChatOptions? o, CancellationToken _) => seen = o)
            .ReturnsAsync("ok");
        var service = CreateService();
        await service.InitializeAsync();

        var shared = new ChatOptions { Temperature = 0.1 };
        await service.ChatAsync(new[] { ChatMessage.User("hi") }, options: shared);

        seen!.ModelId.Should().Be("llama3.2");
        seen.Temperature.Should().Be(0.1);
        shared.ModelId.Should().BeNull("a reused options object must not keep one provider's model id");
    }

    [Fact]
    public async Task Embedding_target_is_independent_of_the_active_chat_provider()
    {
        _settings.ActiveProviderId = "anthropic";
        _settings.EmbeddingModel = "all-minilm";
        var service = CreateService();
        await service.InitializeAsync();

        service.ActiveProvider.ProviderId.Should().Be("anthropic");
        service.ResolveEmbeddingTarget().Should().Be(new EmbeddingTarget("ollama", "all-minilm"));

        var modelsDir = Path.Combine(_storage, "Models");
        Directory.CreateDirectory(modelsDir);
        File.WriteAllBytes(Path.Combine(modelsDir, _settings.LocalModelFileName), new byte[16]);

        service.ResolveEmbeddingTarget().Should().Be(new EmbeddingTarget("local", _settings.LocalModelFileName));
    }

    [Fact]
    public async Task Default_model_ids_are_per_provider()
    {
        _settings.AnthropicDefaultModel = null;
        var service = CreateService();
        await service.InitializeAsync();

        service.GetDefaultModelId("ollama").Should().Be("llama3.2");
        service.GetDefaultModelId("openai").Should().Be("gpt-4o");
        service.GetDefaultModelId("anthropic").Should().Be("claude-sonnet-5");
        service.GetDefaultModelId("local").Should().Be("llama-3.2-3b-instruct-q4_k_m.gguf");
        service.RegisteredProviderIds.Should().BeEquivalentTo("local", "ollama", "openai", "anthropic");
    }

    [Fact]
    public async Task A_fresh_install_asks_Anthropic_for_the_provider_default_model()
    {
        // AppSettings defaulted to a retired, dated model id while the provider, the Settings
        // page and its placeholder had moved on, so a new install requested the old model.
        var fresh = new AppSettings { StoragePath = _storage, ActiveProviderId = "ollama", AnthropicApiKey = "sk-ant-test" };
        fresh.AnthropicDefaultModel.Should().Be(AnthropicProvider.DefaultModelId);

        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(fresh);
        var service = CreateService();
        await service.InitializeAsync();

        service.GetDefaultModelId("anthropic").Should().Be(AnthropicProvider.DefaultModelId);
    }

    [Fact]
    public async Task Cloud_and_Ollama_providers_receive_the_cost_tracker()
    {
        // Only the active provider (a connected fake) is contacted; the real cloud and Ollama
        // providers are constructed but never called, so no request leaves the machine.
        var tracker = new Mock<ICostTracker>().Object;
        var service = new AiService(_settingsService.Object, tracker)
        {
            ProviderFactoryOverride = (id, _) => id == "local" ? Fake("local").Object : null
        };
        _services.Add(service);
        _settings.ActiveProviderId = "local";

        await service.InitializeAsync();

        foreach (var id in new[] { "ollama", "openai", "anthropic" })
        {
            var provider = service.GetProvider(id);
            provider.Should().NotBeNull();
            var field = provider!.GetType().GetField("_costTracker",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.Should().NotBeNull($"{provider.GetType().Name} records usage through _costTracker");
            field!.GetValue(provider).Should().BeSameAs(tracker, $"{id} must record its token usage");
        }
    }
}

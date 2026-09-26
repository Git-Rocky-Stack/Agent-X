using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.AI.Routing;

public class ModelRouterServiceTests
{
    private readonly Mock<IAiService> _mockAiService;
    private readonly Mock<ITaskTypeDetector> _mockDetector;
    private readonly ModelRouterService _router;

    public ModelRouterServiceTests()
    {
        _mockAiService = new Mock<IAiService>();
        _mockDetector = new Mock<ITaskTypeDetector>();

        _mockAiService.Setup(s => s.GetDefaultModelId(It.IsAny<string>())).Returns((string id) => id switch
        {
            "ollama" => "llama3.2",
            "openai" => "gpt-4o-mini",
            "anthropic" => "claude-sonnet-5",
            "local" => "llama-3.2-3b-instruct-q4_k_m.gguf",
            _ => string.Empty
        });

        // Default: a connected Ollama provider is active
        Register("ollama", available: true);
        Activate("ollama", "llama3.2");

        _router = new ModelRouterService(
            _mockAiService.Object,
            _mockDetector.Object,
            Serilog.Log.Logger);
    }

    private void Register(string providerId, bool available)
    {
        var provider = new Mock<IAiProvider>();
        provider.Setup(p => p.ProviderId).Returns(providerId);
        _mockAiService.Setup(s => s.GetProvider(providerId)).Returns(provider.Object);
        _mockAiService.Setup(s => s.IsProviderAvailableAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(available);
    }

    private void Activate(string providerId, string modelId)
    {
        var provider = new Mock<IAiProvider>();
        provider.Setup(p => p.ProviderId).Returns(providerId);
        _mockAiService.Setup(s => s.ActiveProvider).Returns(provider.Object);
        _mockAiService.Setup(s => s.ActiveModelId).Returns(modelId);
    }

    // Active profile

    [Fact]
    public void ActiveProfile_DefaultsToBalanced()
    {
        _router.ActiveProfile.Should().BeSameAs(RoutingProfile.Balanced);
    }

    [Fact]
    public void SetActiveProfile_WithProfileObject_UpdatesActiveProfile()
    {
        _router.SetActiveProfile(RoutingProfile.CostOptimized);
        _router.ActiveProfile.Should().BeSameAs(RoutingProfile.CostOptimized);
    }

    [Fact]
    public void SetActiveProfile_WithProfileId_UpdatesActiveProfile()
    {
        _router.SetActiveProfile("quality-optimized");
        _router.ActiveProfile.Should().BeSameAs(RoutingProfile.QualityOptimized);
    }

    [Fact]
    public void SetActiveProfile_WithUnknownId_FallsBackToBalanced()
    {
        _router.SetActiveProfile("nonexistent-profile");
        _router.ActiveProfile.Should().BeSameAs(RoutingProfile.Balanced);
    }

    [Fact]
    public void SetActiveProfile_NullProfile_ThrowsArgumentNullException()
    {
        var act = () => _router.SetActiveProfile((RoutingProfile)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Saved_profile_is_used_until_another_one_is_selected()
    {
        var settings = new AppSettings { ActiveRoutingProfileId = "quality-optimized" };
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(settings);
        var router = new ModelRouterService(_mockAiService.Object, _mockDetector.Object, Serilog.Log.Logger, settingsService.Object);

        (await router.RouteAsync("hello", TaskType.Chat)).Profile.Should().BeSameAs(RoutingProfile.QualityOptimized);
        router.ActiveProfile.Should().BeSameAs(RoutingProfile.QualityOptimized);

        settings.ActiveRoutingProfileId = "cost-optimized";
        (await router.RouteAsync("hello", TaskType.Chat)).Profile.Should().BeSameAs(RoutingProfile.CostOptimized,
            "a saved change applies without a restart");

        router.SetActiveProfile(RoutingProfile.Balanced);
        (await router.RouteAsync("hello", TaskType.Chat)).Profile.Should().BeSameAs(RoutingProfile.Balanced);
    }

    // Routing decisions

    [Fact]
    public async Task RouteAsync_WithCostOptimized_RoutesToLocalForChat()
    {
        _router.SetActiveProfile(RoutingProfile.CostOptimized);
        _mockDetector.Setup(d => d.Detect("Hello")).Returns(TaskType.Chat);

        var decision = await _router.RouteAsync("Hello");

        decision.ProviderId.Should().Be("ollama");
        decision.ModelId.Should().Be("llama3.2");
        decision.TaskType.Should().BeSameAs(TaskType.Chat);
        decision.Profile.Should().BeSameAs(RoutingProfile.CostOptimized);
        decision.Reason.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RouteAsync_WithQualityOptimized_RoutesToCloudForAnalysis()
    {
        _router.SetActiveProfile(RoutingProfile.QualityOptimized);
        _mockDetector.Setup(d => d.Detect("Analyze this data")).Returns(TaskType.Analysis);
        Register("openai", available: true);

        var decision = await _router.RouteAsync("Analyze this data");

        decision.ProviderId.Should().Be("openai");
        decision.ModelId.Should().Be("gpt-4o-mini", "a provider that is not active gets its own configured model");
        decision.TaskType.Should().BeSameAs(TaskType.Analysis);
        decision.Profile.Should().BeSameAs(RoutingProfile.QualityOptimized);
    }

    [Fact]
    public async Task RouteAsync_WithBalanced_UsesTaskOverrides()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);
        _mockDetector.Setup(d => d.Detect("Write code")).Returns(TaskType.Code);
        Register("anthropic", available: true);

        var decision = await _router.RouteAsync("Write code");

        decision.TaskType.Should().BeSameAs(TaskType.Code);
        decision.Reason.Should().Contain("overrides");
        decision.Reason.Should().Contain("code");
    }

    [Fact]
    public async Task RouteAsync_WithTaskTypeOverride_SkipsDetection()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);

        var decision = await _router.RouteAsync("some prompt", TaskType.Embedding);

        decision.TaskType.Should().BeSameAs(TaskType.Embedding);
        decision.ProviderId.Should().Be("ollama"); // Embedding always prefers local
        _mockDetector.Verify(d => d.Detect(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RouteAsync_FiresDecisionMadeEvent()
    {
        _mockDetector.Setup(d => d.Detect("Hello")).Returns(TaskType.Chat);

        RoutingDecision? firedDecision = null;
        _router.DecisionMade += (_, d) => firedDecision = d;

        var decision = await _router.RouteAsync("Hello");

        firedDecision.Should().NotBeNull();
        firedDecision!.ProviderId.Should().Be(decision.ProviderId);
        firedDecision.TaskType.Should().Be(decision.TaskType);
    }

    [Fact]
    public async Task RouteAsync_DecisionHasTimestamp()
    {
        _mockDetector.Setup(d => d.Detect("Hello")).Returns(TaskType.Chat);

        var decision = await _router.RouteAsync("Hello");

        decision.DecidedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RouteAsync_EmbeddingTaskAlwaysRoutesToLocal()
    {
        _router.SetActiveProfile(RoutingProfile.QualityOptimized);
        _mockDetector.Setup(d => d.Detect("Embed this")).Returns(TaskType.Embedding);
        Register("openai", available: true);

        var decision = await _router.RouteAsync("Embed this");

        decision.ProviderId.Should().Be("ollama");
        decision.TaskType.Should().BeSameAs(TaskType.Embedding);
    }

    [Fact]
    public async Task RouteAsync_ProviderUnavailable_FallsBackToLocal()
    {
        _router.SetActiveProfile(RoutingProfile.QualityOptimized);
        _mockDetector.Setup(d => d.Detect("Analyze")).Returns(TaskType.Analysis);
        Register("openai", available: false);

        var decision = await _router.RouteAsync("Analyze");

        decision.ProviderId.Should().Be("ollama", "the active provider is kept when the requested one is down");
        decision.ModelId.Should().Be("llama3.2");
        decision.Reason.Should().Contain("No preferred provider is available");
    }

    [Fact]
    public async Task RouteAsync_SimpleChat_CostOptimized_RoutesLocal()
    {
        _router.SetActiveProfile(RoutingProfile.CostOptimized);
        _mockDetector.Setup(d => d.Detect("Hello")).Returns(TaskType.Chat);

        var decision = await _router.RouteAsync("Hello");

        decision.ProviderId.Should().Be("ollama");
        decision.Profile.Should().BeSameAs(RoutingProfile.CostOptimized);
    }

    [Fact]
    public async Task RouteAsync_CodeWithBalanced_UsesAnthropicOverride()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);
        _mockDetector.Setup(d => d.Detect("Code this")).Returns(TaskType.Code);
        Register("anthropic", available: true);

        var decision = await _router.RouteAsync("Code this");

        decision.ProviderId.Should().Be("anthropic");
        decision.ModelId.Should().Be("claude-sonnet-5", "the model comes from the Anthropic settings, not the active Ollama model");
        decision.Reason.Should().Contain("overrides");
    }

    // Side effects and fallbacks

    [Fact]
    public async Task Routing_never_switches_the_active_provider_or_model()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);
        Register("anthropic", available: true);
        Register("openai", available: false);

        await _router.RouteAsync("x", TaskType.Code);
        await _router.RouteAsync("x", TaskType.Analysis);
        await _router.RouteAsync("x", TaskType.Chat);

        _mockAiService.Verify(s => s.SwitchProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockAiService.Verify(s => s.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Local_preference_uses_the_built_in_provider_when_Ollama_is_down()
    {
        _router.SetActiveProfile(RoutingProfile.CostOptimized);
        Register("ollama", available: false);
        Register("local", available: true);

        var decision = await _router.RouteAsync("Hello", TaskType.Chat);

        decision.ProviderId.Should().Be("local");
        decision.ModelId.Should().Be("llama-3.2-3b-instruct-q4_k_m.gguf");
    }

    [Fact]
    public async Task Local_preference_never_falls_back_to_a_cloud_provider_the_user_did_not_select()
    {
        _router.SetActiveProfile(RoutingProfile.CostOptimized);
        Activate("local", "llama-3.2-3b-instruct-q4_k_m.gguf");
        Register("local", available: false);
        Register("ollama", available: false);
        Register("openai", available: true);
        Register("anthropic", available: true);

        var decision = await _router.RouteAsync("Summarize my private notes", TaskType.Summarization);

        decision.ProviderId.Should().Be("local");
        _mockAiService.Verify(s => s.IsProviderAvailableAsync("openai", It.IsAny<CancellationToken>()), Times.Never);
        _mockAiService.Verify(s => s.IsProviderAvailableAsync("anthropic", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unavailable_override_falls_back_to_the_profiles_local_preference()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);
        Activate("local", "llama-3.2-3b-instruct-q4_k_m.gguf");
        Register("local", available: true);

        // Balanced routes code to Anthropic, which has no API key (not registered).
        var decision = await _router.RouteAsync("Refactor this method", TaskType.Code);

        decision.ProviderId.Should().Be("local");
        decision.Reason.Should().Contain("'anthropic' is unavailable");
    }

    [Fact]
    public async Task Cloud_preference_keeps_the_active_cloud_provider_first()
    {
        var qualityEverywhere = new RoutingProfile { Id = "quality-everywhere", PreferLocalFirst = false };
        _router.SetActiveProfile(qualityEverywhere);
        Activate("anthropic", "claude-opus-5-5");
        Register("anthropic", available: true);
        Register("openai", available: true);

        var decision = await _router.RouteAsync("Compare these contracts", TaskType.Analysis);

        decision.ProviderId.Should().Be("anthropic");
        decision.ModelId.Should().Be("claude-opus-5-5", "the active provider keeps its active model");
    }

    [Fact]
    public async Task Unregistered_providers_are_skipped_without_a_connection_check()
    {
        _router.SetActiveProfile(RoutingProfile.QualityOptimized);

        await _router.RouteAsync("Analyze", TaskType.Analysis);

        _mockAiService.Verify(s => s.IsProviderAvailableAsync("openai", It.IsAny<CancellationToken>()), Times.Never);
        _mockAiService.Verify(s => s.IsProviderAvailableAsync("anthropic", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Uninitialized_ai_service_still_yields_a_decision()
    {
        _mockAiService.Setup(s => s.ActiveProvider).Throws(new InvalidOperationException("not initialized"));
        _mockAiService.Setup(s => s.GetDefaultModelId(It.IsAny<string>())).Throws(new InvalidOperationException("not initialized"));
        _mockAiService.Setup(s => s.GetProvider(It.IsAny<string>())).Returns((IAiProvider?)null);

        var decision = await _router.RouteAsync("Hello", TaskType.Chat);

        decision.ProviderId.Should().Be("ollama");
        decision.ModelId.Should().Be("llama3.2");
    }

    [Fact]
    public async Task Cancellation_during_an_availability_check_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _mockAiService.Setup(s => s.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _router.RouteAsync("Hello", TaskType.Chat, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

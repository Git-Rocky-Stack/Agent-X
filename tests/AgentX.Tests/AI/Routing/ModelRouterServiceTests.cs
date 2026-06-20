using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
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

        // Default: simulate a connected Ollama provider as active
        var mockProvider = new Mock<IAiProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns("ollama");
        mockProvider.Setup(p => p.IsAvailable).Returns(true);
        _mockAiService.Setup(s => s.ActiveProvider).Returns(mockProvider.Object);
        _mockAiService.Setup(s => s.ActiveModelId).Returns("llama3.2");
        _mockAiService.Setup(s => s.SwitchProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _router = new ModelRouterService(
            _mockAiService.Object,
            _mockDetector.Object,
            Serilog.Log.Logger);
    }

    // ── Active Profile Tests ────────────────────────────────────────

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

    // ── Routing Decision Tests ─────────────────────────────────────

    [Fact]
    public async Task RouteAsync_WithCostOptimized_RoutesToLocalForChat()
    {
        _router.SetActiveProfile(RoutingProfile.CostOptimized);
        _mockDetector.Setup(d => d.Detect("Hello")).Returns(TaskType.Chat);

        var decision = await _router.RouteAsync("Hello");

        decision.ProviderId.Should().Be("ollama");
        decision.TaskType.Should().BeSameAs(TaskType.Chat);
        decision.Profile.Should().BeSameAs(RoutingProfile.CostOptimized);
        decision.Reason.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RouteAsync_WithQualityOptimized_RoutesToCloudForAnalysis()
    {
        _router.SetActiveProfile(RoutingProfile.QualityOptimized);
        _mockDetector.Setup(d => d.Detect("Analyze this data")).Returns(TaskType.Analysis);

        // Set up OpenAI provider mock for switch
        _mockAiService.Setup(s => s.SwitchProviderAsync("openai", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var decision = await _router.RouteAsync("Analyze this data");

        decision.TaskType.Should().BeSameAs(TaskType.Analysis);
        decision.Profile.Should().BeSameAs(RoutingProfile.QualityOptimized);
    }

    [Fact]
    public async Task RouteAsync_WithBalanced_UsesTaskOverrides()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);
        _mockDetector.Setup(d => d.Detect("Write code")).Returns(TaskType.Code);

        // Balanced profile overrides "code" → "anthropic"
        _mockAiService.Setup(s => s.SwitchProviderAsync("anthropic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var decision = await _router.RouteAsync("Write code");

        decision.TaskType.Should().BeSameAs(TaskType.Code);
        decision.Reason.Should().Contain("overrides");
        decision.Reason.Should().Contain("code");
    }

    [Fact]
    public async Task RouteAsync_WithTaskTypeOverride_SkipsDetection()
    {
        _router.SetActiveProfile(RoutingProfile.Balanced);
        // Should NOT call detector when override is provided

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

        var decision = await _router.RouteAsync("Embed this");

        decision.ProviderId.Should().Be("ollama");
        decision.TaskType.Should().BeSameAs(TaskType.Embedding);
    }

    [Fact]
    public async Task RouteAsync_ProviderUnavailable_FallsBackToLocal()
    {
        _router.SetActiveProfile(RoutingProfile.QualityOptimized);
        _mockDetector.Setup(d => d.Detect("Analyze")).Returns(TaskType.Analysis);

        // Active provider is NOT openai (so it tries to switch)
        var mockOllamaProvider = new Mock<IAiProvider>();
        mockOllamaProvider.Setup(p => p.ProviderId).Returns("ollama");
        _mockAiService.Setup(s => s.ActiveProvider).Returns(mockOllamaProvider.Object);
        _mockAiService.Setup(s => s.ActiveModelId).Returns("llama3.2");

        // OpenAI switch fails (provider unavailable)
        _mockAiService.Setup(s => s.SwitchProviderAsync("openai", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Ollama fallback succeeds
        _mockAiService.Setup(s => s.SwitchProviderAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var decision = await _router.RouteAsync("Analyze");

        // Provider should be ollama (local fallback)
        decision.ProviderId.Should().Be("ollama");
        decision.Reason.Should().NotBeEmpty();
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

        _mockAiService.Setup(s => s.SwitchProviderAsync("anthropic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var decision = await _router.RouteAsync("Code this");

        decision.ProviderId.Should().Be("anthropic");
        decision.Reason.Should().Contain("overrides");
    }
}

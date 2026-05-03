using AgentX.Core.AI;
using AgentX.Core.AI.Agents;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Agents;

public sealed class MultiAgentOrchestratorTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly ILogger _logger = Log.ForContext<MultiAgentOrchestratorTests>();

    [Fact]
    public async Task RunParallelAsync_WithDiverseAgentOutputs_ReturnsActionableSynthesisConsensusAndDisagreements()
    {
        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChatMessage> _, string? systemPrompt, ChatOptions? _, CancellationToken _) =>
                systemPrompt switch
                {
                    "security specialist" =>
                        "Recommend a phased launch with audit logging and rollback controls. However, avoid enabling beta automation until permission checks are verified.",
                    "growth specialist" =>
                        "Recommend a phased launch with onboarding messaging and success metrics. The automation can expand after risk checks are passed.",
                    "operations specialist" =>
                        "Recommend a phased launch with runbooks, health dashboards, and rollback owners. Risk is weekend coverage gaps.",
                    _ => "Recommend a phased launch."
                });

        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var result = await sut.RunParallelAsync(
            "Prepare the launch plan",
            [
                new AgentRole
                {
                    Id = "security",
                    Name = "Security",
                    Expertise = "Risk controls",
                    SystemPrompt = "security specialist",
                    Temperature = 0.2,
                },
                new AgentRole
                {
                    Id = "growth",
                    Name = "Growth",
                    Expertise = "GTM sequencing",
                    SystemPrompt = "growth specialist",
                    Temperature = 0.5,
                },
                new AgentRole
                {
                    Id = "operations",
                    Name = "Operations",
                    Expertise = "Operational rollout",
                    SystemPrompt = "operations specialist",
                    Temperature = 0.4,
                },
            ]);

        result.Outputs.Should().HaveCount(3);
        result.Consensus.Should().NotBeNullOrWhiteSpace();
        result.Consensus.Should().Contain("phased launch");
        result.Disagreements.Should().NotBeEmpty();
        result.Disagreements.Should().Contain(disagreement => disagreement.Contains("Security", StringComparison.Ordinal));
        result.CombinedOutput.Should().Contain("Multi-Agent Synthesis");
        result.CombinedOutput.Should().Contain("Consensus");
        result.CombinedOutput.Should().Contain("Trade-offs");
        result.CombinedOutput.Should().Contain("Security");
        result.CombinedOutput.Should().NotContain("\n\n---\n\n");
    }

    [Fact]
    public async Task RunDebateAsync_WithMultipleRounds_ReturnsStructuredSynthesisBeyondTranscriptDigest()
    {
        var responseCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChatMessage> _, string? systemPrompt, ChatOptions? _, CancellationToken _) =>
            {
                var key = systemPrompt ?? string.Empty;
                responseCounts.TryGetValue(key, out var count);
                responseCounts[key] = count + 1;

                return (key, count) switch
                {
                    ("privacy advocate", 0) =>
                        "User data export should ship only with explicit consent, audit logs, and limited scopes.",
                    ("product lead", 0) =>
                        "Export should ship in onboarding because users need continuity, but it must include clear consent and cancellation.",
                    ("privacy advocate", _) =>
                        "I can support a controlled launch if consent is mandatory and administrators see audit logs. The risk is silent broad access.",
                    ("product lead", _) =>
                        "Agree on mandatory consent and audit logs. I disagree with delaying onboarding because it blocks adoption.",
                    _ => "Mandatory consent and audit logs are required."
                };
            });

        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var result = await sut.RunDebateAsync(
            "Should user data export be enabled during onboarding?",
            [
                new AgentRole
                {
                    Id = "privacy",
                    Name = "Privacy Advocate",
                    Expertise = "Privacy controls",
                    SystemPrompt = "privacy advocate",
                    Temperature = 0.2,
                },
                new AgentRole
                {
                    Id = "product",
                    Name = "Product Lead",
                    Expertise = "Product adoption",
                    SystemPrompt = "product lead",
                    Temperature = 0.4,
                },
            ],
            rounds: 2);

        result.Rounds.Should().HaveCount(2);
        result.Synthesis.Should().Contain("Debate Synthesis");
        result.Synthesis.Should().Contain("Consensus");
        result.Synthesis.Should().Contain("Open Disagreements");
        result.Synthesis.Should().Contain("mandatory consent");
        result.Synthesis.Should().Contain("delaying onboarding");
        result.Synthesis.Should().NotStartWith("Debate on:");
        result.Synthesis.Should().NotContain("Key positions:");
        result.WinningPerspective.Should().NotBeNullOrWhiteSpace();

        _aiService.Verify(service => service.ChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }
}

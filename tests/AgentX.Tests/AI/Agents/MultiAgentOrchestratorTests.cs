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

    private static IReadOnlyList<AgentRole> ThreeAgents() =>
    [
        new AgentRole { Id = "a", Name = "Alpha", SystemPrompt = "alpha" },
        new AgentRole { Id = "b", Name = "Beta", SystemPrompt = "beta" },
        new AgentRole { Id = "c", Name = "Gamma", SystemPrompt = "gamma" },
    ];

    private void RespondBySystemPrompt(Func<string?, CancellationToken, Task<string>> respond) =>
        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> _, string? systemPrompt, ChatOptions? _, CancellationToken ct) =>
                respond(systemPrompt, ct));

    [Theory]
    [InlineData(OrchestratorStrategy.Sequential)]
    [InlineData(OrchestratorStrategy.Parallel)]
    [InlineData(OrchestratorStrategy.Debate)]
    [InlineData(OrchestratorStrategy.DivideAndConquer)]
    [InlineData(OrchestratorStrategy.GenerateCritiqueRefine)]
    public async Task Cancellation_propagates_instead_of_becoming_a_failed_result(OrchestratorStrategy strategy)
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        RespondBySystemPrompt((_, ct) =>
        {
            Interlocked.Increment(ref calls);
            cts.Cancel(); // the user presses Stop during the first agent call
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("unreachable");
        });
        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var act = () => sut.RunAsync("task", ThreeAgents(), strategy, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        if (strategy != OrchestratorStrategy.Parallel) // parallel agents all start at once
            calls.Should().Be(1, "no further agent is started after cancellation");
    }

    [Fact]
    public async Task Parallel_run_with_a_failed_agent_reports_it_and_marks_the_answer_incomplete()
    {
        RespondBySystemPrompt((systemPrompt, _) => systemPrompt == "beta"
            ? throw new HttpRequestException("Anthropic API request failed (529): overloaded_error")
            : Task.FromResult($"Recommend a phased launch from {systemPrompt}."));
        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var result = await sut.RunAsync("Plan the launch", ThreeAgents(), OrchestratorStrategy.Parallel);

        result.IsSuccess.Should().BeTrue("two agents still produced an answer");
        result.Contributions.Should().HaveCount(2);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Beta:").And.Contain("overloaded_error");
        result.FinalAnswer.Should().Contain("Note: 1 of 3 agent responses failed").And.Contain("- Beta:");
    }

    [Fact]
    public async Task Provider_timeout_is_an_agent_failure_not_a_cancellation()
    {
        RespondBySystemPrompt((systemPrompt, _) => systemPrompt == "gamma"
            ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")
            : Task.FromResult("An answer."));
        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var result = await sut.RunAsync("task", ThreeAgents(), OrchestratorStrategy.Sequential);

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Gamma:");
        result.FinalAnswer.Should().StartWith("An answer.").And.Contain("Note: 1 of 3");
    }

    [Fact]
    public async Task Debate_records_failed_agents()
    {
        RespondBySystemPrompt((systemPrompt, _) => systemPrompt == "alpha"
            ? throw new InvalidOperationException("model not found")
            : Task.FromResult("Mandatory consent and audit logs are required."));
        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var result = await sut.RunAsync("Should export ship?", ThreeAgents(), OrchestratorStrategy.Debate);

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().HaveCount(2, "Alpha failed in both rounds");
        result.Errors.Should().OnlyContain(error => error.StartsWith("Alpha (round"));
        result.FinalAnswer.Should().Contain("Note: 2 of 6 agent responses failed");
    }

    [Fact]
    public async Task Divide_and_conquer_without_any_sub_task_result_is_not_a_success()
    {
        RespondBySystemPrompt((systemPrompt, _) => systemPrompt == "You are a task planner. Divide complex work among specialized agents."
            ? Task.FromResult("1. Research\n2. Review\n3. Summarize")
            : throw new HttpRequestException("connection refused"));
        var sut = new MultiAgentOrchestrator(_aiService.Object, _logger);

        var result = await sut.RunAsync("task", ThreeAgents(), OrchestratorStrategy.DivideAndConquer);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCount(4);
        result.Errors.Should().Contain("No sub-task produced a result.");
    }

    [Fact]
    public void Numbered_sub_tasks_are_split_on_every_number_and_keep_their_digits()
    {
        var subTasks = MultiAgentOrchestrator.ParseSubTasks(
            "Here is the plan:\n1. Compare the 3 vendors (Researcher)\n2. List 2 risks per vendor (Critic)\n3) Recommend 1 vendor\n10. Archive the notes");

        subTasks.Should().Equal(
            "Compare the 3 vendors (Researcher)",
            "List 2 risks per vendor (Critic)",
            "Recommend 1 vendor",
            "Archive the notes");
    }

    [Fact]
    public void Nested_bullets_and_wrapped_lines_stay_with_their_sub_task()
    {
        var subTasks = MultiAgentOrchestrator.ParseSubTasks(
            "- Researcher: gather usage data\n  - include the 2024 numbers\n  continue with churn\n- Critic: review the findings\r\n* Synthesizer: write it up");

        subTasks.Should().Equal(
            "Researcher: gather usage data include the 2024 numbers continue with churn",
            "Critic: review the findings",
            "Synthesizer: write it up");
    }

    [Fact]
    public void Part_headings_start_sub_tasks_and_plain_text_stays_whole()
    {
        MultiAgentOrchestrator.ParseSubTasks("**Part 1: Market research** (Researcher)\nInvestigate the market.\n**Part 2: Risks** (Critic)")
            .Should().Equal("Part 1: Market research (Researcher) Investigate the market.", "Part 2: Risks (Critic)");

        MultiAgentOrchestrator.ParseSubTasks("Just do the whole thing.")
            .Should().Equal("Just do the whole thing.");

        MultiAgentOrchestrator.ParseSubTasks("\u2022 First part\n\u2022 Second part")
            .Should().Equal("First part", "Second part");
    }
}

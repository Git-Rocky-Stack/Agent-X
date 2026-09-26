using AgentX.Core.AI.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class CostTrackerTests
{
    [Fact]
    public void Versioned_ids_are_priced_by_the_longest_matching_entry()
    {
        var tracker = new CostTracker();

        // gpt-4o-mini-2024-07-18 contains "gpt-4o" too; it must not be billed at gpt-4o rates.
        tracker.RecordUsage("gpt-4o-mini-2024-07-18", "openai", 1_000_000, 0);

        tracker.GetTotalCostUsd().Should().BeApproximately(0.15, 1e-9);
    }

    [Fact]
    public void Haiku_4_5_uses_its_own_price_not_the_3_5_price()
    {
        var tracker = new CostTracker();

        tracker.RecordUsage("claude-haiku-4-5-20251001", "anthropic", 1_000_000, 1_000_000);

        tracker.GetTotalCostUsd().Should().BeApproximately(1.0 + 5.0, 1e-9);
    }

    [Theory]
    [InlineData("claude-sonnet-5", 2.0, 10.0)]
    [InlineData("claude-opus-5-5", 4.0, 20.0)]
    [InlineData("claude-opus-5", 5.0, 25.0)]
    [InlineData("claude-opus-4-1-20250805", 15.0, 75.0)]
    [InlineData("claude-sonnet-4-5-20250929", 3.0, 15.0)]
    public void Current_Anthropic_models_are_priced_per_million_tokens(string model, double input, double output)
    {
        var tracker = new CostTracker();

        tracker.RecordUsage(model, "anthropic", 1_000_000, 1_000_000);

        tracker.GetTotalCostUsd().Should().BeApproximately(input + output, 1e-9);
    }

    [Fact]
    public void Prompt_cache_writes_and_reads_are_priced_separately()
    {
        var tracker = new CostTracker();

        // Sonnet 5: $2 input -> writes at 1.25x ($2.50/M), reads at 0.1x ($0.20/M).
        tracker.RecordUsage("claude-sonnet-5", "anthropic", 0, 0, cacheCreationInputTokens: 1_000_000, cacheReadInputTokens: 1_000_000);

        tracker.GetTotalCostUsd().Should().BeApproximately(2.5 + 0.2, 1e-9);
        tracker.GetTotalInputTokens().Should().Be(2_000_000);
    }

    [Theory]
    [InlineData("gpt-4o", "ollama")]
    [InlineData("llama3.2", "ollama")]
    [InlineData("llama-3.2-3b-instruct-q4_k_m.gguf", "local")]
    [InlineData("some-new-cloud-model", "openai")]
    public void Local_and_unknown_models_cost_nothing(string model, string provider)
    {
        var tracker = new CostTracker();

        tracker.RecordUsage(model, provider, 10_000, 10_000);

        tracker.GetTotalCostUsd().Should().Be(0);
        tracker.GetTotalOutputTokens().Should().Be(10_000, "token counts are still tracked");
    }
}

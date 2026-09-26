using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class ContextWindowManagerTests
{
    private readonly ContextWindowManager _manager = new(Log.Logger);

    private static string Cjk(int count) => new((char)0x4E2D, count);

    [Fact]
    public void English_estimate_stays_at_four_characters_per_token()
    {
        _manager.EstimateTokenCount(new string('a', 40)).Should().Be(10);
        _manager.EstimateTokenCount(new string('a', 41)).Should().Be(11);
    }

    [Fact]
    public void Cjk_text_is_not_estimated_at_four_characters_per_token()
    {
        // 60 ideographs: the flat heuristic said 15 tokens; real tokenizers need 60 or more.
        _manager.EstimateTokenCount(Cjk(60)).Should().Be(100);
    }

    [Fact]
    public async Task Cjk_history_is_trimmed_to_the_window()
    {
        var messages = Enumerable.Range(0, 20)
            .Select(i => i % 2 == 0 ? ChatMessage.User(Cjk(60)) : ChatMessage.Assistant(Cjk(60)))
            .ToList();

        var fitted = await _manager.FitToContextWindowAsync(messages, maxTokens: 1000, reserveForResponse: 200);

        fitted.Count.Should().BeLessThan(20, "twenty 60-ideograph messages need about 2000 tokens");
        _manager.EstimateTokenCount(fitted).Should().BeLessThanOrEqualTo(800);
        fitted[^1].Should().BeSameAs(messages[^1]);
    }

    [Fact]
    public async Task Truncating_a_cjk_message_removes_characters_in_proportion_to_its_tokens()
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.User(Cjk(600)),
            ChatMessage.Assistant("ok"),
            ChatMessage.User("next"),
            ChatMessage.Assistant("sure"),
        };

        var fitted = await _manager.FitToContextWindowAsync(messages, maxTokens: 700, reserveForResponse: 100);

        fitted.Should().HaveCount(4);
        fitted[0].Content.Should().StartWith("...");
        fitted[0].Content.Length.Should().BeGreaterThan(300, "only the excess is cut, not four characters per token");
        _manager.EstimateTokenCount(fitted).Should().BeLessThanOrEqualTo(605);
    }
}

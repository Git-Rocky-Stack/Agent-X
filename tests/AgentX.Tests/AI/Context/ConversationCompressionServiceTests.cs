using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Context;

public sealed class ConversationCompressionServiceTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly IContextWindowManager _contextWindowManager = new ContextWindowManager(Log.Logger);
    private readonly ILogger _logger = Log.ForContext<ConversationCompressionService>();

    [Fact]
    public async Task CompressAsync_WithTooFewMessages_SkipsCompression()
    {
        var sut = new ConversationCompressionService(_aiService.Object, _contextWindowManager, _logger);

        var result = await sut.CompressAsync(
            new ConversationCompressionRequest
            {
                CurrentQuery = "why is startup failing",
                OverflowMessages =
                [
                    new IndexedChatMessage(0, ChatMessage.User("Only one older message exists here."))
                ]
            });

        result.WasSkipped.Should().BeTrue();
        result.SkipReason.Should().Be("overflow_too_small");
    }

    [Fact]
    public async Task CompressAsync_TrimsSummaryToBudget()
    {
        var sut = new ConversationCompressionService(_aiService.Object, _contextWindowManager, _logger);

        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new string('x', 400));

        var result = await sut.CompressAsync(
            new ConversationCompressionRequest
            {
                CurrentQuery = "why is startup failing",
                OverflowMessages =
                [
                    new IndexedChatMessage(0, ChatMessage.User("The migration started failing after the last schema change.")),
                    new IndexedChatMessage(1, ChatMessage.Assistant("We suspected the migration runner and a locked SQLite file.")),
                    new IndexedChatMessage(2, ChatMessage.User("The issue only reproduces during app startup.")),
                ],
                MaxSummaryTokens = 24
            });

        result.WasSkipped.Should().BeTrue();
        result.SkipReason.Should().Be("summary_budget_too_small");
    }

    private readonly List<string> _prompts = new();

    private ConversationCompressionService CreateRecording(Func<int, string>? summaryForCall = null)
    {
        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChatMessage> messages, string? _, ChatOptions? _, CancellationToken _) =>
            {
                _prompts.Add(messages[0].Content);
                return summaryForCall?.Invoke(_prompts.Count) ?? $"Summary number {_prompts.Count} of the earlier discussion.";
            });

        return new ConversationCompressionService(_aiService.Object, _contextWindowManager, _logger);
    }

    // Ten 500-character messages: far more than the 3200-character transcript window.
    private static IReadOnlyList<IndexedChatMessage> LongOverflow(string tag = "") => Enumerable.Range(0, 10)
        .Select(i => new IndexedChatMessage(i, i % 2 == 0
            ? ChatMessage.User($"MARK{i:D2}{tag} " + new string('u', 490))
            : ChatMessage.Assistant($"MARK{i:D2}{tag} " + new string('a', 490))))
        .ToList();

    [Fact]
    public async Task Transcript_keeps_the_newest_overflow_messages_in_chronological_order()
    {
        var sut = CreateRecording();

        await sut.CompressAsync(new ConversationCompressionRequest
        {
            CurrentQuery = "q",
            OverflowMessages = LongOverflow(),
            MaxSummaryTokens = 200
        });

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.Should().Contain("MARK09").And.Contain("MARK08");
        prompt.Should().NotContain("MARK00", "the oldest messages are dropped first when the window is full");
        prompt.IndexOf("MARK08", StringComparison.Ordinal).Should().BeLessThan(prompt.IndexOf("MARK09", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Same_overflow_is_summarized_once_whatever_the_question()
    {
        var sut = CreateRecording();
        var overflow = LongOverflow();

        var first = await sut.CompressAsync(new ConversationCompressionRequest
        {
            CurrentQuery = "why does startup fail",
            OverflowMessages = overflow,
            MaxSummaryTokens = 200
        });
        var second = await sut.CompressAsync(new ConversationCompressionRequest
        {
            CurrentQuery = "and what about the migration?",
            OverflowMessages = overflow,
            MaxSummaryTokens = 200
        });

        _prompts.Should().ContainSingle("the summary does not depend on the question, so it is reused");
        _prompts[0].Should().NotContain("why does startup fail");
        second.Summary.Should().Be(first.Summary);

        await sut.CompressAsync(new ConversationCompressionRequest
        {
            CurrentQuery = "q",
            OverflowMessages = LongOverflow(tag: "-changed"),
            MaxSummaryTokens = 200
        });
        _prompts.Should().HaveCount(2, "different overflow content needs a new summary");
    }

    [Fact]
    public async Task Cached_summary_is_trimmed_to_a_smaller_budget()
    {
        var sut = CreateRecording(_ => new string('s', 600));
        var overflow = LongOverflow();

        await sut.CompressAsync(new ConversationCompressionRequest { OverflowMessages = overflow, MaxSummaryTokens = 400 });
        var small = await sut.CompressAsync(new ConversationCompressionRequest { OverflowMessages = overflow, MaxSummaryTokens = 40 });

        _prompts.Should().ContainSingle();
        small.WasSkipped.Should().BeFalse();
        small.EstimatedSummaryTokens.Should().BeLessThanOrEqualTo(40);
    }

    [Fact]
    public async Task Summary_cache_is_bounded()
    {
        var sut = CreateRecording();

        for (var i = 0; i < 40; i++)
        {
            await sut.CompressAsync(new ConversationCompressionRequest
            {
                OverflowMessages = LongOverflow(tag: $"-{i}"),
                MaxSummaryTokens = 200
            });
        }

        sut.CachedSummaryCount.Should().BeLessThanOrEqualTo(32);
    }
}

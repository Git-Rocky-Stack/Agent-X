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
}

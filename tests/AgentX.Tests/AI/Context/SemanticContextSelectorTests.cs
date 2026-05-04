using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Context;

public sealed class SemanticContextSelectorTests
{
    private readonly Mock<IEmbeddingService> _embeddingService = new();
    private readonly IContextWindowManager _contextWindowManager = new ContextWindowManager(Log.Logger);
    private readonly ILogger _logger = Log.ForContext<SemanticContextSelector>();
    private readonly Mock<IRagConfiguration> _ragConfiguration = new();

    public SemanticContextSelectorTests()
    {
        // Setup default configuration values
        _ragConfiguration.Setup(c => c.SemanticWeight).Returns(0.68);
        _ragConfiguration.Setup(c => c.LexicalWeight).Returns(0.22);
        _ragConfiguration.Setup(c => c.RecencyWeight).Returns(0.10);
    }

    [Fact]
    public async Task SelectRelevantContextAsync_PrefersSemanticMatchesOverIrrelevantMessages()
    {
        var sut = new SemanticContextSelector(_embeddingService.Object, _contextWindowManager, _logger, _ragConfiguration.Object);

        _embeddingService
            .Setup(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1f, 0f });

        _embeddingService
            .Setup(service => service.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]>
            {
                new[] { 0f, 1f },
                new[] { 0.95f, 0.05f },
                new[] { 0.9f, 0.1f },
                new[] { 0.1f, 0.9f }
            });

        var result = await sut.SelectRelevantContextAsync(
            new ContextSelectionRequest
            {
                CurrentQuery = "why does the database migration fail on startup",
                CandidateMessages =
                [
                    new IndexedChatMessage(0, ChatMessage.User("We should plan a beach vacation next month.")),
                    new IndexedChatMessage(1, ChatMessage.User("The database migration fails on startup with a lock timeout.")),
                    new IndexedChatMessage(2, ChatMessage.Assistant("Check the migration runner and pending schema state.")),
                    new IndexedChatMessage(3, ChatMessage.User("Any lunch ideas for tomorrow?"))
                ],
                MaxTokenBudget = 34
            });

        result.SelectedMessages.Select(message => message.Index).Should().Contain(1);
        result.SelectedMessages.Select(message => message.Index).Should().NotContain(0);
        result.UsedLexicalFallback.Should().BeFalse();
    }

    [Fact]
    public async Task SelectRelevantContextAsync_UsesLexicalFallbackWhenEmbeddingFails()
    {
        var sut = new SemanticContextSelector(_embeddingService.Object, _contextWindowManager, _logger, _ragConfiguration.Object);

        _embeddingService
            .Setup(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding offline"));

        var result = await sut.SelectRelevantContextAsync(
            new ContextSelectionRequest
            {
                CurrentQuery = "database migration lock timeout",
                CandidateMessages =
                [
                    new IndexedChatMessage(0, ChatMessage.User("The database migration lock timeout happens during startup.")),
                    new IndexedChatMessage(1, ChatMessage.User("We should buy coffee beans this week."))
                ],
                MaxTokenBudget = 20
            });

        result.UsedLexicalFallback.Should().BeTrue();
        result.SelectedMessages.Should().ContainSingle();
        result.SelectedMessages[0].Index.Should().Be(0);
    }
}

using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Context;

public sealed class ContextAssemblyServiceTests
{
    private readonly IContextWindowManager _contextWindowManager = new ContextWindowManager(Log.Logger);
    private readonly Mock<ISemanticContextSelector> _selector = new();
    private readonly Mock<IConversationCompressionService> _compressionService = new();
    private readonly ILogger _logger = Log.ForContext<ContextAssemblyService>();

    [Fact]
    public async Task AssembleAsync_AppendsOverflowSummaryWhenCompressionSucceeds()
    {
        var sut = new ContextAssemblyService(
            _contextWindowManager,
            _selector.Object,
            _compressionService.Object,
            _logger);

        var messages = new[]
        {
            ChatMessage.User("Earlier context A."),
            ChatMessage.Assistant("Earlier context B."),
            ChatMessage.User("Earlier context C."),
            ChatMessage.Assistant("Earlier context D."),
            ChatMessage.User("Earlier context E."),
            ChatMessage.Assistant("Earlier context F."),
            ChatMessage.User("Recent anchor asking for the next debugging step."),
            ChatMessage.Assistant("Recent anchor response.")
        };

        _selector
            .Setup(selector => selector.SelectRelevantContextAsync(
                It.IsAny<ContextSelectionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextSelectionResult
            {
                SelectedMessages =
                [
                    new IndexedChatMessage(0, messages[0])
                ],
                OverflowMessages =
                [
                    new IndexedChatMessage(1, messages[1]),
                    new IndexedChatMessage(2, messages[2])
                ]
            });

        _compressionService
            .Setup(service => service.CompressAsync(
                It.IsAny<ConversationCompressionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationCompressionResult
            {
                Summary = "The earlier diagnosis focused on migration ordering.",
                EstimatedSummaryTokens = 4,
                SourceMessageCount = 1
            });

        var result = await sut.AssembleAsync(
            new ContextAssemblyRequest
            {
                CurrentQuery = "what should I debug next",
                SystemPrompt = "Base prompt",
                MemoryContext = "[Memory]",
                ConversationMessages = messages,
                ContextWindow = 90,
                ReserveForResponse = 16,
                RecentAnchorCount = 2
            });

        result.SystemPrompt.Should().Contain("Condensed Earlier Conversation Context");
        result.Messages.Should().HaveCount(3);
        result.Diagnostics.AddedOverflowSummary.Should().BeTrue();
    }

    [Fact]
    public async Task AssembleAsync_WhenSelectorThrows_UsesLegacyFallback()
    {
        var sut = new ContextAssemblyService(
            _contextWindowManager,
            _selector.Object,
            _compressionService.Object,
            _logger);

        _selector
            .Setup(selector => selector.SelectRelevantContextAsync(
                It.IsAny<ContextSelectionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("selector failed"));

        var messages = Enumerable.Range(0, 8)
            .Select(index => index % 2 == 0
                ? ChatMessage.User($"user message {index} with enough content to consume some budget")
                : ChatMessage.Assistant($"assistant message {index} with enough content to consume some budget"))
            .ToList();

        var result = await sut.AssembleAsync(
            new ContextAssemblyRequest
            {
                CurrentQuery = "why does startup fail",
                SystemPrompt = "Base prompt",
                ConversationMessages = messages,
                ContextWindow = 48,
                ReserveForResponse = 12,
                RecentAnchorCount = 2
            });

        result.Diagnostics.UsedLegacyFallback.Should().BeTrue();
        result.Messages.Should().NotBeEmpty();
        result.Messages.Last().Role.Should().Be("assistant");
    }
}

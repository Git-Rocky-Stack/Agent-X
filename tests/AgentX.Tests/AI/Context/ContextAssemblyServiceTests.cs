using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
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
    private readonly Mock<IConversationRecallService> _conversationRecallService = new();
    private readonly ILogger _logger = Log.ForContext<ContextAssemblyService>();

    [Fact]
    public async Task AssembleAsync_AppendsOverflowSummaryAndDurableRecallWhenAvailable()
    {
        var bulkyHistory = string.Join(' ', Enumerable.Repeat("migration-ordering startup validation diagnostics", 18));

        var sut = new ContextAssemblyService(
            _contextWindowManager,
            _selector.Object,
            _compressionService.Object,
            _logger,
            _conversationRecallService.Object);

        var messages = new[]
        {
            ChatMessage.User("Earlier context A."),
            ChatMessage.Assistant($"Earlier context B. {bulkyHistory}"),
            ChatMessage.User($"Earlier context C. {bulkyHistory}"),
            ChatMessage.Assistant($"Earlier context D. {bulkyHistory}"),
            ChatMessage.User($"Earlier context E. {bulkyHistory}"),
            ChatMessage.Assistant($"Earlier context F. {bulkyHistory}"),
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
        _conversationRecallService
            .Setup(service => service.SearchRelevantMessagesAsync(
                "what should I debug next",
                3,
                0.72f,
                42,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ConversationRecallResult
                {
                    ConversationId = 99,
                    MessageId = 200,
                    ConversationTitle = "Earlier rollout thread",
                    Role = "assistant",
                    ContentPreview = "The previous rollout issue centered on migration ordering and startup validation.",
                    Timestamp = DateTime.UtcNow.AddHours(-2),
                    Similarity = 0.88f
                }
            ]);

        var result = await sut.AssembleAsync(
            new ContextAssemblyRequest
            {
                ConversationId = 42,
                CurrentQuery = "what should I debug next",
                SystemPrompt = "Base prompt",
                MemoryContext = "[Memory]",
                ConversationMessages = messages,
                ContextWindow = 220,
                ReserveForResponse = 16,
                RecentAnchorCount = 2
            });

        result.SystemPrompt.Should().Contain("Condensed Earlier Conversation Context");
        result.SystemPrompt.Should().Contain("Durable Cross-Conversation Recall");
        result.SystemPrompt.Should().Contain("Earlier rollout thread / Assistant");
        result.Messages.Should().HaveCount(3);
        result.Diagnostics.AddedOverflowSummary.Should().BeTrue();
        result.Diagnostics.AddedDurableRecall.Should().BeTrue();
        result.Diagnostics.RecalledMessageCount.Should().Be(1);
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

    [Fact]
    public async Task AssembleAsync_WhenRecallThrows_ContinuesWithoutRecallBlock()
    {
        var bulkyHistory = string.Join(' ', Enumerable.Repeat("migration-ordering startup validation diagnostics", 18));

        var sut = new ContextAssemblyService(
            _contextWindowManager,
            _selector.Object,
            _compressionService.Object,
            _logger,
            _conversationRecallService.Object);

        var messages = new[]
        {
            ChatMessage.User("Earlier context A."),
            ChatMessage.Assistant($"Earlier context B. {bulkyHistory}"),
            ChatMessage.User($"Earlier context C. {bulkyHistory}"),
            ChatMessage.Assistant($"Earlier context D. {bulkyHistory}"),
            ChatMessage.User($"Earlier context E. {bulkyHistory}"),
            ChatMessage.Assistant($"Earlier context F. {bulkyHistory}"),
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
                ]
            });

        _conversationRecallService
            .Setup(service => service.SearchRelevantMessagesAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("recall failed"));

        var result = await sut.AssembleAsync(
            new ContextAssemblyRequest
            {
                ConversationId = 42,
                CurrentQuery = "what should I debug next",
                SystemPrompt = "Base prompt",
                MemoryContext = "[Memory]",
                ConversationMessages = messages,
                ContextWindow = 220,
                ReserveForResponse = 16,
                RecentAnchorCount = 2
            });

        result.Diagnostics.UsedLegacyFallback.Should().BeFalse();
        result.Diagnostics.AddedDurableRecall.Should().BeFalse();
        result.Diagnostics.DurableRecallSkipReason.Should().Be("recall_error");
        result.SystemPrompt.Should().NotContain("Durable Cross-Conversation Recall");
    }

    private ContextAssemblyService CreateWithFailingSelector()
    {
        _selector
            .Setup(selector => selector.SelectRelevantContextAsync(
                It.IsAny<ContextSelectionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("selector failed"));

        return new ContextAssemblyService(_contextWindowManager, _selector.Object, _compressionService.Object, _logger);
    }

    // 36 characters: 9 content tokens + 4 overhead = 13 estimated tokens per message.
    private static List<ChatMessage> ShortMessages(int count) => Enumerable.Range(0, count)
        .Select(i => i % 2 == 0
            ? ChatMessage.User($"user message number {i:D3} padded xx.")
            : ChatMessage.Assistant($"assistant msg number {i:D3} padded x."))
        .ToList();

    [Fact]
    public async Task Legacy_fallback_counts_the_system_prompt_against_the_window()
    {
        var sut = CreateWithFailingSelector();
        var messages = ShortMessages(20);
        var systemPrompt = new string('s', 400); // ~100 tokens

        var result = await sut.AssembleAsync(new ContextAssemblyRequest
        {
            CurrentQuery = "q",
            SystemPrompt = systemPrompt,
            ConversationMessages = messages,
            ContextWindow = 300,
            ReserveForResponse = 50
        });

        result.Diagnostics.UsedLegacyFallback.Should().BeTrue();
        result.Diagnostics.EstimatedPromptTokens.Should().BeLessThanOrEqualTo(250,
            "system prompt and messages together must leave the response reserve free");
        result.Messages.Should().NotBeEmpty();
        result.Messages[^1].Should().BeSameAs(messages[^1]);
        result.SystemPrompt.Should().Be(systemPrompt);
    }

    [Fact]
    public async Task Legacy_fallback_drops_memory_context_rather_than_overflowing()
    {
        var sut = CreateWithFailingSelector();
        var messages = ShortMessages(3);

        var result = await sut.AssembleAsync(new ContextAssemblyRequest
        {
            CurrentQuery = "q",
            SystemPrompt = "Base prompt",
            MemoryContext = "[Memory] " + new string('m', 800), // ~200 tokens, more than the window allows
            ConversationMessages = messages,
            ContextWindow = 200,
            ReserveForResponse = 50
        });

        result.Diagnostics.UsedLegacyFallback.Should().BeTrue();
        result.Diagnostics.CompressionSkipReason.Should().Be("no_message_budget");
        result.SystemPrompt.Should().Be("Base prompt");
        result.Messages.Should().Equal(messages);
        result.Diagnostics.EstimatedPromptTokens.Should().BeLessThanOrEqualTo(150);
    }

    [Fact]
    public async Task Legacy_fallback_keeps_the_latest_message_when_the_system_prompt_fills_the_window()
    {
        var sut = CreateWithFailingSelector();
        var messages = ShortMessages(6);

        var act = () => sut.AssembleAsync(new ContextAssemblyRequest
        {
            CurrentQuery = "q",
            SystemPrompt = new string('s', 400), // ~100 tokens in an 80-token prompt budget
            ConversationMessages = messages,
            ContextWindow = 100,
            ReserveForResponse = 20
        });

        var result = await act.Should().NotThrowAsync();
        result.Subject.Messages.Should().Equal(messages[^1]);
        result.Subject.Diagnostics.UsedLegacyFallback.Should().BeTrue();
    }
}

using System.Runtime.CompilerServices;
using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Chat;

public sealed class ChatServiceContextAssemblyTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IConversationService> _conversationService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IContextAssemblyService> _contextAssemblyService = new();
    private readonly Mock<IConversationMemoryService> _memoryService = new();
    private readonly ILogger _logger = Log.ForContext<AgentX.Core.Services.Chat.ChatService>();

    [Fact]
    public async Task SendMessageAndWaitAsync_UsesAssembledMessagesAndPrompt()
    {
        _settingsService
            .Setup(service => service.GetSettingsAsync())
            .ReturnsAsync(new AppSettings());

        _memoryService
            .Setup(service => service.GetMemoryContextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _memoryService
            .Setup(service => service.ExtractMemoriesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var conversation = new ConversationEntity
        {
            Id = 42,
            SystemPrompt = "Original prompt",
            Messages =
            [
                new MessageEntity
                {
                    Id = 1,
                    ConversationId = 42,
                    Role = "user",
                    Content = "Why is startup failing?",
                    SortOrder = 1,
                    Timestamp = DateTime.UtcNow
                }
            ]
        };

        _conversationService
            .Setup(service => service.AddMessageAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<double?>()))
            .Returns(Task.CompletedTask);
        _conversationService
            .Setup(service => service.GetConversationAsync(42))
            .ReturnsAsync(conversation);

        var assembledMessages = new List<ChatMessage>
        {
            ChatMessage.User("Selected context message")
        };

        _contextAssemblyService
            .Setup(service => service.AssembleAsync(
                It.IsAny<ContextAssemblyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextAssemblyResult
            {
                Messages = assembledMessages,
                SystemPrompt = "Assembled prompt"
            });

        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("Hello", " world"));

        var sut = new AgentX.Core.Services.Chat.ChatService(
            _aiService.Object,
            _conversationService.Object,
            _settingsService.Object,
            _contextAssemblyService.Object,
            _memoryService.Object,
            _logger);

        var result = await sut.SendMessageAndWaitAsync(42, "Why is startup failing?");

        result.Should().Be("Hello world");
        _aiService.Verify(service => service.StreamChatAsync(
            It.Is<IReadOnlyList<ChatMessage>>(messages => ReferenceEquals(messages, assembledMessages) || messages.SequenceEqual(assembledMessages)),
            "Assembled prompt",
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<string> StreamTokens(
        params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }
}

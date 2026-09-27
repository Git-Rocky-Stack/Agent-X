using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Chat;

/// <summary>
/// A routing decision applies to one reply. ChatService used to apply it by switching the
/// app-wide provider and saving the routed model, so every routed message silently changed
/// what Settings showed as the active provider and model.
/// </summary>
public sealed class ChatServiceRoutingTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IConversationService> _conversationService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IContextAssemblyService> _contextAssemblyService = new();
    private readonly Mock<IConversationMemoryService> _memoryService = new();
    private readonly Mock<IModelRouterService> _router = new();
    private readonly Mock<IAiProvider> _routedProvider = new();

    public ChatServiceRoutingTests()
    {
        _settingsService
            .Setup(service => service.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableModelRouting = true, Temperature = 0.3 });
        _memoryService
            .Setup(service => service.GetMemoryContextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _conversationService
            .Setup(service => service.GetConversationAsync(42))
            .ReturnsAsync(new ConversationEntity { Id = 42, SystemPrompt = "Original prompt", Messages = [] });
        _contextAssemblyService
            .Setup(service => service.AssembleAsync(It.IsAny<ContextAssemblyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContextAssemblyRequest request, CancellationToken _) => new ContextAssemblyResult
            {
                Messages = [ChatMessage.User(request.CurrentQuery)],
                SystemPrompt = "Assembled prompt"
            });
        _router
            .Setup(router => router.RouteAsync("Write a parser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision { ProviderId = "anthropic", ModelId = "claude-sonnet-5", TaskType = TaskType.Code });
        _aiService.Setup(service => service.GetProvider("anthropic")).Returns(_routedProvider.Object);
        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => StreamTokens("Active", " answer"));
    }

    [Fact]
    public async Task A_routed_reply_uses_the_routed_provider_and_model_and_leaves_the_app_selection_alone()
    {
        _aiService
            .Setup(service => service.IsProviderAvailableAsync("anthropic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        IReadOnlyList<ChatMessage>? sentMessages = null;
        ChatOptions? sentOptions = null;
        _routedProvider
            .Setup(provider => provider.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken>((messages, options, _) =>
            {
                sentMessages = messages;
                sentOptions = options;
            })
            .Returns(StreamTokens("Routed", " answer"));

        var tokens = await DrainAsync(CreateSut().SendMessageAsync(42, "Write a parser"));

        tokens.Should().Equal("Routed", " answer");
        sentOptions!.ModelId.Should().Be("claude-sonnet-5");
        sentOptions.Temperature.Should().Be(0.3);
        sentMessages!.Select(message => message.Role).Should().Equal("system", "user");
        sentMessages![0].Content.Should().Be("Assembled prompt");
        _aiService.Verify(service => service.SwitchProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _aiService.Verify(service => service.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _aiService.Verify(service => service.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_reply_routed_to_an_unreachable_provider_is_answered_by_the_active_one()
    {
        _aiService
            .Setup(service => service.IsProviderAvailableAsync("anthropic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var tokens = await DrainAsync(CreateSut().SendMessageAsync(42, "Write a parser"));

        tokens.Should().Equal("Active", " answer");
        _routedProvider.Verify(provider => provider.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _aiService.Verify(service => service.SwitchProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task With_routing_off_the_router_is_not_asked()
    {
        _settingsService
            .Setup(service => service.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableModelRouting = false });

        var tokens = await DrainAsync(CreateSut().SendMessageAsync(42, "Write a parser"));

        tokens.Should().Equal("Active", " answer");
        _router.Verify(router => router.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_routed_reply_is_saved_with_the_routed_model()
    {
        _aiService.SetupGet(service => service.ActiveModelId).Returns("llama3.2:3b");
        _aiService
            .Setup(service => service.IsProviderAvailableAsync("anthropic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _routedProvider
            .Setup(provider => provider.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("Routed", " answer"));

        await DrainAsync(CreateSut().SendMessageAsync(42, "Write a parser"));

        _conversationService.Verify(service => service.AddMessageAsync(
            42, "assistant", "Routed answer", It.IsAny<int?>(), It.IsAny<double?>(), "claude-sonnet-5", null), Times.Once);
    }

    [Fact]
    public async Task A_reply_from_the_active_provider_is_saved_with_the_active_model()
    {
        _aiService.SetupGet(service => service.ActiveModelId).Returns("llama3.2:3b");
        _aiService
            .Setup(service => service.IsProviderAvailableAsync("anthropic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await DrainAsync(CreateSut().SendMessageAsync(42, "Write a parser"));

        _conversationService.Verify(service => service.AddMessageAsync(
            42, "assistant", "Active answer", It.IsAny<int?>(), It.IsAny<double?>(), "llama3.2:3b", null), Times.Once);
    }

    private ChatService CreateSut() => new(
        _aiService.Object,
        _conversationService.Object,
        _settingsService.Object,
        _contextAssemblyService.Object,
        _memoryService.Object,
        Log.ForContext<ChatService>(),
        modelRouterService: _router.Object);

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> stream)
    {
        var tokens = new List<string>();
        await foreach (var token in stream)
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static async IAsyncEnumerable<string> StreamTokens(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }
}

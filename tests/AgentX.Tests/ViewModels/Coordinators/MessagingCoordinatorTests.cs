using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.AI;
using AgentX.Core.AI.Agents;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Feedback;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels.Coordinators;

public class MessagingCoordinatorTests
{
    private readonly Mock<IChatService> _chatService;
    private readonly Mock<IConversationService> _conversationService;
    private readonly Mock<IAiService> _aiService;
    private readonly Mock<IFeedbackService> _feedbackService;
    private readonly Mock<IAiProvider> _provider;
    private readonly Mock<IMultiAgentOrchestrator> _multiAgentOrchestrator;
    private readonly MessagingCoordinator _coordinator;

    public MessagingCoordinatorTests()
    {
        _chatService = new Mock<IChatService>();
        _conversationService = new Mock<IConversationService>();
        _aiService = new Mock<IAiService>();
        _feedbackService = new Mock<IFeedbackService>();
        _provider = new Mock<IAiProvider>();
        _multiAgentOrchestrator = new Mock<IMultiAgentOrchestrator>();

        // Default: ActiveProvider is connected so the coordinator uses IChatService
        _aiService.SetupGet(s => s.ActiveProvider).Returns(_provider.Object);
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _conversationService
            .Setup(s => s.GetMessagesAsync(It.IsAny<long>()))
            .ReturnsAsync(Array.Empty<MessageEntity>());

        _coordinator = new MessagingCoordinator(
            _chatService.Object,
            _conversationService.Object,
            _aiService.Object,
            _feedbackService.Object,
            _multiAgentOrchestrator.Object);
    }

    // ── StopGenerationAsync ────────────────────────────────────────

    [Fact]
    public async Task StopGenerationAsync_WhenNotGenerating_DoesNotThrow()
    {
        // Act
        await _coordinator.StopGenerationAsync();

        // Assert — no exception means success
    }

    // ── SubmitFeedbackAsync ────────────────────────────────────────

    [Fact]
    public async Task SubmitFeedbackAsync_CallsService()
    {
        // Act
        await _coordinator.SubmitFeedbackAsync(1, 10, "positive");

        // Assert
        _feedbackService.Verify(
            s => s.SubmitFeedbackAsync(1, 10, "positive", null, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_SwallowsException()
    {
        // Arrange
        _feedbackService
            .Setup(s => s.SubmitFeedbackAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act — should not throw
        await _coordinator.SubmitFeedbackAsync(1, 10, "negative");
    }

    // ── DeleteMessageAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteMessageAsync_CallsService_WhenMessageIdPositive()
    {
        // Act
        await _coordinator.DeleteMessageAsync(42);

        // Assert
        _conversationService.Verify(s => s.DeleteMessageAsync(42), Times.Once);
    }

    [Fact]
    public async Task DeleteMessageAsync_SkipsService_WhenMessageIdZero()
    {
        // Act
        await _coordinator.DeleteMessageAsync(0);

        // Assert
        _conversationService.Verify(
            s => s.DeleteMessageAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task DeleteMessageAsync_SwallowsException()
    {
        // Arrange
        _conversationService
            .Setup(s => s.DeleteMessageAsync(It.IsAny<long>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act — should not throw
        await _coordinator.DeleteMessageAsync(42);
    }

    // ── SendMessageAsync (with conversation creation) ──────────────

    [Fact]
    public async Task SendMessageAsync_CreatesConversation_WhenNull()
    {
        // Arrange
        var convEntity = new ConversationEntity
        {
            Id = 99,
            Title = "Test message",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _conversationService
            .Setup(s => s.CreateConversationAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(convEntity);

        _chatService
            .Setup(s => s.SendMessageAsync(99, "Hello", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("World"));
        var snapshot = CreateInspectionSnapshot(99);
        _chatService
            .Setup(s => s.GetLatestContextInspection(99))
            .Returns(snapshot);

        // Act
        var result = await _coordinator.SendMessageAsync("Hello", null, null, "model1", false);

        // Assert
        result.ConversationId.Should().Be(99);
        result.ConversationTitle.Should().Be("Test message");
        result.ContextInspection.Should().BeSameAs(snapshot);
        result.WasCancelled.Should().BeFalse();
        result.HadError.Should().BeFalse();
    }

    // ── SendMessageAsync (cancellation) ────────────────────────────

    [Fact]
    public async Task SendMessageAsync_ReturnsCancelled_WhenCtsCancelled()
    {
        // Arrange
        _chatService
            .Setup(s => s.SendMessageAsync(1, "test", It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        // Act
        var result = await _coordinator.SendMessageAsync("test", 1, null, null, false);

        // Assert
        result.WasCancelled.Should().BeTrue();
    }

    // ── SendMessageAsync (error) ───────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_ReturnsError_OnException()
    {
        // Arrange
        _chatService
            .Setup(s => s.SendMessageAsync(1, "fail", It.IsAny<CancellationToken>()))
            .Throws(new Exception("AI error"));

        string? errorReceived = null;
        _coordinator.GenerationError += (s, msg) => errorReceived = msg;

        // Act
        var result = await _coordinator.SendMessageAsync("fail", 1, null, null, false);

        // Assert
        result.HadError.Should().BeTrue();
        errorReceived.Should().NotBeNull();
    }

    // ── Events ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_RaisesTokenReceived_ForEachToken()
    {
        // Arrange
        var tokens = new List<string>();
        _coordinator.TokenReceived += (s, token) => tokens.Add(token);

        _chatService
            .Setup(s => s.SendMessageAsync(1, "Hello", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Hi", "!"));

        // Act
        await _coordinator.SendMessageAsync("Hello", 1, null, null, false);

        // Assert
        tokens.Should().HaveCount(2);
        tokens.Should().Contain("Hi");
        tokens.Should().Contain("!");
    }

    [Fact]
    public async Task SendMessageAsync_RaisesStreamingCompleted()
    {
        // Arrange
        StreamingCompletedEventArgs? completedArgs = null;
        _coordinator.StreamingCompleted += (s, e) => completedArgs = e;
        var snapshot = CreateInspectionSnapshot(1);

        _chatService
            .Setup(s => s.SendMessageAsync(1, "Test", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Response"));
        _chatService
            .Setup(s => s.GetLatestContextInspection(1))
            .Returns(snapshot);

        // Act
        await _coordinator.SendMessageAsync("Test", 1, null, null, false);

        // Assert
        completedArgs.Should().NotBeNull();
        completedArgs!.ResponseContent.Should().Be("Response");
        completedArgs.ConversationId.Should().Be(1);
        completedArgs.ContextInspection.Should().BeSameAs(snapshot);
    }

    [Fact]
    public async Task SendMessageAsync_WithMultiAgentParallelMode_RunsOrchestratorAndPersistsResult()
    {
        var tokens = new List<string>();
        _coordinator.TokenReceived += (_, token) => tokens.Add(token);
        _conversationService
            .Setup(service => service.AddMessageAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<double?>()))
            .Returns(Task.CompletedTask);
        _multiAgentOrchestrator
            .Setup(service => service.RunAsync(
                "Plan launch",
                It.Is<IReadOnlyList<AgentRole>>(agents => agents.Count >= 3),
                OrchestratorStrategy.Parallel,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult
            {
                Task = "Plan launch",
                Strategy = OrchestratorStrategy.Parallel,
                FinalAnswer = "# Multi-Agent Synthesis\n\n## Consensus\nShip in phases.",
                IsSuccess = true
            });

        var result = await _coordinator.SendMessageAsync(
            "Plan launch",
            5,
            null,
            "model1",
            false,
            ChatOrchestrationMode.MultiAgentParallel);

        result.ConversationId.Should().Be(5);
        result.ResponseContent.Should().Contain("Multi-Agent Synthesis");
        result.ContextInspection.Should().NotBeNull();
        result.ContextInspection!.LimitedVisibilityReason.Should().Be("multi_agent_orchestration");
        tokens.Should().ContainSingle().Which.Should().Contain("Multi-Agent Synthesis");
        _chatService.Verify(service => service.SendMessageAsync(
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _conversationService.Verify(service => service.AddMessageAsync(
            5,
            "user",
            "Plan launch",
            null,
            null), Times.Once);
        _conversationService.Verify(service => service.AddMessageAsync(
            5,
            "assistant",
            It.Is<string>(content => content.Contains("Multi-Agent Synthesis", StringComparison.Ordinal)),
            It.Is<int?>(tokenCount => tokenCount > 0),
            It.Is<double?>(duration => duration >= 0)), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ResolvesAssistantMessageId_WhenPersistedAssistantMessageExists()
    {
        var snapshot = CreateInspectionSnapshot(1);

        _chatService
            .Setup(s => s.SendMessageAsync(1, "Test", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Response"));
        _chatService
            .Setup(s => s.GetLatestContextInspection(1))
            .Returns(snapshot);
        _conversationService
            .Setup(s => s.GetMessagesAsync(1))
            .ReturnsAsync(
            [
                new MessageEntity
                {
                    Id = 10,
                    ConversationId = 1,
                    Role = "user",
                    Content = "Test",
                    SortOrder = 0,
                    Timestamp = DateTime.UtcNow.AddMinutes(-1)
                },
                new MessageEntity
                {
                    Id = 55,
                    ConversationId = 1,
                    Role = "assistant",
                    Content = "Response",
                    SortOrder = 1,
                    Timestamp = DateTime.UtcNow
                }
            ]);

        var result = await _coordinator.SendMessageAsync("Test", 1, null, null, false);

        result.AssistantMessageId.Should().Be(55);
    }

    // ── NotificationRequested event ────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_RaisesNotificationRequest_OnError()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _chatService
            .Setup(s => s.SendMessageAsync(1, "err", It.IsAny<CancellationToken>()))
            .Throws(new Exception("AI error"));

        // Act
        await _coordinator.SendMessageAsync("err", 1, null, null, false);

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Generation Failed");
    }

    // ── Direct streaming fallback (disconnected provider) ──────────

    [Fact]
    public async Task SendMessageAsync_UsesDirectStreaming_WhenProviderDisconnected()
    {
        // Arrange — provider returns false for connection check
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _aiService
            .Setup(s => s.StreamChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), null, It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Fallback"));

        // Act
        var result = await _coordinator.SendMessageAsync("test", 1, null, null, false);

        // Assert
        result.ResponseContent.Should().Be("Fallback");
        result.HadError.Should().BeFalse();
        result.ContextInspection.Should().NotBeNull();
        result.ContextInspection!.HasLimitedVisibility.Should().BeTrue();
        result.ContextInspection.LimitedVisibilityReason.Should().Be("provider_disconnected");
    }

    [Fact]
    public async Task SendMessageAsync_UsesDirectStreaming_WhenNoActiveProvider()
    {
        // Arrange — no active provider
        _aiService.SetupGet(s => s.ActiveProvider).Returns((IAiProvider)null!);

        _aiService
            .Setup(s => s.StreamChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), null, It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Direct"));

        // Act
        var result = await _coordinator.SendMessageAsync("test", 1, null, null, false);

        // Assert
        result.ResponseContent.Should().Be("Direct");
        result.HadError.Should().BeFalse();
        result.ContextInspection.Should().NotBeNull();
        result.ContextInspection!.HasLimitedVisibility.Should().BeTrue();
        result.ContextInspection.LimitedVisibilityReason.Should().Be("no_active_provider");
    }

    // ── IsGenerating ───────────────────────────────────────────────

    [Fact]
    public void IsGenerating_IsFalse_Initially()
    {
        _coordinator.IsGenerating.Should().BeFalse();
    }

    // ── Helper: Create async token stream ──────────────────────────

    private static async IAsyncEnumerable<string> CreateTokenStream(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    private static ChatContextInspectionSnapshot CreateInspectionSnapshot(long conversationId) =>
        new()
        {
            ConversationId = conversationId,
            CapturedAt = DateTime.UtcNow,
            CurrentQuery = "How should I proceed?",
            Diagnostics = new AgentX.Core.AI.Context.ContextAssemblyDiagnostics
            {
                SelectedMessageCount = 3,
                AnchorMessageCount = 1,
                EstimatedMessageTokens = 72,
                EstimatedPromptTokens = 180
            },
            AssemblyExplanation = "Structured context assembly completed.",
            CompressionExplanation = "No overflow summary was needed.",
            RecallExplanation = "Durable recall found no relevant cross-conversation matches for this response."
        };
}

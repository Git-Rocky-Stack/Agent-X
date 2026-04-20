using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
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
    private readonly MessagingCoordinator _coordinator;

    public MessagingCoordinatorTests()
    {
        _chatService = new Mock<IChatService>();
        _conversationService = new Mock<IConversationService>();
        _aiService = new Mock<IAiService>();
        _feedbackService = new Mock<IFeedbackService>();
        _provider = new Mock<IAiProvider>();

        // Default: ActiveProvider is connected so the coordinator uses IChatService
        _aiService.SetupGet(s => s.ActiveProvider).Returns(_provider.Object);
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _coordinator = new MessagingCoordinator(
            _chatService.Object,
            _conversationService.Object,
            _aiService.Object,
            _feedbackService.Object);
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

        // Act
        var result = await _coordinator.SendMessageAsync("Hello", null, null, "model1", false);

        // Assert
        result.ConversationId.Should().Be(99);
        result.ConversationTitle.Should().Be("Test message");
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

        _chatService
            .Setup(s => s.SendMessageAsync(1, "Test", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Response"));

        // Act
        await _coordinator.SendMessageAsync("Test", 1, null, null, false);

        // Assert
        completedArgs.Should().NotBeNull();
        completedArgs!.ResponseContent.Should().Be("Response");
        completedArgs.ConversationId.Should().Be(1);
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
    }

    [Fact]
    public async Task SendMessageAsync_UsesDirectStreaming_WhenNoActiveProvider()
    {
        // Arrange — no active provider
        _aiService.SetupGet(s => s.ActiveProvider).Returns((IAiProvider?)null);

        _aiService
            .Setup(s => s.StreamChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), null, It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Direct"));

        // Act
        var result = await _coordinator.SendMessageAsync("test", 1, null, null, false);

        // Assert
        result.ResponseContent.Should().Be("Direct");
        result.HadError.Should().BeFalse();
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
}

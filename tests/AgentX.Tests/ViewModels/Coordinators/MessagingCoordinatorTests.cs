using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.AI;
using AgentX.Core.AI.Agents;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Feedback;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
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

    // --- Provider-aware help ---
    // The failure text told everyone to start Ollama, whatever provider was active.

    [Fact]
    public async Task SendMessageAsync_WhenTheBuiltInModelFails_AdvisesOnTheBuiltInModelNotOllama()
    {
        _provider.SetupGet(p => p.ProviderId).Returns("local");
        _provider.SetupGet(p => p.DisplayName).Returns("Built-in LLM");
        _chatService
            .Setup(s => s.SendMessageAsync(1, "fail", It.IsAny<CancellationToken>()))
            .Throws(new Exception("AI error"));

        var result = await _coordinator.SendMessageAsync("fail", 1, null, null, false);

        result.HadError.Should().BeTrue();
        result.ResponseContent.Should().Contain("built-in model").And.NotContain("Ollama");
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheCloudProviderIsUnreachable_TheFallbackNamesIt()
    {
        _provider.SetupGet(p => p.ProviderId).Returns("anthropic");
        _provider.SetupGet(p => p.DisplayName).Returns("Anthropic Claude");
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _aiService
            .Setup(s => s.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(FailingStream(new HttpRequestException("No connection could be made.")));

        var result = await _coordinator.SendMessageAsync("q", 1, null, null, false);

        result.ResponseContent.Should().Contain("Anthropic Claude is not available")
            .And.Contain("Anthropic Claude API key")
            .And.NotContain("Ollama");
    }

    // The provider-aware failure and offline hints were English whatever the user's language.

    [Fact]
    public async Task SendMessageAsync_WhenTheReplyFails_ExplainsInTheUsersLanguage()
    {
        _provider.SetupGet(p => p.ProviderId).Returns("local");
        _provider.SetupGet(p => p.DisplayName).Returns("Built-in LLM");
        _chatService
            .Setup(s => s.SendMessageAsync(1, "fail", It.IsAny<CancellationToken>()))
            .Throws(new Exception("AI error"));
        var coordinator = CreateLocalizedCoordinator("de");

        var result = await coordinator.SendMessageAsync("fail", 1, null, null, false);

        result.HadError.Should().BeTrue();
        result.ResponseContent.Should().Be(
            "Beim Erstellen einer Antwort ist ein Fehler aufgetreten. " +
            "Prüfen Sie, ob das integrierte Modell installiert ist und genügend freier Arbeitsspeicher zum Laden vorhanden ist.");
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheProviderIsUnreachable_TheFallbackIsInTheUsersLanguage()
    {
        _provider.SetupGet(p => p.ProviderId).Returns("anthropic");
        _provider.SetupGet(p => p.DisplayName).Returns("Anthropic Claude");
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _aiService
            .Setup(s => s.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(FailingStream(new HttpRequestException("No connection could be made.")));
        var coordinator = CreateLocalizedCoordinator("de");

        var result = await coordinator.SendMessageAsync("q", 1, null, null, false);

        result.ResponseContent.Should()
            .Contain("Es kann keine Antwort erstellt werden: Anthropic Claude ist nicht verfügbar.")
            .And.Contain("Prüfen Sie den API-Schlüssel für Anthropic Claude in den Einstellungen und Ihre Netzwerkverbindung.");
    }

    [Fact]
    public async Task SendMessageAsync_WithNoProviderReady_TheFallbackSaysSoWithoutAName()
    {
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _aiService
            .Setup(s => s.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(FailingStream(new HttpRequestException("No connection could be made.")));

        var result = await _coordinator.SendMessageAsync("q", 1, null, null, false);

        result.ResponseContent.Should().StartWith("Unable to generate a response: the AI provider is not available.")
            .And.Contain("Check the AI provider in Settings.");
    }

    [Fact]
    public async Task SendMessageAsync_WhenTheReplyFails_TheNotificationIsInTheUsersLanguageToo()
    {
        _chatService
            .Setup(s => s.SendMessageAsync(1, "fail", It.IsAny<CancellationToken>()))
            .Throws(new Exception("AI error"));
        var coordinator = CreateLocalizedCoordinator("fr");
        NotificationRequestEventArgs? notification = null;
        coordinator.NotificationRequested += (_, e) => notification = e;

        await coordinator.SendMessageAsync("fail", 1, null, null, false);

        notification!.Title.Should().Be("Échec de la génération");
        notification.Message.Should().Be("Impossible de générer une réponse. Vérifiez votre connexion d'IA dans les Paramètres.");
    }

    [Fact]
    public async Task RegenerateResponseAsync_WhenItFails_KeepsThePreviousAnswerAndSaysSoInTheUsersLanguage()
    {
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var coordinator = CreateLocalizedCoordinator("de");

        var result = await coordinator.RegenerateResponseAsync(1, 10, "q", null, ChatOrchestrationMode.Standard);

        result.HadError.Should().BeTrue();
        result.ResponseContent.Should().Be(
            "Beim Erstellen einer neuen Antwort ist ein Fehler aufgetreten. Die vorherige Antwort wurde beibehalten.");
    }

    private MessagingCoordinator CreateLocalizedCoordinator(string locale) =>
        new(
            _chatService.Object,
            _conversationService.Object,
            _aiService.Object,
            _feedbackService.Object,
            _multiAgentOrchestrator.Object,
            localization: ReswLocalization.For(locale));

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
                It.IsAny<double?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
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
            null,
            null,
            null), Times.Once);
        _conversationService.Verify(service => service.AddMessageAsync(
            5,
            "assistant",
            It.Is<string>(content => content.Contains("Multi-Agent Synthesis", StringComparison.Ordinal)),
            It.Is<int?>(tokenCount => tokenCount > 0),
            It.Is<double?>(duration => duration >= 0),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
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
        // First read: the thread before the send. Second read: after it, with its two rows.
        _conversationService
            .SetupSequence(s => s.GetMessagesAsync(1))
            .ReturnsAsync(Array.Empty<MessageEntity>())
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
        result.AssistantMessageSortOrder.Should().Be(1);
        result.UserMessageId.Should().Be(10);
        result.UserMessageSortOrder.Should().Be(0);
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

    // --- Generation ownership ---
    // The cancellation source used to be one shared field: a second send overwrote it, and the
    // first send's finally disposed and cleared it, so Stop no longer reached anything.

    [Fact]
    public async Task StopGenerationAsync_StillStopsTheNewestGeneration_AfterAnOlderOneHasFinished()
    {
        var firstGate = new TaskCompletionSource();
        var secondGate = new TaskCompletionSource();
        _chatService
            .Setup(s => s.SendMessageAsync(1, "first", It.IsAny<CancellationToken>()))
            .Returns<long, string, CancellationToken>((_, _, ct) => GatedStream(firstGate, ct));
        _chatService
            .Setup(s => s.SendMessageAsync(1, "second", It.IsAny<CancellationToken>()))
            .Returns<long, string, CancellationToken>((_, _, ct) => GatedStream(secondGate, ct));

        var first = _coordinator.SendMessageAsync("first", 1, null, null, false);
        var second = _coordinator.SendMessageAsync("second", 1, null, null, false);

        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5));
        firstResult.WasCancelled.Should().BeTrue("a newer generation supersedes the older one");
        _coordinator.IsGenerating.Should().BeTrue("the newer generation is still running");

        await _coordinator.StopGenerationAsync();
        var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(5));

        secondResult.WasCancelled.Should().BeTrue();
        _coordinator.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task SendMessageAsync_AfterStop_ForwardsNoFurtherTokens()
    {
        // Tokens can still arrive after a stop; the chat may already be showing another thread.
        var gate = new TaskCompletionSource();
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Returns<long, string, CancellationToken>((_, _, ct) => StreamIgnoringStopOnce(gate, ct));
        var tokens = new List<string>();
        _coordinator.TokenReceived += (_, token) =>
        {
            tokens.Add(token);
            if (token == "before")
            {
                _ = _coordinator.StopGenerationAsync();
                gate.TrySetResult();
            }
        };
        StreamingCompletedEventArgs? completed = null;
        _coordinator.StreamingCompleted += (_, e) => completed = e;

        var result = await _coordinator.SendMessageAsync("q", 1, null, null, false).WaitAsync(TimeSpan.FromSeconds(5));

        tokens.Should().Equal("before");
        completed.Should().BeNull();
        result.WasCancelled.Should().BeTrue();
    }

    // --- Persisted identity ---

    [Fact]
    public async Task SendMessageAsync_WhenStopped_ReportsThePromptRowItSaved()
    {
        _conversationService
            .SetupSequence(s => s.GetMessagesAsync(1))
            .ReturnsAsync([NewMessage(5, "user", 0), NewMessage(6, "assistant", 1)])
            .ReturnsAsync([NewMessage(5, "user", 0), NewMessage(6, "assistant", 1), NewMessage(10, "user", 2)]);
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        var result = await _coordinator.SendMessageAsync("q", 1, null, null, false);

        result.WasCancelled.Should().BeTrue();
        result.UserMessageId.Should().Be(10);
        result.UserMessageSortOrder.Should().Be(2);
        result.AssistantMessageId.Should().BeNull();
        result.ResponseContent.Should().Be("[Generation stopped]");
    }

    [Fact]
    public async Task SendMessageAsync_WhenStoppedBeforeThePromptWasSaved_DoesNotReportAnEarlierPrompt()
    {
        // "The last user row" would be an earlier question here; acting on it deletes the wrong row.
        _conversationService
            .Setup(s => s.GetMessagesAsync(1))
            .ReturnsAsync([NewMessage(5, "user", 0), NewMessage(6, "assistant", 1)]);
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        var result = await _coordinator.SendMessageAsync("q", 1, null, null, false);

        result.UserMessageId.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_WithAnEmptyReply_DoesNotReportTheEarlierAnswer()
    {
        _conversationService
            .SetupSequence(s => s.GetMessagesAsync(1))
            .ReturnsAsync([NewMessage(5, "user", 0), NewMessage(6, "assistant", 1)])
            .ReturnsAsync([NewMessage(5, "user", 0), NewMessage(6, "assistant", 1), NewMessage(10, "user", 2)]);
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream());

        var result = await _coordinator.SendMessageAsync("q", 1, null, null, false);

        result.AssistantMessageId.Should().BeNull();
        result.UserMessageId.Should().Be(10);
    }

    // --- Regeneration ---

    [Fact]
    public async Task RegenerateResponseAsync_StreamsANewAnswerToTheSavedPromptWithoutSendingItAgain()
    {
        _conversationService
            .SetupSequence(s => s.GetMessagesAsync(42))
            .ReturnsAsync([NewMessage(10, "user", 0), NewMessage(11, "assistant", 1)])
            .ReturnsAsync([NewMessage(10, "user", 0), NewMessage(12, "assistant", 2)]);
        _chatService
            .Setup(s => s.RegenerateResponseAsync(42, 10, It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("New", " answer"));
        var tokens = new List<string>();
        _coordinator.TokenReceived += (_, token) => tokens.Add(token);

        var result = await _coordinator.RegenerateResponseAsync(42, 10, "Why?", null, ChatOrchestrationMode.Standard);

        result.HadError.Should().BeFalse();
        result.ResponseContent.Should().Be("New answer");
        result.UserMessageId.Should().Be(10);
        result.AssistantMessageId.Should().Be(12);
        tokens.Should().Equal("New", " answer");
        _chatService.Verify(
            s => s.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegenerateResponseAsync_WhenTheProviderIsOffline_FailsWithoutTouchingTheThread()
    {
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _coordinator.RegenerateResponseAsync(42, 10, "Why?", null, ChatOrchestrationMode.Standard);

        result.HadError.Should().BeTrue();
        _chatService.Verify(
            s => s.RegenerateResponseAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _conversationService.Verify(s => s.DeleteMessageAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task RegenerateResponseAsync_InMultiAgentMode_ReplacesTheAnswerOnlyAfterSavingTheNewOne()
    {
        var writes = new List<string>();
        _conversationService
            .Setup(s => s.GetMessagesAsync(42))
            .ReturnsAsync([NewMessage(10, "user", 0), NewMessage(11, "assistant", 1)]);
        _conversationService
            .Setup(s => s.AddMessageAsync(
                42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<long, string, string, int?, double?, string?, string?>((_, role, _, _, _, _, _) => writes.Add($"add {role}"))
            .Returns(Task.CompletedTask);
        _conversationService
            .Setup(s => s.DeleteMessageAsync(11))
            .Callback(() => writes.Add("delete 11"))
            .Returns(Task.CompletedTask);
        _multiAgentOrchestrator
            .Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentRole>>(), OrchestratorStrategy.Parallel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult { FinalAnswer = "Synthesis", IsSuccess = true });

        var result = await _coordinator.RegenerateResponseAsync(42, 10, "Plan launch", null, ChatOrchestrationMode.MultiAgentParallel);

        result.HadError.Should().BeFalse();
        writes.Should().Equal("add assistant", "delete 11");
    }

    [Fact]
    public async Task RegenerateResponseAsync_InMultiAgentMode_WhenOrchestrationFails_KeepsTheAnswer()
    {
        _conversationService
            .Setup(s => s.GetMessagesAsync(42))
            .ReturnsAsync([NewMessage(10, "user", 0), NewMessage(11, "assistant", 1)]);
        _multiAgentOrchestrator
            .Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<AgentRole>>(), It.IsAny<OrchestratorStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult { IsSuccess = false });

        var result = await _coordinator.RegenerateResponseAsync(42, 10, "Plan launch", null, ChatOrchestrationMode.MultiAgentDebate);

        result.HadError.Should().BeTrue();
        _conversationService.Verify(
            s => s.AddMessageAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
        _conversationService.Verify(s => s.DeleteMessageAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task DeleteMessageAsync_ReportsWhetherTheRowIsGone()
    {
        (await _coordinator.DeleteMessageAsync(42)).Should().BeTrue();
        (await _coordinator.DeleteMessageAsync(0)).Should().BeFalse();

        _conversationService
            .Setup(s => s.DeleteMessageAsync(43))
            .ThrowsAsync(new Exception("DB error"));
        (await _coordinator.DeleteMessageAsync(43)).Should().BeFalse();
    }

    // --- Research Mode ---
    // The toggle promised web sources while the send path ignored it entirely.

    [Fact]
    public async Task SendMessageAsync_InResearchMode_AddsCitedWebResultsToTheContext()
    {
        var (coordinator, webSearch, _) = CreateResearchCoordinator(researchEnabled: true, configured: true);
        webSearch
            .Setup(s => s.SearchAsync("What changed?", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResponse
            {
                Query = "What changed?",
                Results =
                [
                    new WebSearchResult { Title = "Release notes", Url = "https://example.org/notes", Snippet = "Version 2 ships." },
                    new WebSearchResult { Title = "Blog", Url = "https://example.org/blog", Snippet = "Why it changed." }
                ]
            });
        _chatService
            .Setup(s => s.SendMessageAsync(1, "What changed?", It.IsAny<SupplementalContext?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Answer [1]"));

        var result = await coordinator.SendMessageAsync("What changed?", 1, null, null, true);

        _chatService.Verify(s => s.SendMessageAsync(
            1,
            "What changed?",
            It.Is<SupplementalContext?>(context =>
                context!.PromptContext.Contains("[1] Release notes", StringComparison.Ordinal) &&
                context.PromptContext.Contains("URL: https://example.org/notes", StringComparison.Ordinal) &&
                context.PromptContext.Contains("[2] Blog", StringComparison.Ordinal) &&
                context.Citations.Count == 2 &&
                context.Citations[0].Url == "https://example.org/notes"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.WebCitations.Should().HaveCount(2);
        result.WebCitations![0].Url.Should().Be("https://example.org/notes");
    }

    // Research Mode asked for a fixed 5 results, and the web search service keeps the smaller of
    // the request and Max Search Results, so the setting could lower the count but never raise it.
    [Theory]
    [InlineData(15, 15)]
    [InlineData(3, 3)]
    [InlineData(0, 10)]
    [InlineData(50, 20)]
    public async Task SendMessageAsync_InResearchMode_AsksForAsManySourcesAsMaxSearchResultsAllows(
        int maxSearchResults, int expectedCount)
    {
        var (coordinator, webSearch, _) = CreateResearchCoordinator(
            researchEnabled: true, configured: true, maxSearchResults: maxSearchResults);
        var results = Enumerable.Range(1, 25)
            .Select(i => new WebSearchResult { Title = $"Page {i}", Url = $"https://example.org/{i}", Snippet = "Text." })
            .ToList();
        webSearch
            .Setup(s => s.SearchAsync("What changed?", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResponse { Query = "What changed?", Results = results });
        _chatService
            .Setup(s => s.SendMessageAsync(1, "What changed?", It.IsAny<SupplementalContext?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Answer [1]"));

        var result = await coordinator.SendMessageAsync("What changed?", 1, null, null, true);

        webSearch.Verify(s => s.SearchAsync("What changed?", expectedCount, It.IsAny<CancellationToken>()), Times.Once);
        result.WebCitations.Should().HaveCount(expectedCount);
    }

    [Theory]
    [InlineData(false, true, "turned off in Settings")]
    [InlineData(true, false, "No web search provider")]
    public async Task SendMessageAsync_InResearchMode_WhenWebSearchCannotRun_AnswersLocallyAndSaysWhy(
        bool researchEnabled, bool configured, string reason)
    {
        var (coordinator, webSearch, _) = CreateResearchCoordinator(researchEnabled, configured);
        _chatService
            .Setup(s => s.SendMessageAsync(1, "What changed?", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("Local answer"));
        NotificationRequestEventArgs? notice = null;
        coordinator.NotificationRequested += (_, e) => notice = e;

        var result = await coordinator.SendMessageAsync("What changed?", 1, null, null, true);

        result.ResponseContent.Should().Be("Local answer");
        webSearch.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        notice.Should().NotBeNull();
        notice!.Title.Should().Be("No web sources");
        notice.Message.Should().Contain(reason);
    }

    [Fact]
    public async Task SendMessageAsync_InResearchMode_WhileItCannotRun_SaysWhyOnceNotOnEverySend()
    {
        var (coordinator, _, _) = CreateResearchCoordinator(researchEnabled: true, configured: false);
        _chatService
            .Setup(s => s.SendMessageAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => CreateTokenStream("Local answer"));
        var notices = new List<NotificationRequestEventArgs>();
        coordinator.NotificationRequested += (_, e) => notices.Add(e);

        await coordinator.SendMessageAsync("first", 1, null, null, true);
        await coordinator.SendMessageAsync("second", 1, null, null, true);
        await coordinator.SendMessageAsync("third", 1, null, null, true);

        notices.Should().ContainSingle(notice => notice.Title == "No web sources");
    }

    [Fact]
    public async Task SendMessageAsync_InResearchMode_TurnedBackOnAfterASendWithItOff_SaysWhyAgain()
    {
        var (coordinator, _, _) = CreateResearchCoordinator(researchEnabled: true, configured: false);
        _chatService
            .Setup(s => s.SendMessageAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => CreateTokenStream("Local answer"));
        var notices = new List<NotificationRequestEventArgs>();
        coordinator.NotificationRequested += (_, e) => notices.Add(e);

        await coordinator.SendMessageAsync("first", 1, null, null, true);
        await coordinator.SendMessageAsync("research off", 1, null, null, false);
        await coordinator.SendMessageAsync("research on again", 1, null, null, true);

        notices.Where(notice => notice.Title == "No web sources").Should().HaveCount(2);
    }

    [Fact]
    public async Task SendMessageAsync_OutsideResearchMode_NeverSearchesTheWeb()
    {
        var (coordinator, webSearch, _) = CreateResearchCoordinator(researchEnabled: true, configured: true);
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("a"));

        await coordinator.SendMessageAsync("q", 1, null, null, false);

        webSearch.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_OrchestratedResearchAnswer_IsSavedWithTheActiveModelAndItsWebSources()
    {
        var (coordinator, webSearch, _) = CreateResearchCoordinator(researchEnabled: true, configured: true);
        _aiService.SetupGet(s => s.ActiveModelId).Returns("llama3.2:3b");
        webSearch
            .Setup(s => s.SearchAsync("Plan launch", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResponse
            {
                Query = "Plan launch",
                Results = [new WebSearchResult { Title = "Launch guide", Url = "https://example.org/launch", Snippet = "Ship in phases." }]
            });
        _multiAgentOrchestrator
            .Setup(service => service.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AgentRole>>(),
                OrchestratorStrategy.Parallel,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult
            {
                Task = "Plan launch",
                Strategy = OrchestratorStrategy.Parallel,
                FinalAnswer = "Ship in phases [1].",
                IsSuccess = true
            });

        await coordinator.SendMessageAsync("Plan launch", 5, null, null, true, ChatOrchestrationMode.MultiAgentParallel);

        _conversationService.Verify(service => service.AddMessageAsync(
            5,
            "assistant",
            "Ship in phases [1].",
            It.IsAny<int?>(),
            It.IsAny<double?>(),
            "llama3.2:3b",
            It.Is<string?>(json => MessageCitations.ParseWebCitations(json).Single().Url == "https://example.org/launch")),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_FailedOrchestration_SavesItsNoticeWithoutAModelOrSources()
    {
        _aiService.SetupGet(s => s.ActiveModelId).Returns("llama3.2:3b");
        _multiAgentOrchestrator
            .Setup(service => service.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AgentRole>>(),
                OrchestratorStrategy.Parallel,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult
            {
                Task = "Plan launch",
                Strategy = OrchestratorStrategy.Parallel,
                IsSuccess = false
            });

        await _coordinator.SendMessageAsync("Plan launch", 5, null, null, false, ChatOrchestrationMode.MultiAgentParallel);

        _conversationService.Verify(service => service.AddMessageAsync(
            5, "assistant", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<double?>(), null, null), Times.Once);
    }

    // --- Localized notices ---

    [Theory]
    [InlineData(false, true, "Der Recherchemodus ist in den Einstellungen ausgeschaltet")]
    [InlineData(true, false, "Es ist kein Websuchanbieter konfiguriert.")]
    public async Task SendMessageAsync_InResearchMode_WhenWebSearchCannotRun_SaysWhyInTheUsersLanguage(
        bool researchEnabled, bool configured, string reason)
    {
        var (coordinator, _, _) = CreateResearchCoordinator(
            researchEnabled, configured, localization: ReswLocalization.For("de"));
        _chatService
            .Setup(s => s.SendMessageAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => CreateTokenStream("Lokale Antwort"));
        var notices = new List<NotificationRequestEventArgs>();
        coordinator.NotificationRequested += (_, e) => notices.Add(e);

        await coordinator.SendMessageAsync("Was hat sich geändert?", 1, null, null, true);
        await coordinator.SendMessageAsync("Und jetzt?", 1, null, null, true);

        var notice = notices.Should().ContainSingle().Subject;
        notice.Title.Should().Be("Keine Webquellen");
        notice.Message.Should().StartWith(reason);
    }

    [Fact]
    public async Task SendMessageAsync_InResearchMode_WhenTheSearchFindsNothing_SaysSoInTheUsersLanguage()
    {
        var (coordinator, webSearch, _) = CreateResearchCoordinator(
            researchEnabled: true, configured: true, localization: ReswLocalization.For("zh-CN"));
        webSearch
            .Setup(s => s.SearchAsync("q", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResponse { Query = "q", Results = [] });
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Returns(CreateTokenStream("a"));
        NotificationRequestEventArgs? notice = null;
        coordinator.NotificationRequested += (_, e) => notice = e;

        await coordinator.SendMessageAsync("q", 1, null, null, true);

        notice!.Title.Should().Be("没有网页来源");
        notice.Message.Should().Be("网络搜索没有返回结果，因此此回答仅来自 AI 模型和对话内容。");
    }

    [Fact]
    public async Task SendMessageAsync_WhenStopped_MarksTheAnswerInTheUsersLanguage()
    {
        var coordinator = CreateLocalizedCoordinator("es");
        _chatService
            .Setup(s => s.SendMessageAsync(1, "q", It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        var result = await coordinator.SendMessageAsync("q", 1, null, null, false);

        result.WasCancelled.Should().BeTrue();
        result.ResponseContent.Should().Be("[Generación detenida]");
    }

    [Fact]
    public async Task SendMessageAsync_FailedOrchestration_ExplainsInTheUsersLanguage()
    {
        var coordinator = CreateLocalizedCoordinator("fr");
        _multiAgentOrchestrator
            .Setup(service => service.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AgentRole>>(),
                OrchestratorStrategy.Parallel,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult
            {
                Task = "Plan launch",
                Strategy = OrchestratorStrategy.Parallel,
                IsSuccess = false,
                Errors = ["Critic: timeout"]
            });

        var result = await coordinator.SendMessageAsync(
            "Plan launch", 5, null, null, false, ChatOrchestrationMode.MultiAgentParallel);

        result.ResponseContent.Should().Be(
            "L'orchestration multi-agent n'a pas renvoyé de réponse exploitable.\n\n- Critic: timeout");
    }

    private (MessagingCoordinator Coordinator, Mock<IWebSearchService> WebSearch, Mock<ISettingsService> Settings)
        CreateResearchCoordinator(
            bool researchEnabled,
            bool configured,
            int maxSearchResults = 10,
            ILocalizationService? localization = null)
    {
        var webSearch = new Mock<IWebSearchService>();
        webSearch.SetupGet(s => s.IsConfigured).Returns(configured);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings
        {
            EnableResearchMode = researchEnabled,
            MaxSearchResults = maxSearchResults
        });

        var coordinator = new MessagingCoordinator(
            _chatService.Object,
            _conversationService.Object,
            _aiService.Object,
            _feedbackService.Object,
            _multiAgentOrchestrator.Object,
            webSearch.Object,
            settings.Object,
            localization);
        return (coordinator, webSearch, settings);
    }

    private static MessageEntity NewMessage(long id, string role, int sortOrder) => new()
    {
        Id = id,
        ConversationId = 1,
        Role = role,
        Content = role,
        SortOrder = sortOrder,
        Timestamp = DateTime.UtcNow
    };

    private static async IAsyncEnumerable<string> GatedStream(
        TaskCompletionSource gate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return "token";
        await gate.Task.WaitAsync(ct);
    }

    /// <summary>Yields one token, then one more after the stop, then observes the stop.</summary>
    private static async IAsyncEnumerable<string> StreamIgnoringStopOnce(
        TaskCompletionSource gate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return "before";
        await gate.Task;
        yield return "after";
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>A stream whose provider fails before the first token.</summary>
    private static async IAsyncEnumerable<string> FailingStream(Exception failure, bool fail = true)
    {
        await Task.Yield();
        if (fail)
        {
            throw failure;
        }

        yield break;
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

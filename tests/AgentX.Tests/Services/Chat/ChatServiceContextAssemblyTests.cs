using System.Runtime.CompilerServices;
using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
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
    private readonly Mock<IConversationSummaryService> _conversationSummaryService = new();
    private readonly ILogger _logger = Log.ForContext<AgentX.Core.Services.Chat.ChatService>();

    [Fact]
    public async Task SendMessageAndWaitAsync_UsesAssembledMessagesAndPrompt()
    {
        _settingsService
            .Setup(service => service.GetSettingsAsync())
            .ReturnsAsync(new AppSettings());

        _memoryService
            .Setup(service => service.GetMemoryContextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[Personal memory]");
        _memoryService
            .Setup(service => service.ExtractMemoriesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryContextAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("[Durable Conversation Summary]\nStored summary");

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
                It.Is<ContextAssemblyRequest>(request =>
                    request.ConversationId == 42 &&
                    request.MemoryContext != null &&
                    request.MemoryContext.Contains("[Personal memory]", StringComparison.Ordinal) &&
                    request.MemoryContext.Contains("[Durable Conversation Summary]", StringComparison.Ordinal)),
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
            _logger,
            conversationSummaryService: _conversationSummaryService.Object);

        var result = await sut.SendMessageAndWaitAsync(42, "Why is startup failing?");

        result.Should().Be("Hello world");
        _aiService.Verify(service => service.StreamChatAsync(
            It.Is<IReadOnlyList<ChatMessage>>(messages => ReferenceEquals(messages, assembledMessages) || messages.SequenceEqual(assembledMessages)),
            "Assembled prompt",
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAndWaitAsync_CapturesLatestContextInspectionSnapshot()
    {
        _settingsService
            .Setup(service => service.GetSettingsAsync())
            .ReturnsAsync(new AppSettings());

        _memoryService
            .Setup(service => service.GetMemoryContextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[Personal memory]");
        _memoryService
            .Setup(service => service.ExtractMemoriesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryContextAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("[Durable Conversation Summary]\nStored summary");

        var summaryInspection = new ConversationSummaryInspection
        {
            ConversationId = 42,
            PreviewText = "Investigating startup failures and retry strategies.",
            SummaryText = "Longer durable summary",
            KeyPoints = ["Startup path", "Retry strategy"],
            GeneratedAt = DateTime.UtcNow.AddMinutes(-5),
            LastRefreshedAt = DateTime.UtcNow.AddMinutes(-4),
            IsStale = false,
            PendingMessageCount = 0
        };

        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryInspection);

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
        var durableRecallResults = new List<ConversationRecallResult>
        {
            new()
            {
                MessageId = 77,
                ConversationId = 64,
                ConversationTitle = "Previous Startup Review",
                Role = "assistant",
                ContentPreview = "You previously traced this to the retry backoff window.",
                Timestamp = DateTime.UtcNow.AddHours(-3),
                SortOrder = 5,
                Similarity = 0.91f
            }
        };

        _contextAssemblyService
            .Setup(service => service.AssembleAsync(
                It.IsAny<ContextAssemblyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextAssemblyResult
            {
                Messages = assembledMessages,
                SystemPrompt = "Assembled prompt",
                DurableRecallResults = durableRecallResults,
                Diagnostics = new ContextAssemblyDiagnostics
                {
                    SelectedMessageCount = 1,
                    AnchorMessageCount = 1,
                    OverflowMessageCount = 3,
                    EstimatedMessageTokens = 96,
                    EstimatedPromptTokens = 244,
                    AddedOverflowSummary = true,
                    AddedDurableRecall = true,
                    RecalledMessageCount = 1
                }
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
            _logger,
            conversationSummaryService: _conversationSummaryService.Object);

        await sut.SendMessageAndWaitAsync(42, "Why is startup failing?");

        var snapshot = sut.GetLatestContextInspection(42);

        snapshot.Should().NotBeNull();
        snapshot!.ConversationId.Should().Be(42);
        snapshot.CurrentQuery.Should().Be("Why is startup failing?");
        snapshot.Diagnostics.SelectedMessageCount.Should().Be(1);
        snapshot.Diagnostics.OverflowMessageCount.Should().Be(3);
        snapshot.Summary.Should().BeEquivalentTo(summaryInspection);
        snapshot.RecallMatches.Should().ContainSingle(match =>
            match.MessageId == 77 &&
            match.ConversationId == 64 &&
            match.ConversationTitle == "Previous Startup Review");
        snapshot.ContextStoryText.Should().Be("Using a current durable summary, 1 recalled message from another conversation, and compressed overflow context.");
        snapshot.ContextStorySourceChips.Select(chip => chip.Label).Should().ContainInOrder(
        [
            "Current Summary",
            "1 Recall Match",
            "Compressed Overflow"
        ]);
        snapshot.AssemblyExplanation.Should().NotBeEmpty();
        snapshot.CompressionExplanation.Should().Contain("compressed overflow summary");
        snapshot.RecallExplanation.Should().Contain("added 1 recalled message");
    }

    [Fact]
    public async Task RegenerateLastResponseAsync_PassesConversationIdIntoContextAssembly()
    {
        _settingsService
            .Setup(service => service.GetSettingsAsync())
            .ReturnsAsync(new AppSettings());

        _memoryService
            .Setup(service => service.GetMemoryContextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryContextAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSummaryInspection?)null);

        var existingMessages = new List<MessageEntity>
        {
            new()
            {
                Id = 10,
                ConversationId = 42,
                Role = "user",
                Content = "Retry the last answer",
                SortOrder = 0,
                Timestamp = DateTime.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = 11,
                ConversationId = 42,
                Role = "assistant",
                Content = "Previous answer",
                SortOrder = 1,
                Timestamp = DateTime.UtcNow.AddMinutes(-1)
            }
        };

        var updatedMessages = new List<MessageEntity>
        {
            existingMessages[0]
        };

        _conversationService
            .Setup(service => service.GetMessagesAsync(42))
            .ReturnsAsync(updatedMessages);
        _conversationService
            .Setup(service => service.DeleteLastAssistantMessageAsync(42))
            .Returns(Task.CompletedTask);
        _conversationService
            .Setup(service => service.GetConversationAsync(42))
            .ReturnsAsync(new ConversationEntity
            {
                Id = 42,
                SystemPrompt = "Original prompt"
            });
        _conversationService
            .Setup(service => service.AddMessageAsync(
                42,
                "assistant",
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<double?>()))
            .Returns(Task.CompletedTask);

        _contextAssemblyService
            .Setup(service => service.AssembleAsync(
                It.Is<ContextAssemblyRequest>(request =>
                    request.ConversationId == 42 &&
                    request.CurrentQuery == "Retry the last answer"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextAssemblyResult
            {
                Messages = [ChatMessage.User("Retry the last answer")],
                SystemPrompt = "Assembled prompt",
                Diagnostics = new ContextAssemblyDiagnostics
                {
                    SelectedMessageCount = 1,
                    EstimatedMessageTokens = 24,
                    EstimatedPromptTokens = 84
                }
            });

        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("redo"));

        var sut = new AgentX.Core.Services.Chat.ChatService(
            _aiService.Object,
            _conversationService.Object,
            _settingsService.Object,
            _contextAssemblyService.Object,
            _memoryService.Object,
            _logger,
            conversationSummaryService: _conversationSummaryService.Object);

        await sut.RegenerateLastResponseAsync(42);

        _contextAssemblyService.Verify(service => service.AssembleAsync(
            It.Is<ContextAssemblyRequest>(request => request.ConversationId == 42),
            It.IsAny<CancellationToken>()), Times.Once);
        sut.GetLatestContextInspection(42).Should().NotBeNull();
        sut.GetLatestContextInspection(42)!.CurrentQuery.Should().Be("Retry the last answer");
    }

    [Fact]
    public async Task RefreshConversationSummaryInspectionAsync_UpdatesCachedSummaryInspection()
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
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryContextAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var initialInspection = new ConversationSummaryInspection
        {
            ConversationId = 42,
            PreviewText = "Initial durable summary",
            SummaryText = "Initial summary text",
            KeyPoints = ["Initial point"],
            GeneratedAt = DateTime.UtcNow.AddMinutes(-8),
            LastRefreshedAt = DateTime.UtcNow.AddMinutes(-7),
            IsStale = true,
            PendingMessageCount = 2
        };
        var refreshedInspection = initialInspection with
        {
            PreviewText = "Refreshed durable summary",
            SummaryText = "Refreshed summary text",
            KeyPoints = ["Refreshed point", "Another point"],
            GeneratedAt = DateTime.UtcNow.AddMinutes(-1),
            LastRefreshedAt = DateTime.UtcNow,
            IsStale = false,
            PendingMessageCount = 0
        };

        _conversationSummaryService
            .SetupSequence(service => service.GetConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialInspection)
            .ReturnsAsync(refreshedInspection);
        _conversationSummaryService
            .Setup(service => service.RefreshConversationSummaryAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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

        _contextAssemblyService
            .Setup(service => service.AssembleAsync(
                It.IsAny<ContextAssemblyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextAssemblyResult
            {
                Messages = [ChatMessage.User("Selected context message")],
                SystemPrompt = "Assembled prompt",
                Diagnostics = new ContextAssemblyDiagnostics
                {
                    SelectedMessageCount = 1,
                    EstimatedMessageTokens = 32,
                    EstimatedPromptTokens = 96
                }
            });

        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("Hello"));

        var sut = new AgentX.Core.Services.Chat.ChatService(
            _aiService.Object,
            _conversationService.Object,
            _settingsService.Object,
            _contextAssemblyService.Object,
            _memoryService.Object,
            _logger,
            conversationSummaryService: _conversationSummaryService.Object);

        await sut.SendMessageAndWaitAsync(42, "Why is startup failing?");

        var result = await sut.RefreshConversationSummaryInspectionAsync(42);

        result.Succeeded.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Summary.Should().BeEquivalentTo(refreshedInspection);
        sut.GetLatestContextInspection(42)!.Summary.Should().BeEquivalentTo(refreshedInspection);
    }

    [Fact]
    public async Task RefreshConversationSummaryInspectionAsync_FailurePreservesPriorSnapshot()
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
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryContextAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var initialInspection = new ConversationSummaryInspection
        {
            ConversationId = 42,
            PreviewText = "Initial durable summary",
            SummaryText = "Initial summary text",
            KeyPoints = ["Initial point"],
            GeneratedAt = DateTime.UtcNow.AddMinutes(-8),
            LastRefreshedAt = DateTime.UtcNow.AddMinutes(-7),
            IsStale = true,
            PendingMessageCount = 2
        };

        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialInspection);
        _conversationSummaryService
            .Setup(service => service.RefreshConversationSummaryAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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

        _contextAssemblyService
            .Setup(service => service.AssembleAsync(
                It.IsAny<ContextAssemblyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextAssemblyResult
            {
                Messages = [ChatMessage.User("Selected context message")],
                SystemPrompt = "Assembled prompt",
                Diagnostics = new ContextAssemblyDiagnostics
                {
                    SelectedMessageCount = 1,
                    EstimatedMessageTokens = 32,
                    EstimatedPromptTokens = 96
                }
            });

        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("Hello"));

        var sut = new AgentX.Core.Services.Chat.ChatService(
            _aiService.Object,
            _conversationService.Object,
            _settingsService.Object,
            _contextAssemblyService.Object,
            _memoryService.Object,
            _logger,
            conversationSummaryService: _conversationSummaryService.Object);

        await sut.SendMessageAndWaitAsync(42, "Why is startup failing?");
        var originalSnapshot = sut.GetLatestContextInspection(42);

        var result = await sut.RefreshConversationSummaryInspectionAsync(42);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Keeping the previous summary state");
        result.Snapshot.Should().BeSameAs(originalSnapshot);
        sut.GetLatestContextInspection(42).Should().BeSameAs(originalSnapshot);
        sut.GetLatestContextInspection(42)!.Summary.Should().BeEquivalentTo(initialInspection);
    }

    // ── In-place regeneration ──────────────────────────────────────────────
    // Regenerate used to delete the old answer and resend the prompt: the prompt was saved a
    // second time on every regenerate, and a stop or an error lost the old answer.

    [Fact]
    public async Task RegenerateResponseAsync_AnswersTheSavedPromptAgainAndReplacesTheOldAnswerAfterSaving()
    {
        var writes = SetupRegenerationThread(
            Message(10, "user", "Why is startup failing?", 0),
            Message(11, "assistant", "Previous answer", 1));
        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("New", " answer"));

        var tokens = await DrainAsync(CreateSut().RegenerateResponseAsync(42, 10));

        tokens.Should().Equal("New", " answer");
        writes.Should().Equal("add assistant: New answer", "delete 11");
        _contextAssemblyService.Verify(service => service.AssembleAsync(
            It.Is<ContextAssemblyRequest>(request =>
                request.CurrentQuery == "Why is startup failing?" &&
                request.ConversationMessages.Count == 1 &&
                request.ConversationMessages[0].Content == "Why is startup failing?"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegenerateResponseAsync_WhenStopped_KeepsTheOldAnswer()
    {
        var writes = SetupRegenerationThread(
            Message(10, "user", "Why is startup failing?", 0),
            Message(11, "assistant", "Previous answer", 1));
        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(CancelledStream());

        var act = () => DrainAsync(CreateSut().RegenerateResponseAsync(42, 10));

        await act.Should().ThrowAsync<OperationCanceledException>();
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task RegenerateResponseAsync_WhenLaterMessagesFollowTheAnswer_RefusesAndChangesNothing()
    {
        var writes = SetupRegenerationThread(
            Message(10, "user", "First question", 0),
            Message(11, "assistant", "First answer", 1),
            Message(12, "user", "Second question", 2));

        var act = () => DrainAsync(CreateSut().RegenerateResponseAsync(42, 10));

        await act.Should().ThrowAsync<InvalidOperationException>();
        writes.Should().BeEmpty();
        _aiService.Verify(service => service.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegenerateResponseAsync_ForAnUnansweredPrompt_AnswersItWithoutDeletingAnything()
    {
        var writes = SetupRegenerationThread(
            Message(10, "user", "First question", 0),
            Message(11, "assistant", "First answer", 1),
            Message(12, "user", "Unanswered question", 2));
        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("Answer"));

        await DrainAsync(CreateSut().RegenerateResponseAsync(42, 12));

        writes.Should().Equal("add assistant: Answer");
    }

    [Fact]
    public async Task SendMessageAsync_WithSupplementalContext_AddsItToThisReplysContextOnly()
    {
        SetupRegenerationThread(Message(10, "user", "What changed?", 0));
        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(StreamTokens("Answer"));

        await DrainAsync(CreateSut().SendMessageAsync(42, "What changed?", "[Web Search Results]\n[1] Notes", CancellationToken.None));

        _contextAssemblyService.Verify(service => service.AssembleAsync(
            It.Is<ContextAssemblyRequest>(request =>
                request.MemoryContext != null &&
                request.MemoryContext.Contains("[1] Notes", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(service => service.AddMessageAsync(
            42, "user", "What changed?", It.IsAny<int?>(), It.IsAny<double?>()), Times.Once);
    }

    /// <summary>
    /// Serves conversation 42 with <paramref name="messages"/> and records every add and
    /// delete it receives, in order.
    /// </summary>
    private List<string> SetupRegenerationThread(params MessageEntity[] messages)
    {
        var writes = new List<string>();
        _settingsService.Setup(service => service.GetSettingsAsync()).ReturnsAsync(new AppSettings());
        _memoryService
            .Setup(service => service.GetMemoryContextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _memoryService
            .Setup(service => service.ExtractMemoriesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _conversationSummaryService
            .Setup(service => service.GetConversationSummaryContextAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _conversationService
            .Setup(service => service.GetConversationAsync(42))
            .ReturnsAsync(new ConversationEntity { Id = 42, SystemPrompt = "Original prompt", Messages = messages.ToList() });
        _conversationService
            .Setup(service => service.AddMessageAsync(42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<double?>()))
            .Callback<long, string, string, int?, double?>((_, role, content, _, _) =>
            {
                if (role == "assistant")
                {
                    writes.Add($"add assistant: {content}");
                }
            })
            .Returns(Task.CompletedTask);
        _conversationService
            .Setup(service => service.DeleteMessageAsync(It.IsAny<long>()))
            .Callback<long>(id => writes.Add($"delete {id}"))
            .Returns(Task.CompletedTask);
        _contextAssemblyService
            .Setup(service => service.AssembleAsync(It.IsAny<ContextAssemblyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContextAssemblyRequest request, CancellationToken _) => new ContextAssemblyResult
            {
                Messages = request.ConversationMessages,
                SystemPrompt = request.SystemPrompt
            });
        return writes;
    }

    private AgentX.Core.Services.Chat.ChatService CreateSut() => new(
        _aiService.Object,
        _conversationService.Object,
        _settingsService.Object,
        _contextAssemblyService.Object,
        _memoryService.Object,
        _logger,
        conversationSummaryService: _conversationSummaryService.Object);

    private static MessageEntity Message(long id, string role, string content, int sortOrder) => new()
    {
        Id = id,
        ConversationId = 42,
        Role = role,
        Content = content,
        SortOrder = sortOrder,
        Timestamp = DateTime.UtcNow
    };

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> stream)
    {
        var tokens = new List<string>();
        await foreach (var token in stream)
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static async IAsyncEnumerable<string> CancelledStream()
    {
        await Task.Yield();
        yield return "partial";
        throw new OperationCanceledException();
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

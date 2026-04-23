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

using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class ChatViewModelTests
{
    private readonly Mock<IConversationCoordinator> _conversationCoordinator = new();
    private readonly Mock<IMessagingCoordinator> _messagingCoordinator = new();
    private readonly Mock<IVoiceCoordinator> _voiceCoordinator = new();
    private readonly Mock<IBranchingCoordinator> _branchingCoordinator = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IChatService> _chatService = new();
    private readonly Mock<IModelManager> _modelManager = new();
    private readonly Mock<ISystemPromptService> _systemPromptService = new();
    private readonly Mock<IConversationMemoryService> _memoryService = new();
    private readonly Mock<INotificationService> _notificationService = new();

    public ChatViewModelTests()
    {
        _branchingCoordinator
            .Setup(service => service.LoadBranchTreeAsync(It.IsAny<long>()))
            .ReturnsAsync((ConversationBranchTree?)null);
        _memoryService
            .Setup(service => service.GetSuggestedQuestionsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _memoryService
            .Setup(service => service.GetMemoryCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    [Fact]
    public async Task SelectConversationAsync_LoadsExistingContextInspection()
    {
        var snapshot = CreateInspectionSnapshot(42);

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(
            [
                new MessageSummary
                {
                    MessageId = 1001,
                    ConversationId = 42,
                    SortOrder = 1,
                    Role = "user",
                    Content = "Why is startup failing?",
                    Timestamp = DateTime.UtcNow.AddMinutes(-12)
                }
            ]);
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(snapshot);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        viewModel.ActiveConversationId.Should().Be(42);
        viewModel.HasConversationIntelligenceStrip.Should().BeTrue();
        viewModel.ConversationIntelligenceBadgeText.Should().Be("Current");
        viewModel.ConversationIntelligenceStatusText.Should().Be("Summary current • 2 key points available");
        viewModel.ShowConversationSummaryRefreshAction.Should().BeFalse();
        viewModel.HasContextInspection.Should().BeTrue();
        viewModel.HasContextStory.Should().BeTrue();
        viewModel.ContextStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
        viewModel.ContextStorySourceChips.Select(chip => chip.Label).Should().ContainInOrder(
        [
            "Current Summary",
            "1 Recall Match"
        ]);
        viewModel.ContextAssemblyMode.Should().Be("Structured context assembly");
        viewModel.ContextSelectedMessages.Should().Be("3");
        viewModel.ContextSummaryPreview.Should().Be("Focused on startup retries and backoff behavior.");
        viewModel.HasContextSummaryKeyPoints.Should().BeTrue();
        viewModel.ContextSummaryKeyPoints.Should().Contain("Retry path");
        viewModel.HasContextRecallItems.Should().BeTrue();
        viewModel.ContextRecallStatus.Should().Be("1 recalled message used");
        viewModel.ContextRecallItems.Should().ContainSingle();
        viewModel.ContextRecallItems[0].ConversationLabel.Should().Contain("Previous Startup Review");
    }

    [Fact]
    public async Task NewConversationAsync_ClearsContextInspectionState()
    {
        var snapshot = CreateInspectionSnapshot(42);

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(snapshot);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        await viewModel.NewConversationCommand.ExecuteAsync(null);

        viewModel.ActiveConversationId.Should().BeNull();
        viewModel.HasConversationIntelligenceStrip.Should().BeFalse();
        viewModel.HasContextInspection.Should().BeFalse();
        viewModel.ContextInspectionStatus.Should().Be("No generation context captured yet.");
        viewModel.HasContextStory.Should().BeFalse();
        viewModel.ContextStoryText.Should().BeEmpty();
        viewModel.ContextSummaryStatus.Should().Be("No durable summary captured yet.");
        viewModel.ContextRecallStatus.Should().Be("No durable recall context captured yet.");
        viewModel.ContextSummaryKeyPoints.Should().BeEmpty();
        viewModel.ContextRecallItems.Should().BeEmpty();
    }

    [Fact]
    public void StreamingCompletedEvent_AppliesContextInspectionSnapshot()
    {
        var snapshot = CreateInspectionSnapshot(84);
        var viewModel = CreateViewModel();

        _messagingCoordinator.Raise(
            coordinator => coordinator.StreamingCompleted += null,
            new StreamingCompletedEventArgs
            {
                ConversationId = 84,
                ConversationTitle = "Recovered Thread",
                ResponseContent = "Answer",
                TokenCount = 12,
                GenerationTimeMs = 48,
                ContextInspection = snapshot
            });

        viewModel.ActiveConversationId.Should().Be(84);
        viewModel.ActiveConversationTitle.Should().Be("Recovered Thread");
        viewModel.TokenCount.Should().Be(12);
        viewModel.GenerationTimeMs.Should().Be(48);
        viewModel.HasContextInspection.Should().BeTrue();
        viewModel.ContextInspectionStatus.Should().Be("Latest response context captured");
        viewModel.ContextStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
        viewModel.ContextSummaryPreview.Should().Be("Focused on startup retries and backoff behavior.");
        viewModel.ContextRecallStatus.Should().Be("1 recalled message used");
        viewModel.Conversations.Should().ContainSingle(item => item.Id == 84 && item.Title == "Recovered Thread");
    }

    [Fact]
    public async Task SelectConversationAsync_MapsStaleSummaryToStaleStrip()
    {
        var snapshot = CreateInspectionSnapshot(42, isSummaryStale: true, pendingMessageCount: 3);

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(snapshot);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        viewModel.ConversationIntelligenceBadgeText.Should().Be("Stale");
        viewModel.ConversationIntelligenceIsStale.Should().BeTrue();
        viewModel.ConversationIntelligenceStatusText.Should().Be("Summary stale • 3 newer messages not folded in");
        viewModel.ContextStoryText.Should().Be("Using a stale durable summary with 3 newer messages still outside it and 1 recalled message from another conversation.");
        viewModel.ContextStorySourceChips.Select(chip => chip.Label).Should().Contain("Stale Summary");
        viewModel.ShowConversationSummaryRefreshAction.Should().BeTrue();
    }

    [Fact]
    public async Task SelectConversationAsync_WithoutSnapshot_ShowsUnavailableStrip()
    {
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns((ChatContextInspectionSnapshot?)null);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        viewModel.HasConversationIntelligenceStrip.Should().BeTrue();
        viewModel.ConversationIntelligenceIsUnavailable.Should().BeTrue();
        viewModel.ConversationIntelligenceBadgeText.Should().Be("Unavailable");
        viewModel.ConversationIntelligenceStatusText.Should().Be("No conversation context captured yet");
        viewModel.ContextStoryText.Should().Be("No context story is available until Agent-X assembles a response for this conversation.");
        viewModel.HasContextStorySourceChips.Should().BeFalse();
        viewModel.ShowConversationSummaryRefreshAction.Should().BeTrue();
    }

    [Fact]
    public async Task SelectConversationAsync_WithLimitedVisibilitySnapshot_ShowsReducedContextStory()
    {
        var snapshot = ChatContextInspectionSnapshot.CreateLimited(
            42,
            "How should I proceed?",
            "provider_disconnected");

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(snapshot);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        viewModel.ConversationIntelligenceIsUnavailable.Should().BeTrue();
        viewModel.ContextStoryText.Should().Be("This response used a limited-visibility path, so only partial chat context details are available.");
        viewModel.ContextStorySourceChips.Select(chip => chip.Label).Should().ContainSingle().Which.Should().Be("Limited Visibility");
    }

    [Fact]
    public async Task SendMessageAsync_AttachesInlineContextNoteToCompletedAssistantMessage()
    {
        var snapshot = CreateInspectionSnapshot(42);

        _messagingCoordinator
            .Setup(service => service.SendMessageAsync("How should I proceed?", 42, null, null, false))
            .Returns(async () =>
            {
                _messagingCoordinator.Raise(
                    coordinator => coordinator.StreamingCompleted += null,
                    new StreamingCompletedEventArgs
                    {
                        ConversationId = 42,
                        ConversationTitle = "Startup Investigation",
                        ResponseContent = "Answer",
                        TokenCount = 12,
                        GenerationTimeMs = 48,
                        ContextInspection = snapshot,
                        AssistantMessageId = 5001
                    });

                await Task.Yield();
                return new SendMessageResult
                {
                    ConversationId = 42,
                    ResponseContent = "Answer",
                    TokenCount = 12,
                    GenerationTimeMs = 48,
                    ContextInspection = snapshot,
                    AssistantMessageId = 5001
                };
            });

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "How should I proceed?";

        await viewModel.SendMessageCommand.ExecuteAsync(null);

        var assistantMessage = viewModel.Messages.Last(message => message.IsAssistant);
        assistantMessage.MessageId.Should().Be(5001);
        assistantMessage.HasInlineContextNote.Should().BeTrue();
        assistantMessage.InlineContextStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
        assistantMessage.InlineContextStorySourceChips.Should().ContainInOrder("Current Summary", "1 Recall Match");
    }

    [Fact]
    public async Task SelectConversationAsync_ReappliesInlineContextNoteForAssistantMessageInSameSession()
    {
        var snapshot = CreateInspectionSnapshot(42);

        _messagingCoordinator
            .Setup(service => service.SendMessageAsync("How should I proceed?", 42, null, null, false))
            .Returns(async () =>
            {
                _messagingCoordinator.Raise(
                    coordinator => coordinator.StreamingCompleted += null,
                    new StreamingCompletedEventArgs
                    {
                        ConversationId = 42,
                        ConversationTitle = "Startup Investigation",
                        ResponseContent = "Answer",
                        TokenCount = 12,
                        GenerationTimeMs = 48,
                        ContextInspection = snapshot,
                        AssistantMessageId = 5001
                    });

                await Task.Yield();
                return new SendMessageResult
                {
                    ConversationId = 42,
                    ResponseContent = "Answer",
                    TokenCount = 12,
                    GenerationTimeMs = 48,
                    ContextInspection = snapshot,
                    AssistantMessageId = 5001
                };
            });

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(
            [
                new MessageSummary
                {
                    MessageId = 1001,
                    ConversationId = 42,
                    SortOrder = 0,
                    Role = "user",
                    Content = "How should I proceed?",
                    Timestamp = DateTime.UtcNow.AddMinutes(-2)
                },
                new MessageSummary
                {
                    MessageId = 5001,
                    ConversationId = 42,
                    SortOrder = 1,
                    Role = "assistant",
                    Content = "Answer",
                    Timestamp = DateTime.UtcNow.AddMinutes(-1)
                }
            ]);
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(snapshot);

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "How should I proceed?";

        await viewModel.SendMessageCommand.ExecuteAsync(null);
        await viewModel.NewConversationCommand.ExecuteAsync(null);

        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        var assistantMessage = viewModel.Messages.Single(message => message.MessageId == 5001);
        assistantMessage.HasInlineContextNote.Should().BeTrue();
        assistantMessage.InlineContextStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
        assistantMessage.InlineContextStorySourceChips.Should().Contain("Current Summary");
    }

    [Fact]
    public async Task SendMessageAsync_KeepsInlineContextNoteHiddenWhileAssistantMessageIsStreaming()
    {
        var completion = new TaskCompletionSource<SendMessageResult>();

        _messagingCoordinator
            .Setup(service => service.SendMessageAsync("How should I proceed?", 42, null, null, false))
            .Returns(completion.Task);

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "How should I proceed?";

        var sendTask = viewModel.SendMessageCommand.ExecuteAsync(null);
        await Task.Yield();

        var assistantMessage = viewModel.Messages.Last(message => message.IsAssistant);
        assistantMessage.IsStreaming.Should().BeTrue();
        assistantMessage.HasInlineContextNote.Should().BeFalse();

        var snapshot = CreateInspectionSnapshot(42);
        _messagingCoordinator.Raise(
            coordinator => coordinator.StreamingCompleted += null,
            new StreamingCompletedEventArgs
            {
                ConversationId = 42,
                ConversationTitle = "Startup Investigation",
                ResponseContent = "Answer",
                TokenCount = 12,
                GenerationTimeMs = 48,
                ContextInspection = snapshot,
                AssistantMessageId = 5001
            });
        completion.SetResult(new SendMessageResult
        {
            ConversationId = 42,
            ResponseContent = "Answer",
            TokenCount = 12,
            GenerationTimeMs = 48,
            ContextInspection = snapshot,
            AssistantMessageId = 5001
        });

        await sendTask;

        assistantMessage.HasInlineContextNote.Should().BeTrue();
    }

    [Fact]
    public void ChatWithoutActiveConversation_HidesIntelligenceStrip()
    {
        var viewModel = CreateViewModel();

        viewModel.ActiveConversationId.Should().BeNull();
        viewModel.HasConversationIntelligenceStrip.Should().BeFalse();
        viewModel.ConversationIntelligenceBadgeText.Should().BeEmpty();
        viewModel.ConversationIntelligenceStatusText.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshConversationSummaryAsync_SuccessUpdatesSnapshotState()
    {
        var staleSnapshot = CreateInspectionSnapshot(42, isSummaryStale: true, pendingMessageCount: 2);
        var refreshedSnapshot = CreateInspectionSnapshot(42);

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(staleSnapshot);
        _chatService
            .Setup(service => service.RefreshConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationSummaryRefreshResult.Success(refreshedSnapshot));

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        await viewModel.RefreshConversationSummaryCommand.ExecuteAsync(null);

        viewModel.IsRefreshingConversationSummary.Should().BeFalse();
        viewModel.HasConversationSummaryRefreshError.Should().BeFalse();
        viewModel.ConversationIntelligenceIsCurrent.Should().BeTrue();
        viewModel.ConversationIntelligenceBadgeText.Should().Be("Current");
        viewModel.ConversationIntelligenceStatusText.Should().Be("Summary current • 2 key points available");
        viewModel.ShowConversationSummaryRefreshAction.Should().BeFalse();
        viewModel.ContextSummaryPreview.Should().Be("Focused on startup retries and backoff behavior.");
    }

    [Fact]
    public async Task RefreshConversationSummaryAsync_FailurePreservesStateAndShowsRetryMessage()
    {
        var staleSnapshot = CreateInspectionSnapshot(42, isSummaryStale: true, pendingMessageCount: 2);

        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(staleSnapshot);
        _chatService
            .Setup(service => service.RefreshConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationSummaryRefreshResult.Failure(
                staleSnapshot,
                "Summary refresh failed. Keeping the previous summary state."));

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        await viewModel.RefreshConversationSummaryCommand.ExecuteAsync(null);

        viewModel.IsRefreshingConversationSummary.Should().BeFalse();
        viewModel.HasConversationSummaryRefreshError.Should().BeTrue();
        viewModel.ConversationSummaryRefreshStatusText.Should().Be("Summary refresh failed. Keeping the previous summary state.");
        viewModel.ConversationIntelligenceIsStale.Should().BeTrue();
        viewModel.ConversationIntelligenceStatusText.Should().Be("Summary refresh failed. Keeping the previous summary state.");
        viewModel.ShowConversationSummaryRefreshAction.Should().BeTrue();
        viewModel.ConversationSummaryRefreshActionText.Should().Be("Retry Summary");
        viewModel.ContextSummaryPreview.Should().Be("Focused on startup retries and backoff behavior.");
    }

    private ChatViewModel CreateViewModel() =>
        new(
            _conversationCoordinator.Object,
            _messagingCoordinator.Object,
            _voiceCoordinator.Object,
            _branchingCoordinator.Object,
            _aiService.Object,
            _chatService.Object,
            _modelManager.Object,
            _systemPromptService.Object,
            _memoryService.Object,
            _notificationService.Object);

    private static ChatContextInspectionSnapshot CreateInspectionSnapshot(
        long conversationId,
        bool isSummaryStale = false,
        int pendingMessageCount = 0,
        bool limitedVisibility = false,
        string? limitedVisibilityReason = null) =>
        new()
        {
            ConversationId = conversationId,
            CapturedAt = DateTime.UtcNow.AddMinutes(-2),
            CurrentQuery = "How should I proceed?",
            HasLimitedVisibility = limitedVisibility,
            LimitedVisibilityReason = limitedVisibilityReason,
            Diagnostics = new ContextAssemblyDiagnostics
            {
                SelectedMessageCount = 3,
                AnchorMessageCount = 1,
                OverflowMessageCount = 2,
                EstimatedMessageTokens = 88,
                EstimatedPromptTokens = 216
            },
            Summary = new ConversationSummaryInspection
            {
                ConversationId = conversationId,
                PreviewText = "Focused on startup retries and backoff behavior.",
                SummaryText = "Durable summary text",
                KeyPoints = ["Retry path", "Backoff window"],
                GeneratedAt = DateTime.UtcNow.AddMinutes(-10),
                LastRefreshedAt = DateTime.UtcNow.AddMinutes(-9),
                IsStale = isSummaryStale,
                PendingMessageCount = pendingMessageCount
            },
            RecallMatches =
            [
                new ChatContextRecallInspectionItem
                {
                    ConversationId = 7,
                    MessageId = 99,
                    ConversationTitle = "Previous Startup Review",
                    Role = "assistant",
                    ContentPreview = "You previously traced this to the retry backoff window.",
                    Timestamp = DateTime.UtcNow.AddHours(-4),
                    Similarity = 0.93f
                }
            ],
            AssemblyExplanation = "Agent-X selected a bounded subset of the thread and evaluated overflow context against the remaining budget.",
            CompressionExplanation = "No overflow summary was added for this response.",
            RecallExplanation = "Agent-X added 1 recalled message from another conversation as supporting context."
        };
}

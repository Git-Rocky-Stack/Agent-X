using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using AgentX.Tests.Helpers;
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
    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();
    private readonly Mock<IPrivacyStatusService> _privacyStatusService = new();
    private readonly ILocalizationService _localization = ReswLocalization.For("en-US");

    public ChatViewModelTests()
    {
        _branchingCoordinator
            .Setup(service => service.LoadBranchTreeAsync(It.IsAny<long>()))
            .ReturnsAsync((ConversationBranchTree?)null);
        _privacyStatusService
            .Setup(service => service.GetChatMessageRecipientsAsync(
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PromptRecipient>());
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
    public async Task StreamingCompletedEvent_ForTheFirstSendOfANewConversation_AdoptsItAndAppliesItsContext()
    {
        // The first send of a blank chat learns its conversation id from the completion: the
        // coordinator creates the conversation as it sends.
        var snapshot = CreateInspectionSnapshot(84);
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync("Recover it", null, null, null, false))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.UserInput = "Recover it";
        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);

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
        inFlight.SetResult(new SendMessageResult { ConversationId = 84, ResponseContent = "Answer" });
        await sending;

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
    public async Task SendMessageAsync_WithMultiAgentMode_RoutesThroughOrchestrationMode()
    {
        _messagingCoordinator
            .Setup(service => service.SendMessageAsync(
                "How should I proceed?",
                42,
                null,
                null,
                false,
                ChatOrchestrationMode.MultiAgentParallel))
            .ReturnsAsync(new SendMessageResult
            {
                ConversationId = 42,
                ResponseContent = "# Multi-Agent Synthesis",
                TokenCount = 32,
                GenerationTimeMs = 75
            });

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.OrchestrationMode = ChatOrchestrationMode.MultiAgentParallel;
        viewModel.UserInput = "How should I proceed?";

        await viewModel.SendMessageCommand.ExecuteAsync(null);

        _messagingCoordinator.Verify(service => service.SendMessageAsync(
            "How should I proceed?",
            42,
            null,
            null,
            false,
            ChatOrchestrationMode.MultiAgentParallel), Times.Once);
        _messagingCoordinator.Verify(service => service.SendMessageAsync(
            It.IsAny<string>(),
            It.IsAny<long?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>()), Times.Never);
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
    public async Task InspectInlineContextAsync_OpensInspectorForSelectedMessageWithoutChangingStripState()
    {
        var olderSnapshot = CreateInspectionSnapshot(42, isSummaryStale: true, pendingMessageCount: 2);
        var latestSnapshot = CreateInspectionSnapshot(42);

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
                        ResponseContent = "Earlier answer",
                        TokenCount = 12,
                        GenerationTimeMs = 48,
                        ContextInspection = olderSnapshot,
                        AssistantMessageId = 5001
                    });

                await Task.Yield();
                return new SendMessageResult
                {
                    ConversationId = 42,
                    ResponseContent = "Earlier answer",
                    TokenCount = 12,
                    GenerationTimeMs = 48,
                    ContextInspection = olderSnapshot,
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
                    Content = "Earlier answer",
                    Timestamp = DateTime.UtcNow.AddMinutes(-1)
                }
            ]);
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(latestSnapshot);

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

        viewModel.ConversationIntelligenceBadgeText.Should().Be("Current");
        viewModel.ConversationIntelligenceStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
        viewModel.IsContextInspectorOpen.Should().BeFalse();

        viewModel.InspectInlineContextCommand.Execute(assistantMessage);

        viewModel.IsContextInspectorOpen.Should().BeTrue();
        viewModel.ContextInspectionStatus.Should().Be("Context captured for the selected assistant response.");
        viewModel.ContextStoryText.Should().Be("Using a stale durable summary with 2 newer messages still outside it and 1 recalled message from another conversation.");
        viewModel.ConversationIntelligenceBadgeText.Should().Be("Current");
        viewModel.ConversationIntelligenceStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
    }

    [Fact]
    public async Task InspectInlineContextAsync_WithoutStoredSnapshot_DoesNothing()
    {
        var latestSnapshot = CreateInspectionSnapshot(42);

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
                    MessageId = 7777,
                    ConversationId = 42,
                    SortOrder = 1,
                    Role = "assistant",
                    Content = "Answer without stored session context",
                    Timestamp = DateTime.UtcNow.AddMinutes(-1)
                }
            ]);
        _chatService
            .Setup(service => service.GetLatestContextInspection(42))
            .Returns(latestSnapshot);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        var assistantMessage = viewModel.Messages.Single(message => message.MessageId == 7777);

        viewModel.InspectInlineContextCommand.Execute(assistantMessage);

        viewModel.IsContextInspectorOpen.Should().BeFalse();
        viewModel.ContextInspectionStatus.Should().Be("Latest response context captured");
        viewModel.ContextStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
        viewModel.ConversationIntelligenceStoryText.Should().Be("Using a current durable summary and 1 recalled message from another conversation.");
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
        viewModel.ConversationIntelligenceStoryText.Should().BeEmpty();
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

    // ── Model selection ──────────────────────────────────────────────────────
    // The chat header's model picker is the only way to switch models mid-session.
    // It bound ItemsSource but never surfaced the selection, so choosing a model was
    // silently discarded; these cover the selection path end to end.

    [Fact]
    public async Task SelectModelCommand_ActivatesAndPersistsTheChosenModel()
    {
        var viewModel = CreateViewModel();
        viewModel.AvailableModels.Add(new AiModel { Id = "llama-3.2-3b", Name = "Llama 3.2 3B" });

        await viewModel.SelectModelCommand.ExecuteAsync("llama-3.2-3b");

        viewModel.ActiveModelName.Should().Be("Llama 3.2 3B");
        _aiService.Verify(
            service => service.SetActiveModelAsync("llama-3.2-3b", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectModelCommand_WithUnknownModelId_LeavesTheActiveModelUnchanged()
    {
        var viewModel = CreateViewModel();

        await viewModel.SelectModelCommand.ExecuteAsync("not-installed");

        viewModel.ActiveModelName.Should().Be("No model selected");
        _aiService.Verify(
            service => service.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Navigation payload ───────────────────────────────────────────────────
    // Jump-To lists individual conversations. Selecting one used to open Chat on whatever
    // thread happened to be active, so the chosen conversation never opened.

    [Fact]
    public async Task ApplyNavigationParameterAsync_WithAConversationId_OpensThatConversation()
    {
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });

        await viewModel.ApplyNavigationParameterAsync(42L);

        viewModel.ActiveConversationId.Should().Be(42);
    }

    [Fact]
    public async Task ApplyNavigationParameterAsync_WithNoPayload_DoesNotChangeTheActiveConversation()
    {
        var viewModel = CreateViewModel();

        await viewModel.ApplyNavigationParameterAsync(null);

        viewModel.ActiveConversationId.Should().BeNull();
        _conversationCoordinator.Verify(
            service => service.LoadMessagesAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task NewConversationAsync_MidGeneration_CancelsTheStreamItLeavesBehind()
    {
        // Ctrl+N and the palette's "New Conversation" now reach this from any page, so a
        // generation started in the previous thread can still be running when the blank one
        // opens. Left running, it finishes into OnStreamingCompleted's adopt branch, which
        // sees a null ActiveConversationId, takes the finished stream's id back, and files a
        // second sidebar row: the new conversation silently becomes the one just left.
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "explain the retry backoff";

        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);
        viewModel.IsGenerating.Should().BeTrue(
            "the send has to still be in flight for the rest of this test to mean anything");

        await viewModel.NewConversationCommand.ExecuteAsync(null);

        _messagingCoordinator.Verify(coordinator => coordinator.StopGenerationAsync(), Times.Once);
        viewModel.IsGenerating.Should().BeFalse(
            "the view model must not keep claiming it generates for a thread it has left");

        inFlight.SetResult(new SendMessageResult { ConversationId = 42, WasCancelled = true });
        await sending;

        viewModel.ActiveConversationId.Should().BeNull(
            "the blank conversation must not turn back into the thread the operator left");
    }

    [Fact]
    public async Task NewConversationAsync_MidGeneration_DiscardsAStreamThatCompletesDespiteTheCancel()
    {
        // Cancellation is cooperative. A stream that has already left the token loop and
        // reached MessagingCoordinator.cs:198 raises StreamingCompleted even though the cancel
        // has landed, so cancelling alone cannot close the adopt hole: the handler still sees a
        // null ActiveConversationId, takes the finished stream's id back and files a sidebar
        // row for it. The blank conversation must survive that arrival.
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "explain the retry backoff";

        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);
        viewModel.IsGenerating.Should().BeTrue(
            "the send has to still be in flight for the rest of this test to mean anything");

        await viewModel.NewConversationCommand.ExecuteAsync(null);

        // The cancel lost the race: the coordinator raises completion anyway.
        _messagingCoordinator.Raise(
            coordinator => coordinator.StreamingCompleted += null,
            new StreamingCompletedEventArgs
            {
                ConversationId = 42,
                ConversationTitle = "Startup Investigation",
                ResponseContent = "Answer the operator walked away from",
                TokenCount = 12,
                GenerationTimeMs = 48,
                ContextInspection = CreateInspectionSnapshot(42)
            });

        viewModel.ActiveConversationId.Should().BeNull(
            "the blank conversation must not turn back into the thread the operator left");
        viewModel.ActiveConversationTitle.Should().Be("New Conversation");
        viewModel.Conversations.Should().BeEmpty(
            "a stream the operator walked away from must not file a sidebar row");
        viewModel.TokenCount.Should().Be(0,
            "the abandoned stream's tokens belong to the old thread, not the blank one");
        viewModel.HasContextInspection.Should().BeFalse(
            "the blank conversation has assembled no context of its own yet");

        inFlight.SetResult(new SendMessageResult { ConversationId = 42 });
        await sending;

        viewModel.ActiveConversationId.Should().BeNull();
    }

    [Fact]
    public async Task NewConversationAsync_MidGeneration_DiscardsTheCancelledStreamsContextInspection()
    {
        // The cancel arm at MessagingCoordinator.cs:217 back-fills ContextInspection from the
        // conversation it was generating for, and the send continuation applies it with no
        // check on which thread is now on screen. That needs no lost race at all: a clean
        // cancel re-stamps the old thread's context story onto the blank conversation.
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "explain the retry backoff";

        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);
        await viewModel.NewConversationCommand.ExecuteAsync(null);

        inFlight.SetResult(new SendMessageResult
        {
            ConversationId = 42,
            WasCancelled = true,
            ContextInspection = CreateInspectionSnapshot(42)
        });
        await sending;

        viewModel.HasContextInspection.Should().BeFalse(
            "the blank conversation must not inherit the abandoned thread's context story");
        viewModel.ContextInspectionStatus.Should().Be("No generation context captured yet.");
        viewModel.ActiveConversationId.Should().BeNull();
    }

    [Fact]
    public async Task SelectConversationAsync_MidGeneration_DiscardsTheStreamItLeavesBehind()
    {
        // Ctrl+N is not the only way to move the screen off a running generation: picking
        // another thread in the sidebar does it too, and that path never cancelled anything.
        // The adopt branch then drags the screen back to the generating thread and files a
        // duplicate row for a conversation the sidebar already lists.
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());

        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 42,
            Title = "Startup Investigation",
            UpdatedAt = DateTime.UtcNow
        });
        viewModel.Conversations.Add(new ConversationListItem
        {
            Id = 84,
            Title = "Recovered Thread",
            UpdatedAt = DateTime.UtcNow
        });
        viewModel.ActiveConversationId = 84;
        viewModel.UserInput = "explain the retry backoff";

        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);
        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        _messagingCoordinator.Raise(
            coordinator => coordinator.StreamingCompleted += null,
            new StreamingCompletedEventArgs
            {
                ConversationId = 84,
                ConversationTitle = "Recovered Thread",
                ResponseContent = "Answer for the thread the operator left",
                TokenCount = 12,
                GenerationTimeMs = 48
            });

        viewModel.ActiveConversationId.Should().Be(42,
            "the thread the operator picked must stay on screen");
        viewModel.Conversations.Where(item => item.Id == 84).Should().HaveCount(1,
            "the adopt branch must not file a second row for a conversation already listed");

        inFlight.SetResult(new SendMessageResult { ConversationId = 84 });
        await sending;

        viewModel.ActiveConversationId.Should().Be(42);
    }

    // --- Persisted identity of messages sent this session ---
    // Bubbles created while chatting used to keep MessageId 0 and SortOrder 0 forever, so
    // Save & Resend truncated from SortOrder 0 (wiping all but the first message), Delete
    // skipped the database but reported success, Branch failed on "message 0", and Regenerate
    // left the old prompt behind and saved it again.

    [Fact]
    public async Task SendMessageAsync_StampsThePersistedIdsOnBothBubbles()
    {
        var viewModel = await SendFirstExchangeAsync();

        var prompt = viewModel.Messages[0];
        var answer = viewModel.Messages[1];
        prompt.MessageId.Should().Be(1001);
        prompt.SortOrder.Should().Be(4);
        prompt.ConversationId.Should().Be(42);
        answer.MessageId.Should().Be(1002);
        answer.SortOrder.Should().Be(5);
        answer.IsStreaming.Should().BeFalse();
        viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task SaveEditMessageAsync_OnAMessageSentThisSession_CutsFromItsOwnRowAndResendsOnce()
    {
        var viewModel = await SendFirstExchangeAsync();
        _conversationCoordinator
            .Setup(coordinator => coordinator.DeleteMessageAndFollowingAsync(42, 1001))
            .ReturnsAsync(true);
        SetupSend("Why does startup retry?", new SendMessageResult
        {
            ConversationId = 42,
            ResponseContent = "Because of the backoff.",
            UserMessageId = 1003,
            AssistantMessageId = 1004
        });

        viewModel.UserInput = "a draft the operator is still typing";
        var prompt = viewModel.Messages[0];
        viewModel.StartEditMessageCommand.Execute(prompt);
        prompt.EditContent = "Why does startup retry?";
        await viewModel.SaveEditMessageCommand.ExecuteAsync(prompt);

        _conversationCoordinator.Verify(
            coordinator => coordinator.DeleteMessageAndFollowingAsync(42, 1001), Times.Once);
        _messagingCoordinator.Verify(
            coordinator => coordinator.SendMessageAsync("Why does startup retry?", 42, null, null, false), Times.Once);
        viewModel.Messages.Select(message => message.Content)
            .Should().Equal("Why does startup retry?", "Because of the backoff.");
        viewModel.Messages[0].MessageId.Should().Be(1003);
        viewModel.UserInput.Should().Be("a draft the operator is still typing",
            "resending an edit must not go through, or clear, the input box");
    }

    [Fact]
    public async Task SaveEditMessageAsync_WhenTheCutFails_DoesNotResendAndSaysSo()
    {
        var viewModel = await SendFirstExchangeAsync();
        _conversationCoordinator
            .Setup(coordinator => coordinator.DeleteMessageAndFollowingAsync(42, 1001))
            .ReturnsAsync(false);

        var prompt = viewModel.Messages[0];
        viewModel.StartEditMessageCommand.Execute(prompt);
        prompt.EditContent = "Rewritten";
        await viewModel.SaveEditMessageCommand.ExecuteAsync(prompt);

        _messagingCoordinator.Verify(
            coordinator => coordinator.SendMessageAsync("Rewritten", It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Never);
        viewModel.Messages.Should().HaveCount(2);
        prompt.IsEditing.Should().BeTrue("the edit stays open so it can be retried");
        _notificationService.Verify(
            service => service.ShowError("Edit not sent", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DeleteMessageAsync_OnAMessageSentThisSession_DeletesItsRow()
    {
        var viewModel = await SendFirstExchangeAsync();
        _messagingCoordinator.Setup(coordinator => coordinator.DeleteMessageAsync(1002)).ReturnsAsync(true);

        var answer = viewModel.Messages[1];
        await viewModel.DeleteMessageCommand.ExecuteAsync(answer);

        _messagingCoordinator.Verify(coordinator => coordinator.DeleteMessageAsync(1002), Times.Once);
        viewModel.Messages.Should().NotContain(answer);
        _notificationService.Verify(
            service => service.ShowInfo("Message deleted", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DeleteMessageAsync_WhenTheDeleteFails_KeepsTheMessageAndSaysSo()
    {
        var viewModel = await SendFirstExchangeAsync();
        _messagingCoordinator.Setup(coordinator => coordinator.DeleteMessageAsync(1002)).ReturnsAsync(false);

        var answer = viewModel.Messages[1];
        await viewModel.DeleteMessageCommand.ExecuteAsync(answer);

        viewModel.Messages.Should().Contain(answer);
        _notificationService.Verify(
            service => service.ShowError("Delete failed", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _notificationService.Verify(
            service => service.ShowInfo("Message deleted", It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteMessageAsync_OnAMessageThatWasNeverSaved_OnlyRemovesItFromTheScreenAndSaysSo()
    {
        // The offline fallback streams an answer without persisting anything.
        SetupSend("Are you there?", new SendMessageResult { ConversationId = 42, ResponseContent = "Offline help" });
        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "Are you there?";
        await viewModel.SendMessageCommand.ExecuteAsync(null);

        var answer = viewModel.Messages[1];
        await viewModel.DeleteMessageCommand.ExecuteAsync(answer);

        _messagingCoordinator.Verify(coordinator => coordinator.DeleteMessageAsync(It.IsAny<long>()), Times.Never);
        viewModel.Messages.Should().NotContain(answer);
        _notificationService.Verify(
            service => service.ShowInfo("Message removed", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task BranchFromMessageAsync_OnAMessageSentThisSession_BranchesFromItsRow()
    {
        var viewModel = await SendFirstExchangeAsync();
        _branchingCoordinator
            .Setup(coordinator => coordinator.BranchFromMessageAsync(42, 1001, null))
            .ReturnsAsync(new BranchResult { BranchConversationId = 77, Title = "Branch" });

        await viewModel.BranchFromMessageCommand.ExecuteAsync(viewModel.Messages[0].MessageId);

        _branchingCoordinator.Verify(coordinator => coordinator.BranchFromMessageAsync(42, 1001, null), Times.Once);
    }

    [Fact]
    public async Task BranchFromMessageAsync_OnAMessageThatWasNeverSaved_DoesNotAskTheCoordinator()
    {
        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;

        await viewModel.BranchFromMessageCommand.ExecuteAsync(0L);

        _branchingCoordinator.Verify(
            coordinator => coordinator.BranchFromMessageAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>()),
            Times.Never);
        _notificationService.Verify(
            service => service.ShowInfo("Cannot branch here", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task RegenerateMessageAsync_OnTheLatestResponse_AnswersTheSavedPromptAgainWithoutResendingIt()
    {
        var viewModel = await SendFirstExchangeAsync();
        _messagingCoordinator
            .Setup(coordinator => coordinator.RegenerateResponseAsync(
                42, 1001, "How should I proceed?", null, ChatOrchestrationMode.Standard))
            .ReturnsAsync(new SendMessageResult
            {
                ConversationId = 42,
                ResponseContent = "A better answer",
                UserMessageId = 1001,
                AssistantMessageId = 1003,
                AssistantMessageSortOrder = 6
            });

        var prompt = viewModel.Messages[0];
        await viewModel.RegenerateMessageCommand.ExecuteAsync(viewModel.Messages[1]);

        _messagingCoordinator.Verify(
            coordinator => coordinator.SendMessageAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Once,
            "regenerating must not send, and so persist, the prompt a second time");
        viewModel.Messages.Should().HaveCount(2);
        viewModel.Messages[0].Should().BeSameAs(prompt);
        viewModel.Messages[1].Content.Should().Be("A better answer");
        viewModel.Messages[1].MessageId.Should().Be(1003);
        viewModel.IsGenerating.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RegenerateMessageAsync_WhenStoppedOrFailed_KeepsThePreviousAnswer(bool wasCancelled, bool hadError)
    {
        var viewModel = await SendFirstExchangeAsync();
        _messagingCoordinator
            .Setup(coordinator => coordinator.RegenerateResponseAsync(
                42, 1001, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ChatOrchestrationMode>()))
            .ReturnsAsync(new SendMessageResult
            {
                ConversationId = 42,
                ResponseContent = "partial",
                WasCancelled = wasCancelled,
                HadError = hadError,
                UserMessageId = 1001
            });

        var previousAnswer = viewModel.Messages[1];
        await viewModel.RegenerateMessageCommand.ExecuteAsync(previousAnswer);

        viewModel.Messages.Should().HaveCount(2);
        viewModel.Messages[1].Should().BeSameAs(previousAnswer);
        previousAnswer.Content.Should().Be("Answer");
        previousAnswer.MessageId.Should().Be(1002);
        viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task RegenerateMessageAsync_OnAnEarlierResponse_ExplainsInsteadOfRegenerating()
    {
        var viewModel = await SendFirstExchangeAsync();
        viewModel.Messages.Add(new ChatMessageItem { Role = "user", IsUser = true, Content = "Follow-up", MessageId = 1005 });
        viewModel.Messages.Add(new ChatMessageItem { Role = "assistant", IsAssistant = true, Content = "Later", MessageId = 1006 });

        await viewModel.RegenerateMessageCommand.ExecuteAsync(viewModel.Messages[1]);

        _messagingCoordinator.Verify(
            coordinator => coordinator.RegenerateResponseAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ChatOrchestrationMode>()),
            Times.Never);
        viewModel.Messages.Should().HaveCount(4);
    }

    [Fact]
    public async Task RegenerateAndEdit_WhileAResponseIsGenerating_DoNotStartASecondGeneration()
    {
        // Both used to call SendMessageAsync directly, bypassing the Send guard: a second
        // generation started mid-stream and took over the coordinator's cancellation source.
        var viewModel = await SendFirstExchangeAsync();
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync("Next question", 42, null, null, false))
            .Returns(inFlight.Task);
        viewModel.UserInput = "Next question";
        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);

        await viewModel.RegenerateMessageCommand.ExecuteAsync(viewModel.Messages[1]);
        var prompt = viewModel.Messages[0];
        viewModel.StartEditMessageCommand.Execute(prompt);
        prompt.EditContent = "Rewritten";
        await viewModel.SaveEditMessageCommand.ExecuteAsync(prompt);

        _messagingCoordinator.Verify(
            coordinator => coordinator.RegenerateResponseAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ChatOrchestrationMode>()),
            Times.Never);
        _messagingCoordinator.Verify(
            coordinator => coordinator.SendMessageAsync("Rewritten", It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Never);
        _conversationCoordinator.Verify(
            coordinator => coordinator.DeleteMessageAndFollowingAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
        _notificationService.Verify(
            service => service.ShowInfo("Response in progress", It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));

        inFlight.SetResult(new SendMessageResult { ConversationId = 42, ResponseContent = "Done" });
        await sending;
    }

    // --- Generation state after every outcome ---
    // A stop returned without any event and a thread switch discarded the completion, and in
    // both cases IsGenerating stayed true: Send hidden, Stop pointing at nothing, Enter blocked.

    [Fact]
    public async Task StopGeneration_WhenTheSendReturnsCancelled_ReleasesTheChatAndMarksTheAnswer()
    {
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync("Long question", 42, null, null, false))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "Long question";
        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);
        _messagingCoordinator.Raise(coordinator => coordinator.TokenReceived += null, _messagingCoordinator.Object, "Partial");

        await viewModel.StopGenerationCommand.ExecuteAsync(null);
        inFlight.SetResult(new SendMessageResult
        {
            ConversationId = 42,
            WasCancelled = true,
            ResponseContent = "Partial\n\n[Generation stopped]",
            UserMessageId = 1001
        });
        await sending;

        viewModel.IsGenerating.Should().BeFalse();
        viewModel.UserInput = "Another question";
        viewModel.CanSend.Should().BeTrue();
        viewModel.SendMessageCommand.CanExecute(null).Should().BeTrue();
        var answer = viewModel.Messages[1];
        answer.IsStreaming.Should().BeFalse();
        answer.Content.Should().Be("Partial\n\n[Generation stopped]");
        viewModel.Messages[0].MessageId.Should().Be(1001, "the prompt was saved before the stop");
    }

    [Fact]
    public async Task SelectConversationAsync_MidGeneration_StopsItAndLeavesTheOpenedThreadUsable()
    {
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        var inFlight = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(inFlight.Task);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "Startup Investigation" });
        viewModel.ActiveConversationId = 84;
        viewModel.UserInput = "explain the retry backoff";
        var sending = viewModel.SendMessageCommand.ExecuteAsync(null);

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        _messagingCoordinator.Verify(coordinator => coordinator.StopGenerationAsync(), Times.Once);
        viewModel.IsGenerating.Should().BeFalse();
        viewModel.UserInput = "a question for this thread";
        viewModel.CanSend.Should().BeTrue();

        inFlight.SetResult(new SendMessageResult { ConversationId = 84, WasCancelled = true });
        await sending;
        viewModel.IsGenerating.Should().BeFalse();
        viewModel.ActiveConversationId.Should().Be(42);
    }

    [Fact]
    public async Task SendMessageAsync_AfterAnAbandonedGeneration_StreamsOnlyItsOwnTokens()
    {
        // A generation left behind by a thread switch can still be winding down when the next
        // one starts; nothing it still reports may land in the new answer.
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        var abandoned = new TaskCompletionSource<SendMessageResult>();
        var next = new TaskCompletionSource<SendMessageResult>();
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync("first", 84, null, null, false))
            .Returns(abandoned.Task);
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync("second", 42, null, null, false))
            .Returns(next.Task);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "Startup Investigation" });
        viewModel.ActiveConversationId = 84;
        viewModel.UserInput = "first";
        var firstSend = viewModel.SendMessageCommand.ExecuteAsync(null);
        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        viewModel.UserInput = "second";
        var secondSend = viewModel.SendMessageCommand.ExecuteAsync(null);
        _messagingCoordinator.Raise(coordinator => coordinator.TokenReceived += null, _messagingCoordinator.Object, "stale ");

        abandoned.SetResult(new SendMessageResult { ConversationId = 84, WasCancelled = true });
        await firstSend;
        await WaitUntilAsync(() => _messagingCoordinator.Invocations.Any(invocation =>
            invocation.Method.Name == nameof(IMessagingCoordinator.SendMessageAsync) &&
            Equals(invocation.Arguments[0], "second")));
        _messagingCoordinator.Raise(coordinator => coordinator.TokenReceived += null, _messagingCoordinator.Object, "fresh");

        var answer = viewModel.Messages.Last(message => message.IsAssistant);
        answer.Content.Should().Be("fresh");

        next.SetResult(new SendMessageResult { ConversationId = 42, ResponseContent = "fresh" });
        await secondSend;
    }

    [Fact]
    public async Task SendMessageAsync_WhenNothingStreamed_ShowsTheFinalResponseText()
    {
        // The offline fallback builds its help text without raising a single token.
        SetupSend("Hello?", new SendMessageResult
        {
            ConversationId = 42,
            ResponseContent = "Unable to generate a response. Please ensure Ollama is running."
        });
        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "Hello?";

        await viewModel.SendMessageCommand.ExecuteAsync(null);

        viewModel.Messages[1].Content.Should().Be("Unable to generate a response. Please ensure Ollama is running.");
    }

    [Fact]
    public async Task SendMessageAsync_ShowsTheStatsOfAStreamedReplyAsSoonAsItCompletes()
    {
        var viewModel = await SendFirstExchangeAsync();
        var answer = viewModel.Messages[1];

        answer.FormattedTokens.Should().Be("12 tokens");
        answer.FormattedTokenSpeed.Should().NotBeEmpty();
    }

    [Fact]
    public void ChatMessageItem_StatsSetAfterTheBubbleIsShown_NotifyTheirDisplayText()
    {
        // The stats arrive when streaming completes, after the bubble is already bound.
        var item = new ChatMessageItem { IsAssistant = true };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.TokenCount = 12;
        item.GenerationTimeMs = 480;

        changed.Should().Contain(
        [
            nameof(ChatMessageItem.TokenCount),
            nameof(ChatMessageItem.FormattedTokens),
            nameof(ChatMessageItem.GenerationTimeMs),
            nameof(ChatMessageItem.FormattedGenerationTime),
            nameof(ChatMessageItem.FormattedTokenSpeed)
        ]);
        item.FormattedTokens.Should().Be("12 tokens");
    }

    [Fact]
    public void ChatMessageItem_FormattedTime_UsesTheUsersShortTimeFormat()
    {
        // The time was always "h:mm tt", so a German user read "2:05 PM" instead of "14:05".
        var item = new ChatMessageItem { Timestamp = new DateTime(2026, 9, 27, 14, 5, 0, DateTimeKind.Local) };
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            item.FormattedTime.Should().Be("14:05");

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            item.FormattedTime.Should().StartWith("2:05").And.EndWith("PM");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // --- Web sources ---
    // Research Mode answers showed no sources in the bubble, and chat saved none to reload.

    [Fact]
    public void ChatMessageItem_WebSourcesSetAfterTheBubbleIsShown_NotifyTheChips()
    {
        // The sources arrive with the completion, after the bubble is already bound.
        var item = new ChatMessageItem { IsAssistant = true };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.WebCitations = [new WebCitation { Title = "Release notes", Url = "https://example.org/notes" }];

        changed.Should().Contain(
        [
            nameof(ChatMessageItem.WebCitations),
            nameof(ChatMessageItem.HasWebCitations),
            nameof(ChatMessageItem.WebCitationChips)
        ]);
        item.HasWebCitations.Should().BeTrue();
    }

    [Fact]
    public void WebCitationChip_NumbersSourcesInOrder_NamesTheSite_AndLinksOnlyWebAddresses()
    {
        var chips = WebCitationChip.From(
        [
            new WebCitation { Title = "Release notes", Url = "https://www.example.org/notes" },
            new WebCitation { Title = "", Url = "http://docs.example.net/v2" },
            new WebCitation { Title = "Local file", Url = "file:///C:/notes.txt" },
            new WebCitation { Title = "Script", Url = "javascript:alert(1)" }
        ]);

        chips.Select(chip => chip.Label).Should().Equal(
            "[1] Release notes (example.org)",
            "[2] docs.example.net",
            "[3] Local file",
            "[4] Script");
        chips.Select(chip => chip.HasLink).Should().Equal(true, true, false, false);
        chips[0].Link.Should().Be(new Uri("https://www.example.org/notes"));
    }

    [Fact]
    public async Task SendMessageAsync_InResearchMode_ShowsTheAnswersWebSourcesWhenItCompletes()
    {
        SetupSend("What changed?", new SendMessageResult
        {
            ConversationId = 42,
            ResponseContent = "Version 2 shipped [1].",
            WebCitations = [new WebCitation { Title = "Release notes", Url = "https://example.org/notes" }]
        });
        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "What changed?";

        await viewModel.SendMessageCommand.ExecuteAsync(null);

        viewModel.Messages[1].WebCitationChips.Should().ContainSingle()
            .Which.Label.Should().Be("[1] Release notes (example.org)");
    }

    [Fact]
    public async Task SelectConversationAsync_ShowsTheWebSourcesSavedWithEachAnswer()
    {
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(
            [
                new MessageSummary { MessageId = 1, ConversationId = 42, SortOrder = 0, Role = "user", Content = "What changed?", Timestamp = DateTime.UtcNow },
                new MessageSummary
                {
                    MessageId = 2,
                    ConversationId = 42,
                    SortOrder = 1,
                    Role = "assistant",
                    Content = "Version 2 shipped [1].",
                    Timestamp = DateTime.UtcNow,
                    WebCitations = [new WebCitation { Title = "Release notes", Url = "https://example.org/notes" }]
                }
            ]);
        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "Release", UpdatedAt = DateTime.UtcNow });

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);

        viewModel.Messages.Single(message => message.IsUser).HasWebCitations.Should().BeFalse();
        viewModel.Messages.Single(message => message.IsAssistant).WebCitationChips.Should().ContainSingle()
            .Which.Url.Should().Be("https://example.org/notes");
    }

    [Fact]
    public void OpenWebCitation_OpensOnlyWebAddresses()
    {
        var viewModel = CreateViewModel();
        var opened = new List<Uri>();
        viewModel.OpenExternalLink = opened.Add;
        var chips = WebCitationChip.From(
        [
            new WebCitation { Title = "Notes", Url = "https://example.org/notes" },
            new WebCitation { Title = "Local", Url = "file:///C:/secrets.txt" },
            new WebCitation { Title = "Script", Url = "javascript:alert(1)" }
        ]);

        foreach (var chip in chips)
        {
            viewModel.OpenWebCitationCommand.Execute(chip);
        }

        opened.Should().Equal(new Uri("https://example.org/notes"));
    }

    [Fact]
    public void OpenWebCitation_WhenNoBrowserOpens_DoesNotThrow()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenExternalLink = _ => throw new InvalidOperationException("No browser.");
        var chip = WebCitationChip.From([new WebCitation { Title = "Notes", Url = "https://example.org/notes" }])[0];

        viewModel.Invoking(vm => vm.OpenWebCitationCommand.Execute(chip)).Should().NotThrow();
    }

    [Fact]
    public async Task StreamingCompletedEvent_WithNoSendInFlight_IsIgnored()
    {
        // Another chat sharing the singleton coordinator (or a view model left behind by the
        // page cache) must not have its completion adopted here, nor learned from twice.
        var viewModel = CreateViewModel();

        _messagingCoordinator.Raise(
            coordinator => coordinator.StreamingCompleted += null,
            new StreamingCompletedEventArgs
            {
                ConversationId = 84,
                ConversationTitle = "Someone else's thread",
                ResponseContent = "Answer",
                TokenCount = 12,
                UserMessageId = 1001,
                ContextInspection = CreateInspectionSnapshot(84)
            });
        await Task.Delay(50);

        viewModel.ActiveConversationId.Should().BeNull();
        viewModel.Conversations.Should().BeEmpty();
        viewModel.TokenCount.Should().Be(0);
        _temporalIdentity.Verify(
            service => service.LearnFromMessageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_LearnsFromThePromptOnce_AndRegenerateDoesNotLearnAgain()
    {
        var learned = new TaskCompletionSource();
        _temporalIdentity
            .Setup(service => service.LearnFromMessageAsync(1001, It.IsAny<CancellationToken>()))
            .Callback(() => learned.TrySetResult())
            .Returns(Task.CompletedTask);
        var viewModel = await SendFirstExchangeAsync();
        await learned.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _messagingCoordinator
            .Setup(coordinator => coordinator.RegenerateResponseAsync(
                42, 1001, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ChatOrchestrationMode>()))
            .ReturnsAsync(new SendMessageResult
            {
                ConversationId = 42,
                ResponseContent = "Again",
                UserMessageId = 1001,
                AssistantMessageId = 1003
            });
        await viewModel.RegenerateMessageCommand.ExecuteAsync(viewModel.Messages[1]);
        await Task.Delay(100);

        _temporalIdentity.Verify(
            service => service.LearnFromMessageAsync(1001, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_TracksThePromptsBeliefsFirst_AndOneFailedStepDoesNotStopTheRest()
    {
        // Belief tracking had no caller, so Past Self and the dashboard's belief-conflict panel
        // never had data.
        var steps = new List<string>();
        var finished = new TaskCompletionSource();
        _temporalIdentity
            .Setup(service => service.ProcessMessageAsync(1001, It.IsAny<CancellationToken>()))
            .Callback(() => { lock (steps) steps.Add("beliefs"); })
            .ThrowsAsync(new InvalidOperationException("database is busy"));
        _temporalIdentity
            .Setup(service => service.LearnFromMessageAsync(1001, It.IsAny<CancellationToken>()))
            .Callback(() => { lock (steps) steps.Add("voice"); })
            .Returns(Task.CompletedTask);
        _temporalIdentity
            .Setup(service => service.DetectInsightsAsync(42, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                lock (steps) steps.Add("insights");
                finished.TrySetResult();
            })
            .Returns(Task.CompletedTask);

        await SendFirstExchangeAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        steps.Should().Equal("beliefs", "voice", "insights");
    }

    // --- Engagement ---
    // RecordEngagementAsync had no caller, so "what did I spend time on" never had data.

    [Fact]
    public async Task MovingOffAConversation_RecordsTheTimeItWasOpen()
    {
        var now = new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
        var viewModel = CreateViewModelWithTwoConversations(() => now);

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        now = now.AddSeconds(90);
        await viewModel.SelectConversationCommand.ExecuteAsync(84L);
        now = now.AddSeconds(20);
        await viewModel.NewConversationCommand.ExecuteAsync(null);

        VerifyEngagement(42, 90, Times.Once());
        VerifyEngagement(84, 20, Times.Once());
    }

    [Fact]
    public async Task LeavingTheChatPage_RecordsTheOpenConversation_AndTimeAwayDoesNotCount()
    {
        var now = new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
        var viewModel = CreateViewModelWithTwoConversations(() => now);

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        now = now.AddSeconds(30);
        await viewModel.PauseConversationEngagementAsync();
        now = now.AddMinutes(10);
        viewModel.ResumeConversationEngagement();
        now = now.AddSeconds(15);
        await viewModel.SelectConversationCommand.ExecuteAsync(84L);

        VerifyEngagement(42, 30, Times.Once());
        VerifyEngagement(42, 15, Times.Once());
        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(
                EngagementTargetType.Conversation, It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingTheOpenConversation_RecordsNoTimeAgainstIt()
    {
        var now = new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
        _conversationCoordinator.Setup(service => service.DeleteConversationAsync(42)).ReturnsAsync(true);
        var viewModel = CreateViewModelWithTwoConversations(() => now);

        await viewModel.SelectConversationCommand.ExecuteAsync(42L);
        now = now.AddMinutes(2);
        await viewModel.DeleteConversationCommand.ExecuteAsync(42L);

        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(
                It.IsAny<EngagementTargetType>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ChatViewModel CreateViewModelWithTwoConversations(Func<DateTime> utcNow)
    {
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(It.IsAny<long>()))
            .ReturnsAsync(Array.Empty<MessageSummary>());
        var viewModel = CreateViewModel();
        viewModel.UtcNow = utcNow;
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "First" });
        viewModel.Conversations.Add(new ConversationListItem { Id = 84, Title = "Second" });
        return viewModel;
    }

    private void VerifyEngagement(long conversationId, int seconds, Times times) =>
        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(
                EngagementTargetType.Conversation, conversationId, seconds, It.IsAny<CancellationToken>()),
            times);

    // --- Opening conversations ---

    [Fact]
    public async Task ApplyNavigationParameterAsync_OnAColdPage_OpensTheConversationBeforeTheSidebarLoads()
    {
        // Jump-To navigates before the page has loaded its conversation list.
        _conversationCoordinator
            .Setup(service => service.LoadConversationSummaryAsync(42))
            .ReturnsAsync(new ConversationSummary { Id = 42, Title = "Startup Investigation" });
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(42))
            .ReturnsAsync(
            [
                new MessageSummary { MessageId = 1001, ConversationId = 42, Role = "user", Content = "Why?" }
            ]);

        var viewModel = CreateViewModel();
        viewModel.Conversations.Should().BeEmpty();

        await viewModel.ApplyNavigationParameterAsync(42L);

        viewModel.ActiveConversationId.Should().Be(42);
        viewModel.ActiveConversationTitle.Should().Be("Startup Investigation");
        viewModel.Messages.Should().ContainSingle(message => message.MessageId == 1001);
    }

    [Fact]
    public async Task SelectConversationAsync_ForAConversationThatNoLongerExists_LeavesTheScreenAlone()
    {
        _conversationCoordinator
            .Setup(service => service.LoadConversationSummaryAsync(404))
            .ReturnsAsync((ConversationSummary?)null);
        var viewModel = CreateViewModel();
        viewModel.ActiveConversationId = 42;

        await viewModel.SelectConversationCommand.ExecuteAsync(404L);

        viewModel.ActiveConversationId.Should().Be(42);
        _conversationCoordinator.Verify(service => service.LoadMessagesAsync(It.IsAny<long>()), Times.Never);
        _notificationService.Verify(
            service => service.ShowInfo("Conversation not found", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    // --- Sidebar row actions ---

    [Fact]
    public async Task DeleteConversationAsync_WhenTheDeleteFails_KeepsTheRowAndSaysSo()
    {
        _conversationCoordinator.Setup(service => service.DeleteConversationAsync(42)).ReturnsAsync(false);
        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "Startup Investigation" });
        viewModel.ActiveConversationId = 42;

        await viewModel.DeleteConversationCommand.ExecuteAsync(42L);

        viewModel.Conversations.Should().ContainSingle(item => item.Id == 42);
        viewModel.ActiveConversationId.Should().Be(42);
        _notificationService.Verify(
            service => service.ShowError("Delete failed", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DeleteConversationAsync_OfTheOpenConversation_RemovesItsRowAndStartsOver()
    {
        _conversationCoordinator.Setup(service => service.DeleteConversationAsync(42)).ReturnsAsync(true);
        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "Startup Investigation" });
        viewModel.ActiveConversationId = 42;
        viewModel.Messages.Add(new ChatMessageItem { Role = "user", IsUser = true, Content = "Why?" });

        await viewModel.DeleteConversationCommand.ExecuteAsync(42L);

        viewModel.Conversations.Should().BeEmpty();
        viewModel.ActiveConversationId.Should().BeNull();
        viewModel.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task TogglePinAsync_PinsTheRowAndMovesItToTheTop()
    {
        _conversationCoordinator.Setup(service => service.TogglePinAsync(84)).ReturnsAsync(true);
        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "First" });
        viewModel.Conversations.Add(new ConversationListItem { Id = 84, Title = "Second" });

        await viewModel.TogglePinCommand.ExecuteAsync(84L);

        viewModel.Conversations[0].Id.Should().Be(84);
        viewModel.Conversations[0].IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task TogglePinAsync_WhenTheUpdateFails_LeavesThePinAlone()
    {
        _conversationCoordinator.Setup(service => service.TogglePinAsync(84)).ReturnsAsync(false);
        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 84, Title = "Second" });

        await viewModel.TogglePinCommand.ExecuteAsync(84L);

        viewModel.Conversations[0].IsPinned.Should().BeFalse();
        _notificationService.Verify(
            service => service.ShowError("Pin not changed", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    // --- Lifetime ---
    // The page cache evicts ChatPage; its view model stayed subscribed to the singleton
    // coordinators forever and kept reacting (duplicate toasts, duplicate learning).

    [Fact]
    public void Dispose_DetachesFromTheSharedCoordinators()
    {
        var viewModel = CreateViewModel();

        viewModel.Dispose();
        _messagingCoordinator.Raise(
            coordinator => coordinator.NotificationRequested += null,
            new NotificationRequestEventArgs { Level = "error", Title = "Generation Failed", Message = "x" });
        _voiceCoordinator.Raise(coordinator => coordinator.RecordingStateChanged += null, _voiceCoordinator.Object, true);

        _notificationService.Verify(
            service => service.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        viewModel.IsRecording.Should().BeFalse();
    }

    [Fact]
    public void Dispose_LeavesTheSharedVoiceCoordinatorUsableForTheNextChat()
    {
        var disposableVoice = _voiceCoordinator.As<IDisposable>();
        var viewModel = CreateViewModel();

        viewModel.Dispose();
        viewModel.Dispose();

        disposableVoice.Verify(voice => voice.Dispose(), Times.Never);
    }

    // --- Privacy claim ---
    // The empty chat said "powered by Ollama ... No data leaves your machine" and showed
    // "100% Private" whatever provider was active, and while Research Mode sent questions to a
    // web search provider.

    [Fact]
    public async Task InitializeAsync_WithTheBuiltInModelActive_ClaimsTheMessagesStayOnThisComputer()
    {
        SetupActiveProvider("local", "Built-in LLM");
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsChatPrivate.Should().BeTrue();
        viewModel.PrivacyHint.Should().Be("Built-in LLM runs on this computer, so your messages stay on this device.");
        _privacyStatusService.Verify(
            service => service.GetChatMessageRecipientsAsync("local", false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WithACloudProvider_NamesItAndMakesNoLocalClaim()
    {
        SetupActiveProvider("openai", "OpenAI");
        SetupRecipients("openai", researchModeOn: false, new PromptRecipient(PromptRecipientKind.CloudAiProvider, "OpenAI"));
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsChatPrivate.Should().BeFalse();
        viewModel.PrivacyHint.Should().Be("Your messages are sent to OpenAI for processing.");
    }

    [Fact]
    public async Task InitializeAsync_WithSeveralRecipients_NamesEachOfThem()
    {
        SetupActiveProvider("ollama", "Ollama");
        SetupRecipients(
            "ollama",
            researchModeOn: false,
            new PromptRecipient(PromptRecipientKind.RemoteOllama, "192.168.1.40"),
            new PromptRecipient(PromptRecipientKind.ModelRouting, null));
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsChatPrivate.Should().BeFalse();
        viewModel.PrivacyHint.Should().Be(
            "Your messages are sent to the Ollama server at 192.168.1.40. " +
            "Smart model routing may send your messages to your cloud AI provider.");
    }

    [Fact]
    public async Task SwitchingResearchMode_UpdatesTheClaimForTheSearchProvider()
    {
        SetupActiveProvider("local", "Built-in LLM");
        SetupRecipients("local", researchModeOn: true, new PromptRecipient(PromptRecipientKind.SearXng, "searx.example.org"));
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.IsChatPrivate.Should().BeTrue();

        viewModel.IsResearchMode = true;

        viewModel.IsChatPrivate.Should().BeFalse();
        viewModel.PrivacyHint.Should().Be(
            "With Research Mode on, your questions are sent to the SearXNG instance at searx.example.org, " +
            "which forwards them to public search engines.");

        viewModel.IsResearchMode = false;

        viewModel.IsChatPrivate.Should().BeTrue();
        viewModel.PrivacyHint.Should().Contain("stay on this device");
    }

    [Fact]
    public async Task ShowingThePageAgainAfterTheProviderChanged_UpdatesTheClaim()
    {
        var provider = SetupActiveProvider("local", "Built-in LLM");
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.IsChatPrivate.Should().BeTrue();

        // Settings switched the provider to Anthropic; the page initializes on every visit.
        provider.SetupGet(p => p.ProviderId).Returns("anthropic");
        provider.SetupGet(p => p.DisplayName).Returns("Anthropic Claude");
        SetupRecipients("anthropic", researchModeOn: false, new PromptRecipient(PromptRecipientKind.CloudAiProvider, "Anthropic"));
        await viewModel.InitializeAsync();

        viewModel.IsChatPrivate.Should().BeFalse();
        viewModel.PrivacyHint.Should().Be("Your messages are sent to Anthropic for processing.");
    }

    [Fact]
    public async Task RefreshConnection_ReevaluatesTheClaim()
    {
        SetupActiveProvider("local", "Built-in LLM");
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        SetupRecipients("local", researchModeOn: false, new PromptRecipient(PromptRecipientKind.ModelRouting, null));

        await viewModel.RefreshConnectionCommand.ExecuteAsync(null);

        viewModel.IsChatPrivate.Should().BeFalse();
        viewModel.PrivacyHint.Should().Be("Smart model routing may send your messages to your cloud AI provider.");
    }

    [Fact]
    public async Task InitializeAsync_WhenWhereMessagesGoCannotBeWorkedOut_MakesNoLocalClaim()
    {
        _privacyStatusService
            .Setup(service => service.GetChatMessageRecipientsAsync(
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("settings unreadable"));
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsChatPrivate.Should().BeFalse();
        viewModel.PrivacyHint.Should().Be(
            "Agent-X could not confirm where your messages are sent. Review the AI provider in Settings.");
    }

    [Fact]
    public async Task InitializeAsync_BeforeTheAiServiceIsReady_UsesTheSavedProviderAndNamesNoModel()
    {
        _aiService.SetupGet(s => s.ActiveProvider)
            .Throws(new InvalidOperationException("AI service has not been initialized."));
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        _privacyStatusService.Verify(
            service => service.GetChatMessageRecipientsAsync(null, false, It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.IsChatPrivate.Should().BeTrue();
        viewModel.PrivacyHint.Should().Be("The AI model runs on this computer, so your messages stay on this device.");
    }

    [Fact]
    public async Task ThePrivacyHint_IsReadFromTheUsersLanguage()
    {
        SetupActiveProvider("openai", "OpenAI");
        SetupRecipients("openai", researchModeOn: false, new PromptRecipient(PromptRecipientKind.CloudAiProvider, "OpenAI"));
        var viewModel = CreateViewModel(ReswLocalization.For("de"));

        await viewModel.InitializeAsync();

        viewModel.PrivacyHint.Should().Be("Ihre Nachrichten werden zur Verarbeitung an OpenAI gesendet.");
    }

    // --- Memories ---
    // Memories were counted (MemoryCount) but nothing showed them, so what the app remembered
    // about the operator could be neither seen nor removed.

    [Fact]
    public async Task OpeningTheContextInspector_ListsTheStoredMemoriesAndTheirCount()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"), StoredMemory(2, "Works on Agent-X"));
        var viewModel = CreateViewModel();

        viewModel.IsContextInspectorOpen = true;

        await WaitUntilAsync(() => viewModel.Memories.Count == 2);
        viewModel.Memories.Select(m => m.Content).Should().Equal("Prefers dark mode", "Works on Agent-X");
        viewModel.MemoryCount.Should().Be(2);
        viewModel.HasMemories.Should().BeTrue();
        viewModel.HasMemoriesStatus.Should().BeFalse();
    }

    [Fact]
    public async Task OpeningTheContextInspector_WithNoMemories_SaysSo()
    {
        SetupMemories();
        var viewModel = CreateViewModel();

        viewModel.ToggleContextInspectorCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.HasMemoriesStatus);
        viewModel.MemoriesStatus.Should().Be("No memories are stored.");
        viewModel.HasMemories.Should().BeFalse();
    }

    [Fact]
    public async Task OpeningTheContextInspector_WhenTheMemoriesCannotBeRead_SaysSo()
    {
        _memoryService
            .Setup(service => service.GetAllMemoriesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database busy"));
        var viewModel = CreateViewModel();

        viewModel.IsContextInspectorOpen = true;

        await WaitUntilAsync(() => viewModel.HasMemoriesStatus);
        viewModel.MemoriesStatus.Should().Be("Memories could not be loaded.");
    }

    [Fact]
    public async Task DeleteMemory_DeletesItAndTakesItOffTheList()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"), StoredMemory(2, "Works on Agent-X"));
        _memoryService
            .Setup(service => service.DeleteMemoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var viewModel = CreateViewModel();
        viewModel.IsContextInspectorOpen = true;
        await WaitUntilAsync(() => viewModel.Memories.Count == 2);

        await viewModel.DeleteMemoryCommand.ExecuteAsync(viewModel.Memories[0]);

        _memoryService.Verify(service => service.DeleteMemoryAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        viewModel.Memories.Select(m => m.Content).Should().Equal("Works on Agent-X");
        viewModel.MemoryCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteMemory_WhenItFails_KeepsItListedAndSaysSo()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"));
        _memoryService
            .Setup(service => service.DeleteMemoryAsync(1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database busy"));
        var viewModel = CreateViewModel();
        viewModel.IsContextInspectorOpen = true;
        await WaitUntilAsync(() => viewModel.Memories.Count == 1);

        await viewModel.DeleteMemoryCommand.ExecuteAsync(viewModel.Memories[0]);

        viewModel.Memories.Should().ContainSingle();
        _notificationService.Verify(
            service => service.ShowError(
                "Memory not deleted",
                "The memory could not be deleted and is still stored. Try again.",
                It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearMemories_DeletesEverythingOnceConfirmed()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"), StoredMemory(2, "Works on Agent-X"));
        var viewModel = CreateViewModel();
        var asked = 0;
        viewModel.ConfirmClearMemoriesAsync = () =>
        {
            asked++;
            return Task.FromResult(true);
        };
        viewModel.IsContextInspectorOpen = true;
        await WaitUntilAsync(() => viewModel.Memories.Count == 2);

        await viewModel.ClearMemoriesCommand.ExecuteAsync(null);

        asked.Should().Be(1);
        _memoryService.Verify(service => service.DeleteAllMemoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
        viewModel.Memories.Should().BeEmpty();
        viewModel.MemoryCount.Should().Be(0);
        viewModel.MemoriesStatus.Should().Be("No memories are stored.");
    }

    [Fact]
    public async Task ClearMemories_WhenDeclined_DeletesNothing()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"));
        var viewModel = CreateViewModel();
        viewModel.ConfirmClearMemoriesAsync = () => Task.FromResult(false);
        viewModel.IsContextInspectorOpen = true;
        await WaitUntilAsync(() => viewModel.Memories.Count == 1);

        await viewModel.ClearMemoriesCommand.ExecuteAsync(null);

        _memoryService.Verify(service => service.DeleteAllMemoriesAsync(It.IsAny<CancellationToken>()), Times.Never);
        viewModel.Memories.Should().ContainSingle();
    }

    [Fact]
    public async Task ClearMemories_WithoutAWayToAsk_DeletesNothing()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"));
        var viewModel = CreateViewModel();
        viewModel.IsContextInspectorOpen = true;
        await WaitUntilAsync(() => viewModel.Memories.Count == 1);

        await viewModel.ClearMemoriesCommand.ExecuteAsync(null);

        _memoryService.Verify(service => service.DeleteAllMemoriesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClearMemories_WhenItFails_SaysSoAndShowsWhatIsStillStored()
    {
        SetupMemories(StoredMemory(1, "Prefers dark mode"));
        _memoryService
            .Setup(service => service.DeleteAllMemoriesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database busy"));
        var viewModel = CreateViewModel();
        viewModel.ConfirmClearMemoriesAsync = () => Task.FromResult(true);
        viewModel.IsContextInspectorOpen = true;
        await WaitUntilAsync(() => viewModel.Memories.Count == 1);

        await viewModel.ClearMemoriesCommand.ExecuteAsync(null);

        viewModel.Memories.Should().ContainSingle();
        _notificationService.Verify(
            service => service.ShowError("Memories not deleted", It.IsAny<string>(), It.IsAny<int>()),
            Times.Once);
    }

    private void SetupMemories(params AgentX.Core.Data.Entities.MemoryEntity[] memories) =>
        _memoryService
            .Setup(service => service.GetAllMemoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

    private static AgentX.Core.Data.Entities.MemoryEntity StoredMemory(long id, string content) =>
        new() { Id = id, Content = content };

    // --- Comparing branches ---
    // Compare branches always compared the main thread with the first branch, whichever
    // thread was open.

    [Fact]
    public async Task CompareBranches_OnABranch_ComparesItWithTheThreadItCameFrom()
    {
        var viewModel = await OpenBranchFamilyAsync(openConversationId: 78);
        var comparisons = CaptureComparisons(viewModel);

        viewModel.CompareBranchesCommand.Execute(null);

        comparisons.Should().ContainSingle();
        comparisons[0].Thread.Conversation.Id.Should().Be(42);
        comparisons[0].Branch.Conversation.Id.Should().Be(78);
        comparisons[0].ThreadTitle.Should().Be("Main Thread");
        comparisons[0].BranchTitle.Should().Be("Plan B");
    }

    [Fact]
    public async Task CompareBranches_OnABranchOfABranch_ComparesItWithTheBranchItCameFrom()
    {
        var viewModel = await OpenBranchFamilyAsync(openConversationId: 90);
        var comparisons = CaptureComparisons(viewModel);

        viewModel.CompareBranchesCommand.Execute(null);

        comparisons.Should().ContainSingle();
        comparisons[0].Thread.Conversation.Id.Should().Be(78);
        comparisons[0].ThreadTitle.Should().Be("Plan B");
        comparisons[0].Branch.Conversation.Id.Should().Be(90);
        comparisons[0].BranchTitle.Should().Be("Main (Branch)", "an unlabelled branch is named by its conversation");
    }

    [Fact]
    public async Task CompareBranches_OnTheMainThreadWithSeveralBranches_AsksWhichBranchToOpen()
    {
        var viewModel = await OpenBranchFamilyAsync(openConversationId: 42);
        var comparisons = CaptureComparisons(viewModel);

        viewModel.CompareBranchesCommand.Execute(null);

        comparisons.Should().BeEmpty();
        _notificationService.Verify(
            service => service.ShowInfo(
                "Open a branch to compare",
                "This conversation has several branches. Open the one to compare from the Branches list, then choose Compare branches again.",
                It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task CompareBranches_OnTheMainThreadWithOneBranch_ComparesThatBranch()
    {
        var tree = BranchNode(42, "Main", label: null, BranchNode(77, "Plan A", "Plan A"));
        var viewModel = await OpenBranchFamilyAsync(openConversationId: 42, tree);
        var comparisons = CaptureComparisons(viewModel);

        viewModel.CompareBranchesCommand.Execute(null);

        comparisons.Should().ContainSingle();
        comparisons[0].Thread.Should().BeSameAs(tree);
        comparisons[0].Branch.Conversation.Id.Should().Be(77);
        comparisons[0].BranchTitle.Should().Be("Plan A");
    }

    /// <summary>
    /// Opens <paramref name="openConversationId"/> in a family where main thread 42 has branches
    /// 77 ("Plan A") and 78 ("Plan B"), and 78 has the unlabelled branch 90.
    /// </summary>
    private async Task<ChatViewModel> OpenBranchFamilyAsync(long openConversationId, ConversationBranchTree? tree = null)
    {
        tree ??= BranchNode(
            42, "Main", label: null,
            BranchNode(77, "Plan A", "Plan A"),
            BranchNode(78, "Plan B", "Plan B", BranchNode(90, "Main (Branch)", label: null)));
        _branchingCoordinator
            .Setup(service => service.LoadBranchTreeAsync(It.IsAny<long>()))
            .ReturnsAsync(tree);
        _conversationCoordinator
            .Setup(service => service.LoadMessagesAsync(It.IsAny<long>()))
            .ReturnsAsync(Array.Empty<MessageSummary>());

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = openConversationId, Title = "Open thread" });
        await viewModel.SelectConversationCommand.ExecuteAsync(openConversationId);
        return viewModel;
    }

    private static ConversationBranchTree BranchNode(
        long id, string title, string? label, params ConversationBranchTree[] children)
    {
        var node = new ConversationBranchTree
        {
            Conversation = new AgentX.Core.Data.Entities.ConversationEntity { Id = id, Title = title },
            BranchLabel = label
        };
        node.Children.AddRange(children);
        return node;
    }

    private static List<(ConversationBranchTree Thread, ConversationBranchTree Branch, string ThreadTitle, string BranchTitle)>
        CaptureComparisons(ChatViewModel viewModel)
    {
        var comparisons = new List<(ConversationBranchTree, ConversationBranchTree, string, string)>();
        viewModel.ShowBranchComparison = (thread, branch, threadTitle, branchTitle) =>
            comparisons.Add((thread, branch, threadTitle, branchTitle));
        return comparisons;
    }

    private Mock<IAiProvider> SetupActiveProvider(string providerId, string displayName)
    {
        var provider = new Mock<IAiProvider>();
        provider.SetupGet(p => p.ProviderId).Returns(providerId);
        provider.SetupGet(p => p.DisplayName).Returns(displayName);
        _aiService.SetupGet(s => s.ActiveProvider).Returns(provider.Object);
        return provider;
    }

    private void SetupRecipients(string? providerId, bool researchModeOn, params PromptRecipient[] recipients) =>
        _privacyStatusService
            .Setup(service => service.GetChatMessageRecipientsAsync(providerId, researchModeOn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipients);

    /// <summary>
    /// Sends "How should I proceed?" in conversation 42 and completes it with persisted ids:
    /// prompt 1001 (sort 4) and answer 1002 (sort 5).
    /// </summary>
    private async Task<ChatViewModel> SendFirstExchangeAsync()
    {
        SetupSend("How should I proceed?", new SendMessageResult
        {
            ConversationId = 42,
            ResponseContent = "Answer",
            TokenCount = 12,
            GenerationTimeMs = 480,
            UserMessageId = 1001,
            UserMessageSortOrder = 4,
            AssistantMessageId = 1002,
            AssistantMessageSortOrder = 5
        });

        var viewModel = CreateViewModel();
        viewModel.Conversations.Add(new ConversationListItem { Id = 42, Title = "Startup Investigation" });
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "How should I proceed?";
        await viewModel.SendMessageCommand.ExecuteAsync(null);
        return viewModel;
    }

    // --- Texts in the user's language ---
    // The connection status, notifications, summary strip and context inspector were English,
    // and the header dot took its tone from the English status word, which a translation breaks.

    [Theory]
    [InlineData(true, ChatConnectionState.Connected, "Verbunden", StatusTone.Success)]
    [InlineData(false, ChatConnectionState.Disconnected, "Getrennt", StatusTone.Danger)]
    public async Task RefreshConnectionCommand_ReportsAStateTheDotFollows_AndWordsItInTheUsersLanguage(
        bool connected, ChatConnectionState expectedState, string expectedStatus, StatusTone expectedTone)
    {
        var provider = new Mock<IAiProvider>();
        provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connected);
        _aiService.SetupGet(service => service.ActiveProvider).Returns(provider.Object);
        var viewModel = CreateViewModel(ReswLocalization.For("de"));
        var states = new List<ChatConnectionState>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.ConnectionState))
            {
                states.Add(viewModel.ConnectionState);
            }
        };

        await viewModel.RefreshConnectionCommand.ExecuteAsync(null);

        states.Should().Equal(ChatConnectionState.Checking, expectedState);
        viewModel.ConnectionStatus.Should().Be(expectedStatus);
        StatusToneResolver.Resolve(viewModel.ConnectionState.ToString()).Should().Be(expectedTone);
        StatusToneResolver.Resolve(nameof(ChatConnectionState.Checking)).Should().Be(StatusTone.Neutral);
    }

    [Fact]
    public void ANewChat_StartsWithItsTextsInTheUsersLanguage()
    {
        var viewModel = CreateViewModel(ReswLocalization.For("de"));

        viewModel.ActiveConversationTitle.Should().Be("Neue Unterhaltung");
        viewModel.ConnectionStatus.Should().Be("Getrennt");
        viewModel.ConnectionState.Should().Be(ChatConnectionState.Disconnected);
        viewModel.ContextInspectionStatus.Should().Be("Noch kein Erstellungskontext erfasst.");
        viewModel.ContextAssemblyMode.Should().Be("Kein Kontext verfügbar");
        viewModel.ContextRecallStatus.Should().Be("Noch kein Kontext aus dauerhaftem Recall erfasst.");
        viewModel.ResearchModeTooltip.Should().StartWith("Recherchemodus AUS");
    }

    [Fact]
    public async Task DeleteMessageCommand_OnAnUnsavedMessage_SaysSoInTheUsersLanguage()
    {
        SetupSend("Are you there?", new SendMessageResult { ConversationId = 42, ResponseContent = "Offline help" });
        var viewModel = CreateViewModel(ReswLocalization.For("fr"));
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "Are you there?";
        await viewModel.SendMessageCommand.ExecuteAsync(null);

        await viewModel.DeleteMessageCommand.ExecuteAsync(viewModel.Messages[1]);

        _notificationService.Verify(
            service => service.ShowInfo(
                "Message retiré",
                "Ce message n'a jamais été enregistré : il a donc seulement été retiré de l'écran.",
                It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task AStoppedResponseWithNoText_IsMarkedInTheUsersLanguage()
    {
        SetupSend("Long question", new SendMessageResult { ConversationId = 42, WasCancelled = true, ResponseContent = string.Empty });
        var viewModel = CreateViewModel(ReswLocalization.For("es"));
        viewModel.ActiveConversationId = 42;
        viewModel.UserInput = "Long question";

        await viewModel.SendMessageCommand.ExecuteAsync(null);

        viewModel.Messages[1].Content.Should().Be("[Generación detenida]");
    }

    [Fact]
    public async Task RefreshConversationSummaryCommand_WordsTheServicesReasonInTheUsersLanguage()
    {
        _chatService
            .Setup(service => service.RefreshConversationSummaryInspectionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationSummaryRefreshResult.Failure(
                null, "Summary refresh is unavailable in this app configuration."));
        var viewModel = CreateViewModel(ReswLocalization.For("ja"));
        viewModel.ActiveConversationId = 42;

        await viewModel.RefreshConversationSummaryCommand.ExecuteAsync(null);

        viewModel.ConversationSummaryRefreshError.Should().Be("このアプリの構成では要約を更新できません。");
        viewModel.ConversationSummaryRefreshActionText.Should().Be("要約を再試行");
    }

    [Fact]
    public void AnInspectedResponse_DescribesItsContextInTheUsersLanguage()
    {
        var snapshot = CreateInspectionSnapshot(
            42, limitedVisibility: true, limitedVisibilityReason: "multi_agent_orchestration");
        _chatService.Setup(service => service.GetLatestContextInspection(42)).Returns(snapshot);
        var viewModel = CreateViewModel(ReswLocalization.For("zh-CN"));
        viewModel.ActiveConversationId = 42;

        viewModel.ToggleContextInspectorCommand.Execute(null);

        viewModel.ContextInspectionStatus.Should().Be("可见性受限：多智能体编排");
        viewModel.ContextAssemblyMode.Should().Be("可见性受限");
    }

    private void SetupSend(string content, SendMessageResult result) =>
        _messagingCoordinator
            .Setup(coordinator => coordinator.SendMessageAsync(content, It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(result);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    private ChatViewModel CreateViewModel(ILocalizationService? localization = null) =>
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
            _notificationService.Object,
            _temporalIdentity.Object,
            _privacyStatusService.Object,
            localization ?? _localization);

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

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
        viewModel.HasContextInspection.Should().BeTrue();
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
        viewModel.HasContextInspection.Should().BeFalse();
        viewModel.ContextInspectionStatus.Should().Be("No generation context captured yet.");
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
        viewModel.ContextSummaryPreview.Should().Be("Focused on startup retries and backoff behavior.");
        viewModel.ContextRecallStatus.Should().Be("1 recalled message used");
        viewModel.Conversations.Should().ContainSingle(item => item.Id == 84 && item.Title == "Recovered Thread");
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

    private static ChatContextInspectionSnapshot CreateInspectionSnapshot(long conversationId) =>
        new()
        {
            ConversationId = conversationId,
            CapturedAt = DateTime.UtcNow.AddMinutes(-2),
            CurrentQuery = "How should I proceed?",
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
                IsStale = false,
                PendingMessageCount = 0
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

using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Feedback;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels.Coordinators;

public class ConversationCoordinatorTests
{
    private readonly Mock<IConversationService> _conversationService;
    private readonly Mock<IFeedbackService> _feedbackService;
    private readonly ConversationCoordinator _coordinator;

    public ConversationCoordinatorTests()
    {
        _conversationService = new Mock<IConversationService>();
        _feedbackService = new Mock<IFeedbackService>();
        _coordinator = new ConversationCoordinator(
            _conversationService.Object,
            _feedbackService.Object);
    }

    // ── CreateConversationAsync ────────────────────────────────────

    [Fact]
    public async Task CreateConversationAsync_ReturnsSummary_OnSuccess()
    {
        // Arrange
        var convEntity = new ConversationEntity
        {
            Id = 42,
            Title = "Test Conv",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _conversationService
            .Setup(s => s.CreateConversationAsync("Test Conv", null, null))
            .ReturnsAsync(convEntity);

        // Act
        var result = await _coordinator.CreateConversationAsync("Test Conv", null, null);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Title.Should().Be("Test Conv");
    }

    [Fact]
    public async Task CreateConversationAsync_ReturnsNull_OnFailure()
    {
        // Arrange
        _conversationService
            .Setup(s => s.CreateConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        var result = await _coordinator.CreateConversationAsync("Fail", null, null);

        // Assert
        result.Should().BeNull();
    }

    // ── DeleteConversationAsync ────────────────────────────────────

    [Fact]
    public async Task DeleteConversationAsync_CallsService_And_RaisesEvent()
    {
        // Arrange
        EventHandler? handler = null;
        var eventRaised = false;
        handler = (s, e) => eventRaised = true;
        _coordinator.ConversationsChanged += handler;

        try
        {
            // Act
            await _coordinator.DeleteConversationAsync(99);

            // Assert
            _conversationService.Verify(s => s.DeleteConversationAsync(99), Times.Once);
            eventRaised.Should().BeTrue();
        }
        finally
        {
            _coordinator.ConversationsChanged -= handler;
        }
    }

    // ── LoadConversationsAsync ─────────────────────────────────────

    [Fact]
    public async Task LoadConversationsAsync_ReturnsSummaries()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new()
            {
                Id = 1,
                Title = "Conv 1",
                UpdatedAt = DateTime.UtcNow,
                IsPinned = true,
                MessageCount = 5,
                FolderName = "Work"
            },
            new()
            {
                Id = 2,
                Title = "Conv 2",
                UpdatedAt = DateTime.UtcNow,
                IsPinned = false,
                MessageCount = 0,
                FolderName = null
            }
        };
        _conversationService
            .Setup(s => s.GetAllConversationsAsync(false))
            .ReturnsAsync(conversations);

        // Act
        var result = await _coordinator.LoadConversationsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].IsPinned.Should().BeTrue();
        result[0].FolderName.Should().Be("Work");
        result[1].Id.Should().Be(2);
    }

    [Fact]
    public async Task LoadConversationsAsync_ReturnsEmpty_OnFailure()
    {
        // Arrange
        _conversationService
            .Setup(s => s.GetAllConversationsAsync(false))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        var result = await _coordinator.LoadConversationsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── TogglePinAsync ─────────────────────────────────────────────

    [Fact]
    public async Task TogglePinAsync_CallsService_And_RaisesEvent()
    {
        // Arrange
        var eventRaised = false;
        EventHandler? handler = null;
        handler = (s, e) => eventRaised = true;
        _coordinator.ConversationsChanged += handler;

        try
        {
            // Act
            await _coordinator.TogglePinAsync(10);

            // Assert
            _conversationService.Verify(s => s.TogglePinAsync(10), Times.Once);
            eventRaised.Should().BeTrue();
        }
        finally
        {
            _coordinator.ConversationsChanged -= handler;
        }
    }

    // ── SetConversationFolderAsync ─────────────────────────────────

    [Fact]
    public async Task SetConversationFolderAsync_CallsService_And_RaisesBothEvents()
    {
        // Arrange
        var conversationsChanged = false;
        var folderNamesChanged = false;
        EventHandler? handler1 = null;
        EventHandler? handler2 = null;
        handler1 = (s, e) => conversationsChanged = true;
        handler2 = (s, e) => folderNamesChanged = true;
        _coordinator.ConversationsChanged += handler1;
        _coordinator.FolderNamesChanged += handler2;

        try
        {
            // Act
            await _coordinator.SetConversationFolderAsync(5, "Research");

            // Assert
            _conversationService.Verify(
                s => s.SetConversationFolderAsync(5, "Research"), Times.Once);
            conversationsChanged.Should().BeTrue();
            folderNamesChanged.Should().BeTrue();
        }
        finally
        {
            _coordinator.ConversationsChanged -= handler1;
            _coordinator.FolderNamesChanged -= handler2;
        }
    }

    // ── LoadConversationsByFolderAsync ─────────────────────────────

    [Fact]
    public async Task LoadConversationsByFolderAsync_ReturnsFiltered()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new()
            {
                Id = 3,
                Title = "Work Conv",
                UpdatedAt = DateTime.UtcNow,
                FolderName = "Work"
            }
        };
        _conversationService
            .Setup(s => s.GetConversationsByFolderAsync("Work"))
            .ReturnsAsync(conversations);

        // Act
        var result = await _coordinator.LoadConversationsByFolderAsync("Work");

        // Assert
        result.Should().HaveCount(1);
        result[0].FolderName.Should().Be("Work");
    }

    // ── LoadFolderNamesAsync ───────────────────────────────────────

    [Fact]
    public async Task LoadFolderNamesAsync_ReturnsNames()
    {
        // Arrange
        var folders = new List<string> { "Work", "Personal", "Research" };
        _conversationService
            .Setup(s => s.GetAllFolderNamesAsync())
            .ReturnsAsync(folders);

        // Act
        var result = await _coordinator.LoadFolderNamesAsync();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("Work");
    }

    [Fact]
    public async Task LoadFolderNamesAsync_ReturnsEmpty_OnFailure()
    {
        // Arrange
        _conversationService
            .Setup(s => s.GetAllFolderNamesAsync())
            .ThrowsAsync(new Exception("DB error"));

        // Act
        var result = await _coordinator.LoadFolderNamesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── SearchConversationsAsync ───────────────────────────────────

    [Fact]
    public async Task SearchConversationsAsync_ReturnsMatching()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new()
            {
                Id = 10,
                Title = "Agent-X Discussion",
                UpdatedAt = DateTime.UtcNow
            }
        };
        _conversationService
            .Setup(s => s.SearchConversationsAsync("Agent"))
            .ReturnsAsync(conversations);

        // Act
        var result = await _coordinator.SearchConversationsAsync("Agent");

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Agent-X Discussion");
    }

    // ── LoadMessagesAsync ──────────────────────────────────────────

    [Fact]
    public async Task LoadMessagesAsync_ReturnsMessageSummaries()
    {
        // Arrange
        var messages = new List<MessageEntity>
        {
            new()
            {
                Id = 100,
                ConversationId = 1,
                SortOrder = 0,
                Role = "user",
                Content = "Hello",
                Timestamp = DateTime.UtcNow,
                TokenCount = 0,
                GenerationTimeMs = null
            },
            new()
            {
                Id = 101,
                ConversationId = 1,
                SortOrder = 1,
                Role = "assistant",
                Content = "Hi there!",
                Timestamp = DateTime.UtcNow,
                TokenCount = 5,
                GenerationTimeMs = 200
            }
        };
        _conversationService
            .Setup(s => s.GetMessagesAsync(1))
            .ReturnsAsync(messages);

        // Act
        var result = await _coordinator.LoadMessagesAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].Role.Should().Be("user");
        result[0].MessageId.Should().Be(100);
        result[1].Role.Should().Be("assistant");
        result[1].MessageId.Should().Be(101);
        result[1].TokenCount.Should().Be(5);
    }

    [Fact]
    public async Task LoadMessagesAsync_LoadsFeedbackForAssistantMessages()
    {
        // Arrange
        var messages = new List<MessageEntity>
        {
            new()
            {
                Id = 200,
                ConversationId = 1,
                SortOrder = 0,
                Role = "assistant",
                Content = "Response",
                Timestamp = DateTime.UtcNow,
                TokenCount = 0
            }
        };
        _conversationService
            .Setup(s => s.GetMessagesAsync(1))
            .ReturnsAsync(messages);

        var feedback = new FeedbackEntity
        {
            Id = 1,
            MessageId = 200,
            Rating = "positive"
        };
        _feedbackService
            .Setup(s => s.GetFeedbackForMessageAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedback);

        // Act
        var result = await _coordinator.LoadMessagesAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].FeedbackRating.Should().Be("positive");
    }

    // ── Event helpers ──────────────────────────────────────────────

    [Fact]
    public void RaiseConversationsChanged_RaisesEvent()
    {
        // Arrange
        var eventRaised = false;
        EventHandler? handler = null;
        handler = (s, e) => eventRaised = true;
        _coordinator.ConversationsChanged += handler;

        try
        {
            // Act
            _coordinator.RaiseConversationsChanged();

            // Assert
            eventRaised.Should().BeTrue();
        }
        finally
        {
            _coordinator.ConversationsChanged -= handler;
        }
    }

    [Fact]
    public void RaiseFolderNamesChanged_RaisesEvent()
    {
        // Arrange
        var eventRaised = false;
        EventHandler? handler = null;
        handler = (s, e) => eventRaised = true;
        _coordinator.FolderNamesChanged += handler;

        try
        {
            // Act
            _coordinator.RaiseFolderNamesChanged();

            // Assert
            eventRaised.Should().BeTrue();
        }
        finally
        {
            _coordinator.FolderNamesChanged -= handler;
        }
    }
}

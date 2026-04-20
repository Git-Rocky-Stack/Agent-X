using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels.Coordinators;

public class BranchingCoordinatorTests
{
    private readonly Mock<IConversationBranchService> _branchService;
    private readonly Mock<IConversationService> _conversationService;
    private readonly BranchingCoordinator _coordinator;

    public BranchingCoordinatorTests()
    {
        _branchService = new Mock<IConversationBranchService>();
        _conversationService = new Mock<IConversationService>();
        _coordinator = new BranchingCoordinator(
            _branchService.Object,
            _conversationService.Object);
    }

    // ── BranchFromMessageAsync ──────────────────────────────────────

    [Fact]
    public async Task BranchFromMessageAsync_ReturnsResult_OnSuccess()
    {
        // Arrange
        var branchEntity = new ConversationEntity
        {
            Id = 100,
            Title = "Branch from msg 5",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _branchService
            .Setup(s => s.BranchAtMessageAsync(10, 5, "label", default))
            .ReturnsAsync(branchEntity);

        // Act
        var result = await _coordinator.BranchFromMessageAsync(10, 5, "label");

        // Assert
        result.Should().NotBeNull();
        result!.BranchConversationId.Should().Be(100);
        result.Title.Should().Be("Branch from msg 5");
    }

    [Fact]
    public async Task BranchFromMessageAsync_RaisesBranchTreeChanged_OnSuccess()
    {
        // Arrange
        long? changedConvId = null;
        _coordinator.BranchTreeChanged += (s, id) => changedConvId = id;

        var branchEntity = new ConversationEntity
        {
            Id = 200,
            Title = "New Branch",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _branchService
            .Setup(s => s.BranchAtMessageAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(branchEntity);

        // Act
        await _coordinator.BranchFromMessageAsync(10, 5, null);

        // Assert
        changedConvId.Should().Be(10);
    }

    [Fact]
    public async Task BranchFromMessageAsync_ReturnsNull_OnFailure()
    {
        // Arrange
        _branchService
            .Setup(s => s.BranchAtMessageAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        var result = await _coordinator.BranchFromMessageAsync(10, 5, null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task BranchFromMessageAsync_RaisesNotification_OnFailure()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _branchService
            .Setup(s => s.BranchAtMessageAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        await _coordinator.BranchFromMessageAsync(10, 5, null);

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Branch Failed");
    }

    // ── LoadBranchTreeAsync ─────────────────────────────────────────

    [Fact]
    public async Task LoadBranchTreeAsync_ReturnsTree_OnSuccess()
    {
        // Arrange
        var tree = new ConversationBranchTree
        {
            Conversation = new ConversationEntity { Id = 10, Title = "Root" },
            Children = new List<ConversationBranchTree>
            {
                new()
                {
                    Conversation = new ConversationEntity { Id = 20, Title = "Branch 1" },
                    BranchPointMessageId = 5
                }
            }
        };
        _branchService
            .Setup(s => s.GetBranchTreeAsync(10, default))
            .ReturnsAsync(tree);

        // Act
        var result = await _coordinator.LoadBranchTreeAsync(10);

        // Assert
        result.Should().NotBeNull();
        result!.Children.Should().HaveCount(1);
        result.Children[0].BranchPointMessageId.Should().Be(5);
    }

    [Fact]
    public async Task LoadBranchTreeAsync_ReturnsNull_OnFailure()
    {
        // Arrange
        _branchService
            .Setup(s => s.GetBranchTreeAsync(It.IsAny<long>(), default))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        var result = await _coordinator.LoadBranchTreeAsync(10);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadBranchTreeAsync_RaisesNotification_OnFailure()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _branchService
            .Setup(s => s.GetBranchTreeAsync(It.IsAny<long>(), default))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        await _coordinator.LoadBranchTreeAsync(10);

        // Assert
        notification.Should().NotBeNull();
        notification!.Title.Should().Be("Branch Load Failed");
    }

    // ── MergeToMainAsync ────────────────────────────────────────────

    [Fact]
    public async Task MergeToMainAsync_CallsService_WithProvidedMessageIds()
    {
        // Arrange
        var request = new MergeBranchRequest(20, 10, new List<long> { 1, 2, 3 });

        // Act
        await _coordinator.MergeToMainAsync(request);

        // Assert
        _branchService.Verify(
            s => s.MergeMessagesAsync(20, It.Is<IReadOnlyList<long>>(ids => ids.Count == 3), 10, default),
            Times.Once);
    }

    [Fact]
    public async Task MergeToMainAsync_LoadsAllMessageIds_WhenNoneProvided()
    {
        // Arrange
        var request = new MergeBranchRequest(20, 10, null);
        var messages = new List<MessageEntity>
        {
            new() { Id = 100 },
            new() { Id = 101 }
        };
        _conversationService
            .Setup(s => s.GetMessagesAsync(20))
            .ReturnsAsync(messages);

        // Act
        await _coordinator.MergeToMainAsync(request);

        // Assert
        _branchService.Verify(
            s => s.MergeMessagesAsync(20, It.Is<IReadOnlyList<long>>(ids => ids.Count == 2 && ids.Contains(100) && ids.Contains(101)), 10, default),
            Times.Once);
    }

    [Fact]
    public async Task MergeToMainAsync_RaisesBranchTreeChanged_OnSuccess()
    {
        // Arrange
        long? changedConvId = null;
        _coordinator.BranchTreeChanged += (s, id) => changedConvId = id;

        var request = new MergeBranchRequest(20, 10, new List<long> { 1 });

        // Act
        await _coordinator.MergeToMainAsync(request);

        // Assert
        changedConvId.Should().Be(10);
    }

    [Fact]
    public async Task MergeToMainAsync_RaisesNotification_OnSuccess()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        var request = new MergeBranchRequest(20, 10, new List<long> { 1 });

        // Act
        await _coordinator.MergeToMainAsync(request);

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("info");
        notification.Title.Should().Be("Merge Complete");
    }

    [Fact]
    public async Task MergeToMainAsync_RaisesNotification_OnFailure()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _branchService
            .Setup(s => s.MergeMessagesAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<long>(), default))
            .ThrowsAsync(new Exception("Merge error"));

        var request = new MergeBranchRequest(20, 10, new List<long> { 1 });

        // Act
        await _coordinator.MergeToMainAsync(request);

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Merge Failed");
    }

    [Fact]
    public async Task MergeToMainAsync_DoesNothing_WhenRequestIsNull()
    {
        // Act
        await _coordinator.MergeToMainAsync(null!);

        // Assert
        _branchService.Verify(
            s => s.MergeMessagesAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<long>(), default),
            Times.Never);
    }

    // ── DeleteBranchAsync ───────────────────────────────────────────

    [Fact]
    public async Task DeleteBranchAsync_CallsService()
    {
        // Act
        await _coordinator.DeleteBranchAsync(42);

        // Assert
        _branchService.Verify(s => s.DeleteBranchAsync(42, true, default), Times.Once);
    }

    [Fact]
    public async Task DeleteBranchAsync_RaisesBranchTreeChanged_OnSuccess()
    {
        // Arrange
        long? changedConvId = null;
        _coordinator.BranchTreeChanged += (s, id) => changedConvId = id;

        // Act
        await _coordinator.DeleteBranchAsync(42);

        // Assert
        changedConvId.Should().Be(42);
    }

    [Fact]
    public async Task DeleteBranchAsync_RaisesNotification_OnSuccess()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        // Act
        await _coordinator.DeleteBranchAsync(42);

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("info");
        notification.Title.Should().Be("Branch Deleted");
    }

    [Fact]
    public async Task DeleteBranchAsync_RaisesNotification_OnFailure()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _branchService
            .Setup(s => s.DeleteBranchAsync(It.IsAny<long>(), It.IsAny<bool>(), default))
            .ThrowsAsync(new Exception("Delete error"));

        // Act
        await _coordinator.DeleteBranchAsync(42);

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Delete Failed");
    }
}

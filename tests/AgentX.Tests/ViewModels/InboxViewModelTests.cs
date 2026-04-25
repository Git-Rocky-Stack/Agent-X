using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class InboxViewModelTests
{
    private readonly Mock<IInboxService> _inboxService = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();

    [Fact]
    public async Task InitializeAsync_focuses_requested_operations_item()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _inboxService.Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(2);
        _inboxService.Setup(service => service.GetAllItemsAsync("pending", 0, 100))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Older note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                },
                new InboxItemEntity
                {
                    Id = 7,
                    FileName = "Board update.docx",
                    FileType = "Document",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingInboxRequest())
            .Returns(new OperationsInboxDrillInRequest(7, "Opened inbox item \"Board update.docx\" from Operations"));

        var viewModel = new InboxViewModel(
            _inboxService.Object,
            _collectionService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();

        viewModel.InboxItems.Should().HaveCount(2);
        viewModel.InboxItems[0].Id.Should().Be(7);
        viewModel.InboxItems[0].IsFocused.Should().BeTrue();
        viewModel.FocusedInboxItemId.Should().Be(7);
        viewModel.HasFocusedInboxLanding.Should().BeTrue();
        viewModel.FocusedInboxSourceLabel.Should().Contain("Board update.docx");
        viewModel.FocusedInboxVisibilityHint.Should().BeEmpty();
        viewModel.StatusMessage.Should().Contain("Board update.docx");
    }

    [Fact]
    public async Task InitializeAsync_widens_filter_for_requested_operations_item_and_sets_visibility_hint()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _inboxService.Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(1);
        _inboxService.Setup(service => service.GetAllItemsAsync("pending", 0, 100))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Pending note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                }
            ]);
        _inboxService.Setup(service => service.GetAllItemsAsync(null, 0, 100))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Pending note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                },
                new InboxItemEntity
                {
                    Id = 7,
                    FileName = "Board update.docx",
                    FileType = "Document",
                    Status = "accepted",
                    AddedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingInboxRequest())
            .Returns(new OperationsInboxDrillInRequest(7, "Opened inbox item \"Board update.docx\" from Operations"));

        var viewModel = new InboxViewModel(
            _inboxService.Object,
            _collectionService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();

        viewModel.StatusFilter.Should().Be("all");
        viewModel.InboxItems[0].Id.Should().Be(7);
        viewModel.FocusedInboxVisibilityHint.Should().Be("Status filter widened to show the requested inbox item.");
    }

    [Fact]
    public async Task RefreshCommand_preserves_focused_inbox_landing_until_dismissed()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _inboxService.Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(2);
        _inboxService.Setup(service => service.GetAllItemsAsync("pending", 0, 100))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Older note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                },
                new InboxItemEntity
                {
                    Id = 7,
                    FileName = "Board update.docx",
                    FileType = "Document",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingInboxRequest())
            .Returns(new OperationsInboxDrillInRequest(7, "Opened inbox item \"Board update.docx\" from Operations"));

        var viewModel = new InboxViewModel(
            _inboxService.Object,
            _collectionService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.HasFocusedInboxLanding.Should().BeTrue();
        viewModel.FocusedInboxItemId.Should().Be(7);
        viewModel.InboxItems[0].Id.Should().Be(7);
        viewModel.InboxItems[0].IsFocused.Should().BeTrue();
    }

    [Fact]
    public async Task DismissFocusedInboxLandingCommand_clears_banner_row_focus_and_status()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _inboxService.Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(2);
        _inboxService.Setup(service => service.GetAllItemsAsync("pending", 0, 100))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Older note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                },
                new InboxItemEntity
                {
                    Id = 7,
                    FileName = "Board update.docx",
                    FileType = "Document",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingInboxRequest())
            .Returns(new OperationsInboxDrillInRequest(7, "Opened inbox item \"Board update.docx\" from Operations"));

        var viewModel = new InboxViewModel(
            _inboxService.Object,
            _collectionService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        viewModel.DismissFocusedInboxLandingCommand.Execute(null);

        viewModel.FocusedInboxItemId.Should().Be(0);
        viewModel.HasFocusedInboxLanding.Should().BeFalse();
        viewModel.FocusedInboxSourceLabel.Should().BeEmpty();
        viewModel.FocusedInboxVisibilityHint.Should().BeEmpty();
        viewModel.StatusMessage.Should().BeEmpty();
        viewModel.InboxItems.Should().OnlyContain(item => !item.IsFocused);
    }

    [Fact]
    public async Task AcceptItemCommand_resolves_focused_inbox_item_after_successful_accept()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _inboxService.SetupSequence(service => service.GetPendingCountAsync())
            .ReturnsAsync(2)
            .ReturnsAsync(1);
        _inboxService.SetupSequence(service => service.GetAllItemsAsync("pending", 0, 100))
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Older note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                },
                new InboxItemEntity
                {
                    Id = 7,
                    FileName = "Board update.docx",
                    FileType = "Document",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ])
            .ReturnsAsync(
            [
                new InboxItemEntity
                {
                    Id = 1,
                    FileName = "Older note.md",
                    FileType = "Markdown",
                    Status = "pending",
                    AddedAt = DateTime.UtcNow.AddMinutes(-30)
                }
            ]);
        _inboxService.Setup(service => service.AcceptItemAsync(7, null))
            .Returns(Task.CompletedTask);
        _operationsDrillInService.Setup(service => service.ConsumePendingInboxRequest())
            .Returns(new OperationsInboxDrillInRequest(7, "Opened inbox item \"Board update.docx\" from Operations"));

        var viewModel = new InboxViewModel(
            _inboxService.Object,
            _collectionService.Object,
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.AcceptItemCommand.ExecuteAsync(7L);

        _inboxService.Verify(service => service.AcceptItemAsync(7, null), Times.Once);
        viewModel.FocusedInboxItemId.Should().Be(0);
        viewModel.HasFocusedInboxLanding.Should().BeFalse();
        viewModel.InboxItems.Should().OnlyContain(item => !item.IsFocused);
        viewModel.StatusMessage.Should().Be("Resolved \"Board update.docx\" by accepting it and queuing it for indexing.");
    }
}

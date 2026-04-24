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
        viewModel.StatusMessage.Should().Contain("Board update.docx");
    }
}

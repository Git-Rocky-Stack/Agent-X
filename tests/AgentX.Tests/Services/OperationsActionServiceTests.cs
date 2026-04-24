using AgentX.App.Services;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class OperationsActionServiceTests
{
    private readonly Mock<IConversationSummaryService> _conversationSummaryService = new();
    private readonly Mock<IInboxService> _inboxService = new();
    private readonly Mock<ISyncService> _syncService = new();

    [Fact]
    public async Task GenerateInboxPreviewsAsync_returns_noop_message_when_backlog_is_clear()
    {
        _inboxService
            .Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(0);

        var sut = CreateService();

        var result = await sut.GenerateInboxPreviewsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("No pending inbox items need preview generation.");
        _inboxService.Verify(service => service.GenerateAllPreviewsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateInboxPreviewsAsync_runs_inbox_preview_generation_when_items_are_pending()
    {
        _inboxService
            .Setup(service => service.GetPendingCountAsync())
            .ReturnsAsync(3);
        _inboxService
            .Setup(service => service.GenerateAllPreviewsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateService();

        var result = await sut.GenerateInboxPreviewsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Generated AI previews for pending inbox items.");
        _inboxService.Verify(service => service.GenerateAllPreviewsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshConversationSummariesAsync_returns_success_message_for_refreshed_count()
    {
        _conversationSummaryService
            .Setup(service => service.RefreshStaleSummariesAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = CreateService();

        var result = await sut.RefreshConversationSummariesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Refreshed 2 conversation summaries.");
    }

    [Fact]
    public async Task RunManualSyncAsync_returns_configuration_error_when_sync_is_not_configured()
    {
        _syncService
            .Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync((SyncConfiguration?)null);

        var sut = CreateService();

        var result = await sut.RunManualSyncAsync();

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Save a sync configuration");
        _syncService.Verify(service => service.ExportChangesAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunManualSyncAsync_exports_changes_and_starts_auto_sync()
    {
        _syncService
            .Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService
            .Setup(service => service.ExportChangesAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncChangeSet
            {
                Changes =
                [
                    new SyncChange
                    {
                        EntityType = "ConversationEntity",
                        EntityId = 42,
                        ChangeType = SyncChangeType.Updated,
                        Timestamp = DateTime.UtcNow,
                        SerializedData = "{}"
                    }
                ]
            });
        _syncService
            .Setup(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateService();

        var result = await sut.RunManualSyncAsync();

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("1 change(s) exported");
        _syncService.Verify(service => service.ExportChangesAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        _syncService.Verify(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private OperationsActionService CreateService() =>
        new(
            _conversationSummaryService.Object,
            _inboxService.Object,
            _syncService.Object,
            Log.ForContext<OperationsActionServiceTests>());
}

using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins;
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
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IInboxService> _inboxService = new();
    private readonly Mock<IPluginService> _pluginService = new();
    private readonly Mock<ISyncService> _syncService = new();

    [Fact]
    public async Task EnableConnectorAsync_enables_disabled_connector()
    {
        _pluginService
            .Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(41, "Email Connector", "DataConnector", enabled: false)
            ]);
        _pluginService
            .Setup(service => service.EnablePluginAsync(41))
            .Returns(Task.CompletedTask);

        var sut = CreateService();

        var result = await sut.EnableConnectorAsync(41);

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Enabled Email Connector.");
        _pluginService.Verify(service => service.EnablePluginAsync(41), Times.Once);
    }

    [Fact]
    public async Task EnableConnectorAsync_rejects_non_connector_plugins()
    {
        _pluginService
            .Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(52, "Workflow Step Kit", "WorkflowStep", enabled: false)
            ]);

        var sut = CreateService();

        var result = await sut.EnableConnectorAsync(52);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Plugin Manager");
        _pluginService.Verify(service => service.EnablePluginAsync(It.IsAny<long>()), Times.Never);
    }

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
    public async Task ReindexImportedDocumentAsync_requeues_document_for_indexing()
    {
        _documentService
            .Setup(service => service.ReindexDocumentAsync(501, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateService();

        var result = await sut.ReindexImportedDocumentAsync(501);

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Queued imported document for re-indexing.");
        _documentService.Verify(service => service.ReindexDocumentAsync(501, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReindexImportedDocumentAsync_returns_error_for_invalid_document()
    {
        var sut = CreateService();

        var result = await sut.ReindexImportedDocumentAsync(0);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Select an imported document");
        _documentService.Verify(service => service.ReindexDocumentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _syncService.Verify(service => service.SyncNowAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunManualSyncAsync_runs_a_real_export_and_import_pass()
    {
        _syncService
            .Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService
            .Setup(service => service.SyncNowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncRunResult
            {
                ExportedChanges = 1,
                PeerFilesFound = 2,
                PeerFilesImported = 2,
                ChangesApplied = 5,
            });

        var sut = CreateService();

        var result = await sut.RunManualSyncAsync();

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Sync complete. Exported 1 change(s); imported 2 of 2 peer file(s), 5 change(s) applied.");
        _syncService.Verify(service => service.SyncNowAsync(It.IsAny<CancellationToken>()), Times.Once);
        _syncService.Verify(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()), Times.Never,
            "starting the loop is not an import: its first tick is a whole interval away");
    }

    [Fact]
    public async Task RunManualSyncAsync_reports_files_left_for_retry_as_a_problem()
    {
        _syncService
            .Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService
            .Setup(service => service.SyncNowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncRunResult
            {
                PeerFilesFound = 1,
                PeerFilesPendingRetry = 1,
                ChangesFailed = 2,
            });

        var sut = CreateService();

        var result = await sut.RunManualSyncAsync();

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().StartWith("Sync finished with problems: 1 file(s) will be retried.");
    }

    private OperationsActionService CreateService() =>
        new(
            _conversationSummaryService.Object,
            _documentService.Object,
            _inboxService.Object,
            _pluginService.Object,
            _syncService.Object,
            Log.ForContext<OperationsActionServiceTests>());

    private static PluginEntity CreatePlugin(long id, string name, string pluginType, bool enabled) =>
        new()
        {
            Id = id,
            PluginId = $"com.agentx.{name.Replace(" ", string.Empty).ToLowerInvariant()}",
            Name = name,
            PluginType = pluginType,
            Version = "1.0.0",
            IsEnabled = enabled
        };
}

using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class SyncSettingsViewModelTests
{
    private readonly Mock<ISyncService> _syncService = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();

    public SyncSettingsViewModelTests()
    {
        _syncService.SetupGet(service => service.Status).Returns(new SyncStatus());
        _syncService.Setup(service => service.GetSyncHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<SyncLogEntity>());
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
    }

    [Fact]
    public async Task InitializeAsync_marks_selected_collections_from_saved_configuration()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret",
                SyncScope = SyncScope.SelectedCollections,
                SelectedCollectionIds = "2,3"
            });

        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(new[]
            {
                new CollectionEntity { Id = 1, Name = "Alpha", DocumentCount = 4 },
                new CollectionEntity { Id = 2, Name = "Beta", DocumentCount = 2 },
                new CollectionEntity { Id = 3, Name = "Gamma", DocumentCount = 7 }
            });

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object);

        await viewModel.InitializeAsync();

        viewModel.SyncScope.Should().Be("SelectedCollections");
        viewModel.ShowSelectedCollectionsPicker.Should().BeTrue();
        viewModel.AvailableCollections.Should().HaveCount(3);
        viewModel.AvailableCollections.Single(collection => collection.Id == 2).IsSelected.Should().BeTrue();
        viewModel.AvailableCollections.Single(collection => collection.Id == 3).IsSelected.Should().BeTrue();
        viewModel.SelectedCollectionIds.Should().Be("2,3");
    }

    [Fact]
    public async Task SaveConfigurationAsync_uses_checked_collections_for_selected_scope()
    {
        SyncConfiguration? savedConfig = null;

        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync((SyncConfiguration?)null);
        _syncService.Setup(service => service.ConfigureAsync(It.IsAny<SyncConfiguration>()))
            .Callback<SyncConfiguration>(config => savedConfig = config)
            .Returns(Task.CompletedTask);

        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(new[]
            {
                new CollectionEntity { Id = 10, Name = "Research", DocumentCount = 5, SortOrder = 1 },
                new CollectionEntity { Id = 21, Name = "Operations", DocumentCount = 3, SortOrder = 2 }
            });

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object)
        {
            SyncFolderPath = @"C:\Sync",
            EncryptionKey = "secret",
            SyncScope = "SelectedCollections"
        };

        await viewModel.InitializeAsync();

        viewModel.AvailableCollections[0].IsSelected = true;
        viewModel.AvailableCollections[1].IsSelected = true;

        await viewModel.SaveConfigurationCommand.ExecuteAsync(null);

        savedConfig.Should().NotBeNull();
        savedConfig!.SyncScope.Should().Be(SyncScope.SelectedCollections);
        savedConfig.SelectedCollectionIds.Should().Be("10,21");
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task SaveConfigurationAsync_requires_visible_collection_selection_for_selected_scope()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync((SyncConfiguration?)null);

        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(new[]
            {
                new CollectionEntity { Id = 10, Name = "Research", DocumentCount = 5 }
            });

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object)
        {
            SyncFolderPath = @"C:\Sync",
            EncryptionKey = "secret",
            SyncScope = "SelectedCollections"
        };

        await viewModel.InitializeAsync();
        await viewModel.SaveConfigurationCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("Select at least one collection");
        _syncService.Verify(service => service.ConfigureAsync(It.IsAny<SyncConfiguration>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_focuses_requested_sync_history_entry()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService.Setup(service => service.GetSyncHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new SyncLogEntity
                {
                    Id = 3,
                    Direction = "export",
                    ChangesApplied = 2,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-15),
                    DurationMs = 1200
                },
                new SyncLogEntity
                {
                    Id = 9,
                    Direction = "import",
                    ChangesApplied = 12,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-5),
                    DurationMs = 2400
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingSyncRequest())
            .Returns(new OperationsSyncDrillInRequest(9, "Opened sync history entry \"Import sync\" from Operations"));

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object);

        await viewModel.InitializeAsync();

        viewModel.SyncHistory.Should().HaveCount(2);
        viewModel.FocusedSyncLogId.Should().Be(9);
        viewModel.HasFocusedSyncLanding.Should().BeTrue();
        viewModel.FocusedSyncSourceLabel.Should().Contain("Import sync");
        viewModel.SyncHistory[0].Id.Should().Be(9);
        viewModel.SyncHistory[0].IsFocused.Should().BeTrue();
        viewModel.StatusMessage.Should().Contain("Opened sync history entry");
    }

    [Fact]
    public async Task DismissFocusedSyncLandingCommand_clears_banner_row_focus_and_status()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService.Setup(service => service.GetSyncHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new SyncLogEntity
                {
                    Id = 3,
                    Direction = "export",
                    ChangesApplied = 2,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-15),
                    DurationMs = 1200
                },
                new SyncLogEntity
                {
                    Id = 9,
                    Direction = "import",
                    ChangesApplied = 12,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-5),
                    DurationMs = 2400
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingSyncRequest())
            .Returns(new OperationsSyncDrillInRequest(9, "Opened sync history entry \"Import sync\" from Operations"));

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        viewModel.DismissFocusedSyncLandingCommand.Execute(null);

        viewModel.FocusedSyncLogId.Should().Be(0);
        viewModel.HasFocusedSyncLanding.Should().BeFalse();
        viewModel.FocusedSyncSourceLabel.Should().BeEmpty();
        viewModel.HasStatusMessage.Should().BeFalse();
        viewModel.StatusMessage.Should().BeEmpty();
        viewModel.SyncHistory.Should().OnlyContain(item => !item.IsFocused);
    }

    [Fact]
    public async Task RefreshCommand_preserves_focused_sync_landing_until_dismissed()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService.Setup(service => service.GetSyncHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new SyncLogEntity
                {
                    Id = 3,
                    Direction = "export",
                    ChangesApplied = 2,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-15),
                    DurationMs = 1200
                },
                new SyncLogEntity
                {
                    Id = 9,
                    Direction = "import",
                    ChangesApplied = 12,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-5),
                    DurationMs = 2400
                }
            ]);
        _operationsDrillInService.SetupSequence(service => service.ConsumePendingSyncRequest())
            .Returns(new OperationsSyncDrillInRequest(9, "Opened sync history entry \"Import sync\" from Operations"))
            .Returns((OperationsSyncDrillInRequest?)null);

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.HasFocusedSyncLanding.Should().BeTrue();
        viewModel.FocusedSyncLogId.Should().Be(9);
        viewModel.SyncHistory[0].Id.Should().Be(9);
        viewModel.SyncHistory[0].IsFocused.Should().BeTrue();
        viewModel.StatusMessage.Should().Contain("Opened sync history entry");
    }

    [Fact]
    public async Task SyncNowAsync_resolves_focused_sync_landing_after_successful_sync()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret"
            });
        _syncService.SetupSequence(service => service.GetSyncHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new SyncLogEntity
                {
                    Id = 3,
                    Direction = "export",
                    ChangesApplied = 2,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-15),
                    DurationMs = 1200
                },
                new SyncLogEntity
                {
                    Id = 9,
                    Direction = "import",
                    ChangesApplied = 12,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-5),
                    DurationMs = 2400
                }
            ])
            .ReturnsAsync(
            [
                new SyncLogEntity
                {
                    Id = 11,
                    Direction = "export",
                    ChangesApplied = 1,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-1),
                    DurationMs = 1800
                },
                new SyncLogEntity
                {
                    Id = 9,
                    Direction = "import",
                    ChangesApplied = 12,
                    IsSuccess = true,
                    SyncedAt = DateTime.UtcNow.AddMinutes(-5),
                    DurationMs = 2400
                }
            ]);
        _syncService.Setup(service => service.ExportChangesAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
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
        _syncService.Setup(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _operationsDrillInService.SetupSequence(service => service.ConsumePendingSyncRequest())
            .Returns(new OperationsSyncDrillInRequest(9, "Opened sync history entry \"Import sync\" from Operations"))
            .Returns((OperationsSyncDrillInRequest?)null);

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.SyncNowCommand.ExecuteAsync(null);

        _syncService.Verify(service => service.ExportChangesAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        _syncService.Verify(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        viewModel.FocusedSyncLogId.Should().Be(0);
        viewModel.FocusedSyncSourceLabel.Should().BeEmpty();
        viewModel.HasFocusedSyncLanding.Should().BeFalse();
        viewModel.SyncHistory.Should().OnlyContain(item => !item.IsFocused);
        viewModel.StatusMessage.Should().Be("Resolved the focused sync history entry by running a fresh sync pass.");
    }
}

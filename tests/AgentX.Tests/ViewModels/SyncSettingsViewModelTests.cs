using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Tests.Helpers;
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

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);

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

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object)
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

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object)
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

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);

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

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);

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

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);

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
        _syncService.Setup(service => service.SyncNowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncRunResult { ExportedChanges = 1 });
        _operationsDrillInService.SetupSequence(service => service.ConsumePendingSyncRequest())
            .Returns(new OperationsSyncDrillInRequest(9, "Opened sync history entry \"Import sync\" from Operations"))
            .Returns((OperationsSyncDrillInRequest?)null);

        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await viewModel.SyncNowCommand.ExecuteAsync(null);

        _syncService.Verify(service => service.SyncNowAsync(It.IsAny<CancellationToken>()), Times.Once);
        _syncService.Verify(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()), Times.Never);
        viewModel.FocusedSyncLogId.Should().Be(0);
        viewModel.FocusedSyncSourceLabel.Should().BeEmpty();
        viewModel.HasFocusedSyncLanding.Should().BeFalse();
        viewModel.SyncHistory.Should().OnlyContain(item => !item.IsFocused);
        viewModel.StatusMessage.Should().Be("Resolved the focused sync history entry by running a fresh sync pass.");
    }

    // ── Auto-sync toggle ─────────────────────────────────────────────────────
    // The Auto-Sync switch bound straight to the flag, so turning it off left the
    // background loop running: the control reported a state it did not enforce.

    [Fact]
    public async Task TurningTheAutoSyncToggleOff_StopsTheBackgroundLoop()
    {
        var viewModel = CreateConfiguredViewModel();
        viewModel.AutoSyncEnabled = true;

        viewModel.AutoSyncEnabled = false;
        await viewModel.StopAutoSyncCommand.ExecuteAsync(null);

        _syncService.Verify(service => service.StopAutoSyncAsync(), Times.AtLeastOnce);
        viewModel.AutoSyncEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task LoadingSavedConfiguration_DoesNotToggleTheBackgroundLoop()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret",
                AutoSyncEnabled = true,
                SyncIntervalMinutes = 15,
            });

        var viewModel = new SyncSettingsViewModel(
            _syncService.Object,
            _collectionService.Object,
            EnglishResources.Create(),
            _operationsDrillInService.Object);

        await viewModel.InitializeAsync();

        viewModel.AutoSyncEnabled.Should().BeTrue();
        _syncService.Verify(service => service.StopAutoSyncAsync(), Times.Never);
    }

    // ---- Sync Now reports what really happened ----

    [Fact]
    public async Task SyncNowAsync_ReportsTheImportOutcome_NotJustTheExport()
    {
        var viewModel = CreateConfiguredViewModel();
        await viewModel.InitializeAsync();
        _syncService.Setup(service => service.SyncNowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncRunResult
            {
                ExportedChanges = 2,
                PeerFilesFound = 1,
                PeerFilesImported = 1,
                ChangesApplied = 4,
                ConflictsResolved = 1,
            });

        await viewModel.SyncNowCommand.ExecuteAsync(null);

        viewModel.StatusMessage.Should().Be(
            "Sync complete. Exported 2 changes; imported 1 peer file (4 change(s) applied, 1 older than the local copy and skipped).");
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task SyncNowAsync_SurfacesFilesLeftForRetry_InsteadOfClaimingSuccess()
    {
        var viewModel = CreateConfiguredViewModel();
        await viewModel.InitializeAsync();
        var result = new SyncRunResult { PeerFilesFound = 1, PeerFilesPendingRetry = 1, ChangesFailed = 1 };
        result.Errors.Add("agentx-sync-peer: Conversation (remote id 9) could not be saved: constraint failed");
        _syncService.Setup(service => service.SyncNowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(result);

        await viewModel.SyncNowCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("will be retried on the next sync");
        viewModel.ErrorMessage.Should().Contain("constraint failed");
        viewModel.StatusMessage.Should().StartWith("Sync finished with problems.");
    }

    // ---- Auto-sync toggle persistence ----

    [Fact]
    public async Task StartAutoSync_PersistsTheToggleBeforeStartingTheLoop()
    {
        var viewModel = CreateConfiguredViewModel();
        await viewModel.InitializeAsync();

        var calls = new List<string>();
        _syncService.Setup(service => service.ConfigureAsync(It.IsAny<SyncConfiguration>()))
            .Callback((SyncConfiguration config) => calls.Add($"configure:{config.AutoSyncEnabled}"))
            .Returns(Task.CompletedTask);
        _syncService.Setup(service => service.StartAutoSyncAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("start"))
            .Returns(Task.CompletedTask);
        _syncService.SetupGet(service => service.IsAutoSyncRunning).Returns(true);

        await viewModel.StartAutoSyncCommand.ExecuteAsync(null);

        calls.Should().Equal("configure:True", "start");
        viewModel.AutoSyncEnabled.Should().BeTrue();
        viewModel.StatusMessage.Should().Contain("Auto-sync is on").And.Contain("15 minutes");
    }

    [Fact]
    public async Task StartAutoSync_WhenTheLoopDidNotStart_SaysSoAndTurnsTheToggleBackOff()
    {
        var viewModel = CreateConfiguredViewModel();
        await viewModel.InitializeAsync();
        _syncService.SetupGet(service => service.IsAutoSyncRunning).Returns(false);

        await viewModel.StartAutoSyncCommand.ExecuteAsync(null);

        viewModel.AutoSyncEnabled.Should().BeFalse();
        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("could not be started");
        viewModel.StatusMessage.Should().NotContain("started");
    }

    [Fact]
    public async Task StopAutoSync_PersistsTheDisabledToggle_SoItStaysOffAfterARestart()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret",
                AutoSyncEnabled = true,
                SyncIntervalMinutes = 15,
            });
        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);
        await viewModel.InitializeAsync();

        await viewModel.StopAutoSyncCommand.ExecuteAsync(null);

        _syncService.Verify(service => service.ConfigureAsync(It.Is<SyncConfiguration>(c => !c.AutoSyncEnabled)), Times.Once);
        _syncService.Verify(service => service.StopAutoSyncAsync(), Times.AtLeastOnce);
        viewModel.StatusMessage.Should().Be("Auto-sync stopped.");
    }

    private SyncSettingsViewModel CreateConfiguredViewModel()
    {
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration
            {
                SyncFolderPath = @"C:\Sync",
                EncryptionKey = "secret",
                AutoSyncEnabled = false,
                SyncIntervalMinutes = 15,
            });

        return new SyncSettingsViewModel(
            _syncService.Object,
            _collectionService.Object,
            EnglishResources.Create(),
            _operationsDrillInService.Object);
    }

    // ---- Shown in the user's language ----

    [Fact]
    public async Task SyncState_IsTranslated_WhileTheBadgeDotStillReadsAStatusToken()
    {
        // The badge dot is colored by StatusToColorConverter, which matches English status words.
        // It used to read the displayed state, so a translated state would have lost its color.
        var german = ReswLocalization.For("de");
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration { SyncFolderPath = @"C:\Sync", EncryptionKey = "secret" });
        _syncService.Setup(service => service.SyncNowAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("share offline"));
        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, german, _operationsDrillInService.Object);
        await viewModel.InitializeAsync();

        viewModel.SyncStateTone.Should().Be("idle");
        viewModel.SyncState.Should().Be(german.GetString("Sync_StateIdle")).And.NotBe("Idle");

        await viewModel.SyncNowCommand.ExecuteAsync(null);

        viewModel.CurrentSyncState.Should().Be(SyncState.Error);
        viewModel.SyncStateTone.Should().Be("error");
        viewModel.SyncState.Should().Be(german.GetString("Sync_StateError")).And.NotBe("Error");
        viewModel.ErrorMessage.Should().Be(german.GetString("Sync_SyncFailed", "share offline"));
    }

    [Fact]
    public void SyncScopeDropdown_SelectsByIndex_SoTheShownLabelsCanBeTranslated()
    {
        // The dropdown used to bind the stored value itself, so it listed "SelectedCollections".
        var viewModel = CreateConfiguredViewModel();

        viewModel.SyncScopeOptions.Should().Equal("All", "Selected Collections");
        viewModel.SelectedSyncScopeIndex.Should().Be(0);

        viewModel.SelectedSyncScopeIndex = 1;

        viewModel.SyncScope.Should().Be("SelectedCollections");
        viewModel.ShowSelectedCollectionsPicker.Should().BeTrue();

        viewModel.SyncScope = "All";

        viewModel.SelectedSyncScopeIndex.Should().Be(0);
        viewModel.ShowSelectedCollectionsPicker.Should().BeFalse();
    }

    [Fact]
    public void IntervalOptionsAndCollectionLabels_ComeFromTheResources()
    {
        var viewModel = CreateConfiguredViewModel();

        viewModel.IntervalOptions.Should().Equal(
            "Every 5 minutes", "Every 15 minutes", "Every 30 minutes", "Every hour", "Every 2 hours");
        viewModel.SelectedCollectionSummary.Should().Be("No collections selected");
        viewModel.LastSyncAt.Should().Be("Never");
        viewModel.SyncState.Should().Be("Idle");
    }

    [Fact]
    public async Task DismissingTheFocusedEntry_KeepsAStatusThatReplacedItsLabel()
    {
        // Whether the status line still shows the focus label is tracked as state, not found by
        // comparing the displayed text with the label.
        _syncService.Setup(service => service.GetConfigurationAsync())
            .ReturnsAsync(new SyncConfiguration { SyncFolderPath = @"C:\Sync", EncryptionKey = "secret" });
        _syncService.Setup(service => service.GetSyncHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync([new SyncLogEntity { Id = 9, Direction = "import", IsSuccess = true, SyncedAt = DateTime.UtcNow }]);
        _operationsDrillInService.SetupSequence(service => service.ConsumePendingSyncRequest())
            .Returns(new OperationsSyncDrillInRequest(9, "Opened sync history entry \"Import sync\" from Operations"))
            .Returns((OperationsSyncDrillInRequest?)null);
        var viewModel = new SyncSettingsViewModel(_syncService.Object, _collectionService.Object, EnglishResources.Create(), _operationsDrillInService.Object);
        await viewModel.InitializeAsync();

        await viewModel.SaveConfigurationCommand.ExecuteAsync(null);
        viewModel.DismissFocusedSyncLandingCommand.Execute(null);

        viewModel.StatusMessage.Should().Be("Sync configuration saved successfully.");
        viewModel.HasStatusMessage.Should().BeTrue();
        viewModel.HasFocusedSyncLanding.Should().BeFalse();
    }
}

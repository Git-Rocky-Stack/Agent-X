using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class EmailSettingsViewModelTests
{
    [Fact]
    public async Task SaveSettingsCommand_UpdatesAppSettingsPluginSettingsAndConnectorLifecycle()
    {
        var appSettings = new AppSettings();
        var settings = new Mock<ISettingsService>();
        var email = new Mock<IEmailService>();
        var lifecycle = new Mock<IBuiltinConnectorLifecycleService>();

        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(appSettings);
        email.Setup(e => e.GetSyncSettingsAsync()).ReturnsAsync(new EmailSyncSettings
        {
            EnabledFolders = { ["INBOX"] = true }
        });

        var vm = new EmailSettingsViewModel(
            settings.Object,
            Mock.Of<IOAuthService>(),
            email.Object,
            lifecycle.Object,
            Logger.None)
        {
            EnableEmailSync = true,
            SyncIntervalMinutes = 20,
            MaxMessagesPerSync = 75,
            SyncDaysBack = 60,
            EnableAiCategorization = false,
            IncludeAttachmentNames = true,
        };

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        appSettings.EmailConnector.EnableEmailSync.Should().BeTrue();
        appSettings.EmailConnector.SyncIntervalMinutes.Should().Be(20);
        appSettings.EmailConnector.MessagesPerSync.Should().Be(75);
        appSettings.EmailConnector.DaysBackToSync.Should().Be(60);
        appSettings.EmailConnector.EnableAiCategorization.Should().BeFalse();
        appSettings.EmailConnector.IncludeAttachmentMetadata.Should().BeTrue();

        settings.Verify(s => s.SaveSettingsAsync(appSettings), Times.Once);
        email.Verify(e => e.UpdateSyncSettingsAsync(It.Is<EmailSyncSettings>(sync =>
            sync.EnabledFolders["INBOX"] &&
            sync.SyncIntervalMinutes == 20 &&
            sync.MaxMessagesPerSync == 75 &&
            sync.SyncDaysBack == 60 &&
            !sync.EnableAiCategorization &&
            sync.IncludeAttachmentNames)), Times.Once);
        lifecycle.Verify(l => l.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowCommand_SavesSettingsAndRunsEmailSync()
    {
        var settings = new Mock<ISettingsService>();
        var email = new Mock<IEmailService>();
        var lifecycle = new Mock<IBuiltinConnectorLifecycleService>();

        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings());
        email.Setup(e => e.GetSyncSettingsAsync()).ReturnsAsync(new EmailSyncSettings());
        email.Setup(e => e.SyncMessagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult
            {
                ItemsAdded = 4,
                ItemsSkipped = 2,
                ItemsFailed = 0,
                StartedAt = DateTime.UtcNow.AddSeconds(-1),
                CompletedAt = DateTime.UtcNow,
            });

        var vm = new EmailSettingsViewModel(
            settings.Object,
            Mock.Of<IOAuthService>(),
            email.Object,
            lifecycle.Object,
            Logger.None)
        {
            EnableEmailSync = true,
        };

        await vm.SyncNowCommand.ExecuteAsync(null);

        email.Verify(e => e.SyncMessagesAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.LastSyncTime.Should().NotBe("—");
        vm.SyncStatusText.Should().Contain("Added 4");
        vm.SyncStatusText.Should().Contain("skipped 2");
    }
}

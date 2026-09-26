using System.Collections.Concurrent;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
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
            IncludeAttachmentNames = true,
        };

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        appSettings.EmailConnector.EnableEmailSync.Should().BeTrue();
        appSettings.EmailConnector.SyncIntervalMinutes.Should().Be(20);
        appSettings.EmailConnector.MessagesPerSync.Should().Be(75);
        appSettings.EmailConnector.DaysBackToSync.Should().Be(60);
        appSettings.EmailConnector.IncludeAttachmentMetadata.Should().BeTrue();

        settings.Verify(s => s.SaveSettingsAsync(appSettings), Times.Once);
        email.Verify(e => e.UpdateSyncSettingsAsync(It.Is<EmailSyncSettings>(sync =>
            sync.EnabledFolders["INBOX"] &&
            sync.SyncIntervalMinutes == 20 &&
            sync.MaxMessagesPerSync == 75 &&
            sync.SyncDaysBack == 60 &&
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

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    public async Task ConnectCommand_WithoutOAuthClientCredentials_ExplainsTheSetupInsteadOfTheDeveloperError(string provider)
    {
        // A default install registers no OAuth providers; the raw error told the operator to
        // "Call RegisterProvider()".
        var oauth = new Mock<IOAuthService>();
        oauth
            .Setup(o => o.AuthorizeAsync(provider, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OAuthProviderNotConfiguredException(
                provider, "No OAuth provider configuration registered. Call RegisterProvider() first."));
        var vm = new EmailSettingsViewModel(
            Mock.Of<ISettingsService>(),
            oauth.Object,
            Mock.Of<IEmailService>(),
            Mock.Of<IBuiltinConnectorLifecycleService>(),
            Logger.None);

        await (provider == "google" ? vm.ConnectGoogleCommand : vm.ConnectMicrosoftCommand).ExecuteAsync(null);

        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("settings.json").And.Contain("restart Agent-X");
        vm.ErrorMessage.Should().NotContain("RegisterProvider");
    }

    // -- UI-thread affinity and connection status -------------------------------

    private static Task<T> Later<T>(T value) => Task.Delay(5).ContinueWith(_ => value, TaskScheduler.Default);

    [Fact]
    public async Task Commands_WriteBoundPropertiesOnlyOnTheUiThread()
    {
        // Every service call completes on the thread pool. A command body that awaited with
        // ConfigureAwait(false) would go on writing bound properties from there, which WinUI's
        // x:Bind rejects (the page is left with a spinner that never stops).
        var settings = new Mock<ISettingsService>();
        var oauth = new Mock<IOAuthService>();
        var email = new Mock<IEmailService>();

        settings.Setup(s => s.GetSettingsAsync()).Returns(() => Later(new AppSettings()));
        oauth.Setup(o => o.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() => Later(new OAuthCredential { AccessToken = "a", RefreshToken = "r" }));
        oauth.Setup(o => o.GetCredentialAsync(It.IsAny<string>()))
            .Returns(() => Later<OAuthCredential?>(new OAuthCredential { AccessToken = "a", RefreshToken = "r" }));
        email.Setup(e => e.GetSyncSettingsAsync()).Returns(() => Later(new EmailSyncSettings()));
        email.Setup(e => e.SyncMessagesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Later(new SyncResult { ItemsAdded = 3, CompletedAt = DateTime.UtcNow }));

        var vm = new EmailSettingsViewModel(
            settings.Object, oauth.Object, email.Object,
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None);

        using var ui = new SingleThreadSynchronizationContext();
        var offThreadWrites = new ConcurrentQueue<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (Environment.CurrentManagedThreadId != ui.ThreadId)
                offThreadWrites.Enqueue(e.PropertyName ?? "?");
        };

        await ui.RunAsync(() => vm.InitializeAsync());
        await ui.RunAsync(() => vm.ConnectGoogleCommand.ExecuteAsync(null));
        await ui.RunAsync(() => vm.SyncNowCommand.ExecuteAsync(null));

        offThreadWrites.Should().BeEmpty();
        vm.IsGoogleConnected.Should().BeTrue();
        vm.SyncStatusText.Should().Contain("Added 3");
        vm.IsSyncing.Should().BeFalse();
    }

    [Fact]
    public void DescribeConnection_ACredentialWithoutRefreshToken_NeedsAReconnect()
    {
        EmailSettingsViewModel.DescribeConnection(null)
            .Should().Be((false, "Not connected"));
        EmailSettingsViewModel.DescribeConnection(new OAuthCredential { AccessToken = "a", RefreshToken = "" })
            .Should().Be((false, "Reconnect required"));
        EmailSettingsViewModel.DescribeConnection(new OAuthCredential { AccessToken = "a", RefreshToken = "r" })
            .Should().Be((true, "Connected"));
    }

    [Fact]
    public async Task ConnectMicrosoftCommand_AsksForOfflineAccess()
    {
        var oauth = new Mock<IOAuthService>();
        var vm = new EmailSettingsViewModel(
            Mock.Of<ISettingsService>(), oauth.Object, Mock.Of<IEmailService>(),
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None);

        await vm.ConnectMicrosoftCommand.ExecuteAsync(null);

        oauth.Verify(o => o.AuthorizeAsync(
            "microsoft",
            It.Is<string?>(scopes => scopes != null && scopes.Split(' ', StringSplitOptions.None).Contains("offline_access")),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

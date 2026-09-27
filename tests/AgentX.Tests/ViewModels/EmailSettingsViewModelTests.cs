using System.Collections.Concurrent;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
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
    /// <summary>Localization that returns each key, so a test can tell which string was shown.</summary>
    private static ILocalizationService Keys()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        return localization.Object;
    }

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
            Logger.None, Keys())
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
            Logger.None, Keys())
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
    [InlineData("google", "OAuthApp_GoogleNotSetUp")]
    [InlineData("microsoft", "OAuthApp_MicrosoftNotSetUp")]
    public async Task ConnectCommand_WithoutOAuthClientCredentials_PointsToTheCredentialsForm(string provider, string message)
    {
        // A default install registers no OAuth providers; the raw error told the operator to
        // "Call RegisterProvider()", and the guidance after it to edit settings.json and restart.
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
            Logger.None, Keys());

        await (provider == "google" ? vm.ConnectGoogleCommand : vm.ConnectMicrosoftCommand).ExecuteAsync(null);

        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Be(message);
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
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None, Keys());

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

    // -- Folder selection -----------------------------------------------------------

    private static List<EmailFolderInfo> TwoAccountsFolders() =>
    [
        new() { Id = "INBOX", Name = "INBOX", SourceProvider = "google" },
        new() { Id = "Label_7", Name = "Receipts", SourceProvider = "google" },
        new() { Id = "INBOX", Name = "Inbox", SourceProvider = "microsoft" },
        new() { Id = "AAMkProjects", Name = "Projects", SourceProvider = "microsoft" },
        new() { Id = "", Name = "No id", SourceProvider = "microsoft" },
    ];

    /// <summary>
    /// A view model over two connected accounts whose saved selection syncs the inbox and
    /// Projects, not Receipts, plus a label no account lists any more.
    /// </summary>
    private static (EmailSettingsViewModel Vm, Mock<IEmailService> Email, List<EmailSyncSettings> Saved) CreateWithFolders(
        Dictionary<string, bool>? savedFolders = null,
        List<EmailFolderInfo>? folders = null)
    {
        savedFolders ??= new() { ["INBOX"] = true, ["Label_7"] = false, ["AAMkProjects"] = true, ["Label_gone"] = true };
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings());
        var email = new Mock<IEmailService>();
        email.Setup(e => e.GetSyncSettingsAsync())
            .ReturnsAsync(() => new EmailSyncSettings { EnabledFolders = new Dictionary<string, bool>(savedFolders) });
        email.Setup(e => e.ListAvailableFoldersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders ?? TwoAccountsFolders());
        var saved = new List<EmailSyncSettings>();
        email.Setup(e => e.UpdateSyncSettingsAsync(It.IsAny<EmailSyncSettings>()))
            .Callback((EmailSyncSettings s) => saved.Add(s))
            .Returns(Task.CompletedTask);

        var vm = new EmailSettingsViewModel(
            settings.Object, Mock.Of<IOAuthService>(), email.Object,
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None, Keys());
        return (vm, email, saved);
    }

    [Fact]
    public async Task InitializeAsync_ListsEachFolderOnce_InboxFirst_WithTheSavedSelection()
    {
        var (vm, _, _) = CreateWithFolders();

        await vm.InitializeAsync();

        // Both accounts report their inbox as INBOX, and the selection is keyed by folder id,
        // so one entry stands for both.
        vm.Folders.Select(f => (f.Id, f.Name, f.Accounts, f.IsSelected)).Should().Equal(
            ("INBOX", "Inbox", "Gmail, Outlook", true),
            ("AAMkProjects", "Projects", "Outlook", true),
            ("Label_7", "Receipts", "Gmail", false));
        vm.HasFolders.Should().BeTrue();
        vm.IsFolderSelectionEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSettingsCommand_SavesEachListedFolderAsChecked_AndKeepsFoldersNotListed()
    {
        var (vm, _, saved) = CreateWithFolders();
        await vm.InitializeAsync();

        vm.Folders.Single(f => f.Id == "Label_7").IsSelected = true;
        vm.Folders.Single(f => f.Id == "INBOX").IsSelected = false;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        saved.Single().EnabledFolders.Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["INBOX"] = false,
            ["Label_7"] = true,
            ["AAMkProjects"] = true,
            ["Label_gone"] = true, // an account that could not be listed keeps its setting
        });
    }

    [Fact]
    public async Task ClearingEveryFolder_Warns_AndIsSavedAsShownRatherThanReplacedByTheInbox()
    {
        var (vm, _, saved) = CreateWithFolders();
        await vm.InitializeAsync();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        foreach (var folder in vm.Folders)
            folder.IsSelected = false;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        vm.IsFolderSelectionEmpty.Should().BeTrue();
        changed.Should().Contain(nameof(EmailSettingsViewModel.IsFolderSelectionEmpty));
        saved.Single().EnabledFolders
            .Where(kv => kv.Key != "Label_gone")
            .Should().OnlyContain(kv => !kv.Value, "the page shows no folder checked, so none is saved as synced");
    }

    [Fact]
    public async Task SaveSettingsCommand_WithoutAFolderList_KeepsTheInboxAsTheDefault()
    {
        var (vm, _, saved) = CreateWithFolders(new() { ["INBOX"] = false }, folders: []);
        await vm.InitializeAsync();

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        vm.HasFolders.Should().BeFalse();
        vm.IsFolderSelectionEmpty.Should().BeFalse("there is nothing to choose from yet");
        saved.Single().EnabledFolders["INBOX"].Should().BeTrue();
    }

    [Fact]
    public async Task RefreshFoldersCommand_KeepsUnsavedChecks_AndAddsNewFoldersWithTheirSavedSetting()
    {
        var folders = TwoAccountsFolders();
        var (vm, _, _) = CreateWithFolders(folders: folders);
        await vm.InitializeAsync();
        vm.Folders.Single(f => f.Id == "INBOX").IsSelected = false;

        folders.Add(new EmailFolderInfo { Id = "Label_gone", Name = "Old project", SourceProvider = "google" });
        await vm.RefreshFoldersCommand.ExecuteAsync(null);

        vm.Folders.Single(f => f.Id == "INBOX").IsSelected.Should().BeFalse();
        vm.Folders.Single(f => f.Id == "Label_gone").IsSelected.Should().BeTrue();
        vm.IsLoadingFolders.Should().BeFalse();
    }

    [Fact]
    public async Task ShowNoFoldersHint_StaysHiddenWhileTheListLoads()
    {
        var (vm, email, _) = CreateWithFolders();
        var pending = new TaskCompletionSource<IReadOnlyList<EmailFolderInfo>>();
        email.Setup(e => e.ListAvailableFoldersAsync(It.IsAny<CancellationToken>())).Returns(pending.Task);

        var refresh = vm.RefreshFoldersCommand.ExecuteAsync(null);
        vm.IsLoadingFolders.Should().BeTrue();
        vm.ShowNoFoldersHint.Should().BeFalse("the hint says there are no folders, which is not known yet");

        pending.SetResult([]);
        await refresh;

        vm.ShowNoFoldersHint.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshFoldersCommand_WhenTheAccountsCannotBeRead_KeepsTheList()
    {
        var (vm, email, _) = CreateWithFolders();
        await vm.InitializeAsync();
        email.Setup(e => e.ListAvailableFoldersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        await vm.RefreshFoldersCommand.ExecuteAsync(null);

        vm.Folders.Should().HaveCount(3);
        vm.IsLoadingFolders.Should().BeFalse();
    }

    [Fact]
    public async Task FolderLoading_WritesBoundPropertiesOnlyOnTheUiThread()
    {
        var settings = new Mock<ISettingsService>();
        var oauth = new Mock<IOAuthService>();
        var email = new Mock<IEmailService>();
        settings.Setup(s => s.GetSettingsAsync()).Returns(() => Later(new AppSettings()));
        oauth.Setup(o => o.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() => Later(new OAuthCredential { AccessToken = "a", RefreshToken = "r" }));
        oauth.Setup(o => o.GetCredentialAsync(It.IsAny<string>()))
            .Returns(() => Later<OAuthCredential?>(new OAuthCredential { AccessToken = "a", RefreshToken = "r" }));
        email.Setup(e => e.GetSyncSettingsAsync()).Returns(() => Later(new EmailSyncSettings()));
        email.Setup(e => e.ListAvailableFoldersAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Later<IReadOnlyList<EmailFolderInfo>>(TwoAccountsFolders()));

        var vm = new EmailSettingsViewModel(
            settings.Object, oauth.Object, email.Object,
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None, Keys());

        using var ui = new SingleThreadSynchronizationContext();
        var offThreadWrites = new ConcurrentQueue<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (Environment.CurrentManagedThreadId != ui.ThreadId)
                offThreadWrites.Enqueue(e.PropertyName ?? "?");
        };

        await ui.RunAsync(() => vm.InitializeAsync());
        await ui.RunAsync(() => vm.ConnectMicrosoftCommand.ExecuteAsync(null));
        await ui.RunAsync(() => vm.RefreshFoldersCommand.ExecuteAsync(null));
        await ui.RunAsync(() => vm.SaveSettingsCommand.ExecuteAsync(null));

        offThreadWrites.Should().BeEmpty();
        vm.Folders.Should().HaveCount(3);
        email.Verify(e => e.ListAvailableFoldersAsync(It.IsAny<CancellationToken>()), Times.Exactly(3),
            "the list is read on load, after connecting an account, and on refresh");
    }

    [Fact]
    public async Task ConnectMicrosoftCommand_AsksForOfflineAccess()
    {
        var oauth = new Mock<IOAuthService>();
        var vm = new EmailSettingsViewModel(
            Mock.Of<ISettingsService>(), oauth.Object, Mock.Of<IEmailService>(),
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None, Keys());

        await vm.ConnectMicrosoftCommand.ExecuteAsync(null);

        oauth.Verify(o => o.AuthorizeAsync(
            "microsoft",
            It.Is<string?>(scopes => scopes != null && scopes.Split(' ', StringSplitOptions.None).Contains("offline_access")),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

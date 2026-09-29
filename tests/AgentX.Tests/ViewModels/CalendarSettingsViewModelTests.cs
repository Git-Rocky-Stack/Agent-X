using System.Collections.Concurrent;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class CalendarSettingsViewModelTests
{
    /// <summary>Localization that returns each key, so a test can tell which string was shown.</summary>
    private static ILocalizationService Keys()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        return localization.Object;
    }

    [Fact]
    public async Task SaveSettingsCommand_UpdatesAppSettingsPluginSettingsAndConnectorLifecycle()
    {
        var appSettings = new AppSettings();
        appSettings.CalendarConnector.ConflictResolution = "LocalWins";
        var settings = new Mock<ISettingsService>();
        var calendar = new Mock<ICalendarService>();
        var lifecycle = new Mock<IBuiltinConnectorLifecycleService>();

        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(appSettings);
        calendar.Setup(c => c.GetSyncSettingsAsync()).ReturnsAsync(new CalendarSyncSettings
        {
            EnabledCalendars = { ["primary"] = true },
            ConflictResolution = "Merge",
        });

        var vm = new CalendarSettingsViewModel(
            settings.Object,
            Mock.Of<IOAuthService>(),
            calendar.Object,
            lifecycle.Object,
            Logger.None, Keys())
        {
            EnableCalendarSync = true,
            SyncIntervalMinutes = 30,
            DaysPastToSync = 14,
            DaysFutureToSync = 45,
            IncludeAttendeeDetails = false,
            IncludeDescriptions = false,
        };

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        appSettings.CalendarConnector.EnableCalendarSync.Should().BeTrue();
        appSettings.CalendarConnector.SyncIntervalMinutes.Should().Be(30);
        appSettings.CalendarConnector.DaysPastToSync.Should().Be(14);
        appSettings.CalendarConnector.DaysFutureToSync.Should().Be(45);
        appSettings.CalendarConnector.IncludeAttendeeDetails.Should().BeFalse();
        appSettings.CalendarConnector.IncludeDescriptions.Should().BeFalse();

        // Nothing reads the conflict resolution, so the page neither offers nor rewrites it.
        appSettings.CalendarConnector.ConflictResolution.Should().Be("LocalWins");

        settings.Verify(s => s.SaveSettingsAsync(appSettings), Times.Once);
        calendar.Verify(c => c.UpdateSyncSettingsAsync(It.Is<CalendarSyncSettings>(sync =>
            sync.EnabledCalendars["primary"] &&
            sync.SyncIntervalMinutes == 30 &&
            sync.DaysPastToSync == 14 &&
            sync.DaysFutureToSync == 45 &&
            sync.ConflictResolution == "Merge" &&
            !sync.IncludeAttendeeDetails &&
            !sync.IncludeDescriptions)), Times.Once);
        lifecycle.Verify(l => l.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowCommand_SavesSettingsAndRunsCalendarSync()
    {
        var settings = new Mock<ISettingsService>();
        var calendar = new Mock<ICalendarService>();
        var lifecycle = new Mock<IBuiltinConnectorLifecycleService>();

        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings());
        calendar.Setup(c => c.GetSyncSettingsAsync()).ReturnsAsync(new CalendarSyncSettings());
        calendar.Setup(c => c.ListAvailableCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { new() { Id = "primary", Name = "Primary" } });
        calendar.Setup(c => c.SyncEventsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult
            {
                ItemsAdded = 2,
                ItemsUpdated = 1,
                ItemsSkipped = 3,
                ItemsFailed = 0,
                StartedAt = DateTime.UtcNow.AddSeconds(-1),
                CompletedAt = DateTime.UtcNow,
            });

        var vm = new CalendarSettingsViewModel(
            settings.Object,
            Mock.Of<IOAuthService>(),
            calendar.Object,
            lifecycle.Object,
            Logger.None, EnglishResources.Create())
        {
            EnableCalendarSync = true,
        };

        await vm.SyncNowCommand.ExecuteAsync(null);

        calendar.Verify(c => c.UpdateSyncSettingsAsync(It.Is<CalendarSyncSettings>(sync =>
            sync.EnabledCalendars["primary"])), Times.Once);
        calendar.Verify(c => c.SyncEventsAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.LastSyncTime.Should().NotBe("-");
        vm.SyncStatusText.Should().Contain("Added 2");
        vm.SyncStatusText.Should().Contain("updated 1");
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
        var vm = new CalendarSettingsViewModel(
            Mock.Of<ISettingsService>(),
            oauth.Object,
            Mock.Of<ICalendarService>(),
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
        var calendar = new Mock<ICalendarService>();

        settings.Setup(s => s.GetSettingsAsync()).Returns(() => Later(new AppSettings()));
        oauth.Setup(o => o.GetCredentialAsync(It.IsAny<string>()))
            .Returns(() => Later<OAuthCredential?>(new OAuthCredential { AccessToken = "a", RefreshToken = "r" }));
        calendar.Setup(c => c.GetSyncSettingsAsync())
            .Returns(() => Later(new CalendarSyncSettings { EnabledCalendars = { ["primary"] = true } }));
        calendar.Setup(c => c.SyncEventsAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Later(new SyncResult { ItemsAdded = 1, CompletedAt = DateTime.UtcNow }));

        var vm = new CalendarSettingsViewModel(
            settings.Object, oauth.Object, calendar.Object,
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None, EnglishResources.Create());

        using var ui = new SingleThreadSynchronizationContext();
        var offThreadWrites = new ConcurrentQueue<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (Environment.CurrentManagedThreadId != ui.ThreadId)
                offThreadWrites.Enqueue(e.PropertyName ?? "?");
        };

        await ui.RunAsync(() => vm.InitializeAsync());
        await ui.RunAsync(() => vm.SyncNowCommand.ExecuteAsync(null));

        offThreadWrites.Should().BeEmpty();
        vm.IsGoogleConnected.Should().BeTrue();
        vm.SyncStatusText.Should().Contain("Added 1");
        vm.IsSyncing.Should().BeFalse();
    }

    [Fact]
    public void DescribeConnection_ACredentialWithoutRefreshToken_NeedsAReconnect()
    {
        var english = EnglishResources.Create();
        CalendarSettingsViewModel.DescribeConnection(null, english)
            .Should().Be((false, "Not connected"));
        CalendarSettingsViewModel.DescribeConnection(new OAuthCredential { AccessToken = "a", RefreshToken = "" }, english)
            .Should().Be((false, "Reconnect required"));
        CalendarSettingsViewModel.DescribeConnection(new OAuthCredential { AccessToken = "a", RefreshToken = "r" }, english)
            .Should().Be((true, "Connected"));
    }

    [Fact]
    public void ConflictResolution_IsNoLongerOffered_BecauseCalendarSyncOnlyImports()
    {
        // The page offered Remote wins, Local wins and Merge and saved the choice, but calendar
        // sync is a read-only import that never reads it. The control and its view model state
        // are gone; the stored settings keep the property so existing files still load.
        typeof(CalendarSettingsViewModel).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name => name.StartsWith("ConflictResolution", StringComparison.Ordinal));

        var page = File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Views", "CalendarSettingsPage.xaml"));
        page.Should().NotContain("ConflictResolution").And.NotContain("CalSet_Conflict");
    }

    [Fact]
    public async Task ConnectMicrosoftCommand_AsksForOfflineAccess()
    {
        var oauth = new Mock<IOAuthService>();
        var vm = new CalendarSettingsViewModel(
            Mock.Of<ISettingsService>(), oauth.Object, Mock.Of<ICalendarService>(),
            Mock.Of<IBuiltinConnectorLifecycleService>(), Logger.None, Keys());

        await vm.ConnectMicrosoftCommand.ExecuteAsync(null);

        oauth.Verify(o => o.AuthorizeAsync(
            "microsoft",
            It.Is<string?>(scopes => scopes != null && scopes.Split(' ', StringSplitOptions.None).Contains("offline_access")),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Agent-X source root from the test output directory.");
    }
}

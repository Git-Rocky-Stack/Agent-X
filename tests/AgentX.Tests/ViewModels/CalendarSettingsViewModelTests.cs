using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class CalendarSettingsViewModelTests
{
    [Fact]
    public async Task SaveSettingsCommand_UpdatesAppSettingsPluginSettingsAndConnectorLifecycle()
    {
        var appSettings = new AppSettings();
        var settings = new Mock<ISettingsService>();
        var calendar = new Mock<ICalendarService>();
        var lifecycle = new Mock<IBuiltinConnectorLifecycleService>();

        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(appSettings);
        calendar.Setup(c => c.GetSyncSettingsAsync()).ReturnsAsync(new CalendarSyncSettings
        {
            EnabledCalendars = { ["primary"] = true }
        });

        var vm = new CalendarSettingsViewModel(
            settings.Object,
            Mock.Of<IOAuthService>(),
            calendar.Object,
            lifecycle.Object,
            Logger.None)
        {
            EnableCalendarSync = true,
            SyncIntervalMinutes = 30,
            DaysPastToSync = 14,
            DaysFutureToSync = 45,
            ConflictResolution = "Merge",
            IncludeAttendeeDetails = false,
            IncludeDescriptions = false,
        };

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        appSettings.CalendarConnector.EnableCalendarSync.Should().BeTrue();
        appSettings.CalendarConnector.SyncIntervalMinutes.Should().Be(30);
        appSettings.CalendarConnector.DaysPastToSync.Should().Be(14);
        appSettings.CalendarConnector.DaysFutureToSync.Should().Be(45);
        appSettings.CalendarConnector.ConflictResolution.Should().Be("Merge");
        appSettings.CalendarConnector.IncludeAttendeeDetails.Should().BeFalse();
        appSettings.CalendarConnector.IncludeDescriptions.Should().BeFalse();

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
            Logger.None)
        {
            EnableCalendarSync = true,
        };

        await vm.SyncNowCommand.ExecuteAsync(null);

        calendar.Verify(c => c.UpdateSyncSettingsAsync(It.Is<CalendarSyncSettings>(sync =>
            sync.EnabledCalendars["primary"])), Times.Once);
        calendar.Verify(c => c.SyncEventsAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.LastSyncTime.Should().NotBe("—");
        vm.SyncStatusText.Should().Contain("Added 2");
        vm.SyncStatusText.Should().Contain("updated 1");
    }
}

using AgentX.Core.Services.Plugins.Calendar.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Unit tests for Calendar Connector model classes:
/// <see cref="CalEvent"/>, <see cref="CalAttendee"/>,
/// <see cref="CalendarInfo"/>, <see cref="CalendarSyncSettings"/>,
/// and <see cref="SyncResult"/>.
/// </summary>
public sealed class CalendarModelsTests
{
    // ══════════════════════════════════════════════════════════════════════
    //  CalEvent defaults & construction
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CalEvent_Default_Id_IsEmptyString()
    {
        var evt = new CalEvent();
        evt.Id.Should().BeEmpty();
    }

    [Fact]
    public void CalEvent_Default_Title_IsEmptyString()
    {
        var evt = new CalEvent();
        evt.Title.Should().BeEmpty();
    }

    [Fact]
    public void CalEvent_Default_Description_IsNull()
    {
        var evt = new CalEvent();
        evt.Description.Should().BeNull();
    }

    [Fact]
    public void CalEvent_Default_Attendees_IsEmptyList()
    {
        var evt = new CalEvent();
        evt.Attendees.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CalEvent_Default_IsAllDay_IsFalse()
    {
        var evt = new CalEvent();
        evt.IsAllDay.Should().BeFalse();
    }

    [Fact]
    public void CalEvent_Default_IsRecurring_IsFalse()
    {
        var evt = new CalEvent();
        evt.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public void CalEvent_Default_SourceProvider_IsEmptyString()
    {
        var evt = new CalEvent();
        evt.SourceProvider.Should().BeEmpty();
    }

    [Fact]
    public void CalEvent_CanCreate_WithAllFieldsPopulated()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var attendee = new CalAttendee
        {
            DisplayName = "Jane Doe",
            Email = "jane@example.com",
            ResponseStatus = "accepted",
            IsOrganizer = true,
        };

        // Act
        var evt = new CalEvent
        {
            Id = "evt-123",
            Title = "Sprint Planning",
            Description = "Weekly sprint planning meeting",
            Start = now,
            End = now.AddHours(1),
            Location = "Conference Room B",
            IsAllDay = false,
            IsRecurring = true,
            Attendees = [attendee],
            Organizer = "Jane Doe",
            CalendarName = "Work",
            SourceProvider = "google",
            HtmlLink = "https://calendar.google.com/event/evt-123",
            CalendarId = "cal-work",
        };

        // Assert
        evt.Id.Should().Be("evt-123");
        evt.Title.Should().Be("Sprint Planning");
        evt.Description.Should().Be("Weekly sprint planning meeting");
        evt.Start.Should().Be(now);
        evt.End.Should().Be(now.AddHours(1));
        evt.Location.Should().Be("Conference Room B");
        evt.IsAllDay.Should().BeFalse();
        evt.IsRecurring.Should().BeTrue();
        evt.Attendees.Should().HaveCount(1);
        evt.Attendees[0].Email.Should().Be("jane@example.com");
        evt.Organizer.Should().Be("Jane Doe");
        evt.CalendarName.Should().Be("Work");
        evt.SourceProvider.Should().Be("google");
        evt.HtmlLink.Should().Be("https://calendar.google.com/event/evt-123");
        evt.CalendarId.Should().Be("cal-work");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CalAttendee defaults & construction
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CalAttendee_Default_DisplayName_IsEmptyString()
    {
        var att = new CalAttendee();
        att.DisplayName.Should().BeEmpty();
    }

    [Fact]
    public void CalAttendee_Default_Email_IsEmptyString()
    {
        var att = new CalAttendee();
        att.Email.Should().BeEmpty();
    }

    [Fact]
    public void CalAttendee_Default_ResponseStatus_IsNeedsAction()
    {
        var att = new CalAttendee();
        att.ResponseStatus.Should().Be("needsAction");
    }

    [Fact]
    public void CalAttendee_Default_IsOrganizer_IsFalse()
    {
        var att = new CalAttendee();
        att.IsOrganizer.Should().BeFalse();
    }

    [Fact]
    public void CalAttendee_CanCreate_WithAllFieldsPopulated()
    {
        var att = new CalAttendee
        {
            DisplayName = "John Smith",
            Email = "john@company.com",
            ResponseStatus = "tentative",
            IsOrganizer = true,
        };

        att.DisplayName.Should().Be("John Smith");
        att.Email.Should().Be("john@company.com");
        att.ResponseStatus.Should().Be("tentative");
        att.IsOrganizer.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CalendarInfo defaults & construction
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CalendarInfo_Default_Id_IsEmptyString()
    {
        var info = new CalendarInfo();
        info.Id.Should().BeEmpty();
    }

    [Fact]
    public void CalendarInfo_Default_Name_IsEmptyString()
    {
        var info = new CalendarInfo();
        info.Name.Should().BeEmpty();
    }

    [Fact]
    public void CalendarInfo_Default_EventCount_IsZero()
    {
        var info = new CalendarInfo();
        info.EventCount.Should().Be(0);
    }

    [Fact]
    public void CalendarInfo_Default_IsPrimary_IsFalse()
    {
        var info = new CalendarInfo();
        info.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void CalendarInfo_Default_LastSyncedAt_IsNull()
    {
        var info = new CalendarInfo();
        info.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public void CalendarInfo_CanCreate_WithAllFieldsPopulated()
    {
        var now = DateTime.UtcNow;
        var info = new CalendarInfo
        {
            Id = "cal-primary",
            Name = "Work Calendar",
            Owner = "john@company.com",
            EventCount = 142,
            SourceProvider = "google",
            IsPrimary = true,
            LastSyncedAt = now,
        };

        info.Id.Should().Be("cal-primary");
        info.Name.Should().Be("Work Calendar");
        info.Owner.Should().Be("john@company.com");
        info.EventCount.Should().Be(142);
        info.SourceProvider.Should().Be("google");
        info.IsPrimary.Should().BeTrue();
        info.LastSyncedAt.Should().Be(now);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CalendarSyncSettings defaults
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CalendarSyncSettings_Default_EnabledCalendars_IsEmptyDictionary()
    {
        var settings = new CalendarSyncSettings();
        settings.EnabledCalendars.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CalendarSyncSettings_Default_SyncIntervalMinutes_Is15()
    {
        var settings = new CalendarSyncSettings();
        settings.SyncIntervalMinutes.Should().Be(15);
    }

    [Fact]
    public void CalendarSyncSettings_Default_DaysFutureToSync_Is30()
    {
        var settings = new CalendarSyncSettings();
        settings.DaysFutureToSync.Should().Be(30);
    }

    [Fact]
    public void CalendarSyncSettings_Default_DaysPastToSync_Is90()
    {
        var settings = new CalendarSyncSettings();
        settings.DaysPastToSync.Should().Be(90);
    }

    [Fact]
    public void CalendarSyncSettings_Default_ConflictResolution_IsRemoteWins()
    {
        var settings = new CalendarSyncSettings();
        settings.ConflictResolution.Should().Be("RemoteWins");
    }

    [Fact]
    public void CalendarSyncSettings_Default_IncludeAttendeeDetails_IsTrue()
    {
        var settings = new CalendarSyncSettings();
        settings.IncludeAttendeeDetails.Should().BeTrue();
    }

    [Fact]
    public void CalendarSyncSettings_Default_IncludeDescriptions_IsTrue()
    {
        var settings = new CalendarSyncSettings();
        settings.IncludeDescriptions.Should().BeTrue();
    }

    [Fact]
    public void CalendarSyncSettings_CanCreate_WithCustomValues()
    {
        var settings = new CalendarSyncSettings
        {
            EnabledCalendars = new Dictionary<string, bool>
            {
                ["cal-work"] = true,
                ["cal-personal"] = true,
                ["cal-holidays"] = false,
            },
            SyncIntervalMinutes = 30,
            DaysFutureToSync = 60,
            DaysPastToSync = 180,
            ConflictResolution = "LocalWins",
            IncludeAttendeeDetails = false,
            IncludeDescriptions = false,
        };

        settings.EnabledCalendars.Should().HaveCount(3);
        settings.SyncIntervalMinutes.Should().Be(30);
        settings.DaysFutureToSync.Should().Be(60);
        settings.DaysPastToSync.Should().Be(180);
        settings.ConflictResolution.Should().Be("LocalWins");
        settings.IncludeAttendeeDetails.Should().BeFalse();
        settings.IncludeDescriptions.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SyncResult construction & computed properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SyncResult_Default_Values_AreZero()
    {
        var result = new SyncResult
        {
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        result.ItemsAdded.Should().Be(0);
        result.ItemsUpdated.Should().Be(0);
        result.ItemsSkipped.Should().Be(0);
        result.ItemsFailed.Should().Be(0);
    }

    [Fact]
    public void SyncResult_TotalItemsProcessed_SumsAllCounts()
    {
        var result = new SyncResult
        {
            ItemsAdded = 10,
            ItemsUpdated = 5,
            ItemsSkipped = 100,
            ItemsFailed = 2,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        result.TotalItemsProcessed.Should().Be(117);
    }

    [Fact]
    public void SyncResult_IsSuccess_WhenNoFailures()
    {
        var result = new SyncResult
        {
            ItemsAdded = 5,
            ItemsFailed = 0,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SyncResult_IsNotSuccess_WhenFailuresExist()
    {
        var result = new SyncResult
        {
            ItemsAdded = 5,
            ItemsFailed = 1,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SyncResult_Duration_CalculatesCorrectly()
    {
        var started = DateTime.UtcNow;
        var completed = started.AddSeconds(42);

        var result = new SyncResult
        {
            StartedAt = started,
            CompletedAt = completed,
        };

        result.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(42), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void SyncResult_DeltaToken_CanBeNull()
    {
        var result = new SyncResult
        {
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        result.DeltaToken.Should().BeNull();
    }

    [Fact]
    public void SyncResult_DeltaToken_CanBeSet()
    {
        var result = new SyncResult
        {
            DeltaToken = "delta-abc123",
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        result.DeltaToken.Should().Be("delta-abc123");
    }
}

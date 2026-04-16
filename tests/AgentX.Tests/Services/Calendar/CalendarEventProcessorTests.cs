using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Unit tests for <see cref="CalendarEventProcessor"/> — validates
/// event-to-inbox parameter conversion, content extraction, and
/// searchable text generation.
/// </summary>
public sealed class CalendarEventProcessorTests : IDisposable
{
    private readonly CalendarEventProcessor _processor;
    private readonly ILogger _logger;

    public CalendarEventProcessorTests()
    {
        _logger = new LoggerConfiguration().CreateLogger();
        _processor = new CalendarEventProcessor(_logger);
    }

    public void Dispose()
    {
        (_logger as IDisposable)?.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Construction
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        var act = () => new CalendarEventProcessor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Constants
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PluginId_IsComAgentXCalendar()
    {
        CalendarEventProcessor.PluginId.Should().Be("com.agentx.calendar");
    }

    [Fact]
    public void SourceCategory_IsCalendarEvent()
    {
        CalendarEventProcessor.SourceCategory.Should().Be("calendar_event");
    }

    [Fact]
    public void SourceType_IsCalendarConnector()
    {
        CalendarEventProcessor.SourceType.Should().Be("calendar-connector");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ConvertToInboxParameters
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConvertToInboxParameters_ThrowsOnNullEvent()
    {
        var act = () => _processor.ConvertToInboxParameters(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConvertToInboxParameters_SetsFileTypeToCalendarEvent()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.FileType.Should().Be("CalendarEvent");
    }

    [Fact]
    public void ConvertToInboxParameters_SetsSourcePluginId()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.SourcePluginId.Should().Be("com.agentx.calendar");
    }

    [Fact]
    public void ConvertToInboxParameters_SetsSourceCategory()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.SourceCategory.Should().Be("calendar_event");
    }

    [Fact]
    public void ConvertToInboxParameters_SetsSourceType()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.SourceType.Should().Be("calendar-connector");
    }

    [Fact]
    public void ConvertToInboxParameters_ExternalId_ContainsProviderAndCalendarAndEventId()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.ExternalId.Should().Be("google:cal-work:evt-123");
    }

    [Fact]
    public void ConvertToInboxParameters_SetsSourceUrl()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.SourceUrl.Should().Be("https://calendar.google.com/event/evt-123");
    }

    [Fact]
    public void ConvertToInboxParameters_FileName_ContainsTitle()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.FileName.Should().Contain("Sprint Planning");
    }

    [Fact]
    public void ConvertToInboxParameters_ContentPreview_IsNotEmpty()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.ContentPreview.Should().NotBeEmpty();
    }

    [Fact]
    public void ConvertToInboxParameters_ContentText_IsNotEmpty()
    {
        var calEvent = CreateSampleEvent();
        var result = _processor.ConvertToInboxParameters(calEvent);
        result.ContentText.Should().NotBeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExtractSearchableContent
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractSearchableContent_ThrowsOnNullEvent()
    {
        var act = () => _processor.ExtractSearchableContent(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractSearchableContent_ContainsTitle()
    {
        var calEvent = CreateSampleEvent();
        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("Sprint Planning");
    }

    [Fact]
    public void ExtractSearchableContent_ContainsLocation()
    {
        var calEvent = CreateSampleEvent();
        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("Conference Room B");
    }

    [Fact]
    public void ExtractSearchableContent_ContainsOrganizer()
    {
        var calEvent = CreateSampleEvent();
        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("Jane Doe");
    }

    [Fact]
    public void ExtractSearchableContent_ContainsDescription()
    {
        var calEvent = CreateSampleEvent();
        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("Weekly sprint planning meeting");
    }

    [Fact]
    public void ExtractSearchableContent_ContainsAttendeeEmail()
    {
        var calEvent = CreateSampleEvent();
        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("jane@example.com");
    }

    [Fact]
    public void ExtractSearchableContent_AllDayEvent_ShowsDateNotTime()
    {
        var calEvent = new CalEvent
        {
            Id = "evt-allday",
            Title = "Holiday",
            Start = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
            IsAllDay = true,
            SourceProvider = "google",
        };

        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("all day");
        content.Should().Contain("2026-04-15");
    }

    [Fact]
    public void ExtractSearchableContent_RecurringEvent_NotesRecurring()
    {
        var calEvent = new CalEvent
        {
            Id = "evt-recurring",
            Title = "Daily Standup",
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddMinutes(15),
            IsRecurring = true,
            SourceProvider = "google",
        };

        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("Recurring: yes");
    }

    [Fact]
    public void ExtractSearchableContent_ContainsCalendarName()
    {
        var calEvent = CreateSampleEvent();
        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("Work");
    }

    [Fact]
    public void ExtractSearchableContent_NoAttendees_NoAttendeesSection()
    {
        var calEvent = new CalEvent
        {
            Id = "evt-solo",
            Title = "Solo Event",
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddHours(1),
            Attendees = [],
            SourceProvider = "google",
        };

        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().NotContain("Attendees:");
    }

    [Fact]
    public void ExtractSearchableContent_AttendeeResponseStatus_Mapped()
    {
        var calEvent = new CalEvent
        {
            Id = "evt-responses",
            Title = "Team Meeting",
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddHours(1),
            Attendees =
            [
                new CalAttendee { DisplayName = "Alice", Email = "alice@test.com", ResponseStatus = "accepted" },
                new CalAttendee { DisplayName = "Bob", Email = "bob@test.com", ResponseStatus = "declined" },
                new CalAttendee { DisplayName = "Carol", Email = "carol@test.com", ResponseStatus = "tentative" },
                new CalAttendee { DisplayName = "Dave", Email = "dave@test.com", ResponseStatus = "needsAction" },
            ],
            SourceProvider = "google",
        };

        var content = _processor.ExtractSearchableContent(calEvent);
        content.Should().Contain("[+] Alice");
        content.Should().Contain("[-] Bob");
        content.Should().Contain("[~] Carol");
        content.Should().Contain("[?] Dave");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helper
    // ══════════════════════════════════════════════════════════════════════

    private static CalEvent CreateSampleEvent() => new()
    {
        Id = "evt-123",
        Title = "Sprint Planning",
        Description = "Weekly sprint planning meeting",
        Start = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc),
        End = new DateTime(2026, 4, 15, 11, 0, 0, DateTimeKind.Utc),
        Location = "Conference Room B",
        IsAllDay = false,
        IsRecurring = true,
        Attendees =
        [
            new CalAttendee
            {
                DisplayName = "Jane Doe",
                Email = "jane@example.com",
                ResponseStatus = "accepted",
                IsOrganizer = true,
            },
        ],
        Organizer = "Jane Doe",
        CalendarName = "Work",
        SourceProvider = "google",
        HtmlLink = "https://calendar.google.com/event/evt-123",
        CalendarId = "cal-work",
    };
}
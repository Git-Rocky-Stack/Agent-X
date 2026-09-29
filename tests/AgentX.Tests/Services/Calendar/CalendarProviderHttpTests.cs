using System.Net;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Request shape and response handling of <see cref="GoogleCalendarProvider"/> and
/// <see cref="OutlookCalendarProvider"/>, run against a stub HTTP handler: incremental sync
/// parameters, recovery from rejected sync tokens, paging, recurring meetings, cancellations,
/// and time zone handling that must not depend on the machine's zone.
/// </summary>
public sealed class CalendarProviderHttpTests : IDisposable
{
    private static readonly DateTime WindowStart = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IOAuthService> _oauth = new();
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public CalendarProviderHttpTests()
    {
        _oauth.Setup(o => o.GetAccessTokenAsync(It.IsAny<string>())).ReturnsAsync("access-token");
    }

    public void Dispose() => (_logger as IDisposable)?.Dispose();

    // -- Google --------------------------------------------------------------

    private const string GoogleFullPage1 = """
        {
          "items": [
            { "id": "evt-1", "status": "confirmed", "summary": "Planning",
              "start": { "dateTime": "2026-04-10T09:00:00-05:00" },
              "end": { "dateTime": "2026-04-10T10:00:00-05:00" } },
            { "id": "evt-2", "status": "confirmed", "summary": "Offsite",
              "start": { "date": "2026-04-12" }, "end": { "date": "2026-04-13" } }
          ],
          "nextPageToken": "page-2"
        }
        """;

    // The last page of a list carries the sync token, and may carry no items at all.
    private const string GoogleFullPage2 = """{ "items": [], "nextSyncToken": "sync-1" }""";

    [Fact]
    public async Task Google_FullSync_SendsWindowAndOrdering_AndTakesSyncTokenFromEmptyLastPage()
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json(GoogleFullPage1),
            () => StubHttpMessageHandler.Json(GoogleFullPage2));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync("primary", WindowStart, WindowEnd);

        events.Select(e => e.Id).Should().Equal("evt-1", "evt-2");
        token.Should().Be("sync-1");

        var requests = handler.Requests;
        requests.Should().HaveCount(2);
        requests[0].Uri.AbsolutePath.Should().Be("/calendar/v3/calendars/primary/events");
        requests[0].Query["timeMin"].Should().Be("2026-04-01T00:00:00Z");
        requests[0].Query["timeMax"].Should().Be("2026-05-01T00:00:00Z");
        requests[0].Query["orderBy"].Should().Be("startTime");
        requests[0].Query["syncToken"].Should().BeNull();
        requests[1].Query["pageToken"].Should().Be("page-2");
    }

    [Fact]
    public async Task Google_IncrementalSync_SendsOnlyTheSyncToken()
    {
        // Google answers 400 when a sync token is combined with timeMin, timeMax or orderBy.
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json("""{ "items": [], "nextSyncToken": "sync-2" }"""));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync("primary", WindowStart, WindowEnd, deltaToken: "sync-1");

        events.Should().BeEmpty();
        token.Should().Be("sync-2", "an unchanged calendar still returns a new token on its only, empty page");

        var query = handler.Requests.Single().Query;
        query["syncToken"].Should().Be("sync-1");
        query["timeMin"].Should().BeNull();
        query["timeMax"].Should().BeNull();
        query["orderBy"].Should().BeNull();
        query["singleEvents"].Should().Be("true");
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Google_RejectedSyncToken_FallsBackToFullSync(HttpStatusCode rejection)
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Status(rejection),
            () => StubHttpMessageHandler.Json(GoogleFullPage1),
            () => StubHttpMessageHandler.Json(GoogleFullPage2));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync("primary", WindowStart, WindowEnd, deltaToken: "expired");

        events.Should().HaveCount(2);
        token.Should().Be("sync-1");

        var requests = handler.Requests;
        requests.Should().HaveCount(3);
        requests[0].Query["syncToken"].Should().Be("expired");
        requests[1].Query["syncToken"].Should().BeNull();
        requests[1].Query["timeMin"].Should().Be("2026-04-01T00:00:00Z");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Google_FullSyncFailure_Throws_InsteadOfReturningNothing(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Sequence(() => StubHttpMessageHandler.Status(status));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        var act = () => provider.GetEventsAsync("primary", WindowStart, WindowEnd);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().HaveCount(1, "a failed full read is not retried in a loop");
    }

    [Fact]
    public async Task Google_MapsTimesToUtc_AndAllDayEventsKeepTheirDate()
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json(GoogleFullPage1),
            () => StubHttpMessageHandler.Json(GoogleFullPage2));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        var (events, _) = await provider.GetEventsAsync("primary", WindowStart, WindowEnd);

        var timed = events.Single(e => e.Id == "evt-1");
        timed.IsAllDay.Should().BeFalse();
        timed.Start.Should().Be(new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc));
        timed.Start.Kind.Should().Be(DateTimeKind.Utc);

        var allDay = events.Single(e => e.Id == "evt-2");
        allDay.IsAllDay.Should().BeTrue();
        allDay.Start.Should().Be(new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc));
        allDay.End.Should().Be(new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-06-30")]
    [InlineData("2026-12-31")]
    public void Google_ParseAllDayDate_IsTheSameCalendarDateAtMidnightUtc(string date)
    {
        // Parsing through local time used to move all-day events a day early west of UTC.
        var (value, isAllDay) = GoogleCalendarProvider.ParseGoogleDateTime(date, dateTime: null);

        isAllDay.Should().BeTrue();
        value.Kind.Should().Be(DateTimeKind.Utc);
        value.TimeOfDay.Should().Be(TimeSpan.Zero);
        value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).Should().Be(date);
    }

    [Fact]
    public async Task Google_CancelledEntries_BecomeDeletionNotices()
    {
        // A deleted event arrives as a bare stub; a removed occurrence of a series may still
        // carry details. Google says neither is to be shown, so both retire the stored copy.
        const string changes = """
            {
              "items": [
                { "id": "gone-1", "status": "cancelled" },
                { "id": "series-1_20260415", "status": "cancelled", "summary": "Standup",
                  "recurringEventId": "series-1",
                  "start": { "dateTime": "2026-04-15T15:00:00Z" },
                  "end": { "dateTime": "2026-04-15T15:15:00Z" } },
                { "id": "evt-9", "status": "confirmed", "summary": "Still on",
                  "start": { "dateTime": "2026-04-16T15:00:00Z" },
                  "end": { "dateTime": "2026-04-16T16:00:00Z" } }
              ],
              "nextSyncToken": "sync-3"
            }
            """;
        var handler = StubHttpMessageHandler.Sequence(() => StubHttpMessageHandler.Json(changes));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync("primary", WindowStart, WindowEnd, deltaToken: "sync-2");

        token.Should().Be("sync-3");
        events.Select(e => (e.Id, e.IsDeleted)).Should().Equal(
            ("gone-1", true), ("series-1_20260415", true), ("evt-9", false));
        foreach (var notice in events.Where(e => e.IsDeleted))
        {
            notice.CalendarId.Should().Be("primary");
            notice.SourceProvider.Should().Be("google");
        }

        events.Should().BeOfType<CalendarEventBatch>()
            .Which.IsCompleteWindow.Should().BeFalse("an incremental read lists only what changed");
    }

    [Fact]
    public async Task Google_FullRead_IsTheCompleteContentsOfTheWindow()
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Status(HttpStatusCode.Gone),
            () => StubHttpMessageHandler.Json(GoogleFullPage1),
            () => StubHttpMessageHandler.Json(GoogleFullPage2));
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);

        // A rejected token falls back to a full read, which is complete too.
        var (events, _) = await provider.GetEventsAsync("primary", WindowStart, WindowEnd, deltaToken: "expired");

        events.Should().BeOfType<CalendarEventBatch>().Which.IsCompleteWindow.Should().BeTrue();
    }

    // -- Outlook -------------------------------------------------------------

    private const string OutlookViewPage1 = """
        {
          "value": [
            { "id": "occ-1", "iCalUId": "uid-occ-1", "subject": "Weekly sync", "type": "occurrence",
              "seriesMasterId": "master-1", "isAllDay": false, "isCancelled": false,
              "start": { "dateTime": "2026-04-15T16:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-04-15T16:30:00.0000000", "timeZone": "UTC" } },
            { "id": "occ-2", "iCalUId": "uid-occ-2", "subject": "Weekly sync", "type": "occurrence",
              "seriesMasterId": "master-1", "isAllDay": false, "isCancelled": true,
              "start": { "dateTime": "2026-04-22T16:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-04-22T16:30:00.0000000", "timeZone": "UTC" } }
          ],
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/calendars/cal-1/calendarView?$skip=2"
        }
        """;

    private const string OutlookViewPage2 = """
        {
          "value": [
            { "id": "single-1", "iCalUId": "uid-single-1", "subject": "Holiday", "type": "singleInstance",
              "isAllDay": true,
              "start": { "dateTime": "2026-04-20T00:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-04-21T00:00:00.0000000", "timeZone": "UTC" } }
          ]
        }
        """;

    [Fact]
    public async Task Outlook_FullSync_ReadsTheCalendarView_InUtc()
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json(OutlookViewPage1),
            () => StubHttpMessageHandler.Json(OutlookViewPage2));
        var provider = new OutlookCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync("cal-1", WindowStart, WindowEnd);

        token.Should().BeNull("a calendar view read has no delta link");

        var requests = handler.Requests;
        requests.Should().HaveCount(2);
        requests[0].Uri.AbsolutePath.Should().Be("/v1.0/me/calendars/cal-1/calendarView");
        requests[0].Query["startDateTime"].Should().Be("2026-04-01T00:00:00Z");
        requests[0].Query["endDateTime"].Should().Be("2026-05-01T00:00:00Z");
        requests[0].Query["$filter"].Should().BeNull();
        requests[1].Uri.Query.Should().Contain("$skip=2", "the next page link is followed as given");
        foreach (var request in requests)
            request.Header("Prefer").Should().Contain("outlook.timezone=\"UTC\"");

        events.Select(e => e.Id).Should().Equal("uid-occ-1", "uid-occ-2", "uid-single-1");
        events.Should().BeOfType<CalendarEventBatch>()
            .Which.IsCompleteWindow.Should().BeTrue("the calendar view lists every event in the window");
    }

    [Fact]
    public async Task Outlook_DeltaRead_SkipsRemovedEntries_AndIsNotACompleteWindow()
    {
        // A delta link stored by an older build reports a deletion as an id without an iCalUId,
        // which used to be stored as an untitled event dated 0001-01-01.
        const string deltaPage = """
            {
              "value": [
                { "id": "AAMkDeleted", "@removed": { "reason": "deleted" } },
                { "id": "evt-2", "iCalUId": "uid-evt-2", "subject": "Moved", "isAllDay": false,
                  "start": { "dateTime": "2026-04-18T10:00:00.0000000", "timeZone": "UTC" },
                  "end": { "dateTime": "2026-04-18T11:00:00.0000000", "timeZone": "UTC" } }
              ],
              "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/calendars/cal-1/events/delta?$deltatoken=next"
            }
            """;
        var handler = StubHttpMessageHandler.Sequence(() => StubHttpMessageHandler.Json(deltaPage));
        var provider = new OutlookCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync(
            "cal-1", WindowStart, WindowEnd,
            deltaToken: "https://graph.microsoft.com/v1.0/me/calendars/cal-1/events/delta?$deltatoken=old");

        events.Select(e => e.Id).Should().Equal("uid-evt-2");
        token.Should().EndWith("$deltatoken=next");
        events.Should().BeOfType<CalendarEventBatch>().Which.IsCompleteWindow.Should().BeFalse();
    }

    [Fact]
    public async Task Outlook_RecurringOccurrences_AreExpanded_AndCancellationIsKept()
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json(OutlookViewPage1),
            () => StubHttpMessageHandler.Json(OutlookViewPage2));
        var provider = new OutlookCalendarProvider(_oauth.Object, _logger, handler);

        var (events, _) = await provider.GetEventsAsync("cal-1", WindowStart, WindowEnd);

        var first = events.Single(e => e.Id == "uid-occ-1");
        first.IsRecurring.Should().BeTrue();
        first.IsCancelled.Should().BeFalse();
        first.Start.Should().Be(new DateTime(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc));
        first.Start.Kind.Should().Be(DateTimeKind.Utc);

        events.Single(e => e.Id == "uid-occ-2").IsCancelled.Should().BeTrue();

        var holiday = events.Single(e => e.Id == "uid-single-1");
        holiday.IsAllDay.Should().BeTrue();
        holiday.IsRecurring.Should().BeFalse();
        holiday.Start.Should().Be(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Outlook_RejectedDeltaLink_FallsBackToTheCalendarView(HttpStatusCode rejection)
    {
        const string staleDeltaLink = "https://graph.microsoft.com/v1.0/me/calendars/cal-1/events/delta?$deltatoken=old";
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Status(rejection),
            () => StubHttpMessageHandler.Json(OutlookViewPage2));
        var provider = new OutlookCalendarProvider(_oauth.Object, _logger, handler);

        var (events, token) = await provider.GetEventsAsync("cal-1", WindowStart, WindowEnd, deltaToken: staleDeltaLink);

        events.Should().ContainSingle();
        token.Should().BeNull();
        var requests = handler.Requests;
        requests[0].Uri.AbsoluteUri.Should().Be(staleDeltaLink);
        requests[1].Uri.AbsolutePath.Should().Be("/v1.0/me/calendars/cal-1/calendarView");
    }

    [Fact]
    public void Outlook_ParseUtcWallClock_IsNotReadAsLocalTime()
    {
        var value = OutlookCalendarProvider.ParseGraphDateTime("2026-04-15T09:00:00.0000000", "UTC");

        value.Should().Be(new DateTime(2026, 4, 15, 9, 0, 0, DateTimeKind.Utc));
        value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Outlook_ParseWallClockInANamedZone_ConvertsToUtc()
    {
        // Graph names zones the Windows way; mid-April Pacific time is UTC-7.
        var value = OutlookCalendarProvider.ParseGraphDateTime("2026-04-15T09:00:00.0000000", "Pacific Standard Time");

        value.Should().Be(new DateTime(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Outlook_ParseAllDayDate_KeepsTheDateWhateverTheZone()
    {
        var value = OutlookCalendarProvider.ParseGraphDateTime(
            "2026-04-20T00:00:00.0000000", "Pacific Standard Time", isAllDay: true);

        value.Should().Be(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Outlook_BuildCalendarViewUrl_UsesInvariantUtcBounds()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");

            var url = OutlookCalendarProvider.BuildCalendarViewUrl("cal-1", WindowStart, WindowEnd);

            url.Should().Contain("startDateTime=2026-04-01T00%3A00%3A00Z");
            url.Should().Contain("endDateTime=2026-05-01T00%3A00%3A00Z");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}

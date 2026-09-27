using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// <see cref="ICalendarProvider"/> implementation for Microsoft Outlook Calendar
/// via the Microsoft Graph API v1.0. Uses <see cref="IOAuthService"/> for OAuth2
/// authentication and communicates via <c>HttpClient</c> with JSON responses.
/// </summary>
/// <remarks>
/// Microsoft Graph API reference:
/// <list type="bullet">
///   <item>Calendars list: <c>GET https://graph.microsoft.com/v1.0/me/calendars</c></item>
///   <item>Events in a window: <c>GET https://graph.microsoft.com/v1.0/me/calendars/{id}/calendarView?startDateTime=...&amp;endDateTime=...</c>.
///   Unlike <c>/events</c>, the calendar view expands recurring series into their occurrences.</item>
/// </list>
/// All requests require <c>Authorization: Bearer {accessToken}</c> header, and ask Graph for
/// UTC times with <c>Prefer: outlook.timezone="UTC"</c>. A delta link stored by an earlier sync
/// is still honored; one Graph no longer accepts is discarded in favor of a full read.
/// </remarks>
public sealed class OutlookCalendarProvider : ICalendarProvider
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string CalendarsEndpoint = "/me/calendars";
    private const string ProviderIdValue = "microsoft";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IOAuthService _oauthService;
    private readonly ILogger _log;
    private readonly HttpClient _httpClient;

    /// <inheritdoc />
    public string ProviderId => ProviderIdValue;

    /// <summary>
    /// Creates a new <see cref="OutlookCalendarProvider"/> with the given OAuth service.
    /// </summary>
    /// <param name="oauthService">OAuth service for obtaining access tokens.</param>
    /// <param name="logger">Serilog logger pre-enriched with calendar context.</param>
    public OutlookCalendarProvider(IOAuthService oauthService, ILogger logger)
        : this(oauthService, logger, new HttpClient())
    {
    }

    /// <summary>Test seam: routes every request through <paramref name="handler"/>.</summary>
    internal OutlookCalendarProvider(IOAuthService oauthService, ILogger logger, HttpMessageHandler handler)
        : this(oauthService, logger, new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))))
    {
    }

    private OutlookCalendarProvider(IOAuthService oauthService, ILogger logger, HttpClient httpClient)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<OutlookCalendarProvider>();

        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)
    {
        _log.Debug("Listing Microsoft Outlook calendars");

        var accessToken = await _oauthService.GetAccessTokenAsync(ProviderIdValue).ConfigureAwait(false);
        var calendars = new List<CalendarInfo>();

        string? nextPageUrl = $"{GraphBaseUrl}{CalendarsEndpoint}?$select=id,name,owner,isDefaultCalendar";

        while (nextPageUrl is not null)
        {
            using var response = await SendAuthenticatedRequestAsync(nextPageUrl, accessToken, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphCalendarListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Value is null || result.Value.Count == 0)
                break;

            foreach (var item in result.Value)
            {
                calendars.Add(new CalendarInfo
                {
                    Id = item.Id ?? string.Empty,
                    Name = item.Name ?? "Unnamed Calendar",
                    Owner = item.Owner?.Address ?? item.Owner?.Name,
                    SourceProvider = ProviderIdValue,
                    IsPrimary = item.IsDefaultCalendar ?? false,
                });
            }

            nextPageUrl = result.ODataNextLink;
        }

        _log.Information("Listed {Count} Microsoft Outlook calendars", calendars.Count);
        return calendars;
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<CalEvent> Events, string? DeltaToken)> GetEventsAsync(
        string calendarId,
        DateTime start,
        DateTime end,
        string? deltaToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);

        _log.Debug("Fetching Microsoft Outlook events for CalendarId={CalendarId}", calendarId);

        var accessToken = await _oauthService.GetAccessTokenAsync(ProviderIdValue).ConfigureAwait(false);
        var events = new List<CalEvent>();
        string? deltaLink = null;

        string requestUrl;

        var incremental = deltaToken is not null;
        if (incremental)
        {
            // Delta query: use the provided delta link for incremental sync.
            requestUrl = deltaToken!;
            _log.Debug("Using delta token for incremental sync on CalendarId={CalendarId}", calendarId);
        }
        else
        {
            // Full sync: the calendar view of the window. Unlike /events filtered on start
            // time, which returns only series masters, it expands every recurring meeting into
            // its occurrences. Window bounds carry 'Z', so Graph reads them as UTC.
            requestUrl = BuildCalendarViewUrl(calendarId, start, end);
        }

        string? nextPageUrl = requestUrl;

        while (nextPageUrl is not null)
        {
            using var response = await SendAuthenticatedRequestAsync(nextPageUrl, accessToken, cancellationToken).ConfigureAwait(false);

            // A delta link Graph no longer accepts (410 Gone when it expired, 400 when it is
            // malformed or from another query) is discarded for a full read. The status is
            // checked before EnsureSuccessStatusCode, which would otherwise throw first.
            if (incremental && response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest)
            {
                _log.Warning(
                    "Microsoft Graph rejected the delta link for CalendarId={CalendarId} ({Status}) - discarding it and running a full sync",
                    calendarId, (int)response.StatusCode);
                return await GetEventsAsync(calendarId, start, end, deltaToken: null, cancellationToken).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphEventsListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            foreach (var item in result?.Value ?? [])
            {
                // A delta read reports a deleted event as {"id": ..., "@removed": {...}}, with no
                // iCalUId to match the vault copy by; it is skipped rather than stored as an
                // empty event. A full read never contains these: the sync retires what it no
                // longer lists instead.
                if (item.Removed is not null)
                    continue;

                // Cancelled meetings are passed on (marked) so the vault copy says so.
                events.Add(MapToCalEvent(item, calendarId));
            }

            // The delta link arrives on the last page, which may be empty, so it is read
            // before any loop exit.
            if (result?.ODataDeltaLink is not null)
                deltaLink = result.ODataDeltaLink;
            nextPageUrl = result?.ODataNextLink;
        }

        _log.Information(
            "Fetched {EventCount} Microsoft Outlook events for CalendarId={CalendarId}",
            events.Count, calendarId);

        // The calendar view lists every event in the window; a delta link read lists only the
        // changes since it.
        return (new CalendarEventBatch(events, isCompleteWindow: !incremental), deltaLink);
    }

    // -- Private: HTTP request helper -----------------------------------------------

    /// <summary>
    /// The calendar view of one calendar between <paramref name="start"/> and
    /// <paramref name="end"/> (both UTC).
    /// </summary>
    internal static string BuildCalendarViewUrl(string calendarId, DateTime start, DateTime end)
    {
        var encodedCalendarId = Uri.EscapeDataString(calendarId);
        var startStr = Uri.EscapeDataString(start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var endStr = Uri.EscapeDataString(end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

        return $"{GraphBaseUrl}/me/calendars/{encodedCalendarId}/calendarView"
            + $"?startDateTime={startStr}&endDateTime={endStr}"
            + "&$select=id,iCalUId,subject,body,start,end,location,attendees,organizer,isAllDay,isCancelled,recurrence,seriesMasterId,type,webLink"
            + "&$top=100";
    }

    /// <summary>
    /// Sends a GET with the bearer token. The caller inspects the status: an incremental
    /// request needs to see 400 and 410 before treating the response as a failure.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // Page size, and every start/end in UTC: Graph otherwise answers in the mailbox's
        // own time zone without an offset.
        request.Headers.Add("Prefer", "odata.maxpagesize=100");
        request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Private: response mapping ───────────────────────────────────────────────

    private static CalEvent MapToCalEvent(GraphCalendarEvent item, string calendarId)
    {
        // Parse start/end times from Graph's dateTimeTimeZone format.
        var isAllDay = item.IsAllDay ?? false;
        var start = ParseGraphDateTime(item.Start?.DateTimeStr, item.Start?.TimeZone, isAllDay);
        var end = ParseGraphDateTime(item.End?.DateTimeStr, item.End?.TimeZone, isAllDay);

        // Map attendees.
        var attendees = item.Attendees?
            .Select(a => new CalAttendee
            {
                DisplayName = a.EmailAddress?.Name ?? string.Empty,
                Email = a.EmailAddress?.Address ?? string.Empty,
                ResponseStatus = MapResponseStatus(a.Status?.Response),
                IsOrganizer = a.Type == "organizer",
            })
            .ToList() ?? [];

        // Find the organizer.
        var organizer = item.Organizer?.EmailAddress?.Name
                        ?? item.Organizer?.EmailAddress?.Address;

        // Extract body content.
        string? description = null;
        if (item.Body?.ContentType == "html" && item.Body.Content is not null)
        {
            // Strip HTML tags for search indexing.
            description = StripHtmlTags(item.Body.Content);
        }
        else if (item.Body?.Content is not null)
        {
            description = item.Body.Content;
        }

        return new CalEvent
        {
            Id = item.ICalUId ?? item.Id ?? string.Empty, // Use iCalUId for cross-platform stability
            Title = item.Subject ?? string.Empty,
            Description = description,
            Start = start,
            End = end,
            Location = item.Location?.DisplayName,
            IsAllDay = isAllDay,
            IsRecurring = item.Recurrence is not null || item.SeriesMasterId is not null,
            IsCancelled = item.IsCancelled ?? false,
            Attendees = attendees,
            Organizer = organizer,
            CalendarName = null, // Filled later by sync service
            SourceProvider = ProviderIdValue,
            HtmlLink = item.WebLink,
            CalendarId = calendarId,
        };
    }

    /// <summary>
    /// Parses a Microsoft Graph dateTimeTimeZone value, e.g.
    /// <c>{"dateTime": "2026-04-15T09:00:00.0000000", "timeZone": "UTC"}</c>. The dateTime has
    /// no offset; it is a wall-clock time in <paramref name="timeZone"/>, which the
    /// <c>Prefer: outlook.timezone="UTC"</c> header makes UTC. Never read as machine-local time.
    /// </summary>
    /// <remarks>
    /// An all-day event is a date, not an instant: its midnight is kept as that date at 00:00
    /// UTC whatever zone it was reported in.
    /// </remarks>
    internal static DateTime ParseGraphDateTime(string? dateTime, string? timeZone, bool isAllDay = false)
    {
        if (string.IsNullOrWhiteSpace(dateTime)
            || !DateTime.TryParse(
                dateTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var utcWallClock))
        {
            return DateTime.MinValue;
        }

        // utcWallClock holds the digits Graph sent, labelled UTC. That is right for UTC and for
        // all-day dates; any other zone is converted from its wall clock.
        if (isAllDay || IsUtcZone(timeZone))
            return utcWallClock;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone!);
            var wallClock = DateTime.SpecifyKind(utcWallClock, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(wallClock, zone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            // An unknown zone name is rarer than a UTC answer; keep the digits as UTC.
            return utcWallClock;
        }
    }

    private static bool IsUtcZone(string? timeZone) =>
        string.IsNullOrWhiteSpace(timeZone)
        || timeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase)
        || timeZone.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase)
        || timeZone.Equals("Coordinated Universal Time", StringComparison.OrdinalIgnoreCase)
        || timeZone.Equals("tzone://Microsoft/Utc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps Microsoft Graph response status values to the standardized
    /// attendee response status strings.
    /// </summary>
    private static string MapResponseStatus(string? graphStatus)
    {
        return graphStatus?.ToLowerInvariant() switch
        {
            "accepted" => "accepted",
            "declined" => "declined",
            "tentativelyaccepted" => "tentative",
            "notresponded" => "needsAction",
            "organizer" => "accepted",
            _ => "needsAction",
        };
    }

    /// <summary>
    /// Strips basic HTML tags from a string for search indexing.
    /// Handles &lt;br&gt;, &lt;p&gt;, &lt;div&gt;, and similar block elements
    /// by replacing them with newlines, then removes all remaining tags.
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Replace block-level tags with newlines.
        var text = System.Text.RegularExpressions.Regex.Replace(
            html, @"</?(p|div|br|li|h[1-6])[^>]*>", "\n",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        // Remove all remaining HTML tags.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"<[^>]+>", "",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        // Decode common HTML entities.
        text = text.Replace("&nbsp;", " ")
                   .Replace("&amp;", "&")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&quot;", "\"");

        // Collapse multiple consecutive newlines.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"\n{3,}", "\n\n",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        return text.Trim();
    }

    // ── Private: Microsoft Graph API JSON response models ───────────────────────
    // Internal deserialization-only models matching the Microsoft Graph API v1.0
    // JSON response format. Property names use C# conventions; JsonPropertyName
    // is used for non-standard names like @odata.nextLink.

    private sealed class GraphCalendarListResponse
    {
        public List<GraphCalendar>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? ODataNextLink { get; set; }
    }

    private sealed class GraphCalendar
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public GraphEmailAddress? Owner { get; set; }
        public bool? IsDefaultCalendar { get; set; }
    }

    private sealed class GraphEmailAddress
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
    }

    private sealed class GraphEventsListResponse
    {
        public List<GraphCalendarEvent>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? ODataNextLink { get; set; }

        [JsonPropertyName("@odata.deltaLink")]
        public string? ODataDeltaLink { get; set; }
    }

    private sealed class GraphCalendarEvent
    {
        public string? Id { get; set; }

        [JsonPropertyName("iCalUId")]
        public string? ICalUId { get; set; }

        public string? Subject { get; set; }
        public GraphEventBody? Body { get; set; }
        public GraphDateTimeTimeZone? Start { get; set; }
        public GraphDateTimeTimeZone? End { get; set; }
        public GraphEventLocation? Location { get; set; }
        public bool? IsAllDay { get; set; }
        public bool? IsCancelled { get; set; }
        public GraphRecurrence? Recurrence { get; set; }
        public string? SeriesMasterId { get; set; }

        public List<GraphEventAttendee>? Attendees { get; set; }
        public GraphEventOrganizer? Organizer { get; set; }
        public string? WebLink { get; set; }

        /// <summary>
        /// Present (an object such as <c>{"reason": "deleted"}</c>) on a delta read's entry for
        /// an event that was deleted.
        /// </summary>
        [JsonPropertyName("@removed")]
        public GraphRemoved? Removed { get; set; }
    }

    private sealed class GraphRemoved
    {
        public string? Reason { get; set; }
    }

    private sealed class GraphEventBody
    {
        public string? Content { get; set; }
        public string? ContentType { get; set; }
    }

    private sealed class GraphDateTimeTimeZone
    {
        // Named DateTimeStr to avoid clash with System.DateTime.
        [JsonPropertyName("dateTime")]
        public string? DateTimeStr { get; set; }

        public string? TimeZone { get; set; }
    }

    private sealed class GraphEventLocation
    {
        public string? DisplayName { get; set; }
    }

    private sealed class GraphEventAttendee
    {
        public GraphEmailAddress? EmailAddress { get; set; }
        public GraphAttendeeStatus? Status { get; set; }
        public string? Type { get; set; }
    }

    private sealed class GraphAttendeeStatus
    {
        public string? Response { get; set; }
    }

    private sealed class GraphEventOrganizer
    {
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private sealed class GraphRecurrence
    {
        // Minimal — we only need to detect if recurrence is present.
        public object? Pattern { get; set; }
    }
}

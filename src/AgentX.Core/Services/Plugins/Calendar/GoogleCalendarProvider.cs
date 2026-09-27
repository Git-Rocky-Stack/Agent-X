using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// <see cref="ICalendarProvider"/> implementation for Google Calendar API v3.
/// Uses <see cref="IOAuthService"/> for OAuth2 authentication and communicates
/// via <c>HttpClient</c> with JSON responses.
/// </summary>
/// <remarks>
/// Google Calendar API v3 reference:
/// <list type="bullet">
///   <item>Calendars list: <c>GET https://www.googleapis.com/calendar/v3/users/me/calendarList</c></item>
///   <item>Events list: <c>GET https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events</c></item>
/// </list>
/// All requests require <c>Authorization: Bearer {accessToken}</c> header.
/// </remarks>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    private const string CalendarListEndpoint = "https://www.googleapis.com/calendar/v3/users/me/calendarList";
    private const string EventsBaseEndpoint = "https://www.googleapis.com/calendar/v3/calendars";
    private const string ProviderIdValue = "google";

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
    /// Creates a new <see cref="GoogleCalendarProvider"/> with the given OAuth service.
    /// </summary>
    /// <param name="oauthService">OAuth service for obtaining access tokens.</param>
    /// <param name="logger">Serilog logger pre-enriched with calendar context.</param>
    public GoogleCalendarProvider(IOAuthService oauthService, ILogger logger)
        : this(oauthService, logger, new HttpClient())
    {
    }

    /// <summary>Test seam: routes every request through <paramref name="handler"/>.</summary>
    internal GoogleCalendarProvider(IOAuthService oauthService, ILogger logger, HttpMessageHandler handler)
        : this(oauthService, logger, new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))))
    {
    }

    private GoogleCalendarProvider(IOAuthService oauthService, ILogger logger, HttpClient httpClient)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<GoogleCalendarProvider>();

        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)
    {
        _log.Debug("Listing Google calendars");

        var accessToken = await _oauthService.GetAccessTokenAsync(ProviderIdValue).ConfigureAwait(false);
        var calendars = new List<CalendarInfo>();

        string? pageToken = null;

        do
        {
            var url = CalendarListEndpoint;
            if (pageToken is not null)
                url += $"?pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await SendAuthenticatedRequestAsync(url, accessToken, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GoogleCalendarListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Items is null || result.Items.Count == 0)
                break;

            foreach (var item in result.Items)
            {
                calendars.Add(new CalendarInfo
                {
                    Id = item.Id ?? string.Empty,
                    Name = item.Summary ?? "Unnamed Calendar",
                    Owner = item.Id, // Google uses the calendar ID as the owner identifier
                    SourceProvider = ProviderIdValue,
                    IsPrimary = item.Primary ?? false,
                });
            }

            pageToken = result.NextPageToken;
        } while (pageToken is not null);

        _log.Information("Listed {Count} Google calendars", calendars.Count);
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

        _log.Debug("Fetching Google Calendar events for CalendarId={CalendarId}", calendarId);

        var accessToken = await _oauthService.GetAccessTokenAsync(ProviderIdValue).ConfigureAwait(false);
        var events = new List<CalEvent>();

        var encodedCalendarId = Uri.EscapeDataString(calendarId);
        var baseUrl = $"{EventsBaseEndpoint}/{encodedCalendarId}/events";
        var incremental = deltaToken is not null;

        // Build query parameters. An incremental request carries the sync token and must
        // not repeat timeMin, timeMax, or orderBy: Google rejects that combination with 400.
        var queryParams = new List<string>
        {
            "singleEvents=true", // Expand recurring events into individual instances
            "maxResults=250",
        };

        if (incremental)
        {
            queryParams.Add($"syncToken={Uri.EscapeDataString(deltaToken!)}");
        }
        else
        {
            queryParams.Add($"timeMin={Uri.EscapeDataString(start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}");
            queryParams.Add($"timeMax={Uri.EscapeDataString(end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}");
            queryParams.Add("orderBy=startTime");
        }

        string? pageToken = null;
        string? nextSyncToken = null;

        do
        {
            var url = $"{baseUrl}?{string.Join("&", queryParams)}";
            if (pageToken is not null)
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await SendAuthenticatedRequestAsync(url, accessToken, cancellationToken).ConfigureAwait(false);

            // A stored sync token Google no longer accepts (410 Gone when it expired, 400 when
            // it is invalid) means the calendar has to be read in full again. The status is
            // checked before EnsureSuccessStatusCode, which would otherwise throw first.
            if (incremental && response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest)
            {
                _log.Warning(
                    "Google Calendar rejected the sync token for CalendarId={CalendarId} ({Status}) - discarding it and running a full sync",
                    calendarId, (int)response.StatusCode);
                return await GetEventsAsync(calendarId, start, end, deltaToken: null, cancellationToken).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GoogleEventsListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            foreach (var item in result?.Items ?? [])
            {
                if (item.Status == "cancelled")
                {
                    // Google marks a deleted event, and an occurrence removed from a series, as
                    // "cancelled": the API says neither is to be shown any more, and a deleted
                    // event may arrive as a bare stub with nothing but its id. Each is passed on
                    // as a deletion notice so the sync retires the stored copy.
                    if (!string.IsNullOrEmpty(item.Id))
                        events.Add(CreateDeletionNotice(item.Id, calendarId));
                    continue;
                }

                events.Add(MapToCalEvent(item, calendarId));
            }

            pageToken = result?.NextPageToken;

            // The sync token arrives on the last page, which may carry no items at all (an
            // incremental sync with no changes), so it is read before any loop exit.
            if (result?.NextSyncToken is not null)
                nextSyncToken = result.NextSyncToken;
        } while (pageToken is not null);

        _log.Information(
            "Fetched {EventCount} Google Calendar events for CalendarId={CalendarId}",
            events.Count, calendarId);

        // A read without a sync token lists every event in the window; one with a token lists
        // only the changes since it.
        return (new CalendarEventBatch(events, isCompleteWindow: !incremental), nextSyncToken);
    }

    // ── Private: HTTP request helper ────────────────────────────────────────────

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

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Private: response mapping ───────────────────────────────────────────────

    private static CalEvent MapToCalEvent(GoogleCalendarEvent item, string calendarId)
    {
        // Parse start/end times from Google's dateTime or date fields.
        var (start, isAllDay) = ParseGoogleDateTime(item.Start?.Date, item.Start?.DateTime);
        var (end, _) = ParseGoogleDateTime(item.End?.Date, item.End?.DateTime);

        // Map attendees.
        var attendees = item.Attendees?
            .Select(a => new CalAttendee
            {
                DisplayName = a.DisplayName ?? string.Empty,
                Email = a.Email ?? string.Empty,
                ResponseStatus = a.ResponseStatus ?? "needsAction",
                IsOrganizer = a.Self ?? false,
            })
            .ToList() ?? [];

        // Find the organizer.
        var organizer = item.Organizer?.DisplayName ?? item.Organizer?.Email;

        return new CalEvent
        {
            Id = item.Id ?? string.Empty,
            Title = item.Summary ?? string.Empty,
            Description = item.Description,
            Start = start,
            End = end,
            Location = item.Location,
            IsAllDay = isAllDay,
            IsRecurring = item.RecurringEventId is not null,
            Attendees = attendees,
            Organizer = organizer,
            CalendarName = null, // Filled later by sync service
            SourceProvider = ProviderIdValue,
            HtmlLink = item.HtmlLink,
            CalendarId = calendarId,
        };
    }

    /// <summary>The deletion notice for an event an incremental sync reported as deleted.</summary>
    private static CalEvent CreateDeletionNotice(string eventId, string calendarId) => new()
    {
        Id = eventId,
        IsDeleted = true,
        SourceProvider = ProviderIdValue,
        CalendarId = calendarId,
    };

    /// <summary>
    /// Parses a Google Calendar start or end, which has either a "dateTime" (a timed event,
    /// RFC 3339 with an offset) or a "date" (an all-day event, YYYY-MM-DD). Neither path
    /// depends on the machine's time zone.
    /// </summary>
    /// <remarks>
    /// An all-day date is a calendar date, not an instant, so it is returned as that date at
    /// 00:00 UTC. Parsing it as an instant and converting through local time (the old
    /// behavior) moved every all-day event to the previous day west of UTC.
    /// </remarks>
    internal static (DateTime UtcDateTime, bool IsAllDay) ParseGoogleDateTime(string? date, string? dateTime)
    {
        // All-day events have a "date" field (YYYY-MM-DD), not a "dateTime".
        if (!string.IsNullOrWhiteSpace(date)
            && DateTime.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var day))
        {
            return (day, true);
        }

        if (!string.IsNullOrWhiteSpace(dateTime)
            && DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
        {
            return (instant.UtcDateTime, false);
        }

        return (DateTime.MinValue, false);
    }

    // ── Private: Google API JSON response models ────────────────────────────────
    // These are internal deserialization-only models matching the Google Calendar
    // API v3 JSON response format. Kept minimal to reduce memory allocations.

    private sealed class GoogleCalendarListResponse
    {
        public List<GoogleCalendarListEntry>? Items { get; set; }
        public string? NextPageToken { get; set; }
    }

    private sealed class GoogleCalendarListEntry
    {
        public string? Id { get; set; }
        public string? Summary { get; set; }
        public bool? Primary { get; set; }
    }

    private sealed class GoogleEventsListResponse
    {
        public List<GoogleCalendarEvent>? Items { get; set; }
        public string? NextPageToken { get; set; }
        public string? NextSyncToken { get; set; }
    }

    private sealed class GoogleCalendarEvent
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public GoogleDateTime? Start { get; set; }
        public GoogleDateTime? End { get; set; }
        public string? Location { get; set; }
        public string? HtmlLink { get; set; }
        public string? RecurringEventId { get; set; }
        public List<GoogleEventAttendee>? Attendees { get; set; }
        public GoogleEventOrganizer? Organizer { get; set; }
    }

    private sealed class GoogleDateTime
    {
        public string? DateTime { get; set; }
        public string? Date { get; set; }
    }

    private sealed class GoogleEventAttendee
    {
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string? ResponseStatus { get; set; }
        public bool? Self { get; set; }
    }

    private sealed class GoogleEventOrganizer
    {
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
    }
}

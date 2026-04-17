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
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<GoogleCalendarProvider>();

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
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

            var response = await SendAuthenticatedRequestAsync(url, accessToken, cancellationToken).ConfigureAwait(false);

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

        // Build query parameters.
        var queryParams = new List<string>
        {
            $"timeMin={Uri.EscapeDataString(start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}",
            $"timeMax={Uri.EscapeDataString(end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}",
            "singleEvents=true", // Expand recurring events into individual instances
            "orderBy=startTime",
            "maxResults=250",
        };

        if (deltaToken is not null)
            queryParams.Add($"syncToken={Uri.EscapeDataString(deltaToken)}");

        string? pageToken = null;
        string? nextSyncToken = null;

        do
        {
            var url = $"{baseUrl}?{string.Join("&", queryParams)}";
            if (pageToken is not null)
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            var response = await SendAuthenticatedRequestAsync(url, accessToken, cancellationToken).ConfigureAwait(false);

            // Handle 410 Gone for stale sync tokens — caller should retry without deltaToken.
            if (response.StatusCode == HttpStatusCode.Gone)
            {
                _log.Warning("Google Calendar sync token expired for CalendarId={CalendarId} — full sync required", calendarId);
                return ([], null);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GoogleEventsListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Items is null || result.Items.Count == 0)
                break;

            foreach (var item in result.Items)
            {
                if (item.Status == "cancelled")
                    continue; // Skip deleted events

                var calEvent = MapToCalEvent(item, calendarId);
                events.Add(calEvent);
            }

            pageToken = result.NextPageToken;

            // Capture sync token from each page — the last page's value is the
            // definitive one returned by the API after all results are enumerated.
            if (result.NextSyncToken is not null)
                nextSyncToken = result.NextSyncToken;
        } while (pageToken is not null);

        _log.Information(
            "Fetched {EventCount} Google Calendar events for CalendarId={CalendarId}",
            events.Count, calendarId);

        return (events, nextSyncToken);
    }

    // ── Private: HTTP request helper ────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return response;
    }

    // ── Private: response mapping ───────────────────────────────────────────────

    private static CalEvent MapToCalEvent(GoogleCalendarEvent item, string calendarId)
    {
        // Parse start/end times from Google's dateTime or date fields.
        var (start, isAllDay) = ParseGoogleDateTime(item.Start);
        var (end, _) = ParseGoogleDateTime(item.End);

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

    /// <summary>
    /// Parses a Google Calendar datetime object which has either a "dateTime" field
    /// (for timed events) or a "date" field (for all-day events).
    /// </summary>
    private static (DateTime UtcDateTime, bool IsAllDay) ParseGoogleDateTime(GoogleDateTime? googleDt)
    {
        if (googleDt is null)
            return (DateTime.MinValue, false);

        // All-day events have a "date" field (YYYY-MM-DD), not a "dateTime".
        if (googleDt.Date is not null)
        {
            var date = DateTime.Parse(googleDt.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            return (DateTime.SpecifyKind(date, DateTimeKind.Utc), true);
        }

        if (googleDt.DateTime is not null)
        {
            var dt = DateTime.Parse(googleDt.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return (dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime(), false);
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
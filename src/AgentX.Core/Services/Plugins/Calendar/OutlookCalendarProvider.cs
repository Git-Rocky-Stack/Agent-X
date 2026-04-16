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
///   <item>Events list: <c>GET https://graph.microsoft.com/v1.0/me/calendars/{id}/events</c></item>
///   <item>Delta query: <c>GET https://graph.microsoft.com/v1.0/me/calendars/{id}/events/delta</c></item>
/// </list>
/// All requests require <c>Authorization: Bearer {accessToken}</c> header.
/// Delta queries return <c>@odata.deltaLink</c> for incremental sync.
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
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<OutlookCalendarProvider>();

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
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
            var response = await SendAuthenticatedRequestAsync(nextPageUrl, accessToken, cancellationToken).ConfigureAwait(false);

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

        if (deltaToken is not null)
        {
            // Delta query: use the provided delta link for incremental sync.
            requestUrl = deltaToken;
            _log.Debug("Using delta token for incremental sync on CalendarId={CalendarId}", calendarId);
        }
        else
        {
            // Full sync: list events with time range filter.
            var encodedCalendarId = Uri.EscapeDataString(calendarId);
            var startStr = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var endStr = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            requestUrl = $"{GraphBaseUrl}/me/calendars/{encodedCalendarId}/events"
                + $"?$filter=start/dateTime ge '{startStr}' and end/dateTime le '{endStr}'"
                + "&$select=id,iCalUId,subject,body,start,end,location,attendees,organizer,isAllDay,recurrence,webLink"
                + "&$top=100";
        }

        string? nextPageUrl = requestUrl;

        while (nextPageUrl is not null)
        {
            var response = await SendAuthenticatedRequestAsync(nextPageUrl, accessToken, cancellationToken).ConfigureAwait(false);

            // Handle 410 Gone for stale delta tokens.
            if (response.StatusCode == HttpStatusCode.Gone)
            {
                _log.Warning("Microsoft Graph delta token expired for CalendarId={CalendarId} — full sync required", calendarId);
                return ([], null);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphEventsListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Value is null || result.Value.Count == 0)
                break;

            foreach (var item in result.Value)
            {
                if (item.IsCancelled ?? false)
                    continue; // Skip cancelled events from delta queries

                var calEvent = MapToCalEvent(item, calendarId);
                events.Add(calEvent);
            }

            // Check for delta link (returned on the last page of delta queries).
            deltaLink = result.ODataDeltaLink;
            nextPageUrl = result.ODataNextLink;
        }

        _log.Information(
            "Fetched {EventCount} Microsoft Outlook events for CalendarId={CalendarId}",
            events.Count, calendarId);

        return (events, deltaLink);
    }

    // ── Private: HTTP request helper ────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // Request full JSON without minimal metadata.
        request.Headers.Add("Prefer", "odata.maxpagesize=100");

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return response;
    }

    // ── Private: response mapping ───────────────────────────────────────────────

    private static CalEvent MapToCalEvent(GraphCalendarEvent item, string calendarId)
    {
        // Parse start/end times from Graph's dateTimeTimeZone format.
        var start = ParseGraphDateTime(item.Start);
        var end = ParseGraphDateTime(item.End);
        var isAllDay = item.IsAllDay ?? false;

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
            IsRecurring = item.Recurrence is not null,
            Attendees = attendees,
            Organizer = organizer,
            CalendarName = null, // Filled later by sync service
            SourceProvider = ProviderIdValue,
            HtmlLink = item.WebLink,
            CalendarId = calendarId,
        };
    }

    /// <summary>
    /// Parses a Microsoft Graph dateTimeTimeZone object.
    /// The Graph API returns <c>{"dateTime": "2026-04-15T09:00:00", "timeZone": "UTC"}</c>.
    /// </summary>
    private static DateTime ParseGraphDateTime(GraphDateTimeTimeZone? dt)
    {
        if (dt?.DateTimeStr is null)
            return DateTime.MinValue;

        if (DateTime.TryParse(dt.DateTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            return result.Kind == DateTimeKind.Utc ? result : result.ToUniversalTime();
        }

        return DateTime.MinValue;
    }

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
        public List<GraphEventAttendee>? Attendees { get; set; }
        public GraphEventOrganizer? Organizer { get; set; }
        public string? WebLink { get; set; }
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
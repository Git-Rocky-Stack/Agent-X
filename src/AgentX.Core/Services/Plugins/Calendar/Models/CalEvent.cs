namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// Represents a calendar event fetched from an external provider
/// (Google Calendar or Microsoft Outlook). This is the unified DTO that
/// provider implementations map their API-specific responses into.
/// </summary>
public sealed class CalEvent
{
    /// <summary>
    /// Provider-specific event identifier (e.g. Google's <c>id</c> or
    /// Microsoft Graph's <c>iCalUId</c>). Used for deduplication during sync.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Event title / subject line.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Full event description or body content. May contain HTML from the provider.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Event start time in UTC.
    /// </summary>
    public DateTime Start { get; init; }

    /// <summary>
    /// Event end time in UTC.
    /// </summary>
    public DateTime End { get; init; }

    /// <summary>
    /// Event location (e.g. "Conference Room B" or a video call URL).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Whether this is an all-day event. All-day events have <see cref="Start"/>
    /// at midnight and <see cref="End"/> at the following midnight.
    /// </summary>
    public bool IsAllDay { get; init; }

    /// <summary>
    /// Whether this event is part of a recurring series.
    /// </summary>
    public bool IsRecurring { get; init; }

    /// <summary>
    /// List of attendees (including the organizer, who is also listed separately).
    /// </summary>
    public IReadOnlyList<CalAttendee> Attendees { get; init; } = [];

    /// <summary>
    /// Display name of the event organizer.
    /// </summary>
    public string? Organizer { get; init; }

    /// <summary>
    /// Name of the calendar this event belongs to (e.g. "Work", "Personal").
    /// </summary>
    public string? CalendarName { get; init; }

    /// <summary>
    /// Identifier of the source provider: <c>"google"</c> or <c>"microsoft"</c>.
    /// </summary>
    public string SourceProvider { get; init; } = string.Empty;

    /// <summary>
    /// Link to view the event in the provider's web UI.
    /// </summary>
    public string? HtmlLink { get; init; }

    /// <summary>
    /// Provider-specific calendar identifier that this event belongs to.
    /// Used to correlate events with their parent calendar during sync.
    /// </summary>
    public string? CalendarId { get; init; }
}

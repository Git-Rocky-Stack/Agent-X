using System.Globalization;
using System.Text;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Plugins.Calendar.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// Converts <see cref="CalEvent"/> instances into <see cref="InboxItemEntity"/>
/// objects and extracts searchable content for indexing. Handles the mapping
/// between the unified calendar event DTO and the inbox entity schema.
/// </summary>
public sealed class CalendarEventProcessor
{
    private readonly ILogger _log;

    public CalendarEventProcessor(ILogger logger)
    {
        _log = (logger ?? throw new ArgumentNullException(nameof(logger)))
            .ForContext<CalendarEventProcessor>();
    }

    /// <summary>
    /// The source plugin ID used for all calendar items in the inbox.
    /// </summary>
    public const string PluginId = "com.agentx.calendar";

    /// <summary>
    /// The source category for calendar events.
    /// </summary>
    public const string SourceCategory = "calendar_event";

    /// <summary>
    /// The source type identifier for inbox items from the calendar connector.
    /// </summary>
    public const string SourceType = "calendar-connector";

    /// <summary>
    /// Converts a <see cref="CalEvent"/> into a set of parameters suitable for
    /// <see cref="Inbox.IInboxService.TriageExternalAsync"/>.
    /// Returns the file name, file type, source type, source URL, plugin ID,
    /// source category, external ID, content preview, and full content text.
    /// </summary>
    /// <param name="calEvent">The calendar event to convert.</param>
    /// <param name="settings">
    /// The connector's sync settings; <see cref="CalendarSyncSettings.IncludeDescriptions"/> and
    /// <see cref="CalendarSyncSettings.IncludeAttendeeDetails"/> decide what is indexed. Null
    /// includes everything.
    /// </param>
    /// <returns>A tuple of all parameters needed for TriageExternalAsync.</returns>
    public (string FileName, string FileType, string SourceType, string? SourceUrl,
            string SourcePluginId, string SourceCategory, string ExternalId,
            string? ContentPreview, string ContentText)
        ConvertToInboxParameters(CalEvent calEvent, CalendarSyncSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(calEvent);

        var fileName = BuildFileName(calEvent);
        var contentPreview = BuildContentPreview(calEvent);
        var contentText = ExtractSearchableContent(calEvent, settings);

        return (
            FileName: fileName,
            FileType: "CalendarEvent",
            SourceType: SourceType,
            SourceUrl: calEvent.HtmlLink,
            SourcePluginId: PluginId,
            SourceCategory: SourceCategory,
            ExternalId: $"{calEvent.SourceProvider}:{calEvent.CalendarId}:{calEvent.Id}",
            ContentPreview: contentPreview,
            ContentText: contentText
        );
    }

    /// <summary>
    /// Extracts all searchable content from a calendar event as a single text string.
    /// This content is written to a temp file and then indexed through the standard
    /// chunking + embedding pipeline.
    /// </summary>
    /// <param name="calEvent">The calendar event to extract content from.</param>
    /// <param name="settings">
    /// Sync settings deciding whether the description and attendee details are indexed.
    /// Null includes everything.
    /// </param>
    /// <returns>Full text content suitable for search indexing.</returns>
    /// <remarks>
    /// Dates are written in ISO form with the invariant culture: the Gregorian calendar and
    /// ASCII digits regardless of the user's region (a Thai or Arabic culture would otherwise
    /// write years such as 2569 or 1448 into the index).
    /// </remarks>
    public string ExtractSearchableContent(CalEvent calEvent, CalendarSyncSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(calEvent);

        var includeDescription = settings?.IncludeDescriptions ?? true;
        var includeAttendees = settings?.IncludeAttendeeDetails ?? true;
        var invariant = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(1024);

        // Title
        sb.AppendLine($"Title: {calEvent.Title}");

        if (calEvent.IsCancelled)
            sb.AppendLine("Status: Cancelled");

        // Time range
        if (calEvent.IsAllDay)
        {
            sb.AppendLine(invariant, $"Date: {calEvent.Start:yyyy-MM-dd} (all day)");
        }
        else
        {
            sb.AppendLine(invariant, $"Start: {calEvent.Start:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine(invariant, $"End: {calEvent.End:yyyy-MM-dd HH:mm} UTC");
        }

        // Location
        if (!string.IsNullOrWhiteSpace(calEvent.Location))
            sb.AppendLine($"Location: {calEvent.Location}");

        // Organizer
        if (!string.IsNullOrWhiteSpace(calEvent.Organizer))
            sb.AppendLine($"Organizer: {calEvent.Organizer}");

        // Description
        if (includeDescription && !string.IsNullOrWhiteSpace(calEvent.Description))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(calEvent.Description);
        }

        // Attendees (names, addresses, and responses are the "attendee details")
        if (includeAttendees && calEvent.Attendees.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Attendees:");
            foreach (var attendee in calEvent.Attendees)
            {
                var status = attendee.ResponseStatus switch
                {
                    "accepted" => "+",
                    "declined" => "-",
                    "tentative" => "~",
                    _ => "?",
                };
                var name = !string.IsNullOrWhiteSpace(attendee.DisplayName)
                    ? attendee.DisplayName
                    : attendee.Email;
                sb.AppendLine($"  [{status}] {name} ({attendee.Email})");
            }
        }

        // Calendar info
        if (!string.IsNullOrWhiteSpace(calEvent.CalendarName))
            sb.AppendLine($"Calendar: {calEvent.CalendarName}");

        // Provider
        sb.AppendLine($"Source: {calEvent.SourceProvider}");

        // Recurring
        if (calEvent.IsRecurring)
            sb.AppendLine("Recurring: yes");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a short content preview for the inbox item.
    /// This is shown in the Smart Inbox UI before full indexing.
    /// </summary>
    private static string BuildContentPreview(CalEvent calEvent)
    {
        var parts = new List<string>(5);
        var invariant = CultureInfo.InvariantCulture;

        if (calEvent.IsCancelled)
            parts.Add("Cancelled");

        if (calEvent.IsAllDay)
            parts.Add(calEvent.Start.ToString("yyyy-MM-dd", invariant));
        else
            parts.Add(string.Create(invariant, $"{calEvent.Start:yyyy-MM-dd HH:mm} - {calEvent.End:HH:mm}"));

        if (!string.IsNullOrWhiteSpace(calEvent.Location))
            parts.Add($"at {calEvent.Location}");

        if (calEvent.Attendees.Count > 0)
            parts.Add($"{calEvent.Attendees.Count} attendee(s)");

        if (!string.IsNullOrWhiteSpace(calEvent.Organizer))
            parts.Add($"organized by {calEvent.Organizer}");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Builds a display-friendly file name for the inbox item.
    /// Format: "Calendar: {title} ({date})", with ", cancelled" for a cancelled event.
    /// </summary>
    private static string BuildFileName(CalEvent calEvent)
    {
        var date = calEvent.IsAllDay
            ? calEvent.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : calEvent.Start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var title = !string.IsNullOrWhiteSpace(calEvent.Title)
            ? calEvent.Title
            : "Untitled Event";

        return calEvent.IsCancelled
            ? $"Calendar: {title} ({date}, cancelled)"
            : $"Calendar: {title} ({date})";
    }

}

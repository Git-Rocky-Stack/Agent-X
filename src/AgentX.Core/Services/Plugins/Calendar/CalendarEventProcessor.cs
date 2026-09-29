using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Inbox;
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

    /// <summary>The tag a retired event's stored name carries after its date.</summary>
    private const string RemovedTag = "removed";

    /// <summary>The first word of a retired event's preview.</summary>
    private const string RemovedPreview = "Removed";

    private const string StatusLinePrefix = "Status: ";

    /// <summary>
    /// A stored name as <see cref="BuildFileName"/> writes it: "{head} ({date})", where the date
    /// is yyyy-MM-dd or yyyy-MM-dd HH:mm (UTC) and may be followed by ", cancelled" or
    /// ", removed". The head is matched greedily, so a title holding parentheses is kept whole.
    /// </summary>
    private static readonly Regex StoredName = new(
        @"^(?<head>.*) \((?<date>\d{4}-\d{2}-\d{2})(?<time> \d{2}:\d{2})?(?<tag>, (?:cancelled|removed))?\)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// The inbox external ID of a calendar event, <c>{provider}:{calendar}:{event}</c>, which
    /// keys its row for dedupe, updates and removal.
    /// </summary>
    public static string BuildExternalId(CalEvent calEvent)
    {
        ArgumentNullException.ThrowIfNull(calEvent);
        return $"{calEvent.SourceProvider}:{calEvent.CalendarId}:{calEvent.Id}";
    }

    /// <summary>
    /// The part every external ID of one calendar's events starts with. Provider calendar IDs
    /// hold no ':' (Google uses "primary" or an e-mail style address, Graph an opaque base64
    /// string), so the prefix of one calendar never matches another calendar's events.
    /// </summary>
    public static string BuildCalendarExternalIdPrefix(string providerId, string calendarId) =>
        $"{providerId}:{calendarId}:";

    /// <summary>
    /// True when <paramref name="externalId"/> is the stored ID of an occurrence of the event
    /// whose ID is <paramref name="seriesExternalId"/>. Google names an occurrence
    /// "{seriesId}_{originalStart}", where the start begins with the digits of its year, and may
    /// report a deleted series by the series ID alone. A series split off by an edit to "this and
    /// following events" is named "{seriesId}_R{time}" and is not an occurrence. Outlook reports
    /// each occurrence under its own iCalUId, which never has this form.
    /// </summary>
    internal static bool IsOccurrenceOf(string externalId, string seriesExternalId) =>
        externalId.Length > seriesExternalId.Length + 1
        && externalId.StartsWith(seriesExternalId, StringComparison.Ordinal)
        && externalId[seriesExternalId.Length] == '_'
        && char.IsAsciiDigit(externalId[seriesExternalId.Length + 1]);

    /// <summary>
    /// The stored copy of an event that left its calendar, with a removal notice: the name ends
    /// in ", removed", the preview starts with "Removed" and the text carries a status line that
    /// says why, so a search hit or a chat answer does not present the meeting as current. The
    /// rest of the text is kept. Marking an already marked copy changes nothing.
    /// </summary>
    public static ExternalItemContent MarkRemoved(ExternalItemContent stored, CalendarRemovalReason reason)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return new ExternalItemContent(
            MarkRemovedName(stored.FileName),
            MarkRemovedPreview(stored.Preview),
            MarkRemovedText(stored.ContentText, reason));
    }

    /// <summary>True when a stored name already carries the removal tag.</summary>
    internal static bool IsMarkedRemoved(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var match = StoredName.Match(fileName);
        return match.Success
            ? match.Groups["tag"].Value == $", {RemovedTag}"
            : fileName.EndsWith($"({RemovedTag})", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the start of an event back from the name <see cref="BuildFileName"/> gave its stored
    /// copy: UTC to the minute, or midnight UTC of an all-day date. False for a name of any other
    /// form (a row from an older build, for one), which the caller then leaves alone.
    /// </summary>
    internal static bool TryReadStartFromStoredName(string? fileName, out DateTime startUtc)
    {
        startUtc = default;
        if (string.IsNullOrEmpty(fileName))
            return false;

        var match = StoredName.Match(fileName);
        if (!match.Success)
            return false;

        var hasTime = match.Groups["time"].Success;
        return DateTime.TryParseExact(
            match.Groups["date"].Value + match.Groups["time"].Value,
            hasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out startUtc);
    }

    internal static string MarkRemovedName(string fileName)
    {
        var match = StoredName.Match(fileName ?? string.Empty);
        if (match.Success)
        {
            // The date stays: it is how the user recognizes the meeting that went away.
            return $"{match.Groups["head"].Value} ({match.Groups["date"].Value}{match.Groups["time"].Value}, {RemovedTag})";
        }

        var suffix = $"({RemovedTag})";
        return string.IsNullOrEmpty(fileName)
            ? suffix
            : fileName.EndsWith(suffix, StringComparison.Ordinal) ? fileName : $"{fileName} {suffix}";
    }

    internal static string MarkRemovedPreview(string? preview)
    {
        if (string.IsNullOrWhiteSpace(preview))
            return RemovedPreview;

        if (preview.StartsWith(RemovedPreview, StringComparison.Ordinal))
            return preview;

        const string cancelled = "Cancelled, ";
        var rest = preview.StartsWith(cancelled, StringComparison.Ordinal) ? preview[cancelled.Length..] : preview;
        return $"{RemovedPreview}, {rest}";
    }

    internal static string MarkRemovedText(string? contentText, CalendarRemovalReason reason)
    {
        var statusLine = StatusLinePrefix + (reason == CalendarRemovalReason.Deleted
            ? "Deleted or cancelled in the calendar"
            : "No longer in the calendar's synced date range (deleted, or moved outside the range)");

        var text = contentText ?? string.Empty;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(newline).ToList();

        // The status line sits right under the title, where a cancellation is noted too.
        var at = lines[0].StartsWith("Title: ", StringComparison.Ordinal) ? 1 : 0;
        if (at < lines.Count && lines[at].StartsWith(StatusLinePrefix, StringComparison.Ordinal))
        {
            if (string.Equals(lines[at], statusLine, StringComparison.Ordinal))
                return text;

            lines[at] = statusLine;
        }
        else
        {
            lines.Insert(at, statusLine);
        }

        return string.Join(newline, lines);
    }

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
            ExternalId: BuildExternalId(calEvent),
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

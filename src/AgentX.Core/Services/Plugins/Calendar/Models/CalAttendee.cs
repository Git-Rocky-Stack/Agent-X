namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// Represents an attendee on a calendar event, including their response status.
/// </summary>
public sealed class CalAttendee
{
    /// <summary>
    /// Display name of the attendee (may be empty if only email is available).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Email address of the attendee.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The attendee's response to the invitation.
    /// Valid values: <c>"accepted"</c>, <c>"declined"</c>, <c>"tentative"</c>, <c>"needsAction"</c>.
    /// </summary>
    public string ResponseStatus { get; init; } = "needsAction";

    /// <summary>
    /// Whether this attendee is the event organizer.
    /// </summary>
    public bool IsOrganizer { get; init; }
}
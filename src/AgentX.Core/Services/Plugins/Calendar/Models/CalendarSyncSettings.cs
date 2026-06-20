namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// Per-plugin sync settings that control which calendars are synced and how.
/// Persisted to the plugin's data directory as JSON.
/// </summary>
public sealed class CalendarSyncSettings
{
    /// <summary>
    /// Map of calendar ID to enabled state. Only calendars with <c>true</c>
    /// are included in sync operations.
    /// </summary>
    public Dictionary<string, bool> EnabledCalendars { get; set; } = new();

    /// <summary>
    /// How often (in minutes) to poll for calendar changes.
    /// Default: 15 minutes.
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Number of days in the future to include when syncing events.
    /// Default: 30 days.
    /// </summary>
    public int DaysFutureToSync { get; set; } = 30;

    /// <summary>
    /// Number of days in the past to include when syncing events.
    /// Default: 90 days.
    /// </summary>
    public int DaysPastToSync { get; set; } = 90;

    /// <summary>
    /// Strategy for resolving conflicting events during sync.
    /// Valid values: <c>"LocalWins"</c>, <c>"RemoteWins"</c>, <c>"Merge"</c>.
    /// Default: <c>"RemoteWins"</c>.
    /// </summary>
    public string ConflictResolution { get; set; } = "RemoteWins";

    /// <summary>
    /// Whether to include attendee details (names, emails, response status)
    /// when syncing calendar events.
    /// </summary>
    public bool IncludeAttendeeDetails { get; set; } = true;

    /// <summary>
    /// Whether to include the full event description/body when syncing.
    /// </summary>
    public bool IncludeDescriptions { get; set; } = true;
}

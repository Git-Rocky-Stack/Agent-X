namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// Metadata about a calendar from an external provider, used to populate
/// the calendar selection UI where users choose which calendars to sync.
/// </summary>
public sealed class CalendarInfo
{
    /// <summary>
    /// Provider-specific calendar identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable calendar name (e.g. "Work", "Personal", "Holidays").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Display name or email of the calendar owner.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Approximate number of events in this calendar within the sync window.
    /// Used for display in the settings UI.
    /// </summary>
    public int EventCount { get; init; }

    /// <summary>
    /// Identifier of the provider this calendar belongs to:
    /// <c>"google"</c> or <c>"microsoft"</c>.
    /// </summary>
    public string SourceProvider { get; init; } = string.Empty;

    /// <summary>
    /// Whether this calendar is the user's primary calendar.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// UTC timestamp of the last successful sync for this calendar.
    /// Null if the calendar has never been synced.
    /// </summary>
    public DateTime? LastSyncedAt { get; init; }
}

using AgentX.Core.Services.Plugins.Calendar.Models;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// High-level calendar service exposed by the <see cref="CalendarPlugin"/>.
/// Provides upcoming event queries, full sync operations, and event detail retrieval.
/// </summary>
/// <remarks>
/// This is the public API that other AgentX services (search, RAG, Quick Chat) consume
/// to access calendar data. It abstracts over the individual provider implementations.
/// </remarks>
public interface ICalendarService
{
    /// <summary>
    /// Returns upcoming calendar events within the specified number of days ahead.
    /// Queries all enabled calendars across all connected providers.
    /// </summary>
    /// <param name="daysAhead">Number of days in the future to look. Default: 7.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of upcoming events, sorted by start time.</returns>
    Task<IReadOnlyList<CalEvent>> GetUpcomingEventsAsync(
        int daysAhead = 7,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a full sync cycle: fetches events from all enabled calendars across
    /// all connected providers and pushes new/updated items into the Smart Inbox pipeline.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated sync result across all providers and calendars.</returns>
    Task<SyncResult> SyncEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the full details of a specific calendar event by its provider-specific
    /// event ID and source provider.
    /// </summary>
    /// <param name="eventId">Provider-specific event identifier.</param>
    /// <param name="sourceProvider">Provider that owns the event (<c>"google"</c> or <c>"microsoft"</c>).</param>
    /// <param name="calendarId">Calendar that the event belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event with full details, or <c>null</c> if not found.</returns>
    Task<CalEvent?> GetEventDetailsAsync(
        string eventId,
        string sourceProvider,
        string calendarId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all calendars available from connected providers.
    /// Used by the settings UI to populate the calendar selection list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of calendar metadata.</returns>
    Task<IReadOnlyList<CalendarInfo>> ListAvailableCalendarsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether at least one calendar provider is connected (has valid OAuth credentials).
    /// </summary>
    Task<bool> IsConnectedAsync();

    /// <summary>
    /// Returns the current sync settings for the calendar connector.
    /// </summary>
    Task<CalendarSyncSettings> GetSyncSettingsAsync();

    /// <summary>
    /// Updates the sync settings and persists them to the plugin data directory.
    /// </summary>
    /// <param name="settings">New settings to apply.</param>
    Task UpdateSyncSettingsAsync(CalendarSyncSettings settings);
}
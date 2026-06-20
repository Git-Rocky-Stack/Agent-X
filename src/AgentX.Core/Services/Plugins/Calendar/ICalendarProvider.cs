using AgentX.Core.Services.Plugins.Calendar.Models;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// Abstraction over a specific calendar API provider (Google Calendar, Microsoft Outlook).
/// Implementations are registered per provider and use <see cref="OAuth.IOAuthService"/>
/// for authentication.
/// </summary>
/// <remarks>
/// Each provider maps its API-specific response format into the unified
/// <see cref="CalendarInfo"/> and <see cref="CalEvent"/> DTOs so that
/// <see cref="CalendarSyncService"/> can work with any provider uniformly.
/// </remarks>
public interface ICalendarProvider
{
    /// <summary>
    /// The provider identifier matching <see cref="OAuth.OAuthProviderConfig.ProviderId"/>
    /// (e.g. <c>"google"</c> or <c>"microsoft"</c>).
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Lists all calendars the authenticated user has read access to.
    /// Used to populate the calendar selection UI in settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the API request.</param>
    /// <returns>Read-only list of calendar metadata.</returns>
    Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches events from a specific calendar within the given time range.
    /// Supports incremental sync via <paramref name="deltaToken"/> when the provider
    /// supports it; pass <c>null</c> for a full sync.
    /// </summary>
    /// <param name="calendarId">Provider-specific calendar identifier.</param>
    /// <param name="start">Start of the time range (UTC).</param>
    /// <param name="end">End of the time range (UTC).</param>
    /// <param name="deltaToken">
    /// Opaque token from a previous sync for incremental changes.
    /// Null for full sync.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the API request.</param>
    /// <returns>
    /// A tuple of the fetched events and an optional new delta token for the next sync.
    /// </returns>
    Task<(IReadOnlyList<CalEvent> Events, string? DeltaToken)> GetEventsAsync(
        string calendarId,
        DateTime start,
        DateTime end,
        string? deltaToken = null,
        CancellationToken cancellationToken = default);
}

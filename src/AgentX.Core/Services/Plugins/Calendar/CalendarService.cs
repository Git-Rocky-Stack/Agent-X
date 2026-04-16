using System.Text.Json;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// Concrete implementation of <see cref="ICalendarService"/> that delegates
/// to the <see cref="CalendarPlugin"/> for provider iteration and sync orchestration.
/// </summary>
/// <remarks>
/// This service is registered in the plugin-scoped DI container during
/// <see cref="CalendarPlugin.InitializeAsync"/> so that other AgentX services
/// (search, RAG, Quick Chat) can resolve <see cref="ICalendarService"/> without
/// needing a direct reference to the plugin instance.
/// </remarks>
public sealed class CalendarService : ICalendarService
{
    private readonly CalendarPlugin _plugin;
    private readonly ILogger _log;

    /// <summary>
    /// Creates a new <see cref="CalendarService"/> bound to the given plugin instance.
    /// </summary>
    /// <param name="plugin">The owning <see cref="CalendarPlugin"/> that manages providers and sync.</param>
    /// <param name="logger">Serilog logger pre-enriched with calendar context.</param>
    public CalendarService(CalendarPlugin plugin, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _log = logger?.ForContext<CalendarService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalEvent>> GetUpcomingEventsAsync(
        int daysAhead = 7,
        CancellationToken cancellationToken = default)
    {
        var allEvents = new List<CalEvent>();
        var start = DateTime.UtcNow;
        var end = start.AddDays(daysAhead);

        foreach (var provider in _plugin.GetProviders())
        {
            try
            {
                var enabledCalendarIds = _plugin.GetSettings().EnabledCalendars
                    .Where(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();

                var calendars = await provider.ListCalendarsAsync(cancellationToken).ConfigureAwait(false);

                foreach (var calendar in calendars)
                {
                    if (!enabledCalendarIds.Contains(calendar.Id))
                        continue;

                    var (events, _) = await provider.GetEventsAsync(
                        calendar.Id, start, end,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    allEvents.AddRange(events);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Failed to fetch upcoming events from provider {ProviderId}",
                    provider.ProviderId);
            }
        }

        // Sort by start time ascending.
        var sorted = allEvents.OrderBy(e => e.Start).ToList();
        _log.Debug("Returning {Count} upcoming events across {Days} days", sorted.Count, daysAhead);
        return sorted;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncEventsAsync(CancellationToken cancellationToken = default)
    {
        _log.Information("Manual calendar sync triggered");
        var result = await _plugin.TriggerSyncAsync(cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            _log.Warning("Sync returned no result — sync may already be in progress");
            return new SyncResult
            {
                ItemsAdded = 0,
                ItemsUpdated = 0,
                ItemsSkipped = 0,
                ItemsFailed = 0,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            };
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<CalEvent?> GetEventDetailsAsync(
        string eventId,
        string sourceProvider,
        string calendarId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProvider);

        var provider = _plugin.GetProviders()
            .FirstOrDefault(p => p.ProviderId == sourceProvider);

        if (provider is null)
        {
            _log.Warning("No provider found for SourceProvider={SourceProvider}", sourceProvider);
            return null;
        }

        try
        {
            // Fetch events from the specific calendar and find the matching event.
            var start = DateTime.UtcNow.AddDays(-_plugin.GetSettings().DaysPastToSync);
            var end = DateTime.UtcNow.AddDays(_plugin.GetSettings().DaysFutureToSync);

            var (events, _) = await provider.GetEventsAsync(
                calendarId, start, end,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return events.FirstOrDefault(e => e.Id == eventId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex,
                "Failed to get event details for EventId={EventId} Provider={Provider}",
                eventId, sourceProvider);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarInfo>> ListAvailableCalendarsAsync(
        CancellationToken cancellationToken = default)
    {
        var allCalendars = new List<CalendarInfo>();

        foreach (var provider in _plugin.GetProviders())
        {
            try
            {
                var calendars = await provider.ListCalendarsAsync(cancellationToken).ConfigureAwait(false);
                allCalendars.AddRange(calendars);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Failed to list calendars from provider {ProviderId}",
                    provider.ProviderId);
            }
        }

        return allCalendars;
    }

    /// <inheritdoc />
    public async Task<bool> IsConnectedAsync()
    {
        var oauthService = _plugin.GetOAuthService();
        if (oauthService is null)
            return false;

        // Check both Google and Microsoft providers.
        var googleCred = await oauthService.GetCredentialAsync("google").ConfigureAwait(false);
        var msCred = await oauthService.GetCredentialAsync("microsoft").ConfigureAwait(false);

        return googleCred is not null || msCred is not null;
    }

    /// <inheritdoc />
    public Task<CalendarSyncSettings> GetSyncSettingsAsync()
    {
        return Task.FromResult(_plugin.GetSettings());
    }

    /// <inheritdoc />
    public async Task UpdateSyncSettingsAsync(CalendarSyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _plugin.UpdateSettingsAsync(settings).ConfigureAwait(false);
    }
}
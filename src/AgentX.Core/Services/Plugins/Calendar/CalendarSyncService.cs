using System.Text.Json;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins.Calendar.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// Orchestrates calendar sync cycles: fetches events from all registered providers,
/// converts them into inbox items via <see cref="CalendarEventProcessor"/>,
/// and pushes them into the Smart Inbox via <see cref="IInboxService.UpsertExternalAsync"/>,
/// which updates the existing item when an event was rescheduled, edited, or cancelled.
/// </summary>
/// <remarks>
/// This service is used by <see cref="CalendarPlugin.ExecuteSyncCycleAsync"/> to
/// perform the actual sync work. It encapsulates the event processing pipeline
/// so that the plugin can focus on lifecycle management.
/// </remarks>
public sealed class CalendarSyncService
{
    private static readonly JsonSerializerOptions DeltaTokenJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string DeltaTokenFileName = "calendar-delta-tokens.json";

    private readonly IInboxService _inboxService;
    private readonly CalendarEventProcessor _processor;
    private readonly ILogger _log;
    private readonly string _pluginDataPath;

    /// <summary>
    /// Creates a new <see cref="CalendarSyncService"/>.
    /// </summary>
    /// <param name="inboxService">The Smart Inbox service for triaging external items.</param>
    /// <param name="processor">The event processor for converting CalEvent → InboxItem.</param>
    /// <param name="logger">Serilog logger.</param>
    /// <param name="pluginDataPath">Path to the plugin's data directory for persisting delta tokens.</param>
    public CalendarSyncService(
        IInboxService inboxService,
        CalendarEventProcessor processor,
        ILogger logger,
        string pluginDataPath)
    {
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<CalendarSyncService>();
        _pluginDataPath = pluginDataPath ?? throw new ArgumentNullException(nameof(pluginDataPath));
    }

    /// <summary>
    /// Runs a full sync cycle across the given providers and enabled calendar settings.
    /// For each enabled calendar on each provider, fetches events, processes them through
    /// <see cref="CalendarEventProcessor"/>, and pushes them into the Smart Inbox.
    /// </summary>
    /// <param name="providers">The registered calendar providers to sync from.</param>
    /// <param name="settings">The sync settings controlling which calendars to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated sync result across all providers and calendars.</returns>
    public async Task<SyncResult> SyncAsync(
        IReadOnlyList<ICalendarProvider> providers,
        CalendarSyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var totalAdded = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var totalFailed = 0;
        var deltaTokens = await LoadDeltaTokensAsync().ConfigureAwait(false);
        var deltaTokensChanged = false;

        _log.Information(
            "Starting calendar sync across {ProviderCount} provider(s) with {EnabledCalendarCount} enabled calendar(s)",
            providers.Count,
            settings.EnabledCalendars.Count(kv => kv.Value));

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var enabledCalendarIds = settings.EnabledCalendars
                    .Where(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();

                if (enabledCalendarIds.Count == 0)
                {
                    _log.Debug("No enabled calendars for provider {ProviderId} — skipping", provider.ProviderId);
                    continue;
                }

                var calendars = await provider.ListCalendarsAsync(cancellationToken).ConfigureAwait(false);

                foreach (var calendar in calendars)
                {
                    if (!enabledCalendarIds.Contains(calendar.Id))
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    var deltaKey = $"{provider.ProviderId}:{calendar.Id}";
                    var existingDeltaToken = deltaTokens.GetValueOrDefault(deltaKey);

                    // One calendar failing (a share that was revoked, a transient server error)
                    // must not stop the remaining calendars from syncing.
                    try
                    {
                        var start = DateTime.UtcNow.AddDays(-settings.DaysPastToSync);
                        var end = DateTime.UtcNow.AddDays(settings.DaysFutureToSync);

                        var (events, newDeltaToken) = await provider.GetEventsAsync(
                            calendar.Id, start, end,
                            deltaToken: existingDeltaToken,
                            cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        var calendarFailures = 0;

                        // Process each event through the CalendarEventProcessor -> InboxService pipeline.
                        foreach (var calEvent in events)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                var (fileName, fileType, sourceType, sourceUrl,
                                     sourcePluginId, sourceCategory, externalId,
                                     contentPreview, contentText) = _processor.ConvertToInboxParameters(calEvent, settings);

                                var triage = await _inboxService.UpsertExternalAsync(
                                    fileName, fileType, sourceType, sourceUrl,
                                    sourcePluginId, sourceCategory, externalId,
                                    contentPreview, contentText).ConfigureAwait(false);

                                // The inbox reports what it did, so counts never depend on timestamps.
                                switch (triage.Outcome)
                                {
                                    case ExternalTriageOutcome.Created: totalAdded++; break;
                                    case ExternalTriageOutcome.Updated: totalUpdated++; break;
                                    default: totalSkipped++; break;
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                totalFailed++;
                                calendarFailures++;
                                _log.Error(ex,
                                    "Failed to process calendar event {EventId} from {ProviderId}/{CalendarId}",
                                    calEvent.Id, provider.ProviderId, calendar.Id);
                            }
                        }

                        if (calendarFailures > 0)
                        {
                            // Advancing the token would skip the failed events for good: an
                            // incremental read only reports changes made after the token.
                            // Keeping it re-reads them next cycle (unchanged ones are no-ops).
                            _log.Warning(
                                "{FailureCount} event(s) from {ProviderId}/{CalendarId} failed; keeping the previous sync position so they are retried",
                                calendarFailures, provider.ProviderId, calendar.Id);
                        }
                        else if (newDeltaToken is not null)
                        {
                            if (!string.Equals(newDeltaToken, existingDeltaToken, StringComparison.Ordinal))
                            {
                                deltaTokens[deltaKey] = newDeltaToken;
                                deltaTokensChanged = true;
                            }
                        }
                        else if (existingDeltaToken is not null)
                        {
                            // The provider returned no token after being given one: it rejected
                            // the stored token and read the calendar in full. Resending the
                            // rejected token every cycle would repeat that, so it is dropped.
                            deltaTokens.Remove(deltaKey);
                            deltaTokensChanged = true;
                        }

                        _log.Debug(
                            "Processed {EventCount} events from {ProviderId}/{CalendarId}",
                            events.Count, provider.ProviderId, calendar.Id);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        totalFailed++;
                        _log.Error(ex,
                            "Failed to sync calendar {CalendarId} from {ProviderId}",
                            calendar.Id, provider.ProviderId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                totalFailed++;
                _log.Error(ex,
                    "Failed to sync with provider {ProviderId}",
                    provider.ProviderId);
            }
        }

        // Persist the sync positions that moved (or were dropped) this cycle.
        if (deltaTokensChanged)
            await SaveDeltaTokensAsync(deltaTokens).ConfigureAwait(false);


        var completedAt = DateTime.UtcNow;

        var result = new SyncResult
        {
            ItemsAdded = totalAdded,
            ItemsUpdated = totalUpdated,
            ItemsSkipped = totalSkipped,
            ItemsFailed = totalFailed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };

        _log.Information(
            "Calendar sync complete. Added={Added} Updated={Updated} Skipped={Skipped} Failed={Failed} Duration={Duration}",
            result.ItemsAdded, result.ItemsUpdated, result.ItemsSkipped,
            result.ItemsFailed, result.Duration);

        return result;
    }

    // ── Private: delta token persistence ────────────────────────────────────────

    private async Task<Dictionary<string, string>> LoadDeltaTokensAsync()
    {
        var path = Path.Combine(_pluginDataPath, DeltaTokenFileName);

        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(json, DeltaTokenJsonOptions);
            return tokens ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load calendar delta tokens from {Path} — starting fresh", path);
            return new Dictionary<string, string>();
        }
    }

    private async Task SaveDeltaTokensAsync(Dictionary<string, string> tokens)
    {
        var path = Path.Combine(_pluginDataPath, DeltaTokenFileName);

        try
        {
            var json = JsonSerializer.Serialize(tokens, DeltaTokenJsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            _log.Debug("Saved {Count} calendar delta tokens to {Path}", tokens.Count, path);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save calendar delta tokens to {Path}", path);
        }
    }
}

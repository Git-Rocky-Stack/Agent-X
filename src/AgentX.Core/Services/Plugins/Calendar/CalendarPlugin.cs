using System.Text.Json;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Calendar;

/// <summary>
/// First-party DataConnector plugin that syncs Google Calendar and Microsoft Outlook
/// calendar events into the AgentX knowledge vault. Events flow through the Smart Inbox
/// pipeline and become searchable alongside documents.
/// </summary>
/// <remarks>
/// Lifecycle:
/// <list type="number">
///   <item><see cref="InitializeAsync"/> — resolves <see cref="IOAuthService"/> from the
///     plugin context, loads persisted sync settings, and registers provider implementations.</item>
///   <item><see cref="ActivateAsync"/> — starts the periodic sync timer.</item>
///   <item><see cref="DeactivateAsync"/> — stops the sync timer and flushes pending operations.</item>
/// </list>
/// </remarks>
public sealed class CalendarPlugin : IPlugin
{
    // ── IPlugin metadata ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string Id => "com.agentx.calendar";

    /// <inheritdoc />
    public string Name => "Calendar Connector";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "AgentX";

    /// <inheritdoc />
    public string Description => "Syncs Outlook and Google Calendar events into your knowledge vault for AI-powered search.";

    /// <inheritdoc />
    public PluginType Type => PluginType.DataConnector;

    // ── Private state ───────────────────────────────────────────────────────────

    private IPluginContext? _context;
    private IOAuthService? _oauthService;
    private IInboxService? _inboxService;
    private CalendarSyncService? _syncService;
    private CalendarEventProcessor? _eventProcessor;
    private ILogger? _log;
    private CalendarSyncSettings _syncSettings = new();
    private Timer? _syncTimer;
    private readonly List<ICalendarProvider> _providers = [];
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _isActivated;
    private bool _isDisposed;

    /// <summary>
    /// Event fired after each sync cycle completes. Subscribers (e.g. settings UI)
    /// can use this to update their display without polling.
    /// </summary>
    public event EventHandler<SyncResult>? SyncCompleted;

    /// <summary>
    /// The last sync result, or <c>null</c> if no sync has run yet.
    /// </summary>
    public SyncResult? LastSyncResult { get; private set; }

    // ── IPlugin: InitializeAsync ────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Resolves <see cref="IOAuthService"/> from the plugin context, loads persisted
    /// sync settings from the plugin data directory, and checks which providers
    /// have valid OAuth credentials to initialize <see cref="ICalendarProvider"/>
    /// implementations.
    /// Does NOT start background sync — that happens in <see cref="ActivateAsync"/>.
    /// </remarks>
    public async Task InitializeAsync(IPluginContext context)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _context = context ?? throw new ArgumentNullException(nameof(context));
        _log = context.Logger.ForContext<CalendarPlugin>();

        // Resolve IOAuthService from the scoped plugin container.
        _oauthService = context.Services.GetService(typeof(IOAuthService)) as IOAuthService;

        if (_oauthService is null)
        {
            _log.Warning(
                "IOAuthService not available in plugin context. " +
                "Calendar sync will not function until OAuth2 is configured.");
        }
        else
        {
            _log.Information("IOAuthService resolved successfully for CalendarPlugin");
        }

        // Resolve IInboxService for pushing events into the Smart Inbox.
        _inboxService = context.Services.GetService(typeof(IInboxService)) as IInboxService;

        if (_inboxService is null)
        {
            _log.Warning(
                "IInboxService not available in plugin context. " +
                "Calendar events will be fetched but not indexed until InboxService is available.");
        }
        else
        {
            _log.Information("IInboxService resolved successfully for CalendarPlugin");
        }

        // Load persisted sync settings from the plugin data directory.
        await LoadSyncSettingsAsync().ConfigureAwait(false);

        // Register providers based on available OAuth credentials.
        await RegisterProvidersAsync().ConfigureAwait(false);

        _log.Information(
            "CalendarPlugin initialized. Providers={ProviderCount} EnabledCalendars={EnabledCount}",
            _providers.Count,
            _syncSettings.EnabledCalendars.Count(kv => kv.Value));
    }

    // ── IPlugin: ActivateAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Starts the periodic sync timer. The interval is controlled by
    /// <see cref="CalendarSyncSettings.SyncIntervalMinutes"/>.
    /// </remarks>
    public Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isActivated)
        {
            _log?.Debug("CalendarPlugin is already activated — ActivateAsync is a no-op");
            return Task.CompletedTask;
        }

        _log?.Information(
            "Activating CalendarPlugin. SyncInterval={Interval}min",
            _syncSettings.SyncIntervalMinutes);

        StartSyncTimer();

        _isActivated = true;
        return Task.CompletedTask;
    }

    // ── IPlugin: DeactivateAsync ────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Stops the sync timer and waits for any in-progress sync to complete
    /// before returning. Persists the current sync settings.
    /// </remarks>
    public async Task DeactivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_isActivated)
        {
            _log?.Debug("CalendarPlugin is not activated — DeactivateAsync is a no-op");
            return;
        }

        _log?.Information("Deactivating CalendarPlugin — stopping sync timer");

        StopSyncTimer();

        // Wait for any in-progress sync to complete (with a 30-second timeout).
        if (!await _syncLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
        {
            _log?.Warning("Timed out waiting for in-progress sync to complete during deactivation");
        }
        else
        {
            _syncLock.Release();
        }

        // Persist current settings.
        await SaveSyncSettingsAsync().ConfigureAwait(false);

        _isActivated = false;
        _log?.Information("CalendarPlugin deactivated");
    }

    // ── IDisposable ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _syncTimer?.Dispose();
        _syncTimer = null;
        _syncLock.Dispose();

        _providers.Clear();
        _isDisposed = true;
    }

    // ── Internal: provider management ───────────────────────────────────────────

    /// <summary>
    /// Returns the currently registered calendar providers.
    /// Used by <see cref="ICalendarService"/> implementations to iterate providers.
    /// </summary>
    internal IReadOnlyList<ICalendarProvider> GetProviders() => _providers.AsReadOnly();

    /// <summary>
    /// Returns the current sync settings.
    /// </summary>
    internal CalendarSyncSettings GetSettings() => _syncSettings;

    /// <summary>
    /// Updates the sync settings and restarts the timer if the interval changed.
    /// </summary>
    internal async Task UpdateSettingsAsync(CalendarSyncSettings settings)
    {
        _syncSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        await SaveSyncSettingsAsync().ConfigureAwait(false);

        if (_isActivated)
        {
            // Restart the timer with the new interval.
            StopSyncTimer();
            StartSyncTimer();
        }

        _log?.Information("CalendarPlugin settings updated. SyncInterval={Interval}min", _syncSettings.SyncIntervalMinutes);
    }

    /// <summary>
    /// Adds a provider to the internal list. Called during initialization
    /// and when a new OAuth connection is established.
    /// </summary>
    internal void AddProvider(ICalendarProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (_providers.Any(p => p.ProviderId == provider.ProviderId))
        {
            _log?.Debug("Provider {ProviderId} already registered — replacing", provider.ProviderId);
            _providers.RemoveAll(p => p.ProviderId == provider.ProviderId);
        }

        _providers.Add(provider);
    }

    /// <summary>
    /// Removes a provider by its identifier. Called when a user disconnects
    /// an OAuth account.
    /// </summary>
    internal void RemoveProvider(string providerId)
    {
        var removed = _providers.RemoveAll(p => p.ProviderId == providerId);
        if (removed > 0)
            _log?.Information("Removed provider {ProviderId} from CalendarPlugin", providerId);
    }

    /// <summary>
    /// Returns the resolved IOAuthService, or null if unavailable.
    /// </summary>
    internal IOAuthService? GetOAuthService() => _oauthService;

    // ── Internal: sync orchestration ─────────────────────────────────────────────

    /// <summary>
    /// Triggers a manual sync cycle, regardless of the timer schedule.
    /// Thread-safe: if a sync is already running, this call is a no-op.
    /// </summary>
    internal async Task<SyncResult?> TriggerSyncAsync(CancellationToken cancellationToken = default)
    {
        // FU-2: WaitAsync(0) has identical semantics to Wait(0) (non-blocking
        // try-acquire) but is the analyzer-approved form in an async method.
        if (!await _syncLock.WaitAsync(0).ConfigureAwait(false))
        {
            _log?.Debug("Sync already in progress — TriggerSyncAsync is a no-op");
            return LastSyncResult;
        }

        try
        {
            var result = await ExecuteSyncCycleAsync(cancellationToken).ConfigureAwait(false);
            LastSyncResult = result;
            SyncCompleted?.Invoke(this, result);
            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ── Private: sync timer ─────────────────────────────────────────────────────

    private void StartSyncTimer()
    {
        var interval = TimeSpan.FromMinutes(_syncSettings.SyncIntervalMinutes);
        _syncTimer = new Timer(
            // FU-2: changed from async-void lambda to fire-and-forget on a
            // wrapper that catches exceptions. async-void in a Timer callback
            // would crash the process on any unhandled exception inside
            // OnSyncTimerTickAsync.
            callback: _ => _ = SafeOnSyncTimerTickAsync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1), // First sync 1 min after activation
            period: interval);

        _log?.Debug("Sync timer started. Interval={Interval}", interval);
    }

    private async Task SafeOnSyncTimerTickAsync()
    {
        try
        {
            await OnSyncTimerTickAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Error(ex, "Calendar sync timer callback faulted");
        }
    }

    private void StopSyncTimer()
    {
        if (_syncTimer is not null)
        {
            _syncTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _syncTimer.Dispose();
            _syncTimer = null;
            _log?.Debug("Sync timer stopped");
        }
    }

    private async Task OnSyncTimerTickAsync()
    {
        // Timer callbacks have no CancellationToken — use a default 5-minute timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        if (!await _syncLock.WaitAsync(0, cts.Token).ConfigureAwait(false))
        {
            _log?.Debug("Sync timer tick skipped — sync already in progress");
            return;
        }

        try
        {
            var result = await ExecuteSyncCycleAsync(cts.Token).ConfigureAwait(false);
            LastSyncResult = result;
            SyncCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            _log?.Warning("Sync cycle cancelled by timeout");
        }
        catch (Exception ex)
        {
            _log?.Error(ex, "Unexpected error during sync cycle");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ── Private: sync execution ─────────────────────────────────────────────────

    /// <summary>
    /// Executes one sync cycle across all enabled calendars and all registered providers.
    /// Delegates to <see cref="CalendarSyncService"/> for event processing and
    /// inbox triage. If <see cref="IInboxService"/> is not available, falls back to
    /// a fetch-only cycle that counts events without indexing them.
    /// </summary>
    private async Task<SyncResult> ExecuteSyncCycleAsync(CancellationToken cancellationToken)
    {
        // If InboxService is available, use the full sync pipeline.
        if (_inboxService is not null && _context is not null)
        {
            // Lazily create the sync service (depends on InboxService).
            _eventProcessor ??= new CalendarEventProcessor(_log!);
            _syncService ??= new CalendarSyncService(
                _inboxService, _eventProcessor, _log!, _context.PluginDataPath);

            return await _syncService.SyncAsync(_providers, _syncSettings, cancellationToken)
                .ConfigureAwait(false);
        }

        // Fallback: fetch-only cycle when InboxService is unavailable.
        return await FetchOnlySyncCycleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch-only sync cycle that counts events without processing them into the inbox.
    /// Used as a fallback when <see cref="IInboxService"/> is not available.
    /// </summary>
    private async Task<SyncResult> FetchOnlySyncCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var totalAdded = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var totalFailed = 0;
        string? lastDeltaToken = null;

        _log?.Information("Starting calendar sync cycle across {ProviderCount} provider(s)", _providers.Count);

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Only sync calendars that are enabled in settings.
                var enabledCalendarIds = _syncSettings.EnabledCalendars
                    .Where(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();

                if (enabledCalendarIds.Count == 0)
                {
                    _log?.Debug("No enabled calendars for provider {ProviderId} — skipping", provider.ProviderId);
                    continue;
                }

                var calendars = await provider.ListCalendarsAsync(cancellationToken).ConfigureAwait(false);

                foreach (var calendar in calendars)
                {
                    if (!enabledCalendarIds.Contains(calendar.Id))
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    var start = DateTime.UtcNow.AddDays(-_syncSettings.DaysPastToSync);
                    var end = DateTime.UtcNow.AddDays(_syncSettings.DaysFutureToSync);

                    var (events, deltaToken) = await provider.GetEventsAsync(
                        calendar.Id, start, end, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    lastDeltaToken = deltaToken;

                    // Fetch-only fallback: with no IInboxService available we count
                    // events without indexing them. The full event → inbox pipeline
                    // (CalendarEventProcessor → InboxService) runs in ExecuteSyncCycleAsync
                    // via CalendarSyncService whenever the inbox service is present.
                    totalSkipped += events.Count;

                    _log?.Debug(
                        "Fetched {EventCount} events from {ProviderId}/{CalendarId}",
                        events.Count, provider.ProviderId, calendar.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                totalFailed++;
                _log?.Error(ex,
                    "Failed to sync with provider {ProviderId}",
                    provider.ProviderId);
            }
        }

        var completedAt = DateTime.UtcNow;

        return new SyncResult
        {
            ItemsAdded = totalAdded,
            ItemsUpdated = totalUpdated,
            ItemsSkipped = totalSkipped,
            ItemsFailed = totalFailed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DeltaToken = lastDeltaToken,
        };
    }

    // ── Private: provider registration ──────────────────────────────────────────

    /// <summary>
    /// Checks which providers have valid OAuth credentials and registers
    /// their <see cref="ICalendarProvider"/> implementations.
    /// Called during initialization.
    /// </summary>
    private async Task RegisterProvidersAsync()
    {
        _providers.Clear();

        if (_oauthService is null)
            return;

        // Check Google credentials — register GoogleCalendarProvider if connected.
        var googleCred = await _oauthService.GetCredentialAsync("google").ConfigureAwait(false);
        if (googleCred is not null)
        {
            _log?.Information("Google OAuth credential found — registering GoogleCalendarProvider");
            AddProvider(new GoogleCalendarProvider(_oauthService, _log!));
        }

        // Check Microsoft credentials — register OutlookCalendarProvider if connected.
        var msCred = await _oauthService.GetCredentialAsync("microsoft").ConfigureAwait(false);
        if (msCred is not null)
        {
            _log?.Information("Microsoft OAuth credential found — registering OutlookCalendarProvider");
            AddProvider(new OutlookCalendarProvider(_oauthService, _log!));
        }
    }

    // ── Private: settings persistence ───────────────────────────────────────────

    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string SettingsFileName = "calendar-sync-settings.json";

    private async Task LoadSyncSettingsAsync()
    {
        if (_context is null)
            return;

        var settingsPath = Path.Combine(_context.PluginDataPath, SettingsFileName);

        if (!File.Exists(settingsPath))
        {
            _log?.Debug("No persisted calendar sync settings found — using defaults");
            _syncSettings = new CalendarSyncSettings();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<CalendarSyncSettings>(json, SettingsJsonOptions);

            if (settings is not null)
            {
                _syncSettings = settings;
                _log?.Information("Loaded calendar sync settings from {Path}", settingsPath);
            }
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Failed to load calendar sync settings from {Path} — using defaults", settingsPath);
            _syncSettings = new CalendarSyncSettings();
        }
    }

    private async Task SaveSyncSettingsAsync()
    {
        if (_context is null)
            return;

        var settingsPath = Path.Combine(_context.PluginDataPath, SettingsFileName);

        try
        {
            var json = JsonSerializer.Serialize(_syncSettings, SettingsJsonOptions);
            await File.WriteAllTextAsync(settingsPath, json).ConfigureAwait(false);
            _log?.Debug("Saved calendar sync settings to {Path}", settingsPath);
        }
        catch (Exception ex)
        {
            _log?.Error(ex, "Failed to save calendar sync settings to {Path}", settingsPath);
        }
    }
}
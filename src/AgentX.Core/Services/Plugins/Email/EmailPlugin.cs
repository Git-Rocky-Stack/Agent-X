using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Email Connector plugin. Implements the IPlugin lifecycle to provide
/// email sync capabilities from Gmail and Microsoft Outlook.
/// </summary>
public sealed class EmailPlugin : IPlugin
{
    private IPluginContext? _context;
    private IOAuthService? _oauthService;
    private IInboxService? _inboxService;
    private EmailSyncService? _syncService;
    private EmailTriageProcessor? _processor;
    private Timer? _syncTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    // Cancelled by DeactivateAsync and Dispose so a running sync stops at its next request
    // or message instead of holding deactivation (a settings save, app shutdown) until it ends.
    private CancellationTokenSource _lifetimeCts = new();
    private bool _isActivated;

    /// <summary>
    /// How long <see cref="DeactivateAsync"/> waits for a cancelled sync to stop.
    /// </summary>
    internal TimeSpan DeactivationWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    // ── IPlugin ─────────────────────────────────────────────────────────────────

    public string Id => "com.agentx.email";
    public string Name => "Email Connector";
    public string Description => "Syncs Gmail and Outlook emails into the knowledge vault.";
    public string Author => "AgentX";
    public PluginType Type => PluginType.DataConnector;
    public string Version => "1.0.0";

    // ── Internal state ─────────────────────────────────────────────────────────

    private readonly List<IEmailProvider> _providers = [];
    private EmailSyncSettings _settings = new();
    private string _dataPath = string.Empty;
    private ILogger _log = Log.ForContext<EmailPlugin>();
    private bool _isInitialized;
    private bool _isDisposed;

    // ── Public surface ──────────────────────────────────────────────────────────

    public IReadOnlyList<IEmailProvider> Providers => _providers.AsReadOnly();
    public event EventHandler<SyncResult>? SyncCompleted;
    public SyncResult? LastSyncResult { get; private set; }

    // ── IPlugin lifecycle ───────────────────────────────────────────────────────

    public Task InitializeAsync(IPluginContext context)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dataPath = context.PluginDataPath;
        Directory.CreateDirectory(_dataPath);
        _log = context.Logger.ForContext<EmailPlugin>();

        _oauthService = context.Services.GetService(typeof(IOAuthService)) as IOAuthService;
        _inboxService = context.Services.GetService(typeof(IInboxService)) as IInboxService;

        // Load persisted settings.
        var settingsPath = Path.Combine(_dataPath, "email-sync-settings.json");
        _settings = EmailSyncSettings.Load(settingsPath);

        _isInitialized = true;
        _log.Information("EmailPlugin initialized. DataPath={DataPath}", _dataPath);

        return Task.CompletedTask;
    }

    public async Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_isInitialized) throw new InvalidOperationException("EmailPlugin not initialized.");

        await RegisterProvidersAsync().ConfigureAwait(false);

        // Create the triage processor and sync service.
        _processor = new EmailTriageProcessor(_log);

        if (_inboxService is not null && _providers.Count > 0)
        {
            _syncService = new EmailSyncService(
                _inboxService, _processor, _log, _dataPath);
        }

        // Start periodic sync timer.
        // FU-2: fire-and-forget through a wrapper that catches exceptions —
        // async-void in a Timer callback crashes the process on faults.
        _syncTimer = new Timer(
            callback: _ => _ = SafeOnSyncTimerTickAsync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(_settings.SyncIntervalMinutes));

        _isActivated = true;

        _log.Information(
            "EmailPlugin activated. Providers={Count} SyncInterval={Min}m",
            _providers.Count, _settings.SyncIntervalMinutes);
    }

    /// <summary>
    /// Stops the sync timer, cancels a sync that is running and waits (at most
    /// <see cref="DeactivationWaitTimeout"/>) for it to stop.
    /// </summary>
    /// <remarks>
    /// Deactivation runs when sync is turned off, on every connector settings save (the
    /// connectors are restarted) and at app shutdown. It used to "flush" by running a full
    /// mail sync without cancellation and outside the sync lock, which made each of those
    /// start a sync and could hang shutdown. Nothing is pending between syncs, so there is
    /// nothing to flush.
    /// </remarks>
    public async Task DeactivateAsync()
    {
        _isActivated = false;

        // Wave 4a: DisposeAsync awaits any in-flight Timer callback before tearing
        // down the timer — prevents a race with the SafeOnSyncTimerTickAsync wrapper.
        if (_syncTimer is not null)
            await _syncTimer.DisposeAsync().ConfigureAwait(false);
        _syncTimer = null;

        var lifetime = _lifetimeCts;
        await lifetime.CancelAsync().ConfigureAwait(false);

        if (!await _syncLock.WaitAsync(DeactivationWaitTimeout).ConfigureAwait(false))
        {
            // The old source is left undisposed: the sync still running may hold a link to it.
            _log.Warning("Timed out waiting for the cancelled email sync to stop during deactivation");
            _lifetimeCts = new CancellationTokenSource();
        }
        else
        {
            // A fresh source for the next activation or manual sync.
            _lifetimeCts = new CancellationTokenSource();
            _syncLock.Release();
            lifetime.Dispose();
        }

        _log.Information("EmailPlugin deactivated");
    }

    // ── Public: settings access ────────────────────────────────────────────────

    /// <summary>
    /// Returns the current email sync settings (thread-safe snapshot).
    /// </summary>
    public EmailSyncSettings GetSettings()
    {
        // Return a copy so caller can't mutate internal state.
        var copy = new EmailSyncSettings
        {
            SyncIntervalMinutes = _settings.SyncIntervalMinutes,
            MaxMessagesPerSync = _settings.MaxMessagesPerSync,
            SyncDaysBack = _settings.SyncDaysBack,
            EnableAiCategorization = _settings.EnableAiCategorization,
            CategorizationPrompt = _settings.CategorizationPrompt,
            IncludeHtmlBody = _settings.IncludeHtmlBody,
            IncludeAttachmentNames = _settings.IncludeAttachmentNames,
        };
        foreach (var kv in _settings.EnabledFolders)
            copy.EnabledFolders[kv.Key] = kv.Value;
        return copy;
    }

    /// <summary>
    /// Updates the email sync settings and persists them to disk.
    /// </summary>
    public void UpdateSettings(EmailSyncSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var settingsPath = Path.Combine(_dataPath, "email-sync-settings.json");
        _settings.Save(settingsPath);
        _log.Information("Email sync settings updated and persisted");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _syncTimer?.Dispose();
        _syncTimer = null;
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _syncLock.Dispose();
        _providers.Clear();
        _log.Information("EmailPlugin disposed");
    }

    /// <summary>
    /// Providers for the accounts connected now, to list their folders. They are built from the
    /// current credentials rather than taken from the registered providers, which exist only
    /// while sync is on and only for the accounts connected when it was turned on; the settings
    /// page offers folders as soon as an account is connected.
    /// </summary>
    public async Task<IReadOnlyList<IEmailProvider>> GetProvidersForFolderListingAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return await CreateProvidersAsync().ConfigureAwait(false);
    }

    // ── Internal: provider registration ────────────────────────────────────────

    private async Task RegisterProvidersAsync()
    {
        _providers.Clear();
        _providers.AddRange(await CreateProvidersAsync().ConfigureAwait(false));

        foreach (var provider in _providers)
            _log.Information("Email provider {ProviderId} registered", provider.ProviderId);
    }

    /// <summary>A provider for each account that has a stored OAuth credential.</summary>
    private async Task<List<IEmailProvider>> CreateProvidersAsync()
    {
        var providers = new List<IEmailProvider>();

        if (_oauthService is null)
        {
            _log.Warning("IOAuthService not available — no email providers can be registered");
            return providers;
        }

        // Google provider if a credential exists.
        var googleCred = await _oauthService.GetCredentialAsync("google").ConfigureAwait(false);
        if (googleCred is not null)
        {
            var googleScopes = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/userinfo.profile";
            providers.Add(new GmailProvider(_oauthService, _log, googleScopes));
        }

        // Microsoft provider if a credential exists.
        var msCred = await _oauthService.GetCredentialAsync("microsoft").ConfigureAwait(false);
        if (msCred is not null)
        {
            var msScopes = "Mail.Read User.Read";
            providers.Add(new OutlookEmailProvider(_oauthService, _log, msScopes));
        }

        return providers;
    }

    // ── Internal: sync cycle ───────────────────────────────────────────────────

    private async Task SafeOnSyncTimerTickAsync()
    {
        try
        {
            await OnSyncTimerTickAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Email sync timer callback faulted");
        }
    }

    private async Task OnSyncTimerTickAsync()
    {
        if (!await _syncLock.WaitAsync(0).ConfigureAwait(false))
        {
            _log.Debug("Email sync timer tick skipped — sync already in progress");
            return;
        }

        try
        {
            // A tick that fired while the plugin was being deactivated has nothing to do.
            if (!_isActivated)
                return;

            // Timer callbacks have no CancellationToken: bound the cycle at 5 minutes, and let
            // deactivation cancel it sooner.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            var result = await ExecuteSyncCycleAsync(cts.Token).ConfigureAwait(false);
            LastSyncResult = result;
            SyncCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            _log.Debug("Email sync cycle cancelled (timeout or deactivation)");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Email sync cycle failed");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    internal async Task<SyncResult> TriggerSyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _log.Debug("Email sync already in progress — TriggerSyncAsync is a no-op");
            return LastSyncResult ?? CreateEmptyResult();
        }

        try
        {
            // Deactivation cancels a manual sync too.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
            var result = await ExecuteSyncCycleAsync(linked.Token).ConfigureAwait(false);
            LastSyncResult = result;
            SyncCompleted?.Invoke(this, result);
            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<SyncResult> ExecuteSyncCycleAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || _providers.Count == 0)
            return CreateEmptyResult();

        try
        {
            if (_syncService is not null)
            {
                var result = await _syncService.SyncAsync(
                    _providers, _settings, cancellationToken).ConfigureAwait(false);

                _log.Information(
                    "Email sync complete. Added={Added} Skipped={Skipped} Failed={Failed}",
                    result.ItemsAdded, result.ItemsSkipped, result.ItemsFailed);

                return result;
            }

            // Fetch-only fallback when InboxService is not available.
            return await FetchOnlySyncCycleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {

            _log.Error(ex, "Email sync cycle failed");
            return new SyncResult
            {
                ItemsFailed = 1,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            };
        }
    }

    /// <summary>
    /// Fallback: fetches messages without pushing to the Inbox pipeline.
    /// Used when IInboxService is not available in the plugin context.
    /// </summary>
    private async Task<SyncResult> FetchOnlySyncCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var skipped = 0;
        var failed = 0;

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var folders = await provider.ListFoldersAsync(cancellationToken).ConfigureAwait(false);
                skipped += folders.Count;
                _log.Debug(
                    "FetchOnly: {ProviderId} has {FolderCount} folders",
                    provider.ProviderId, folders.Count);
            }
            catch (Exception ex)
            {
                failed++;
                _log.Error(ex, "FetchOnly: failed to list folders for {ProviderId}", provider.ProviderId);
            }
        }

        return new SyncResult
        {
            ItemsSkipped = skipped,
            ItemsFailed = failed,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
        };
    }

    private static SyncResult CreateEmptyResult()
    {
        var now = DateTime.UtcNow;
        return new SyncResult
        {
            StartedAt = now,
            CompletedAt = now,
        };
    }
}

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
        _syncTimer = new Timer(
            callback: async _ => await OnSyncTimerTickAsync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(_settings.SyncIntervalMinutes));

        _log.Information(
            "EmailPlugin activated. Providers={Count} SyncInterval={Min}m",
            _providers.Count, _settings.SyncIntervalMinutes);
    }

    public async Task DeactivateAsync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;

        // Flush pending sync if possible.
        if (_syncService is not null && _providers.Count > 0)
        {
            try
            {
                await _syncService.SyncAsync(
                    _providers, _settings, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to flush email sync on deactivation");
            }
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
        _syncLock.Dispose();
        _providers.Clear();
        _log.Information("EmailPlugin disposed");
    }

    // ── Internal: provider registration ────────────────────────────────────────

    private async Task RegisterProvidersAsync()
    {
        _providers.Clear();

        if (_oauthService is null)
        {
            _log.Warning("IOAuthService not available — no email providers can be registered");
            return;
        }

        // Register Google provider if credential exists.
        var googleCred = await _oauthService.GetCredentialAsync("google").ConfigureAwait(false);
        if (googleCred is not null)
        {
            var googleScopes = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/userinfo.profile";
            _providers.Add(new GmailProvider(_oauthService, _log, googleScopes));
            _log.Information("GmailProvider registered");
        }

        // Register Microsoft provider if credential exists.
        var msCred = await _oauthService.GetCredentialAsync("microsoft").ConfigureAwait(false);
        if (msCred is not null)
        {
            var msScopes = "Mail.Read User.Read";
            _providers.Add(new OutlookEmailProvider(_oauthService, _log, msScopes));
            _log.Information("OutlookEmailProvider registered");
        }
    }

    // ── Internal: sync cycle ───────────────────────────────────────────────────

    private async Task OnSyncTimerTickAsync()
    {
        // Timer callbacks have no CancellationToken — use a default 5-minute timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        if (!await _syncLock.WaitAsync(0, cts.Token).ConfigureAwait(false))
        {
            _log.Debug("Email sync timer tick skipped — sync already in progress");
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
            _log.Debug("Email sync cycle cancelled (5-minute timeout)");
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
        catch (Exception ex)
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

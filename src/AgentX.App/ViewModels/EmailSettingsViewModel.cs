using AgentX.App.Services;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the Email Connector Settings page. Manages OAuth connection
/// state, folder selection, sync configuration, and manual sync triggering.
/// </summary>
public sealed partial class EmailSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IOAuthService _oauthService;
    private readonly IEmailService _emailService;
    private readonly IBuiltinConnectorLifecycleService _connectorLifecycle;
    private readonly ILogger _log;

    public EmailSettingsViewModel(
        ISettingsService settingsService,
        IOAuthService oauthService,
        IEmailService emailService,
        IBuiltinConnectorLifecycleService connectorLifecycle,
        ILogger logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _connectorLifecycle = connectorLifecycle ?? throw new ArgumentNullException(nameof(connectorLifecycle));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<EmailSettingsViewModel>();
    }

    // ── Observable properties ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private bool _isMicrosoftConnected;

    [ObservableProperty]
    private string _googleStatusText = "Not connected";

    [ObservableProperty]
    private string _microsoftStatusText = "Not connected";

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusText = "Not synced yet";

    [ObservableProperty]
    private string _lastSyncTime = "—";

    [ObservableProperty]
    private string _nextSyncTime = "—";

    [ObservableProperty]
    private bool _enableEmailSync;

    [ObservableProperty]
    private int _syncIntervalMinutes = 10;

    [ObservableProperty]
    private int _maxMessagesPerSync = 50;

    [ObservableProperty]
    private int _syncDaysBack = 30;

    [ObservableProperty]
    private bool _enableAiCategorization = true;

    [ObservableProperty]
    private bool _includeAttachmentNames = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Index into <see cref="SyncIntervalOptions"/> for the sync interval ComboBox.
    /// </summary>
    [ObservableProperty]
    private int _syncIntervalIndex = 1; // 10 min is index 1 in [5,10,15,30,60]

    /// <summary>
    /// Available sync interval options for the ComboBox.
    /// </summary>
    public List<int> SyncIntervalOptions { get; } = [5, 10, 15, 30, 60];

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

            EnableEmailSync = settings.EmailConnector.EnableEmailSync;
            SyncIntervalMinutes = settings.EmailConnector.SyncIntervalMinutes;
            MaxMessagesPerSync = settings.EmailConnector.MessagesPerSync;
            SyncDaysBack = settings.EmailConnector.DaysBackToSync;
            EnableAiCategorization = settings.EmailConnector.EnableAiCategorization;
            IncludeAttachmentNames = settings.EmailConnector.IncludeAttachmentMetadata;

            SyncIntervalIndex = SyncIntervalOptions.IndexOf(SyncIntervalMinutes);
            if (SyncIntervalIndex < 0) SyncIntervalIndex = 1;

            await CheckConnectionStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize EmailSettingsViewModel");
            HasError = true;
            ErrorMessage = $"Failed to load settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ConnectGoogleAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            _log.Information("Initiating Gmail OAuth2 connection");
            await _oauthService.AuthorizeAsync("google",
                scopes: "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/userinfo.profile")
                .ConfigureAwait(false);

            await CheckConnectionStatusAsync().ConfigureAwait(false);
            _log.Information("Gmail connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Gmail OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = "Connection cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Gmail");
            HasError = true;
            ErrorMessage = $"Failed to connect: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ConnectMicrosoftAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            _log.Information("Initiating Outlook Email OAuth2 connection");
            await _oauthService.AuthorizeAsync("microsoft",
                scopes: "Mail.Read User.Read")
                .ConfigureAwait(false);

            await CheckConnectionStatusAsync().ConfigureAwait(false);
            _log.Information("Outlook Email connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Outlook Email OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = "Connection cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Outlook Email");
            HasError = true;
            ErrorMessage = $"Failed to connect: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectGoogleAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            await _oauthService.RevokeAsync("google").ConfigureAwait(false);
            await CheckConnectionStatusAsync().ConfigureAwait(false);
            _log.Information("Gmail disconnected");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to disconnect Gmail");
            HasError = true;
            ErrorMessage = $"Failed to disconnect: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectMicrosoftAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            await _oauthService.RevokeAsync("microsoft").ConfigureAwait(false);
            await CheckConnectionStatusAsync().ConfigureAwait(false);
            _log.Information("Outlook Email disconnected");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to disconnect Outlook Email");
            HasError = true;
            ErrorMessage = $"Failed to disconnect: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            await PersistConnectorSettingsAsync(refreshLifecycle: true).ConfigureAwait(false);
            _log.Information("Email connector settings saved");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save email settings");
            HasError = true;
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing)
            return;

        IsSyncing = true;
        IsLoading = true;
        HasError = false;
        SyncStatusText = "Syncing...";

        try
        {
            EnableEmailSync = true;
            await PersistConnectorSettingsAsync(refreshLifecycle: true).ConfigureAwait(false);

            var result = await _emailService.SyncMessagesAsync().ConfigureAwait(false);
            LastSyncTime = FormatSyncTime(result.CompletedAt);
            SyncStatusText = FormatSyncResult(result);
            _log.Information(
                "Email manual sync completed. Added={Added} Updated={Updated} Skipped={Skipped} Failed={Failed}",
                result.ItemsAdded,
                result.ItemsUpdated,
                result.ItemsSkipped,
                result.ItemsFailed);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to run email sync");
            HasError = true;
            ErrorMessage = $"Failed to sync: {ex.Message}";
            SyncStatusText = "Sync failed";
        }
        finally
        {
            IsSyncing = false;
            IsLoading = false;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private async Task PersistConnectorSettingsAsync(bool refreshLifecycle)
    {
        var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

        settings.EmailConnector.EnableEmailSync = EnableEmailSync;
        settings.EmailConnector.SyncIntervalMinutes = SyncIntervalMinutes;
        settings.EmailConnector.MessagesPerSync = MaxMessagesPerSync;
        settings.EmailConnector.DaysBackToSync = SyncDaysBack;
        settings.EmailConnector.EnableAiCategorization = EnableAiCategorization;
        settings.EmailConnector.IncludeAttachmentMetadata = IncludeAttachmentNames;

        await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);

        var syncSettings = await _emailService.GetSyncSettingsAsync().ConfigureAwait(false);
        syncSettings.SyncIntervalMinutes = SyncIntervalMinutes;
        syncSettings.MaxMessagesPerSync = MaxMessagesPerSync;
        syncSettings.SyncDaysBack = SyncDaysBack;
        syncSettings.EnableAiCategorization = EnableAiCategorization;
        syncSettings.IncludeAttachmentNames = IncludeAttachmentNames;

        if (!syncSettings.EnabledFolders.Any(kv => kv.Value))
        {
            syncSettings.EnabledFolders["INBOX"] = true;
        }

        await _emailService.UpdateSyncSettingsAsync(syncSettings).ConfigureAwait(false);

        if (refreshLifecycle)
        {
            await _connectorLifecycle.RefreshAsync().ConfigureAwait(false);
        }
    }

    private async Task CheckConnectionStatusAsync()
    {
        var googleCred = await _oauthService.GetCredentialAsync("google").ConfigureAwait(false);
        IsGoogleConnected = googleCred is not null;
        GoogleStatusText = IsGoogleConnected ? "Connected" : "Not connected";

        var msCred = await _oauthService.GetCredentialAsync("microsoft").ConfigureAwait(false);
        IsMicrosoftConnected = msCred is not null;
        MicrosoftStatusText = IsMicrosoftConnected ? "Connected" : "Not connected";
    }

    // ── Reactive property changes ──────────────────────────────────────────────

    partial void OnSyncIntervalMinutesChanged(int value)
    {
        UpdateNextSyncTime();
    }

    partial void OnSyncIntervalIndexChanged(int value)
    {
        if (value >= 0 && value < SyncIntervalOptions.Count)
            SyncIntervalMinutes = SyncIntervalOptions[value];
    }

    private void UpdateNextSyncTime()
    {
        NextSyncTime = $"Every {SyncIntervalMinutes} min";
    }

    private static string FormatSyncTime(DateTime completedAt)
    {
        var timestamp = completedAt == default ? DateTime.UtcNow : completedAt;
        return timestamp.ToLocalTime().ToString("g");
    }

    private static string FormatSyncResult(SyncResult result)
    {
        return $"Added {result.ItemsAdded}, updated {result.ItemsUpdated}, skipped {result.ItemsSkipped}, failed {result.ItemsFailed}";
    }
}

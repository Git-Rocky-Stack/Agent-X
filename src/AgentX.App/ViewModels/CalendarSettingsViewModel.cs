using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the Calendar Connector Settings page. Manages OAuth connection
/// state, calendar selection, sync configuration, and manual sync triggering.
/// </summary>
public sealed partial class CalendarSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IOAuthService _oauthService;
    private readonly ICalendarService _calendarService;
    private readonly IBuiltinConnectorLifecycleService _connectorLifecycle;
    private readonly ILocalizationService _localization;
    private readonly ILogger _log;

    public CalendarSettingsViewModel(
        ISettingsService settingsService,
        IOAuthService oauthService,
        ICalendarService calendarService,
        IBuiltinConnectorLifecycleService connectorLifecycle,
        ILogger logger,
        ILocalizationService localization)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _calendarService = calendarService ?? throw new ArgumentNullException(nameof(calendarService));
        _connectorLifecycle = connectorLifecycle ?? throw new ArgumentNullException(nameof(connectorLifecycle));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<CalendarSettingsViewModel>();
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        var notConnected = DescribeConnection(null, _localization).StatusText;
        _googleStatusText = notConnected;
        _microsoftStatusText = notConnected;
        _syncStatusText = _localization.GetString("CalSet_NotSyncedYet");
    }

    // -- Observable properties --------------------------------------------------

    [ObservableProperty]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private bool _isMicrosoftConnected;

    [ObservableProperty]
    private string _googleStatusText;

    [ObservableProperty]
    private string _microsoftStatusText;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusText;

    [ObservableProperty]
    private string _lastSyncTime = "-";

    [ObservableProperty]
    private string _nextSyncTime = "-";

    [ObservableProperty]
    private bool _enableCalendarSync;

    [ObservableProperty]
    private int _syncIntervalMinutes = 15;

    [ObservableProperty]
    private int _daysPastToSync = 90;

    [ObservableProperty]
    private int _daysFutureToSync = 30;

    /// <summary>
    /// Index into <see cref="SyncIntervalOptions"/> for the sync interval ComboBox.
    /// </summary>
    [ObservableProperty]
    private int _syncIntervalIndex = 2; // 15 min is index 2 in [5,10,15,30,60]

    [ObservableProperty]
    private bool _includeAttendeeDetails = true;

    [ObservableProperty]
    private bool _includeDescriptions = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Available sync interval options for the ComboBox.
    /// </summary>
    public List<int> SyncIntervalOptions { get; } = [5, 10, 15, 30, 60];

    // -- Initialization ---------------------------------------------------------

    /// <summary>
    /// Loads current settings and checks OAuth connection status.
    /// Called from the page's Loaded event.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            var settings = await _settingsService.GetSettingsAsync();

            // Load calendar settings.
            EnableCalendarSync = settings.CalendarConnector.EnableCalendarSync;
            SyncIntervalMinutes = settings.CalendarConnector.SyncIntervalMinutes;
            DaysPastToSync = settings.CalendarConnector.DaysPastToSync;
            DaysFutureToSync = settings.CalendarConnector.DaysFutureToSync;
            IncludeAttendeeDetails = settings.CalendarConnector.IncludeAttendeeDetails;
            IncludeDescriptions = settings.CalendarConnector.IncludeDescriptions;

            // Set the ComboBox selected index.
            SyncIntervalIndex = SyncIntervalOptions.IndexOf(SyncIntervalMinutes);
            if (SyncIntervalIndex < 0) SyncIntervalIndex = 2; // default to 15 min

            // Check OAuth connection status.
            await CheckConnectionStatusAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize CalendarSettingsViewModel");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_LoadFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // -- Commands ---------------------------------------------------------------

    [RelayCommand]
    private async Task ConnectGoogleAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            _log.Information("Initiating Google Calendar OAuth2 connection");
            await _oauthService.AuthorizeAsync("google",
                scopes: "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/userinfo.profile");

            await CheckConnectionStatusAsync();
            _log.Information("Google Calendar connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Google Calendar OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_ConnectionCancelled");
        }
        catch (OAuthProviderNotConfiguredException ex)
        {
            _log.Warning(ex, "Google Calendar OAuth2 is not configured");
            HasError = true;
            ErrorMessage = _localization.GetString("OAuthApp_GoogleNotSetUp");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Google Calendar");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_ConnectFailed", ex.Message);
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
            _log.Information("Initiating Microsoft Outlook OAuth2 connection");
            // offline_access makes Microsoft issue a refresh token; without it the connector
            // loses access when the first access token expires.
            await _oauthService.AuthorizeAsync("microsoft",
                scopes: "offline_access Calendars.Read User.Read");

            await CheckConnectionStatusAsync();
            _log.Information("Microsoft Outlook Calendar connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Microsoft Outlook OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_ConnectionCancelled");
        }
        catch (OAuthProviderNotConfiguredException ex)
        {
            _log.Warning(ex, "Microsoft Outlook OAuth2 is not configured");
            HasError = true;
            ErrorMessage = _localization.GetString("OAuthApp_MicrosoftNotSetUp");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Microsoft Outlook Calendar");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_ConnectFailed", ex.Message);
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
            await _oauthService.RevokeAsync("google");
            await CheckConnectionStatusAsync();
            _log.Information("Google Calendar disconnected");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to disconnect Google Calendar");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_DisconnectFailed", ex.Message);
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
            await _oauthService.RevokeAsync("microsoft");
            await CheckConnectionStatusAsync();
            _log.Information("Microsoft Outlook Calendar disconnected");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to disconnect Microsoft Outlook Calendar");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_DisconnectFailed", ex.Message);
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
            await PersistConnectorSettingsAsync(refreshLifecycle: true);
            _log.Information("Calendar connector settings saved");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save calendar settings");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_SaveFailed", ex.Message);
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
        SyncStatusText = _localization.GetString("CalSet_Syncing");

        try
        {
            EnableCalendarSync = true;
            await PersistConnectorSettingsAsync(refreshLifecycle: true);

            var result = await _calendarService.SyncEventsAsync();
            LastSyncTime = FormatSyncTime(result.CompletedAt);
            SyncStatusText = FormatSyncResult(result);
            _log.Information(
                "Calendar manual sync completed. Added={Added} Updated={Updated} Skipped={Skipped} Failed={Failed}",
                result.ItemsAdded,
                result.ItemsUpdated,
                result.ItemsSkipped,
                result.ItemsFailed);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to run calendar sync");
            HasError = true;
            ErrorMessage = _localization.GetString("CalSet_SyncFailed", ex.Message);
            SyncStatusText = _localization.GetString("CalSet_SyncFailedStatus");
        }
        finally
        {
            IsSyncing = false;
            IsLoading = false;
        }
    }

    // -- Private helpers ---------------------------------------------------------

    private async Task PersistConnectorSettingsAsync(bool refreshLifecycle)
    {
        var settings = await _settingsService.GetSettingsAsync();

        // ConflictResolution is left as stored: calendar sync only imports, so nothing reads it.
        settings.CalendarConnector.EnableCalendarSync = EnableCalendarSync;
        settings.CalendarConnector.SyncIntervalMinutes = SyncIntervalMinutes;
        settings.CalendarConnector.DaysPastToSync = DaysPastToSync;
        settings.CalendarConnector.DaysFutureToSync = DaysFutureToSync;
        settings.CalendarConnector.IncludeAttendeeDetails = IncludeAttendeeDetails;
        settings.CalendarConnector.IncludeDescriptions = IncludeDescriptions;

        await _settingsService.SaveSettingsAsync(settings);

        var syncSettings = await _calendarService.GetSyncSettingsAsync();
        syncSettings.SyncIntervalMinutes = SyncIntervalMinutes;
        syncSettings.DaysPastToSync = DaysPastToSync;
        syncSettings.DaysFutureToSync = DaysFutureToSync;
        syncSettings.IncludeAttendeeDetails = IncludeAttendeeDetails;
        syncSettings.IncludeDescriptions = IncludeDescriptions;

        if (!syncSettings.EnabledCalendars.Any(kv => kv.Value))
        {
            var calendars = await _calendarService.ListAvailableCalendarsAsync();
            foreach (var calendar in calendars.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
            {
                syncSettings.EnabledCalendars[calendar.Id] = true;
            }
        }

        await _calendarService.UpdateSyncSettingsAsync(syncSettings);

        if (refreshLifecycle)
        {
            await _connectorLifecycle.RefreshAsync();
        }
    }

    private async Task CheckConnectionStatusAsync()
    {
        var googleCred = await _oauthService.GetCredentialAsync("google");
        (IsGoogleConnected, GoogleStatusText) = DescribeConnection(googleCred, _localization);

        var msCred = await _oauthService.GetCredentialAsync("microsoft");
        (IsMicrosoftConnected, MicrosoftStatusText) = DescribeConnection(msCred, _localization);
    }

    /// <summary>
    /// A stored credential without a refresh token stops working when its access token
    /// expires, so it is reported as needing a reconnect (the Connect button stays offered)
    /// rather than as connected.
    /// </summary>
    internal static (bool IsConnected, string StatusText) DescribeConnection(
        OAuthCredential? credential, ILocalizationService localization) =>
        credential switch
        {
            null => (false, localization.GetString("CalSet_StatusNotConnected")),
            { RequiresReauthorization: true } => (false, localization.GetString("CalSet_StatusReconnectRequired")),
            _ => (true, localization.GetString("CalSet_StatusConnected")),
        };

    // -- Reactive property changes ----------------------------------------------

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
        NextSyncTime = _localization.GetString("CalSet_EveryMinutes", SyncIntervalMinutes);
    }

    private static string FormatSyncTime(DateTime completedAt)
    {
        var timestamp = completedAt == default ? DateTime.UtcNow : completedAt;
        return timestamp.ToLocalTime().ToString("g");
    }

    private string FormatSyncResult(SyncResult result)
    {
        return _localization.GetString(
            "CalSet_SyncResult", result.ItemsAdded, result.ItemsUpdated, result.ItemsSkipped, result.ItemsFailed);
    }
}

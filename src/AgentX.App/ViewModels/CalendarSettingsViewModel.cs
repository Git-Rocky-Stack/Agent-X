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
    private readonly ILogger _log;

    public CalendarSettingsViewModel(
        ISettingsService settingsService,
        IOAuthService oauthService,
        ILogger logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<CalendarSettingsViewModel>();
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
    private int _syncIntervalMinutes = 15;

    [ObservableProperty]
    private int _daysPastToSync = 90;

    [ObservableProperty]
    private int _daysFutureToSync = 30;

    [ObservableProperty]
    private string _conflictResolution = "RemoteWins";

    /// <summary>
    /// Index into <see cref="SyncIntervalOptions"/> for the sync interval ComboBox.
    /// </summary>
    [ObservableProperty]
    private int _syncIntervalIndex = 2; // 15 min is index 2 in [5,10,15,30,60]

    /// <summary>
    /// Index into <see cref="ConflictResolutionOptions"/> for the conflict resolution ComboBox.
    /// </summary>
    [ObservableProperty]
    private int _conflictResolutionIndex;

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
    /// Available conflict resolution options for the ComboBox.
    /// </summary>
    public List<string> ConflictResolutionOptions { get; } = ["RemoteWins", "LocalWins", "Merge"];

    /// <summary>
    /// Available sync interval options for the ComboBox.
    /// </summary>
    public List<int> SyncIntervalOptions { get; } = [5, 10, 15, 30, 60];

    // ── Initialization ─────────────────────────────────────────────────────────

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
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

            // Load calendar settings.
            SyncIntervalMinutes = settings.CalendarConnector.SyncIntervalMinutes;
            DaysPastToSync = settings.CalendarConnector.DaysPastToSync;
            DaysFutureToSync = settings.CalendarConnector.DaysFutureToSync;
            ConflictResolution = settings.CalendarConnector.ConflictResolution;
            IncludeAttendeeDetails = settings.CalendarConnector.IncludeAttendeeDetails;
            IncludeDescriptions = settings.CalendarConnector.IncludeDescriptions;

            // Set ComboBox selected indices.
            SyncIntervalIndex = SyncIntervalOptions.IndexOf(SyncIntervalMinutes);
            if (SyncIntervalIndex < 0) SyncIntervalIndex = 2; // default to 15 min
            ConflictResolutionIndex = ConflictResolutionOptions.IndexOf(ConflictResolution);
            if (ConflictResolutionIndex < 0) ConflictResolutionIndex = 0;

            // Check OAuth connection status.
            await CheckConnectionStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize CalendarSettingsViewModel");
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
            _log.Information("Initiating Google Calendar OAuth2 connection");
            await _oauthService.AuthorizeAsync("google",
                scopes: "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/userinfo.profile")
                .ConfigureAwait(false);

            await CheckConnectionStatusAsync().ConfigureAwait(false);
            _log.Information("Google Calendar connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Google Calendar OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = "Connection cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Google Calendar");
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
            _log.Information("Initiating Microsoft Outlook OAuth2 connection");
            await _oauthService.AuthorizeAsync("microsoft",
                scopes: "Calendars.Read User.Read")
                .ConfigureAwait(false);

            await CheckConnectionStatusAsync().ConfigureAwait(false);
            _log.Information("Microsoft Outlook Calendar connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Microsoft Outlook OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = "Connection cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Microsoft Outlook Calendar");
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
            _log.Information("Google Calendar disconnected");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to disconnect Google Calendar");
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
            _log.Information("Microsoft Outlook Calendar disconnected");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to disconnect Microsoft Outlook Calendar");
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
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

            settings.CalendarConnector.SyncIntervalMinutes = SyncIntervalMinutes;
            settings.CalendarConnector.DaysPastToSync = DaysPastToSync;
            settings.CalendarConnector.DaysFutureToSync = DaysFutureToSync;
            settings.CalendarConnector.ConflictResolution = ConflictResolution;
            settings.CalendarConnector.IncludeAttendeeDetails = IncludeAttendeeDetails;
            settings.CalendarConnector.IncludeDescriptions = IncludeDescriptions;

            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);
            _log.Information("Calendar connector settings saved");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save calendar settings");
            HasError = true;
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

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

    partial void OnConflictResolutionIndexChanged(int value)
    {
        if (value >= 0 && value < ConflictResolutionOptions.Count)
            ConflictResolution = ConflictResolutionOptions[value];
    }

    private void UpdateNextSyncTime()
    {
        NextSyncTime = $"Every {SyncIntervalMinutes} min";
    }
}
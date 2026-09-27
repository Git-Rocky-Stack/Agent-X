using System.Collections.ObjectModel;
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

        Folders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFolders));
            OnPropertyChanged(nameof(ShowNoFoldersHint));
            OnPropertyChanged(nameof(IsFolderSelectionEmpty));
        };
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

    /// <summary>
    /// The mail folders of the connected accounts, and whether each is synced. The selection is
    /// saved per folder id and applies to every account with that id: Gmail and Outlook both
    /// report their inbox as <see cref="IEmailProvider.InboxFolderId"/>, so one entry stands for
    /// both inboxes.
    /// </summary>
    public ObservableCollection<EmailFolderSelectionItem> Folders { get; } = new();

    [ObservableProperty]
    private bool _isLoadingFolders;

    /// <summary>True when the connected accounts reported at least one folder.</summary>
    public bool HasFolders => Folders.Count > 0;

    /// <summary>
    /// True when there is no folder to show and none is being read, so the page can say how to
    /// get some without flashing that hint while the list loads.
    /// </summary>
    public bool ShowNoFoldersHint => !HasFolders && !IsLoadingFolders;

    /// <summary>True when folders are listed but none is selected, so a sync would read no mail.</summary>
    public bool IsFolderSelectionEmpty => Folders.Count > 0 && !Folders.Any(folder => folder.IsSelected);

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            var settings = await _settingsService.GetSettingsAsync();

            EnableEmailSync = settings.EmailConnector.EnableEmailSync;
            SyncIntervalMinutes = settings.EmailConnector.SyncIntervalMinutes;
            MaxMessagesPerSync = settings.EmailConnector.MessagesPerSync;
            SyncDaysBack = settings.EmailConnector.DaysBackToSync;
            IncludeAttachmentNames = settings.EmailConnector.IncludeAttachmentMetadata;

            SyncIntervalIndex = SyncIntervalOptions.IndexOf(SyncIntervalMinutes);
            if (SyncIntervalIndex < 0) SyncIntervalIndex = 1;

            await CheckConnectionStatusAsync();
            await RefreshFoldersAsync();
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
                scopes: "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/userinfo.profile");

            await CheckConnectionStatusAsync();
            await RefreshFoldersAsync();
            _log.Information("Gmail connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Gmail OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = "Connection cancelled.";
        }
        catch (OAuthProviderNotConfiguredException ex)
        {
            _log.Warning(ex, "Gmail OAuth2 is not configured");
            HasError = true;
            ErrorMessage = ex.UserGuidance;
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
            // offline_access makes Microsoft issue a refresh token; without it the connector
            // loses access when the first access token expires.
            await _oauthService.AuthorizeAsync("microsoft",
                scopes: "offline_access Mail.Read User.Read");

            await CheckConnectionStatusAsync();
            await RefreshFoldersAsync();
            _log.Information("Outlook Email connected successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Outlook Email OAuth2 flow cancelled by user");
            HasError = true;
            ErrorMessage = "Connection cancelled.";
        }
        catch (OAuthProviderNotConfiguredException ex)
        {
            _log.Warning(ex, "Outlook Email OAuth2 is not configured");
            HasError = true;
            ErrorMessage = ex.UserGuidance;
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
            await _oauthService.RevokeAsync("google");
            await CheckConnectionStatusAsync();
            await RefreshFoldersAsync();
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
            await _oauthService.RevokeAsync("microsoft");
            await CheckConnectionStatusAsync();
            await RefreshFoldersAsync();
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
            await PersistConnectorSettingsAsync(refreshLifecycle: true);
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
            await PersistConnectorSettingsAsync(refreshLifecycle: true);

            var result = await _emailService.SyncMessagesAsync();
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

    /// <summary>
    /// Lists the folders of the connected accounts. A folder already on the page keeps its check,
    /// even when that change is not saved yet; a folder new to the page shows its saved setting.
    /// When the accounts cannot be read the list stays as it was.
    /// </summary>
    [RelayCommand]
    private async Task RefreshFoldersAsync()
    {
        IsLoadingFolders = true;

        try
        {
            var syncSettings = await _emailService.GetSyncSettingsAsync();
            var folders = await _emailService.ListAvailableFoldersAsync();
            ApplyFolders(folders ?? [], syncSettings.EnabledFolders);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to list email folders");
        }
        finally
        {
            IsLoadingFolders = false;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the folder list with <paramref name="folders"/>: one entry per folder id, the
    /// inbox first and the rest by name.
    /// </summary>
    private void ApplyFolders(IReadOnlyList<EmailFolderInfo> folders, IReadOnlyDictionary<string, bool> savedSelection)
    {
        var shownSelection = Folders.ToDictionary(folder => folder.Id, folder => folder.IsSelected, StringComparer.Ordinal);

        var items = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .GroupBy(folder => folder.Id, StringComparer.Ordinal)
            .Select(reports =>
            {
                var isSelected = shownSelection.TryGetValue(reports.Key, out var shown)
                    ? shown
                    : savedSelection.TryGetValue(reports.Key, out var saved) && saved;
                return new EmailFolderSelectionItem(
                    reports.Key, ReadableName(reports), AccountNames(reports), isSelected, OnFolderSelectionChanged);
            })
            .OrderBy(item => item.Id == IEmailProvider.InboxFolderId ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Folders.Clear();
        foreach (var item in items)
            Folders.Add(item);
    }

    private void OnFolderSelectionChanged() => OnPropertyChanged(nameof(IsFolderSelectionEmpty));

    /// <summary>
    /// The folder's name, preferring one that is not all capitals: Gmail names its system labels
    /// by their ids ("INBOX"), while Outlook reports "Inbox" for the same shared entry.
    /// </summary>
    private static string ReadableName(IEnumerable<EmailFolderInfo> reports)
    {
        var names = reports.Select(folder => string.IsNullOrWhiteSpace(folder.Name) ? folder.Id : folder.Name).ToList();
        return names.FirstOrDefault(name => name.Any(char.IsLower)) ?? names[0];
    }

    /// <summary>The accounts that report a folder, by product name ("Gmail", "Outlook").</summary>
    private static string AccountNames(IEnumerable<EmailFolderInfo> reports) =>
        string.Join(", ", reports
            .Select(folder => folder.SourceProvider switch
            {
                "google" => "Gmail",
                "microsoft" => "Outlook",
                var other => other,
            })
            .Distinct(StringComparer.Ordinal));

    private async Task PersistConnectorSettingsAsync(bool refreshLifecycle)
    {
        var settings = await _settingsService.GetSettingsAsync();

        settings.EmailConnector.EnableEmailSync = EnableEmailSync;
        settings.EmailConnector.SyncIntervalMinutes = SyncIntervalMinutes;
        settings.EmailConnector.MessagesPerSync = MaxMessagesPerSync;
        settings.EmailConnector.DaysBackToSync = SyncDaysBack;
        settings.EmailConnector.IncludeAttachmentMetadata = IncludeAttachmentNames;

        await _settingsService.SaveSettingsAsync(settings);

        var syncSettings = await _emailService.GetSyncSettingsAsync();
        syncSettings.SyncIntervalMinutes = SyncIntervalMinutes;
        syncSettings.MaxMessagesPerSync = MaxMessagesPerSync;
        syncSettings.SyncDaysBack = SyncDaysBack;
        syncSettings.IncludeAttachmentNames = IncludeAttachmentNames;

        if (Folders.Count > 0)
        {
            // Each listed folder is saved as checked, an empty selection included (the page warns
            // about it). A folder not listed now, such as one of an account that could not be
            // reached, keeps its saved setting.
            foreach (var folder in Folders)
                syncSettings.EnabledFolders[folder.Id] = folder.IsSelected;
        }
        else if (!syncSettings.EnabledFolders.Any(kv => kv.Value))
        {
            // No folders to choose from yet: the inbox, as by default.
            syncSettings.EnabledFolders[IEmailProvider.InboxFolderId] = true;
        }

        await _emailService.UpdateSyncSettingsAsync(syncSettings);

        if (refreshLifecycle)
        {
            await _connectorLifecycle.RefreshAsync();
        }
    }

    private async Task CheckConnectionStatusAsync()
    {
        var googleCred = await _oauthService.GetCredentialAsync("google");
        (IsGoogleConnected, GoogleStatusText) = DescribeConnection(googleCred);

        var msCred = await _oauthService.GetCredentialAsync("microsoft");
        (IsMicrosoftConnected, MicrosoftStatusText) = DescribeConnection(msCred);
    }

    /// <summary>
    /// A stored credential without a refresh token stops working when its access token
    /// expires, so it is reported as needing a reconnect (the Connect button stays offered)
    /// rather than as connected.
    /// </summary>
    internal static (bool IsConnected, string StatusText) DescribeConnection(OAuthCredential? credential) =>
        credential switch
        {
            null => (false, "Not connected"),
            { RequiresReauthorization: true } => (false, "Reconnect required"),
            _ => (true, "Connected"),
        };

    // ── Reactive property changes ──────────────────────────────────────────────

    partial void OnSyncIntervalMinutesChanged(int value)
    {
        UpdateNextSyncTime();
    }

    partial void OnIsLoadingFoldersChanged(bool value) => OnPropertyChanged(nameof(ShowNoFoldersHint));

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

/// <summary>
/// One mail folder on the Email settings page: its id, a readable name, the accounts that have
/// it, and whether it is synced.
/// </summary>
public sealed partial class EmailFolderSelectionItem : ObservableObject
{
    private readonly Action _selectionChanged;

    public EmailFolderSelectionItem(string id, string name, string accounts, bool isSelected, Action selectionChanged)
    {
        Id = id;
        Name = name;
        Accounts = accounts;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    /// <summary>The folder id the sync settings are keyed by.</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>The accounts that have this folder, such as "Gmail" or "Gmail, Outlook".</summary>
    public string Accounts { get; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

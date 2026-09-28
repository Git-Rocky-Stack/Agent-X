using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.App.ViewModels.Sync;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

// =============================================================================
// SYNC SETTINGS VIEW MODEL
//
// Manages the Collaborative Sync settings page. Allows the user to configure
// an encrypted sync folder, an encryption passphrase, auto-sync scheduling,
// and sync scope. Drives a manual "Sync Now" pass (ISyncService.SyncNowAsync:
// export, then import every peer file) and reports what really happened.
// Exposes discrete Start and Stop commands for the background auto-sync loop;
// both persist the toggle before acting on the loop, and the loop belongs to the
// service (it keeps running after the page is closed). Loads paginated sync
// history via LoadHistoryAsync. The View owns the native folder picker and writes
// the chosen path straight into SyncFolderPath.
//
// Constructor accepts ISyncService via DI. All long-running paths are guarded
// by IsLoading / IsSyncing flags and surfaced through the SetError / SetStatus
// / ClearError / ClearStatus helpers that the View binds to its notification
// strip.
// =============================================================================

public partial class SyncSettingsViewModel : ObservableObject, IDisposable
{
    // -- Services --------------------------------------------------------------

    private readonly ISyncService _syncService;
    private readonly ICollectionService _collectionService;
    private readonly ILocalizationService _localization;
    private readonly IOperationsDrillInService? _operationsDrillInService;

    // -- Page State ------------------------------------------------------------

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private long _focusedSyncLogId;
    [ObservableProperty] private string _focusedSyncSourceLabel = string.Empty;

    // -- Configuration Fields -------------------------------------------------

    /// <summary>
    /// Absolute path to the shared sync folder (OneDrive, Google Drive, NAS, USB, etc.).
    /// Must be read/write accessible by the application.
    /// </summary>
    [ObservableProperty] private string _syncFolderPath = string.Empty;

    /// <summary>
    /// User-supplied passphrase used to derive the AES-256 key via PBKDF2.
    /// Never written to disk in plain text beyond the settings store.
    /// </summary>
    [ObservableProperty] private string _encryptionKey = string.Empty;

    /// <summary>Whether the background auto-sync loop should be kept active.</summary>
    [ObservableProperty] private bool _autoSyncEnabled;

    /// <summary>
    /// Auto-sync polling interval in minutes, held as a string so it binds
    /// directly to a TextBox without requiring a converter. Validated as a
    /// positive integer in SaveConfigurationAsync before being stored.
    /// </summary>
    [ObservableProperty] private string _syncIntervalMinutes = "15";

    /// <summary>
    /// Which entities are included in a sync export.
    /// "All" - every supported entity type.
    /// "SelectedCollections" - only the collections listed in SelectedCollectionIds.
    /// </summary>
    [ObservableProperty] private string _syncScope = "All";

    /// <summary>
    /// Comma-separated collection IDs to include when SyncScope is
    /// "SelectedCollections". Null or empty when SyncScope is "All".
    /// </summary>
    [ObservableProperty] private string? _selectedCollectionIds;

    // -- Sync Status Fields ----------------------------------------------------

    /// <summary>The current sync state in the user's language, e.g. "Idle".</summary>
    [ObservableProperty] private string _syncState;

    /// <summary>
    /// Typed mirror of <see cref="SyncState"/> so the view can pick LED tones
    /// from the enum instead of keying off a display string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStateTone))]
    private AgentX.Core.Services.Sync.Models.SyncState _currentSyncState =
        AgentX.Core.Services.Sync.Models.SyncState.Idle;

    /// <summary>Relative timestamp of the last successful sync pass, e.g. "3m ago".</summary>
    [ObservableProperty] private string _lastSyncAt;

    /// <summary>Number of locally-originated changes exported but not yet confirmed received by a peer.</summary>
    [ObservableProperty] private int _pendingChanges;

    /// <summary>Formatted wall-clock duration of the most recent sync pass, e.g. "1.4s".</summary>
    [ObservableProperty] private string _lastSyncDurationMs = "--";

    // -- Interval Options ----------------------------------------------------

    /// <summary>
    /// Display strings for the sync interval dropdown. Indexes map to
    /// { 5, 15, 30, 60, 120 } minutes respectively.
    /// </summary>
    public List<string> IntervalOptions { get; }

    /// <summary>
    /// Currently selected index in <see cref="IntervalOptions"/>.
    /// Defaults to 1 (Every 15 minutes).
    /// </summary>
    [ObservableProperty] private int _selectedIntervalIndex = 1;

    /// <summary>The stored <see cref="SyncScope"/> values, in the order of <see cref="SyncScopeOptions"/>.</summary>
    private static readonly string[] SyncScopeValues = { "All", "SelectedCollections" };

    /// <summary>
    /// Display strings for the sync scope dropdown, one per entry of <see cref="SyncScopeValues"/>.
    /// The dropdown binds <see cref="SelectedSyncScopeIndex"/>, so the labels are shown in the
    /// user's language while <see cref="SyncScope"/> keeps the stored value.
    /// </summary>
    public List<string> SyncScopeOptions { get; }

    /// <summary>Index of <see cref="SyncScope"/> in <see cref="SyncScopeOptions"/>, bound two-way by the dropdown.</summary>
    public int SelectedSyncScopeIndex
    {
        get => Array.IndexOf(SyncScopeValues, SyncScope);
        set
        {
            if (value >= 0 && value < SyncScopeValues.Length)
            {
                SyncScope = SyncScopeValues[value];
            }
        }
    }

    public ObservableCollection<SyncCollectionSelectionItem> AvailableCollections { get; } = new();

    // -- Sync History ----------------------------------------------------------

    /// <summary>
    /// Ordered newest-first; populated by LoadHistoryAsync.
    /// Bound to the history ItemsControl / ListView in the View.
    /// </summary>
    public ObservableCollection<SyncHistoryItem> SyncHistory { get; } = new();

    // -- Computed Properties ---------------------------------------------------

    /// <summary>
    /// True when both a sync folder path and an encryption key have been supplied.
    /// Controls whether configuration-dependent sections of the page are active.
    /// </summary>
    public bool HasConfiguration =>
        !string.IsNullOrWhiteSpace(SyncFolderPath) &&
        !string.IsNullOrWhiteSpace(EncryptionKey);

    /// <summary>
    /// True when a manual sync can be launched: the service is configured and
    /// no sync pass is currently running.
    /// </summary>
    public bool CanSync => HasConfiguration && !IsSyncing;

    /// <summary>Alias for <see cref="HasConfiguration"/> used by SyncSettingsPage.xaml.</summary>
    public bool IsConfigured => HasConfiguration;

    /// <summary>Alias for <see cref="SyncState"/> used by SyncSettingsPage.xaml.</summary>
    public string SyncStateDisplay => SyncState;

    /// <summary>
    /// Status token for the header badge dot, mapped to a brush by StatusToColorConverter. The
    /// converter reads English status words, so the dot follows <see cref="CurrentSyncState"/>
    /// rather than the translated <see cref="SyncState"/>.
    /// </summary>
    public string SyncStateTone => CurrentSyncState switch
    {
        AgentX.Core.Services.Sync.Models.SyncState.Idle => "idle",
        AgentX.Core.Services.Sync.Models.SyncState.Syncing => "syncing",
        AgentX.Core.Services.Sync.Models.SyncState.Error => "error",
        AgentX.Core.Services.Sync.Models.SyncState.Conflict => "conflict",
        _ => "unknown"
    };

    /// <summary>Alias for <see cref="HasStatusMessage"/> used by SyncSettingsPage.xaml.</summary>
    public bool HasSuccess => HasStatusMessage;

    /// <summary>Alias for <see cref="StatusMessage"/> used by SyncSettingsPage.xaml.</summary>
    public string SuccessMessage => StatusMessage;

    /// <summary>Alias for <see cref="LastSyncDurationMs"/> used by SyncSettingsPage.xaml.</summary>
    public string LastSyncDuration => LastSyncDurationMs;

    /// <summary>True when the sync history collection contains at least one entry.</summary>
    public bool HasSyncHistory => SyncHistory.Count > 0;
    public bool HasFocusedSyncLanding => !string.IsNullOrWhiteSpace(FocusedSyncSourceLabel);
    public bool ShowSelectedCollectionsPicker => SyncScope == "SelectedCollections";
    public bool HasAvailableCollections => AvailableCollections.Count > 0;
    public string SelectedCollectionSummary
    {
        get
        {
            var selectedCount = AvailableCollections.Count(collection => collection.IsSelected);
            return selectedCount switch
            {
                0 => _localization.GetString("Sync_CollectionsSelectedNone"),
                1 => _localization.GetString("Sync_CollectionsSelectedOne"),
                _ => _localization.GetString("Sync_CollectionsSelectedMany", selectedCount)
            };
        }
    }

    // -- Constructor -----------------------------------------------------------

    public SyncSettingsViewModel(
        ISyncService syncService,
        ICollectionService collectionService,
        ILocalizationService localization,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _syncService = syncService;
        _collectionService = collectionService;
        _localization = localization;
        _operationsDrillInService = operationsDrillInService;

        _syncState = DescribeSyncState(AgentX.Core.Services.Sync.Models.SyncState.Idle);
        _lastSyncAt = _localization.GetString("Sync_LastSyncNever");
        IntervalOptions = new List<string>
        {
            _localization.GetString("Sync_IntervalEvery5Minutes"),
            _localization.GetString("Sync_IntervalEvery15Minutes"),
            _localization.GetString("Sync_IntervalEvery30Minutes"),
            _localization.GetString("Sync_IntervalEveryHour"),
            _localization.GetString("Sync_IntervalEvery2Hours")
        };
        SyncScopeOptions = new List<string>
        {
            _localization.GetString("Sync_ScopeAll"),
            _localization.GetString("Sync_ScopeSelectedCollections")
        };

        Log.Debug("SyncSettingsViewModel created");
    }

    // =========================================================================
    // INITIALIZATION
    // Called from the page's OnPageLoaded handler.
    // =========================================================================

    public async Task InitializeAsync()
    {
        Log.Information("SyncSettingsViewModel initializing...");

        IsLoading = true;
        ClearError();
        ClearStatus();

        try
        {
            await LoadConfigurationAsync();
            await LoadAvailableCollectionsAsync();
            RefreshStatusFromService();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SyncSettingsViewModel initialization failed");
            SetError(_localization.GetString("Sync_LoadFailed"));
        }
        finally
        {
            IsLoading = false;
        }

        Log.Information("SyncSettingsViewModel initialized");
    }

    // =========================================================================
    // LOAD CONFIGURATION (private, called from InitializeAsync)
    // Reads the stored SyncConfiguration and populates all bound fields.
    // =========================================================================

    private async Task LoadConfigurationAsync()
    {
        try
        {
            var config = await _syncService.GetConfigurationAsync();

            if (config is not null)
            {
                SyncFolderPath = config.SyncFolderPath ?? string.Empty;
                EncryptionKey = config.EncryptionKey ?? string.Empty;
                SetAutoSyncEnabledSilently(config.AutoSyncEnabled);
                SyncIntervalMinutes = config.SyncIntervalMinutes.ToString();
                SyncScope = config.SyncScope == AgentX.Core.Services.Sync.Models.SyncScope.SelectedCollections
                    ? "SelectedCollections"
                    : "All";
                SelectedCollectionIds = config.SelectedCollectionIds;

                // Map the stored interval in minutes back to the dropdown index
                SelectedIntervalIndex = config.SyncIntervalMinutes switch
                {
                    5 => 0,
                    15 => 1,
                    30 => 2,
                    60 => 3,
                    120 => 4,
                    _ => 1  // default to "Every 15 minutes" for non-standard values
                };

                Log.Debug(
                    "Sync configuration loaded: HasConfiguration={Has}, AutoSync={Auto}, Interval={Interval}min, Scope={Scope}",
                    HasConfiguration, AutoSyncEnabled, config.SyncIntervalMinutes, SyncScope);
            }
            else
            {
                Log.Debug("No sync configuration found - first-time setup");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load sync configuration");
        }

        NotifyComputedProperties();
    }

    private async Task LoadAvailableCollectionsAsync()
    {
        try
        {
            var collections = await _collectionService.GetAllCollectionsAsync();
            var selectedIds = ParseSelectedCollectionIds(SelectedCollectionIds);

            AvailableCollections.Clear();
            foreach (var collection in collections.OrderBy(collection => collection.SortOrder).ThenBy(collection => collection.Name))
            {
                var detailLabel = collection.DocumentCount == 1
                    ? _localization.GetString("Sync_CollectionDocumentCountOne")
                    : _localization.GetString("Sync_CollectionDocumentCountMany", collection.DocumentCount);
                AvailableCollections.Add(new SyncCollectionSelectionItem(
                    collection.Id,
                    collection.Name,
                    collection.DocumentCount,
                    detailLabel,
                    selectedIds.Contains(collection.Id),
                    UpdateSelectedCollectionIdsFromSelections));
            }

            UpdateSelectedCollectionIdsFromSelections();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load collections for sync scope selection");
            AvailableCollections.Clear();
            OnPropertyChanged(nameof(HasAvailableCollections));
            OnPropertyChanged(nameof(SelectedCollectionSummary));
        }
    }

    // =========================================================================
    // REFRESH STATUS (private helper)
    // Reads the live SyncStatus from the service and updates all display fields.
    // Synchronous because SyncStatus is a thread-safe value property.
    // =========================================================================

    private void RefreshStatusFromService()
    {
        try
        {
            var status = _syncService.Status;

            ShowSyncState(status.SyncState);

            PendingChanges = status.PendingChanges;

            LastSyncAt = status.LastSyncAt.HasValue
                ? FormatHelper.TimeAgoWithMonths(status.LastSyncAt.Value)
                : _localization.GetString("Sync_LastSyncNever");

            LastSyncDurationMs = status.LastSyncDurationMs > 0
                ? FormatHelper.FormatDuration(status.LastSyncDurationMs)
                : "--";

            IsSyncing = status.SyncState == AgentX.Core.Services.Sync.Models.SyncState.Syncing;

            // Surface a persistent service-level error into the error banner
            if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
                SetError(status.ErrorMessage);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh sync status display fields");
        }
    }

    // =========================================================================
    // COMMAND: SaveConfigurationAsync
    // Validates all fields, builds a SyncConfiguration, persists it via the
    // service, and reconciles the auto-sync loop state with the toggle.
    // =========================================================================

    [RelayCommand]
    private async Task SaveConfigurationAsync()
    {
        UpdateSelectedCollectionIdsFromSelections();

        if (string.IsNullOrWhiteSpace(SyncFolderPath))
        {
            SetError(_localization.GetString("Sync_FolderRequired"));
            return;
        }

        if (string.IsNullOrWhiteSpace(EncryptionKey))
        {
            SetError(_localization.GetString("Sync_KeyRequired"));
            return;
        }

        if (!int.TryParse(SyncIntervalMinutes?.Trim(), out int intervalMinutes) || intervalMinutes < 1)
        {
            SetError(_localization.GetString("Sync_IntervalInvalid"));
            return;
        }

        // If the user picked an interval from the dropdown, override with the mapped value
        int[] intervalMap = { 5, 15, 30, 60, 120 };
        if (SelectedIntervalIndex >= 0 && SelectedIntervalIndex < intervalMap.Length)
        {
            intervalMinutes = intervalMap[SelectedIntervalIndex];
            SyncIntervalMinutes = intervalMinutes.ToString();
        }

        var scope = SyncScope == "SelectedCollections"
            ? AgentX.Core.Services.Sync.Models.SyncScope.SelectedCollections
            : AgentX.Core.Services.Sync.Models.SyncScope.All;

        if (scope == AgentX.Core.Services.Sync.Models.SyncScope.SelectedCollections &&
            string.IsNullOrWhiteSpace(SelectedCollectionIds))
        {
            SetError(_localization.GetString("Sync_SelectCollectionRequired"));
            return;
        }

        Log.Information(
            "Saving sync configuration: Folder={Folder}, AutoSync={Auto}, Interval={Interval}min, Scope={Scope}",
            SyncFolderPath, AutoSyncEnabled, intervalMinutes, scope);

        IsLoading = true;
        IsSaving = true;
        ClearError();
        ClearStatus();

        try
        {
            var config = new SyncConfiguration
            {
                SyncFolderPath = SyncFolderPath.Trim(),
                EncryptionKey = EncryptionKey,
                AutoSyncEnabled = AutoSyncEnabled,
                SyncIntervalMinutes = intervalMinutes,
                SyncScope = scope,
                SelectedCollectionIds = scope == AgentX.Core.Services.Sync.Models.SyncScope.SelectedCollections
                    ? SelectedCollectionIds?.Trim()
                    : null
            };

            await _syncService.ConfigureAsync(config);

            // Reflect the validated interval back so the TextBox shows the
            // canonical stored value rather than whatever the user typed.
            SyncIntervalMinutes = intervalMinutes.ToString();

            // Reconcile the auto-sync loop with the persisted toggle state
            if (AutoSyncEnabled)
                await StartAutoSyncLoopAsync();
            else
                await StopAutoSyncLoopAsync();

            SetStatus(_localization.GetString("Sync_ConfigSaved"));
            Log.Information("Sync configuration saved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save sync configuration");
            SetError(_localization.GetString("Sync_ConfigSaveFailed", ex.Message));
        }
        finally
        {
            IsLoading = false;
            IsSaving = false;
            NotifyComputedProperties();
        }
    }

    // =========================================================================
    // COMMAND: SyncNowAsync
    // Runs one complete pass through ISyncService.SyncNowAsync: exports local
    // changes since the persisted watermark, then imports every peer file in the
    // sync folder. The status line reports the real outcome (exported, applied,
    // retried, unreadable) instead of assuming success.
    // =========================================================================

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncNowAsync()
    {
        if (!HasConfiguration)
        {
            SetError(_localization.GetString("Sync_SaveBeforeSync"));
            return;
        }

        Log.Information("Manual sync requested");
        var resolvedFocusedSyncMessage = BuildFocusedSyncResolutionMessage();

        IsSyncing = true;
        ClearError();
        ClearStatus();
        ShowSyncState(AgentX.Core.Services.Sync.Models.SyncState.Syncing);
        NotifyComputedProperties();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            SetStatus(_localization.GetString("Sync_SyncInProgress"));
            var result = await _syncService.SyncNowAsync(cts.Token);

            // Refresh the status display and history list from what the pass recorded.
            RefreshStatusFromService();
            await LoadHistoryAsync();

            if (result.HasProblems)
            {
                SetError(BuildSyncProblemMessage(result));
                SetStatus(BuildSyncOutcomeMessage(result));
            }
            else if (resolvedFocusedSyncMessage is not null && TryResolveFocusedSyncAction(resolvedFocusedSyncMessage))
            {
                Log.Information("Manual sync completed and resolved focused sync history entry");
            }
            else
            {
                SetStatus(BuildSyncOutcomeMessage(result));
            }

            Log.Information(
                "Manual sync completed: exported {Exported}, imported {Imported} of {Found} peer file(s), " +
                "{Applied} applied, {Failed} failed, {Rejected} rejected",
                result.ExportedChanges, result.PeerFilesImported, result.PeerFilesFound,
                result.ChangesApplied, result.ChangesFailed, result.ChangesRejected);
        }
        catch (OperationCanceledException)
        {
            ShowSyncState(AgentX.Core.Services.Sync.Models.SyncState.Error);
            SetError(_localization.GetString("Sync_SyncTimedOut"));
            Log.Warning("Manual sync timed out");
        }
        catch (Exception ex)
        {
            ShowSyncState(AgentX.Core.Services.Sync.Models.SyncState.Error);
            SetError(_localization.GetString("Sync_SyncFailed", ex.Message));
            Log.Error(ex, "Manual sync failed");
        }
        finally
        {
            IsSyncing = false;
            NotifyComputedProperties();
        }
    }

    /// <summary>
    /// Set while the view model itself writes <see cref="AutoSyncEnabled"/> - loading a
    /// saved configuration, or the start/stop commands recording their own outcome - so
    /// those writes are not mistaken for the user flipping the switch.
    /// </summary>
    private bool _applyingAutoSyncState;

    /// <summary>
    /// Starts or stops the background loop when the user moves the Auto-Sync switch.
    /// The switch binds this property two-way; without this it changed a flag and left
    /// the loop running, so the control reported a state it did not enforce.
    /// </summary>
    partial void OnAutoSyncEnabledChanged(bool value)
    {
        if (_applyingAutoSyncState)
        {
            return;
        }

        if (value)
        {
            StartAutoSyncCommand.Execute(null);
        }
        else
        {
            StopAutoSyncCommand.Execute(null);
        }
    }

    /// <summary>
    /// Writes <see cref="AutoSyncEnabled"/> without re-entering the toggle handler.
    /// </summary>
    private void SetAutoSyncEnabledSilently(bool value)
    {
        _applyingAutoSyncState = true;
        try
        {
            AutoSyncEnabled = value;
        }
        finally
        {
            _applyingAutoSyncState = false;
        }
    }

    // =========================================================================
    // COMMAND: StartAutoSyncAsync
    // Persists AutoSyncEnabled=true on the SAVED configuration first (the
    // service only runs the loop when the stored flag is on), then starts the
    // loop and reports whether it is actually running.
    // =========================================================================

    [RelayCommand]
    private async Task StartAutoSyncAsync()
    {
        Log.Information("Start auto-sync requested");
        ClearError();
        ClearStatus();

        if (!HasConfiguration)
        {
            SetAutoSyncEnabledSilently(false);
            SetError(_localization.GetString("Sync_SaveBeforeAutoSync"));
            return;
        }

        try
        {
            var stored = await _syncService.GetConfigurationAsync();
            if (stored is null)
            {
                SetAutoSyncEnabledSilently(false);
                SetError(_localization.GetString("Sync_AutoSyncNeedsSavedConfig"));
                return;
            }

            if (!stored.AutoSyncEnabled)
            {
                stored.AutoSyncEnabled = true;
                await _syncService.ConfigureAsync(stored);
            }

            await StartAutoSyncLoopAsync();

            if (!_syncService.IsAutoSyncRunning)
            {
                SetAutoSyncEnabledSilently(false);
                SetError(_localization.GetString("Sync_AutoSyncNotStarted"));
                Log.Warning("Auto-sync start requested but the loop is not running");
                return;
            }

            SetAutoSyncEnabledSilently(true);
            SetStatus(BuildAutoSyncStartedMessage(stored.SyncIntervalMinutes));
            Log.Information("Auto-sync loop started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start auto-sync");
            SetAutoSyncEnabledSilently(_syncService.IsAutoSyncRunning);
            SetError(_localization.GetString("Sync_AutoSyncStartFailed", ex.Message));
        }
    }

    // =========================================================================
    // COMMAND: StopAutoSyncAsync
    // Persists AutoSyncEnabled=false (so the loop does not come back after a
    // restart), then stops the loop.
    // =========================================================================

    [RelayCommand]
    private async Task StopAutoSyncAsync()
    {
        Log.Information("Stop auto-sync requested");
        ClearError();
        ClearStatus();

        string? persistError = null;
        try
        {
            var stored = await _syncService.GetConfigurationAsync();
            if (stored is { AutoSyncEnabled: true })
            {
                stored.AutoSyncEnabled = false;
                await _syncService.ConfigureAsync(stored);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist the disabled auto-sync setting");
            persistError = ex.Message;
        }

        try
        {
            await StopAutoSyncLoopAsync();
            SetAutoSyncEnabledSilently(false);

            if (persistError is null)
            {
                SetStatus(_localization.GetString("Sync_AutoSyncStopped"));
            }
            else
            {
                SetError(_localization.GetString("Sync_AutoSyncStoppedNotSaved", persistError));
            }

            Log.Information("Auto-sync loop stopped");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while stopping auto-sync");
            SetError(_localization.GetString("Sync_AutoSyncStopFailed", ex.Message));
        }
    }

    // =========================================================================
    // COMMAND: LoadHistoryAsync
    // Loads the 50 most recent SyncLogEntity records from the service and
    // rebuilds SyncHistory with presentation-ready SyncLogDisplayItem wrappers.
    // Exposed as a RelayCommand so the View can offer a manual refresh button.
    // =========================================================================

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        Log.Debug("Loading sync history");

        try
        {
            var history = await _syncService.GetSyncHistoryAsync(50);

            SyncHistory.Clear();

            foreach (var entry in history)
            {
                SyncHistory.Add(MapToDisplayItem(entry));
            }

            Log.Debug("Sync history loaded: {Count} entries", SyncHistory.Count);
            ApplyPendingOperationsRequest();
        }
        catch (Exception ex)
        {
            // Non-fatal: history unavailability must not block the rest of the page
            Log.Warning(ex, "Failed to load sync history");
        }

        OnPropertyChanged(nameof(HasSyncHistory));
    }

    // =========================================================================
    // COMMAND: RefreshAsync
    // Refreshes sync status from the service and reloads sync history.
    // =========================================================================

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Log.Debug("Refresh requested");
        ClearError();
        ClearStatus();

        try
        {
            RefreshStatusFromService();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh sync status");
            SetError(_localization.GetString("Sync_RefreshFailed", ex.Message));
        }
    }

    [RelayCommand]
    private void DismissFocusedSyncLanding()
    {
        var shouldClearStatus = HasStatusMessage && _statusShowsFocusedSyncSource;

        FocusedSyncLogId = 0;
        FocusedSyncSourceLabel = string.Empty;
        ClearSyncHistoryFocus();

        if (shouldClearStatus)
        {
            ClearStatus();
        }
    }

    // =========================================================================
    // COMMAND: ClearSyncHistoryAsync
    // Clears the in-memory sync history collection and notifies the View.
    // =========================================================================

    [RelayCommand]
    private Task ClearSyncHistoryAsync()
    {
        Log.Debug("Clear sync history requested");
        var shouldClearStatus = HasStatusMessage && _statusShowsFocusedSyncSource;

        SyncHistory.Clear();
        FocusedSyncLogId = 0;
        FocusedSyncSourceLabel = string.Empty;
        OnPropertyChanged(nameof(HasSyncHistory));

        if (shouldClearStatus)
        {
            ClearStatus();
        }

        return Task.CompletedTask;
    }

    // =========================================================================
    // PROPERTY CHANGE HOOKS
    // Keep CanSync and CanExecute guards fresh whenever fields that influence
    // computed properties change.
    // =========================================================================

    partial void OnSyncFolderPathChanged(string value)
    {
        NotifyComputedProperties();
    }

    partial void OnEncryptionKeyChanged(string value)
    {
        NotifyComputedProperties();
    }

    partial void OnIsSyncingChanged(bool value)
    {
        SyncNowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSync));
    }

    partial void OnFocusedSyncSourceLabelChanged(string value) =>
        OnPropertyChanged(nameof(HasFocusedSyncLanding));

    partial void OnSyncStateChanged(string value)
    {
        OnPropertyChanged(nameof(SyncStateDisplay));
    }

    partial void OnLastSyncDurationMsChanged(string value)
    {
        OnPropertyChanged(nameof(LastSyncDuration));
    }

    partial void OnSyncScopeChanged(string value)
    {
        // When the scope is switched back to "All", wipe any stale collection
        // ID filter so it cannot be accidentally persisted.
        if (value == "All")
        {
            foreach (var collection in AvailableCollections)
                collection.IsSelected = false;

            SelectedCollectionIds = null;
        }
        else
        {
            UpdateSelectedCollectionIdsFromSelections();
        }

        OnPropertyChanged(nameof(SelectedSyncScopeIndex));
        OnPropertyChanged(nameof(ShowSelectedCollectionsPicker));
        OnPropertyChanged(nameof(SelectedCollectionSummary));
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    /// <summary>
    /// Asks the service to (re)start its background loop from the stored configuration.
    /// The loop is owned by the service, not by this page: it keeps running after the
    /// page is closed. Shared by SaveConfigurationAsync and StartAutoSyncAsync.
    /// </summary>
    private async Task StartAutoSyncLoopAsync()
    {
        await _syncService.StartAutoSyncAsync();
        Log.Debug("Auto-sync loop start requested from the settings page");
    }

    /// <summary>
    /// Calls ISyncService.StopAutoSyncAsync. Safe to call when no loop is active.
    /// </summary>
    private async Task StopAutoSyncLoopAsync()
    {
        try
        {
            await _syncService.StopAutoSyncAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception while stopping auto-sync loop");
        }
    }

    private string BuildAutoSyncStartedMessage(int intervalMinutes)
    {
        var minutes = Math.Max(1, intervalMinutes);
        return minutes == 1
            ? _localization.GetString("Sync_AutoSyncStartedOneMinute")
            : _localization.GetString("Sync_AutoSyncStartedMinutes", minutes);
    }

    /// <summary>Plain-language summary of a completed sync pass.</summary>
    internal string BuildSyncOutcomeMessage(SyncRunResult result)
    {
        var lead = result.HasProblems
            ? _localization.GetString("Sync_OutcomeWithProblems")
            : _localization.GetString("Sync_OutcomeComplete");
        var exported = result.ExportedChanges == 1
            ? _localization.GetString("Sync_ChangeCountOne")
            : _localization.GetString("Sync_ChangeCountMany", result.ExportedChanges);
        if (result.PeerFilesFound == 0)
            return _localization.GetString("Sync_OutcomeNoPeerChanges", lead, exported);

        var files = result.PeerFilesImported == 1
            ? _localization.GetString("Sync_PeerFileCountOne")
            : _localization.GetString("Sync_PeerFileCountMany", result.PeerFilesImported);
        return result.ConflictsResolved > 0
            ? _localization.GetString("Sync_OutcomeImportedSkipped", lead, exported, files, result.ChangesApplied, result.ConflictsResolved)
            : _localization.GetString("Sync_OutcomeImported", lead, exported, files, result.ChangesApplied);
    }

    /// <summary>Describes what went wrong in a pass, for the error banner.</summary>
    internal string BuildSyncProblemMessage(SyncRunResult result)
    {
        var parts = new List<string>();
        if (result.PeerFilesPendingRetry > 0)
            parts.Add(_localization.GetString("Sync_ProblemPendingRetry", result.PeerFilesPendingRetry));
        if (result.PeerFilesUnreadable > 0)
            parts.Add(_localization.GetString("Sync_ProblemUnreadable", result.PeerFilesUnreadable));
        if (result.ChangesRejected > 0)
            parts.Add(_localization.GetString("Sync_ProblemRejected", result.ChangesRejected));

        var problems = string.Join(_localization.GetString("Sync_ProblemSeparator"), parts);
        return result.Errors.Count > 0
            ? _localization.GetString("Sync_ProblemsWithFirstError", problems, result.Errors[0])
            : _localization.GetString("Sync_Problems", problems);
    }

    /// <summary>Shows <paramref name="state"/> in the badge text and the typed state the LEDs read.</summary>
    private void ShowSyncState(AgentX.Core.Services.Sync.Models.SyncState state)
    {
        CurrentSyncState = state;
        SyncState = DescribeSyncState(state);
    }

    private string DescribeSyncState(AgentX.Core.Services.Sync.Models.SyncState state) => state switch
    {
        AgentX.Core.Services.Sync.Models.SyncState.Idle => _localization.GetString("Sync_StateIdle"),
        AgentX.Core.Services.Sync.Models.SyncState.Syncing => _localization.GetString("Sync_StateSyncing"),
        AgentX.Core.Services.Sync.Models.SyncState.Error => _localization.GetString("Sync_StateError"),
        AgentX.Core.Services.Sync.Models.SyncState.Conflict => _localization.GetString("Sync_StateConflict"),
        _ => _localization.GetString("Sync_StateUnknown")
    };

    /// <summary>
    /// Maps a raw <see cref="SyncLogEntity"/> to a <see cref="SyncHistoryItem"/>
    /// with all string formatting applied, ready for direct binding.
    /// </summary>
    private SyncHistoryItem MapToDisplayItem(SyncLogEntity entry) => new()
    {
        Id = entry.Id,
        Direction = entry.Direction,
        ChangesApplied = entry.ChangesApplied,
        ConflictsDetected = entry.ConflictsDetected,
        IsSuccess = entry.IsSuccess,
        IsFocused = false,
        SyncedAtFormatted = FormatHelper.TimeAgoWithMonths(entry.SyncedAt),
        SyncedAtFull = entry.SyncedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt"),
        DurationFormatted = FormatHelper.FormatDuration(entry.DurationMs),
        ErrorMessage = entry.ErrorMessage,
        ExportLabel = _localization.GetString("Sync_HistoryDirectionExport"),
        ImportLabel = _localization.GetString("Sync_HistoryDirectionImport"),
        SuccessLabel = _localization.GetString("Sync_HistoryStatusSuccess"),
        FailedLabel = _localization.GetString("Sync_HistoryStatusFailed")
    };

    private void ApplyPendingOperationsRequest()
    {
        var request = _operationsDrillInService?.ConsumePendingSyncRequest();
        if (request is not null && request.SyncLogId > 0)
        {
            FocusedSyncLogId = request.SyncLogId;
            FocusedSyncSourceLabel = request.SourceLabel;
        }

        if (FocusedSyncLogId <= 0 || string.IsNullOrWhiteSpace(FocusedSyncSourceLabel))
        {
            ClearSyncHistoryFocus();
            return;
        }

        var focusedItem = SyncHistory.FirstOrDefault(item => item.Id == FocusedSyncLogId);
        if (focusedItem is null)
        {
            FocusedSyncLogId = 0;
            FocusedSyncSourceLabel = string.Empty;
            ClearSyncHistoryFocus();
            SetStatus(_localization.GetString("Sync_FocusedEntryMissing"));
            return;
        }

        ClearSyncHistoryFocus();
        focusedItem.IsFocused = true;

        var currentIndex = SyncHistory.IndexOf(focusedItem);
        if (currentIndex > 0)
        {
            SyncHistory.Move(currentIndex, 0);
        }

        SetStatus(FocusedSyncSourceLabel, showsFocusedSyncSource: true);
    }

    private bool TryResolveFocusedSyncAction(string resolutionMessage)
    {
        if (FocusedSyncLogId <= 0 || string.IsNullOrWhiteSpace(FocusedSyncSourceLabel))
        {
            return false;
        }

        FocusedSyncLogId = 0;
        FocusedSyncSourceLabel = string.Empty;
        ClearSyncHistoryFocus();
        SetStatus(resolutionMessage);
        return true;
    }

    private string? BuildFocusedSyncResolutionMessage()
    {
        if (FocusedSyncLogId <= 0 || string.IsNullOrWhiteSpace(FocusedSyncSourceLabel))
        {
            return null;
        }

        return _localization.GetString("Sync_FocusedEntryResolved");
    }

    private void ClearSyncHistoryFocus()
    {
        foreach (var item in SyncHistory)
        {
            item.IsFocused = false;
        }
    }

    private void UpdateSelectedCollectionIdsFromSelections()
    {
        var selected = AvailableCollections
            .Where(collection => collection.IsSelected)
            .Select(collection => collection.Id.ToString())
            .ToArray();

        SelectedCollectionIds = selected.Length > 0 ? string.Join(",", selected) : null;
        OnPropertyChanged(nameof(SelectedCollectionSummary));
        OnPropertyChanged(nameof(HasAvailableCollections));
    }

    private static HashSet<long> ParseSelectedCollectionIds(string? value)
    {
        var ids = new HashSet<long>();
        if (string.IsNullOrWhiteSpace(value))
            return ids;

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(token, out var parsed))
                ids.Add(parsed);
        }

        return ids;
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(HasConfiguration));
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(CanSync));
        OnPropertyChanged(nameof(SyncStateDisplay));
        OnPropertyChanged(nameof(HasSuccess));
        OnPropertyChanged(nameof(SuccessMessage));
        OnPropertyChanged(nameof(LastSyncDuration));
        OnPropertyChanged(nameof(HasSyncHistory));
        OnPropertyChanged(nameof(ShowSelectedCollectionsPicker));
        OnPropertyChanged(nameof(HasAvailableCollections));
        OnPropertyChanged(nameof(SelectedCollectionSummary));
        SyncNowCommand.NotifyCanExecuteChanged();
        SaveConfigurationCommand.NotifyCanExecuteChanged();
    }

    // -- Error / Status Management ---------------------------------------------

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    /// <summary>
    /// True while the status line shows the focused history entry's source label, so dismissing
    /// the focus clears that line without comparing displayed text.
    /// </summary>
    private bool _statusShowsFocusedSyncSource;

    private void SetStatus(string message, bool showsFocusedSyncSource = false)
    {
        StatusMessage = message;
        HasStatusMessage = true;
        _statusShowsFocusedSyncSource = showsFocusedSyncSource;
        OnPropertyChanged(nameof(HasSuccess));
        OnPropertyChanged(nameof(SuccessMessage));
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        HasStatusMessage = false;
        _statusShowsFocusedSyncSource = false;
        OnPropertyChanged(nameof(HasSuccess));
        OnPropertyChanged(nameof(SuccessMessage));
    }

    // =========================================================================
    // DISPOSAL
    // =========================================================================

    public void Dispose()
    {
        // The auto-sync loop belongs to the service and deliberately outlives this page.
        Log.Debug("SyncSettingsViewModel disposed");
    }
}

public sealed partial class SyncCollectionSelectionItem : ObservableObject
{
    private readonly Action _selectionChanged;

    public long Id { get; }
    public string Name { get; }
    public int DocumentCount { get; }

    /// <summary>The document count as shown under the name, e.g. "3 documents".</summary>
    public string DetailLabel { get; }

    [ObservableProperty] private bool _isSelected;

    public SyncCollectionSelectionItem(long id, string name, int documentCount, string detailLabel, bool isSelected, Action selectionChanged)
    {
        Id = id;
        Name = name;
        DocumentCount = documentCount;
        DetailLabel = detailLabel;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

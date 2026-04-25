using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.App.Services;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Data.Entities;
using AgentX.App.ViewModels.Sync;
using Serilog;

namespace AgentX.App.ViewModels;

// =============================================================================
// SYNC SETTINGS VIEW MODEL
//
// Manages the Collaborative Sync settings page. Allows the user to configure
// an encrypted sync folder, an encryption passphrase, auto-sync scheduling,
// and sync scope. Drives a manual "Sync Now" flow that calls ExportChangesAsync
// then watches for imports via StartAutoSyncAsync. Exposes discrete Start and
// Stop commands for the background auto-sync loop. Loads paginated sync history
// via LoadHistoryAsync. Raises FolderPickerRequested so the View can present a
// native folder picker without any UI coupling in this ViewModel.
//
// Constructor accepts ISyncService via DI. All long-running paths are guarded
// by IsLoading / IsSyncing flags and surfaced through the SetError / SetStatus
// / ClearError / ClearStatus helpers that the View binds to its notification
// strip.
// =============================================================================

public partial class SyncSettingsViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────────────────────

    private readonly ISyncService _syncService;
    private readonly ICollectionService _collectionService;
    private readonly IOperationsDrillInService? _operationsDrillInService;

    /// <summary>
    /// CancellationTokenSource for the running auto-sync loop.
    /// Cancelled and replaced whenever StartAutoSyncLoopAsync is called,
    /// and cancelled on Dispose.
    /// </summary>
    private CancellationTokenSource? _autoSyncCts;

    // ── Page State ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private long _focusedSyncLogId;
    [ObservableProperty] private string _focusedSyncSourceLabel = string.Empty;

    // ── Configuration Fields ─────────────────────────────────────────────────

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
    /// "All" — every supported entity type.
    /// "SelectedCollections" — only the collections listed in SelectedCollectionIds.
    /// </summary>
    [ObservableProperty] private string _syncScope = "All";

    /// <summary>
    /// Comma-separated collection IDs to include when SyncScope is
    /// "SelectedCollections". Null or empty when SyncScope is "All".
    /// </summary>
    [ObservableProperty] private string? _selectedCollectionIds;

    // ── Sync Status Fields ────────────────────────────────────────────────────

    /// <summary>Human-readable representation of the current SyncState, e.g. "Idle".</summary>
    [ObservableProperty] private string _syncState = "Idle";

    /// <summary>Relative timestamp of the last successful sync pass, e.g. "3m ago".</summary>
    [ObservableProperty] private string _lastSyncAt = "Never";

    /// <summary>Number of locally-originated changes exported but not yet confirmed received by a peer.</summary>
    [ObservableProperty] private int _pendingChanges;

    /// <summary>Formatted wall-clock duration of the most recent sync pass, e.g. "1.4s".</summary>
    [ObservableProperty] private string _lastSyncDurationMs = "--";

    // ── Interval Options ────────────────────────────────────────────────────

    /// <summary>
    /// Display strings for the sync interval dropdown. Indexes map to
    /// { 5, 15, 30, 60, 120 } minutes respectively.
    /// </summary>
    public List<string> IntervalOptions { get; } = new()
    {
        "Every 5 minutes",
        "Every 15 minutes",
        "Every 30 minutes",
        "Every hour",
        "Every 2 hours"
    };

    /// <summary>
    /// Currently selected index in <see cref="IntervalOptions"/>.
    /// Defaults to 1 (Every 15 minutes).
    /// </summary>
    [ObservableProperty] private int _selectedIntervalIndex = 1;

    public List<string> SyncScopeOptions { get; } = new() { "All", "SelectedCollections" };

    public ObservableCollection<SyncCollectionSelectionItem> AvailableCollections { get; } = new();

    // ── Sync History ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ordered newest-first; populated by LoadHistoryAsync.
    /// Bound to the history ItemsControl / ListView in the View.
    /// </summary>
    public ObservableCollection<SyncHistoryItem> SyncHistory { get; } = new();

    // ── Computed Properties ───────────────────────────────────────────────────

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
                0 => "No collections selected",
                1 => "1 collection selected",
                _ => $"{selectedCount} collections selected"
            };
        }
    }

    // ── Folder Picker Event ───────────────────────────────────────────────────

    /// <summary>
    /// Raised by BrowseFolderAsync. The View subscribes, shows a native
    /// StorageFolder picker, and returns the selected path (or null when cancelled).
    /// Mirrors the FilePickerRequested pattern used in PluginManagerViewModel.
    /// </summary>
    public event Func<Task<string?>>? FolderPickerRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SyncSettingsViewModel(
        ISyncService syncService,
        ICollectionService collectionService,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _syncService = syncService;
        _collectionService = collectionService;
        _operationsDrillInService = operationsDrillInService;
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
            SetError("Failed to load sync settings. Please try again.");
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
                SyncFolderPath       = config.SyncFolderPath ?? string.Empty;
                EncryptionKey        = config.EncryptionKey ?? string.Empty;
                AutoSyncEnabled      = config.AutoSyncEnabled;
                SyncIntervalMinutes  = config.SyncIntervalMinutes.ToString();
                SyncScope            = config.SyncScope == AgentX.Core.Services.Sync.Models.SyncScope.SelectedCollections
                    ? "SelectedCollections"
                    : "All";
                SelectedCollectionIds = config.SelectedCollectionIds;

                // Map the stored interval in minutes back to the dropdown index
                SelectedIntervalIndex = config.SyncIntervalMinutes switch
                {
                    5   => 0,
                    15  => 1,
                    30  => 2,
                    60  => 3,
                    120 => 4,
                    _   => 1  // default to "Every 15 minutes" for non-standard values
                };

                Log.Debug(
                    "Sync configuration loaded: HasConfiguration={Has}, AutoSync={Auto}, Interval={Interval}min, Scope={Scope}",
                    HasConfiguration, AutoSyncEnabled, config.SyncIntervalMinutes, SyncScope);
            }
            else
            {
                Log.Debug("No sync configuration found — first-time setup");
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
                AvailableCollections.Add(new SyncCollectionSelectionItem(
                    collection.Id,
                    collection.Name,
                    collection.DocumentCount,
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

            SyncState = status.SyncState switch
            {
                AgentX.Core.Services.Sync.Models.SyncState.Idle     => "Idle",
                AgentX.Core.Services.Sync.Models.SyncState.Syncing  => "Syncing",
                AgentX.Core.Services.Sync.Models.SyncState.Error    => "Error",
                AgentX.Core.Services.Sync.Models.SyncState.Conflict => "Conflict",
                _                                                     => "Unknown"
            };

            PendingChanges = status.PendingChanges;

            LastSyncAt = status.LastSyncAt.HasValue
                ? FormatHelper.TimeAgoWithMonths(status.LastSyncAt.Value)
                : "Never";

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
            SetError("A sync folder path is required. Use the Browse button to select one.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EncryptionKey))
        {
            SetError("An encryption key is required. This passphrase encrypts all data written to the sync folder.");
            return;
        }

        if (!int.TryParse(SyncIntervalMinutes?.Trim(), out int intervalMinutes) || intervalMinutes < 1)
        {
            SetError("Sync interval must be a whole number of minutes (minimum 1).");
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
            SetError("Select at least one collection when Sync Scope is set to Selected Collections.");
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
                SyncFolderPath        = SyncFolderPath.Trim(),
                EncryptionKey         = EncryptionKey,
                AutoSyncEnabled       = AutoSyncEnabled,
                SyncIntervalMinutes   = intervalMinutes,
                SyncScope             = scope,
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

            SetStatus("Sync configuration saved successfully.");
            Log.Information("Sync configuration saved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save sync configuration");
            SetError($"Failed to save configuration: {ex.Message}");
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
    // Performs a one-shot manual sync: first calls ExportChangesAsync to package
    // local changes and write the encrypted .axs file to the sync folder, then
    // calls StartAutoSyncAsync (with a short-lived CTS) so the service reads and
    // imports any .axs files written by peer installations.
    // =========================================================================

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncNowAsync()
    {
        if (!HasConfiguration)
        {
            SetError("Please save a sync configuration before syncing.");
            return;
        }

        Log.Information("Manual sync requested");
        var resolvedFocusedSyncMessage = BuildFocusedSyncResolutionMessage();

        IsSyncing = true;
        ClearError();
        ClearStatus();
        SyncState = "Syncing";
        NotifyComputedProperties();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            // Step 1 — export: collect local changes and write to the sync folder
            SetStatus("Exporting local changes to sync folder...");
            var changeSet = await _syncService.ExportChangesAsync(ct: cts.Token);

            Log.Debug("Export produced {Count} change(s)", changeSet.Changes.Count);

            // Step 2 — import: run a single auto-sync pass so the service reads
            // and applies any .axs files placed by peer installations
            SetStatus($"Exported {changeSet.Changes.Count} change(s). Importing remote changes...");
            await _syncService.StartAutoSyncAsync(cts.Token);

            // Step 3 — refresh: update the status display and history list
            RefreshStatusFromService();
            await LoadHistoryAsync();

            if (resolvedFocusedSyncMessage is not null && TryResolveFocusedSyncAction(resolvedFocusedSyncMessage))
            {
                Log.Information("Manual sync completed and resolved focused sync history entry");
            }
            else
            {
                SetStatus($"Sync complete — {changeSet.Changes.Count} change(s) exported.");
            }
            Log.Information("Manual sync completed: {Count} change(s) exported", changeSet.Changes.Count);
        }
        catch (OperationCanceledException)
        {
            SyncState = "Error";
            SetError("Sync timed out after 10 minutes. Please check the sync folder connectivity and try again.");
            Log.Warning("Manual sync timed out");
        }
        catch (Exception ex)
        {
            SyncState = "Error";
            SetError($"Sync failed: {ex.Message}");
            Log.Error(ex, "Manual sync failed");
        }
        finally
        {
            IsSyncing = false;
            NotifyComputedProperties();
        }
    }

    // =========================================================================
    // COMMAND: StartAutoSyncAsync
    // Starts the background polling loop. Guards against double-starts by
    // cancelling any existing loop first via StartAutoSyncLoopAsync.
    // =========================================================================

    [RelayCommand]
    private async Task StartAutoSyncAsync()
    {
        Log.Information("Start auto-sync requested");
        ClearError();
        ClearStatus();

        if (!HasConfiguration)
        {
            SetError("Please save a sync configuration before enabling auto-sync.");
            return;
        }

        try
        {
            await StartAutoSyncLoopAsync();
            AutoSyncEnabled = true;
            SetStatus("Auto-sync started.");
            Log.Information("Auto-sync loop started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start auto-sync");
            SetError($"Failed to start auto-sync: {ex.Message}");
        }
    }

    // =========================================================================
    // COMMAND: StopAutoSyncAsync
    // Cancels the running loop CTS and calls ISyncService.StopAutoSyncAsync.
    // =========================================================================

    [RelayCommand]
    private async Task StopAutoSyncAsync()
    {
        Log.Information("Stop auto-sync requested");
        ClearError();
        ClearStatus();

        try
        {
            await StopAutoSyncLoopAsync();
            AutoSyncEnabled = false;
            SetStatus("Auto-sync stopped.");
            Log.Information("Auto-sync loop stopped");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while stopping auto-sync");
            SetError($"Failed to stop auto-sync: {ex.Message}");
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
    // COMMAND: BrowseFolderAsync
    // Raises FolderPickerRequested so the View can show a StorageFolder picker
    // and return the chosen path. Writes the result into SyncFolderPath.
    // =========================================================================

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        Log.Debug("Folder picker requested");

        try
        {
            var selectedPath = FolderPickerRequested is not null
                ? await FolderPickerRequested.Invoke()
                : null;

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                SyncFolderPath = selectedPath;
                Log.Debug("Sync folder selected: {Path}", selectedPath);
            }
            else
            {
                Log.Debug("Folder picker dismissed — no path selected");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling folder picker result");
            SetError($"Could not apply the selected folder: {ex.Message}");
        }
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
            SetError($"Failed to refresh: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DismissFocusedSyncLanding()
    {
        var shouldClearStatus = HasStatusMessage && string.Equals(StatusMessage, FocusedSyncSourceLabel, StringComparison.Ordinal);

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
        var shouldClearStatus = HasStatusMessage && string.Equals(StatusMessage, FocusedSyncSourceLabel, StringComparison.Ordinal);

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

        OnPropertyChanged(nameof(ShowSelectedCollectionsPicker));
        OnPropertyChanged(nameof(SelectedCollectionSummary));
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    /// <summary>
    /// Cancels any existing auto-sync CTS, allocates a fresh one, and calls
    /// ISyncService.StartAutoSyncAsync. Shared by SaveConfigurationAsync and
    /// StartAutoSyncAsync to guarantee identical loop lifecycle behaviour.
    /// </summary>
    private async Task StartAutoSyncLoopAsync()
    {
        // Tear down any loop already running before launching a new one
        await StopAutoSyncLoopAsync();

        _autoSyncCts = new CancellationTokenSource();
        await _syncService.StartAutoSyncAsync(_autoSyncCts.Token);

        Log.Debug("Auto-sync loop CTS created and loop started");
    }

    /// <summary>
    /// Cancels the current CTS and calls ISyncService.StopAutoSyncAsync.
    /// Safe to call when no loop is active.
    /// </summary>
    private async Task StopAutoSyncLoopAsync()
    {
        try
        {
            if (_autoSyncCts is not null)
            {
                _autoSyncCts.Cancel();
                _autoSyncCts.Dispose();
                _autoSyncCts = null;
            }

            await _syncService.StopAutoSyncAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception while stopping auto-sync loop");
        }
    }

    /// <summary>
    /// Maps a raw <see cref="SyncLogEntity"/> to a <see cref="SyncHistoryItem"/>
    /// with all string formatting applied, ready for direct binding.
    /// </summary>
    private static SyncHistoryItem MapToDisplayItem(SyncLogEntity entry) => new()
    {
        Id                = entry.Id,
        Direction         = entry.Direction,
        ChangesApplied    = entry.ChangesApplied,
        ConflictsDetected = entry.ConflictsDetected,
        IsSuccess         = entry.IsSuccess,
        IsFocused         = false,
        SyncedAtFormatted = FormatHelper.TimeAgoWithMonths(entry.SyncedAt),
        SyncedAtFull      = entry.SyncedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt"),
        DurationFormatted = FormatHelper.FormatDuration(entry.DurationMs),
        ErrorMessage      = entry.ErrorMessage
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
            SetStatus("The requested sync history entry is no longer available.");
            return;
        }

        ClearSyncHistoryFocus();
        focusedItem.IsFocused = true;

        var currentIndex = SyncHistory.IndexOf(focusedItem);
        if (currentIndex > 0)
        {
            SyncHistory.Move(currentIndex, 0);
        }

        SetStatus(FocusedSyncSourceLabel);
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

        return "Resolved the focused sync history entry by running a fresh sync pass.";
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

    // ── Error / Status Management ─────────────────────────────────────────────

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

    private void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatusMessage = true;
        OnPropertyChanged(nameof(HasSuccess));
        OnPropertyChanged(nameof(SuccessMessage));
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        HasStatusMessage = false;
        OnPropertyChanged(nameof(HasSuccess));
        OnPropertyChanged(nameof(SuccessMessage));
    }

    // =========================================================================
    // DISPOSAL
    // =========================================================================

    public void Dispose()
    {
        _autoSyncCts?.Cancel();
        _autoSyncCts?.Dispose();
        _autoSyncCts = null;
        Log.Debug("SyncSettingsViewModel disposed");
    }
}

public sealed partial class SyncCollectionSelectionItem : ObservableObject
{
    private readonly Action _selectionChanged;

    public long Id { get; }
    public string Name { get; }
    public int DocumentCount { get; }
    public string DetailLabel => DocumentCount == 1 ? "1 document" : $"{DocumentCount} documents";

    [ObservableProperty] private bool _isSelected;

    public SyncCollectionSelectionItem(long id, string name, int documentCount, bool isSelected, Action selectionChanged)
    {
        Id = id;
        Name = name;
        DocumentCount = documentCount;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

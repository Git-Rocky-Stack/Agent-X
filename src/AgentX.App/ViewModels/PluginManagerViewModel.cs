using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class PluginManagerViewModel : ObservableObject, IDisposable
{
    // -- Services ---------------------------------------------------------
    private readonly IPluginService _pluginService;
    private readonly ILocalizationService _localization;
    private readonly IOperationsDrillInService? _operationsDrillInService;

    // -- Page Properties --------------------------------------------------
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _pluginCount;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // -- Multi-Select State -----------------------------------------------
    [ObservableProperty] private bool _isMultiSelectMode;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private long _focusedPluginId;
    [ObservableProperty] private string _focusedPluginSourceLabel = string.Empty;

    public ObservableCollection<long> SelectedPluginIds { get; } = new();

    public ObservableCollection<PluginDisplayItem> Plugins { get; } = new();

    /// <summary>
    /// Raised when the ViewModel needs the View to show a file picker.
    /// The View subscribes to this and provides the selected file path back.
    /// </summary>
    public event Func<Task<string?>>? FilePickerRequested;

    /// <summary>
    /// Asks the user to confirm an uninstall and answers true when they do. The page supplies
    /// it (a ContentDialog). While it is unset, Uninstall removes nothing.
    /// </summary>
    public Func<ConfirmationRequest, Task<bool>>? ConfirmDestructiveActionAsync { get; set; }

    // -- Constructor ------------------------------------------------------
    /// <param name="pluginService">Plugin install, uninstall and state changes.</param>
    /// <param name="localization">Every text the page shows: status lines, errors, labels and the uninstall confirmations.</param>
    /// <param name="operationsDrillInService">Focus requests from the Operations page.</param>
    public PluginManagerViewModel(
        IPluginService pluginService,
        ILocalizationService localization,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _pluginService = pluginService;
        _localization = localization;
        _operationsDrillInService = operationsDrillInService;
        StatusMessage = localization.GetString("Plugin_StatusReady");
        Log.Debug("PluginManagerViewModel created with services");
    }

    // -- Initialization ---------------------------------------------------
    public async Task InitializeAsync()
    {
        Log.Information("PluginManager initializing...");
        await LoadPluginsAsync();
    }

    // -- Load Plugins -----------------------------------------------------
    private async Task LoadPluginsAsync()
    {
        IsLoading = true;
        ClearError();

        try
        {
            var plugins = await _pluginService.GetInstalledPluginsAsync();

            Plugins.Clear();

            foreach (var plugin in plugins)
            {
                Plugins.Add(CreateDisplayItem(plugin));
            }

            PluginCount = Plugins.Count;
            StatusMessage = BuildInstalledPluginsStatusMessage();

            ApplyPendingOperationsRequest();

            Log.Information("Loaded {Count} plugins", PluginCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load plugins");
            SetError(_localization.GetString("Plugin_LoadFailed"));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // -- Refresh Command --------------------------------------------------
    [RelayCommand]
    private async Task RefreshPluginsAsync()
    {
        Log.Debug("Refresh plugins requested");
        await LoadPluginsAsync();
    }

    // -- Install Plugin Command -------------------------------------------
    [RelayCommand]
    private async Task InstallPluginAsync()
    {
        Log.Debug("Install plugin requested");
        ClearError();

        try
        {
            // Ask the View to show the file picker and return the selected path
            var packagePath = FilePickerRequested is not null
                ? await FilePickerRequested.Invoke()
                : null;

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                Log.Debug("Install cancelled: no file selected");
                return;
            }

            IsLoading = true;
            StatusMessage = _localization.GetString("Plugin_Installing");

            var installed = await _pluginService.InstallPluginAsync(packagePath);

            // Add the newly installed plugin to the collection
            Plugins.Add(CreateDisplayItem(installed));
            PluginCount = Plugins.Count;

            StatusMessage = _localization.GetString("Plugin_InstallSucceeded", installed.Name);
            Log.Information("Plugin installed: {PluginName} v{Version} (ID: {Id})",
                installed.Name, installed.Version, installed.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install plugin");
            SetError(_localization.GetString("Plugin_InstallFailedDetail", ex.Message));
            StatusMessage = _localization.GetString("Plugin_InstallFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // -- Uninstall Plugin Command -----------------------------------------
    /// <summary>
    /// Uninstalls a plugin once the user confirms: its folder, with any data it keeps there,
    /// and its record are deleted.
    /// </summary>
    [RelayCommand]
    private async Task UninstallPluginAsync(long id)
    {
        var target = Plugins.FirstOrDefault(p => p.Id == id);
        var pluginName = target?.Name ?? $"#{id}";

        var confirmed = await IsConfirmedAsync(new ConfirmationRequest(
            _localization.GetString("Plugin_UninstallConfirmTitle"),
            _localization.GetString("Plugin_UninstallConfirmMessage", pluginName),
            _localization.GetString("Plugin_UninstallConfirmButton"),
            _localization.GetString("Plugin_ConfirmCancelButton")));
        if (!confirmed)
        {
            Log.Information("Uninstall of plugin ID {PluginId} was not confirmed", id);
            return;
        }

        Log.Information("Uninstalling plugin ID: {PluginId}", id);
        ClearError();

        try
        {
            StatusMessage = _localization.GetString("Plugin_Uninstalling", pluginName);

            var result = await _pluginService.UninstallPluginAsync(id);

            // Remove from local collection
            if (target is not null)
            {
                Plugins.Remove(target);
            }

            PluginCount = Plugins.Count;
            if (result.LeftoverDirectory is not null)
            {
                // The plugin is gone but Windows still held some of its files; say so rather
                // than report a clean uninstall.
                SetError(_localization.GetString("Plugin_UninstalledLeftoverError", pluginName, result.LeftoverDirectory));
                StatusMessage = _localization.GetString("Plugin_UninstalledLeftoverStatus", pluginName);
            }
            else
            {
                StatusMessage = _localization.GetString("Plugin_UninstallSucceeded", pluginName);
            }

            Log.Information("Plugin uninstalled: {PluginName} (ID: {PluginId})", pluginName, id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to uninstall plugin ID: {PluginId}", id);
            SetError(_localization.GetString("Plugin_UninstallFailedDetail", ex.Message));
            StatusMessage = _localization.GetString("Plugin_UninstallFailed");
        }
    }

    // -- Enable Plugin Command --------------------------------------------
    [RelayCommand]
    private async Task EnablePluginAsync(long id)
    {
        Log.Information("Enabling plugin ID: {PluginId}", id);
        ClearError();

        try
        {
            await _pluginService.EnablePluginAsync(id);

            // Update the local display item. The service stores the activation time in UTC, and
            // TimeAgo reads UTC: a local time here read "5h ago" west of UTC a moment after enabling.
            var target = Plugins.FirstOrDefault(p => p.Id == id);
            if (target is not null)
            {
                var activatedAt = DateTime.UtcNow;
                target.IsEnabled = true;
                target.LastActivatedAt = activatedAt;
                target.LastActivatedAtFormatted = FormatHelper.TimeAgoWithMonths(activatedAt);
            }

            if (TryResolveFocusedPluginAction(id, target?.Name, out var resolutionMessage))
            {
                StatusMessage = resolutionMessage;
            }
            else
            {
                var pluginName = target?.Name ?? _localization.GetString("Plugin_FallbackName", id);
                StatusMessage = _localization.GetString("Plugin_Enabled", pluginName);
            }
            Log.Information("Plugin enabled: {PluginId}", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enable plugin ID: {PluginId}", id);
            SetError(_localization.GetString("Plugin_EnableFailed", ex.Message));
        }
    }

    // -- Disable Plugin Command -------------------------------------------
    [RelayCommand]
    private async Task DisablePluginAsync(long id)
    {
        Log.Information("Disabling plugin ID: {PluginId}", id);
        ClearError();

        try
        {
            await _pluginService.DisablePluginAsync(id);

            // Update the local display item
            var target = Plugins.FirstOrDefault(p => p.Id == id);
            if (target is not null)
            {
                target.IsEnabled = false;
            }

            var pluginName = target?.Name ?? _localization.GetString("Plugin_FallbackName", id);
            StatusMessage = _localization.GetString("Plugin_Disabled", pluginName);
            Log.Information("Plugin disabled: {PluginId}", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to disable plugin ID: {PluginId}", id);
            SetError(_localization.GetString("Plugin_DisableFailed", ex.Message));
        }
    }

    // -- Multi-Select Commands --------------------------------------------

    /// <summary>
    /// Toggles multi-select mode on or off. When toggled off, all selections are cleared.
    /// </summary>
    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        if (!IsMultiSelectMode)
        {
            SelectedPluginIds.Clear();
            SetSelectionFlags(isSelected: false);
            SelectedCount = 0;
        }
        Log.Debug("Multi-select mode toggled: {IsActive}", IsMultiSelectMode);
    }

    /// <summary>
    /// Toggles the selection state of a single plugin by its database ID.
    /// If already selected, it is deselected; otherwise it is added to the selection.
    /// </summary>
    [RelayCommand]
    private void TogglePluginSelection(long pluginId)
    {
        if (SelectedPluginIds.Contains(pluginId))
            SelectedPluginIds.Remove(pluginId);
        else
            SelectedPluginIds.Add(pluginId);

        var plugin = Plugins.FirstOrDefault(item => item.Id == pluginId);
        if (plugin is not null)
        {
            plugin.IsSelected = SelectedPluginIds.Contains(pluginId);
        }

        SelectedCount = SelectedPluginIds.Count;
    }

    /// <summary>
    /// Selects all currently displayed plugins.
    /// </summary>
    [RelayCommand]
    private void SelectAllPlugins()
    {
        SelectedPluginIds.Clear();
        foreach (var plugin in Plugins)
            SelectedPluginIds.Add(plugin.Id);

        SetSelectionFlags(isSelected: true);
        SelectedCount = SelectedPluginIds.Count;
        Log.Debug("Selected all {Count} plugins", SelectedCount);
    }

    /// <summary>
    /// Applies a selection flag to every listed plugin so each row's checkbox matches the
    /// view model's selection.
    /// </summary>
    private void SetSelectionFlags(bool isSelected)
    {
        foreach (var plugin in Plugins)
        {
            plugin.IsSelected = isSelected;
        }
    }

    /// <summary>
    /// Enables all currently selected plugins in bulk.
    /// After completion, the selection is cleared and the plugin list is refreshed.
    /// </summary>
    [RelayCommand]
    private async Task BulkEnableAsync()
    {
        if (SelectedPluginIds.Count == 0) return;

        var count = SelectedPluginIds.Count;
        var resolvedPluginName = Plugins.FirstOrDefault(plugin => plugin.Id == FocusedPluginId)?.Name;
        var shouldResolveFocusedPlugin = FocusedPluginId > 0
            && !string.IsNullOrWhiteSpace(FocusedPluginSourceLabel)
            && SelectedPluginIds.Contains(FocusedPluginId);
        Log.Information("Bulk enabling {Count} plugins", count);
        ClearError();
        IsLoading = true;
        StatusMessage = count == 1
            ? _localization.GetString("Plugin_BulkEnablingOne")
            : _localization.GetString("Plugin_BulkEnablingMany", count);

        try
        {
            foreach (var id in SelectedPluginIds.ToList())
            {
                await _pluginService.EnablePluginAsync(id);
            }

            await LoadPluginsAsync();
            Log.Information("Bulk enabled {Count} plugins", count);
            if (shouldResolveFocusedPlugin)
            {
                ClearPluginFocus(clearStatus: false);
                StatusMessage = BuildFocusedPluginResolutionMessage(resolvedPluginName);
            }
            else
            {
                StatusMessage = count == 1
                    ? _localization.GetString("Plugin_BulkEnabledOne")
                    : _localization.GetString("Plugin_BulkEnabledMany", count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk enable failed");
            SetError(_localization.GetString("Plugin_BulkEnableFailedDetail", ex.Message));
            StatusMessage = _localization.GetString("Plugin_BulkEnableFailed");
        }
        finally
        {
            SelectedPluginIds.Clear();
            SetSelectionFlags(isSelected: false);
            SelectedCount = 0;
            IsLoading = false;
        }
    }

    /// <summary>
    /// Disables all currently selected plugins in bulk.
    /// After completion, the selection is cleared and the plugin list is refreshed.
    /// </summary>
    [RelayCommand]
    private async Task BulkDisableAsync()
    {
        if (SelectedPluginIds.Count == 0) return;

        var count = SelectedPluginIds.Count;
        Log.Information("Bulk disabling {Count} plugins", count);
        ClearError();
        IsLoading = true;
        StatusMessage = count == 1
            ? _localization.GetString("Plugin_BulkDisablingOne")
            : _localization.GetString("Plugin_BulkDisablingMany", count);

        try
        {
            foreach (var id in SelectedPluginIds.ToList())
            {
                await _pluginService.DisablePluginAsync(id);
            }

            await LoadPluginsAsync();
            Log.Information("Bulk disabled {Count} plugins", count);
            StatusMessage = count == 1
                ? _localization.GetString("Plugin_BulkDisabledOne")
                : _localization.GetString("Plugin_BulkDisabledMany", count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk disable failed");
            SetError(_localization.GetString("Plugin_BulkDisableFailedDetail", ex.Message));
            StatusMessage = _localization.GetString("Plugin_BulkDisableFailed");
        }
        finally
        {
            SelectedPluginIds.Clear();
            SetSelectionFlags(isSelected: false);
            SelectedCount = 0;
            IsLoading = false;
        }
    }

    /// <summary>
    /// Uninstalls all currently selected plugins in bulk, once the user confirms.
    /// After completion, the selection is cleared, multi-select mode is exited,
    /// and the plugin list is refreshed. Without confirmation the selection is kept.
    /// </summary>
    [RelayCommand]
    private async Task BulkUninstallAsync()
    {
        if (SelectedPluginIds.Count == 0) return;

        var count = SelectedPluginIds.Count;
        var confirmed = await IsConfirmedAsync(new ConfirmationRequest(
            _localization.GetString("Plugin_BulkUninstallConfirmTitle"),
            _localization.GetString("Plugin_BulkUninstallConfirmMessage", count),
            _localization.GetString("Plugin_UninstallConfirmButton"),
            _localization.GetString("Plugin_ConfirmCancelButton")));
        if (!confirmed)
        {
            Log.Information("Bulk uninstall of {Count} plugins was not confirmed", count);
            return;
        }

        Log.Information("Bulk uninstalling {Count} plugins", count);
        ClearError();
        IsLoading = true;
        StatusMessage = count == 1
            ? _localization.GetString("Plugin_BulkUninstallingOne")
            : _localization.GetString("Plugin_BulkUninstallingMany", count);

        try
        {
            var leftovers = new List<string>();
            foreach (var id in SelectedPluginIds.ToList())
            {
                var result = await _pluginService.UninstallPluginAsync(id);
                if (result.LeftoverDirectory is not null)
                {
                    leftovers.Add(result.LeftoverDirectory);
                }
            }

            await LoadPluginsAsync();
            Log.Information("Bulk uninstalled {Count} plugins", count);
            StatusMessage = count == 1
                ? _localization.GetString("Plugin_BulkUninstalledOne")
                : _localization.GetString("Plugin_BulkUninstalledMany", count);
            if (leftovers.Count > 0)
            {
                SetError(_localization.GetString("Plugin_BulkLeftoverError", string.Join("; ", leftovers)));
                StatusMessage = count == 1
                    ? _localization.GetString("Plugin_BulkUninstalledLeftoverOne")
                    : _localization.GetString("Plugin_BulkUninstalledLeftoverMany", count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk uninstall failed");
            SetError(_localization.GetString("Plugin_BulkUninstallFailedDetail", ex.Message));
            StatusMessage = _localization.GetString("Plugin_BulkUninstallFailed");
        }
        finally
        {
            SelectedPluginIds.Clear();
            SetSelectionFlags(isSelected: false);
            SelectedCount = 0;
            IsMultiSelectMode = false;
            IsLoading = false;
        }
    }

    // -- Helpers ----------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="PluginDisplayItem"/> from a <see cref="PluginEntity"/>.
    /// </summary>
    private PluginDisplayItem CreateDisplayItem(PluginEntity entity) => new()
    {
        Id = entity.Id,
        PluginId = entity.PluginId,
        Name = entity.Name,
        Version = entity.Version,
        Author = entity.Author,
        Description = entity.Description,
        PluginType = entity.PluginType,
        TypeLabel = string.IsNullOrWhiteSpace(entity.PluginType)
            ? _localization.GetString("Plugin_TypeExtension")
            : char.ToUpperInvariant(entity.PluginType[0]) + entity.PluginType[1..].ToLowerInvariant(),
        InstallPath = entity.InstallPath,
        IsEnabled = entity.IsEnabled,
        InstalledAt = entity.InstalledAt,
        InstalledAtFormatted = FormatHelper.TimeAgoWithMonths(entity.InstalledAt),
        LastActivatedAt = entity.LastActivatedAt,
        LastActivatedAtFormatted = entity.LastActivatedAt.HasValue
            ? FormatHelper.TimeAgoWithMonths(entity.LastActivatedAt.Value)
            : _localization.GetString("Plugin_NeverActivated"),
        SettingsJson = entity.SettingsJson,
        ReadmeContent = entity.ReadmeContent
    };

    private void ApplyPendingOperationsRequest()
    {
        var request = _operationsDrillInService?.ConsumePendingPluginRequest();
        if (request is not null && request.PluginId > 0)
        {
            FocusedPluginId = request.PluginId;
            FocusedPluginSourceLabel = request.SourceLabel;
        }

        if (FocusedPluginId <= 0 || string.IsNullOrWhiteSpace(FocusedPluginSourceLabel))
        {
            ClearPluginFocus(clearStatus: false);
            return;
        }

        var target = Plugins.FirstOrDefault(plugin => plugin.Id == FocusedPluginId);
        if (target is null)
        {
            ClearPluginFocus(clearStatus: false);
            return;
        }

        foreach (var plugin in Plugins)
        {
            plugin.IsFocused = plugin.Id == FocusedPluginId;
        }

        target.IsFocused = true;
        FocusedPluginId = target.Id;
        StatusMessage = FocusedPluginSourceLabel;

        if (Plugins.Remove(target))
        {
            Plugins.Insert(0, target);
        }
    }

    [RelayCommand]
    private void DismissFocusedPluginLanding()
    {
        ClearPluginFocus(clearStatus: true);
    }

    private void ClearPluginFocus(bool clearStatus)
    {
        var sourceLabel = FocusedPluginSourceLabel;

        foreach (var plugin in Plugins)
        {
            plugin.IsFocused = false;
        }

        FocusedPluginId = 0;
        FocusedPluginSourceLabel = string.Empty;

        if (clearStatus && string.Equals(StatusMessage, sourceLabel, StringComparison.Ordinal))
        {
            StatusMessage = BuildInstalledPluginsStatusMessage();
        }
    }

    private bool TryResolveFocusedPluginAction(long pluginId, string? pluginName, out string resolutionMessage)
    {
        resolutionMessage = string.Empty;
        if (FocusedPluginId != pluginId || string.IsNullOrWhiteSpace(FocusedPluginSourceLabel))
        {
            return false;
        }

        ClearPluginFocus(clearStatus: false);
        resolutionMessage = BuildFocusedPluginResolutionMessage(pluginName);
        return true;
    }

    private string BuildFocusedPluginResolutionMessage(string? pluginName) =>
        !string.IsNullOrWhiteSpace(pluginName)
            ? _localization.GetString("Plugin_ResolvedFocused", pluginName)
            : _localization.GetString("Plugin_ResolvedFocusedUnnamed");

    private string BuildInstalledPluginsStatusMessage() => PluginCount switch
    {
        <= 0 => _localization.GetString("Plugin_NoneInstalled"),
        1 => _localization.GetString("Plugin_InstalledOne"),
        _ => _localization.GetString("Plugin_InstalledMany", PluginCount)
    };

    /// <summary>
    /// Asks <see cref="ConfirmDestructiveActionAsync"/>. No handler, or a dialog that fails to
    /// open, counts as "not confirmed": nothing is uninstalled without an answer.
    /// </summary>
    private async Task<bool> IsConfirmedAsync(ConfirmationRequest request)
    {
        if (ConfirmDestructiveActionAsync is not { } confirm)
        {
            Log.Warning("No confirmation handler is attached; '{Title}' was not carried out", request.Title);
            return false;
        }

        try
        {
            return await confirm(request);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The confirmation '{Title}' could not be shown", request.Title);
            return false;
        }
    }

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

    public void Dispose()
    {
        Log.Debug("PluginManagerViewModel disposed");
    }
}

// -- Display Item ----------------------------------------------------------

/// <summary>
/// Observable wrapper around <see cref="PluginEntity"/> fields for data-binding
/// in the Plugin Manager UI. Each property is observable so the UI reflects
/// real-time state changes (e.g. enable/disable toggling).
/// </summary>
public partial class PluginDisplayItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _pluginId = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _pluginType = string.Empty;
    [ObservableProperty] private string _installPath = string.Empty;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private DateTime _installedAt;
    [ObservableProperty] private string _installedAtFormatted = string.Empty;
    [ObservableProperty] private DateTime? _lastActivatedAt;

    /// <summary>When the plugin was last enabled, or the view model's localized "Never".</summary>
    [ObservableProperty] private string _lastActivatedAtFormatted = string.Empty;
    [ObservableProperty] private string? _settingsJson;
    [ObservableProperty] private string? _readmeContent;
    [ObservableProperty] private bool _isFocused;

    /// <summary>
    /// Whether this plugin is ticked in multi-select mode. Lives on the item so the row's
    /// checkbox has something to bind to; the view model's id list alone cannot drive a
    /// per-row control.
    /// </summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Returns a Segoe Fluent Icons glyph string based on the plugin type.
    /// </summary>
    public string TypeGlyph => PluginType?.ToLowerInvariant() switch
    {
        "ai" or "model" => "\uE945",        // Brain / neural
        "tool" or "utility" => "\uE90F",     // Repair / wrench
        "connector" or "integration" => "\uE71B", // Link
        "theme" or "visual" => "\uE771",     // Color
        "data" or "storage" => "\uEDA2",     // Database
        "search" => "\uE721",               // Search
        "chat" or "conversation" => "\uE8BD", // Chat
        "workflow" or "automation" => "\uE9D5", // Flow
        _ => "\uE74C"                         // Puzzle piece / extension
    };

    /// <summary>
    /// Human-readable label for the plugin type (title-cased), set by the view model, which
    /// words a plugin that declares no type in the user's language.
    /// </summary>
    [ObservableProperty] private string _typeLabel = string.Empty;

    /// <summary>
    /// Status label reflecting the enabled state.
    /// </summary>
    public string StatusLabel => IsEnabled ? "ACTIVE" : "DISABLED";
}

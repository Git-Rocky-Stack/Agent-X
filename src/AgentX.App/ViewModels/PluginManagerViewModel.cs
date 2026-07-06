using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class PluginManagerViewModel : ObservableObject, IDisposable
{
    // -- Services ---------------------------------------------------------
    private readonly IPluginService _pluginService;
    private readonly IOperationsDrillInService? _operationsDrillInService;

    // -- Page Properties --------------------------------------------------
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
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

    // -- Constructor ------------------------------------------------------
    public PluginManagerViewModel(
        IPluginService pluginService,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _pluginService = pluginService;
        _operationsDrillInService = operationsDrillInService;
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
            SetError("Failed to load installed plugins. Please try again.");
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
            StatusMessage = "Installing plugin...";

            var installed = await _pluginService.InstallPluginAsync(packagePath);

            // Add the newly installed plugin to the collection
            Plugins.Add(CreateDisplayItem(installed));
            PluginCount = Plugins.Count;

            StatusMessage = $"Successfully installed {installed.Name}";
            Log.Information("Plugin installed: {PluginName} v{Version} (ID: {Id})",
                installed.Name, installed.Version, installed.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install plugin");
            SetError($"Installation failed: {ex.Message}");
            StatusMessage = "Installation failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // -- Uninstall Plugin Command -----------------------------------------
    [RelayCommand]
    private async Task UninstallPluginAsync(long id)
    {
        Log.Information("Uninstalling plugin ID: {PluginId}", id);
        ClearError();

        try
        {
            var target = Plugins.FirstOrDefault(p => p.Id == id);
            var pluginName = target?.Name ?? $"#{id}";

            StatusMessage = $"Uninstalling {pluginName}...";

            await _pluginService.UninstallPluginAsync(id);

            // Remove from local collection
            if (target is not null)
            {
                Plugins.Remove(target);
            }

            PluginCount = Plugins.Count;
            StatusMessage = $"Successfully uninstalled {pluginName}";
            Log.Information("Plugin uninstalled: {PluginName} (ID: {PluginId})", pluginName, id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to uninstall plugin ID: {PluginId}", id);
            SetError($"Failed to uninstall plugin. {ex.Message}");
            StatusMessage = "Uninstall failed";
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

            // Update the local display item
            var target = Plugins.FirstOrDefault(p => p.Id == id);
            if (target is not null)
            {
                target.IsEnabled = true;
                target.LastActivatedAt = DateTime.Now;
                target.LastActivatedAtFormatted = FormatHelper.TimeAgoWithMonths(DateTime.Now);
            }

            if (TryResolveFocusedPluginAction(id, target?.Name, out var resolutionMessage))
            {
                StatusMessage = resolutionMessage;
            }
            else
            {
                StatusMessage = $"Enabled {target?.Name ?? $"plugin #{id}"}";
            }
            Log.Information("Plugin enabled: {PluginId}", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enable plugin ID: {PluginId}", id);
            SetError($"Failed to enable plugin. {ex.Message}");
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

            StatusMessage = $"Disabled {target?.Name ?? $"plugin #{id}"}";
            Log.Information("Plugin disabled: {PluginId}", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to disable plugin ID: {PluginId}", id);
            SetError($"Failed to disable plugin. {ex.Message}");
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

        SelectedCount = SelectedPluginIds.Count;
        Log.Debug("Selected all {Count} plugins", SelectedCount);
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
        StatusMessage = $"Enabling {count} plugins...";

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
                StatusMessage = $"Successfully enabled {count} plugin{(count == 1 ? "" : "s")}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk enable failed");
            SetError($"Bulk enable failed: {ex.Message}");
            StatusMessage = "Bulk enable failed";
        }
        finally
        {
            SelectedPluginIds.Clear();
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
        StatusMessage = $"Disabling {count} plugins...";

        try
        {
            foreach (var id in SelectedPluginIds.ToList())
            {
                await _pluginService.DisablePluginAsync(id);
            }

            await LoadPluginsAsync();
            Log.Information("Bulk disabled {Count} plugins", count);
            StatusMessage = $"Successfully disabled {count} plugin{(count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk disable failed");
            SetError($"Bulk disable failed: {ex.Message}");
            StatusMessage = "Bulk disable failed";
        }
        finally
        {
            SelectedPluginIds.Clear();
            SelectedCount = 0;
            IsLoading = false;
        }
    }

    /// <summary>
    /// Uninstalls all currently selected plugins in bulk.
    /// After completion, the selection is cleared, multi-select mode is exited,
    /// and the plugin list is refreshed.
    /// </summary>
    [RelayCommand]
    private async Task BulkUninstallAsync()
    {
        if (SelectedPluginIds.Count == 0) return;

        var count = SelectedPluginIds.Count;
        Log.Information("Bulk uninstalling {Count} plugins", count);
        ClearError();
        IsLoading = true;
        StatusMessage = $"Uninstalling {count} plugins...";

        try
        {
            foreach (var id in SelectedPluginIds.ToList())
            {
                await _pluginService.UninstallPluginAsync(id);
            }

            await LoadPluginsAsync();
            Log.Information("Bulk uninstalled {Count} plugins", count);
            StatusMessage = $"Successfully uninstalled {count} plugin{(count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk uninstall failed");
            SetError($"Bulk uninstall failed: {ex.Message}");
            StatusMessage = "Bulk uninstall failed";
        }
        finally
        {
            SelectedPluginIds.Clear();
            SelectedCount = 0;
            IsMultiSelectMode = false;
            IsLoading = false;
        }
    }

    // -- Helpers ----------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="PluginDisplayItem"/> from a <see cref="PluginEntity"/>.
    /// </summary>
    private static PluginDisplayItem CreateDisplayItem(PluginEntity entity) => new()
    {
        Id = entity.Id,
        PluginId = entity.PluginId,
        Name = entity.Name,
        Version = entity.Version,
        Author = entity.Author,
        Description = entity.Description,
        PluginType = entity.PluginType,
        InstallPath = entity.InstallPath,
        IsEnabled = entity.IsEnabled,
        InstalledAt = entity.InstalledAt,
        InstalledAtFormatted = FormatHelper.TimeAgoWithMonths(entity.InstalledAt),
        LastActivatedAt = entity.LastActivatedAt,
        LastActivatedAtFormatted = entity.LastActivatedAt.HasValue
            ? FormatHelper.TimeAgoWithMonths(entity.LastActivatedAt.Value)
            : "Never",
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

    private static string BuildFocusedPluginResolutionMessage(string? pluginName)
    {
        var resolvedLabel = !string.IsNullOrWhiteSpace(pluginName)
            ? $"\"{pluginName}\""
            : "the focused connector";
        return $"Resolved {resolvedLabel} by enabling it.";
    }

    private string BuildInstalledPluginsStatusMessage() =>
        PluginCount > 0
            ? $"{PluginCount} plugin{(PluginCount == 1 ? "" : "s")} installed"
            : "No plugins installed";

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
    [ObservableProperty] private string _lastActivatedAtFormatted = "Never";
    [ObservableProperty] private string? _settingsJson;
    [ObservableProperty] private string? _readmeContent;
    [ObservableProperty] private bool _isFocused;

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
    /// Human-readable label for the plugin type (title-cased).
    /// </summary>
    public string TypeLabel => string.IsNullOrWhiteSpace(PluginType)
        ? "Extension"
        : char.ToUpperInvariant(PluginType[0]) + PluginType[1..].ToLowerInvariant();

    /// <summary>
    /// Status label reflecting the enabled state.
    /// </summary>
    public string StatusLabel => IsEnabled ? "ACTIVE" : "DISABLED";
}

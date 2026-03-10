using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Data.Entities;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class PluginManagerViewModel : ObservableObject, IDisposable
{
    // -- Services ---------------------------------------------------------
    private readonly IPluginService _pluginService;

    // -- Page Properties --------------------------------------------------
    [ObservableProperty] private string _pageTitle = "Plugin Manager";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private int _pluginCount;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<PluginDisplayItem> Plugins { get; } = new();

    /// <summary>
    /// Raised when the ViewModel needs the View to show a file picker.
    /// The View subscribes to this and provides the selected file path back.
    /// </summary>
    public event Func<Task<string?>>? FilePickerRequested;

    // -- Constructor ------------------------------------------------------
    public PluginManagerViewModel(IPluginService pluginService)
    {
        _pluginService = pluginService;
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
            StatusMessage = PluginCount > 0
                ? $"{PluginCount} plugin{(PluginCount == 1 ? "" : "s")} installed"
                : "No plugins installed";

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

            StatusMessage = $"Enabled {target?.Name ?? $"plugin #{id}"}";
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
        SettingsJson = entity.SettingsJson
    };

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

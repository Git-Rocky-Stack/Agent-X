using System.ComponentModel;
using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

/// <summary>
/// Plugin Manager page: Two-panel master/detail layout with a scrollable plugin
/// list sidebar on the left and a full detail panel on the right. Handles file
/// picker for plugin installation, toggle enable/disable, and uninstall actions.
/// </summary>
public sealed partial class PluginManagerPage : Page
{
    /// <summary>
    /// The currently selected plugin displayed in the detail panel.
    /// Tracked here because the ViewModel does not own selection state.
    /// </summary>
    private PluginDisplayItem? _selectedPlugin;

    /// <summary>Set while an enable or disable started from the detail switch runs.</summary>
    private bool _pluginChangeInFlight;

    public PluginManagerViewModel ViewModel { get; }

    public PluginManagerPage()
    {
        ViewModel = PageViewModelFactory.Create<PluginManagerViewModel>();
        ViewModel.ConfirmDestructiveActionAsync = request => ConfirmationDialog.ShowAsync(XamlRoot, request);
        InitializeComponent();

        // The page is cached, so Loaded runs on every visit while the constructor runs once.
        // The file-picker request is wired here, symmetrically with Unloaded: wired in the
        // constructor, the first Unloaded removed it for good and Install did nothing after.
        Loaded += async (_, _) =>
        {
            Log.Debug("PluginManagerPage loaded");
            ViewModel.FilePickerRequested -= OnFilePickerRequestedAsync;
            ViewModel.FilePickerRequested += OnFilePickerRequestedAsync;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            await ViewModel.InitializeAsync();
            SelectFocusedPluginFromViewModel();
        };

        Unloaded += (_, _) =>
        {
            ViewModel.FilePickerRequested -= OnFilePickerRequestedAsync;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    // ===============================================================
    // FILE PICKER
    // ===============================================================

    /// <summary>
    /// Shows a file picker for .agentx-plugin / .zip files and returns the
    /// selected path, or null if the user cancelled.
    /// </summary>
    private async Task<string?> OnFilePickerRequestedAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".agentx-plugin");
            picker.FileTypeFilter.Add(".zip");

            // Initialize the picker with the current window handle (required for WinUI 3)
            var window = App.MainWindow;
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();

            if (file is not null)
            {
                Log.Debug("Plugin file selected: {Path}", file.Path);
                return file.Path;
            }

            Log.Debug("File picker cancelled by user");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show file picker for plugin installation");
            return null;
        }
    }

    // ===============================================================
    // SELECTION - MASTER/DETAIL BINDING
    // ===============================================================

    /// <summary>
    /// Uninstalls the selected plugins. Uninstall removes files from disk and cannot be undone,
    /// so the view model asks for confirmation first, the same way as for a single plugin.
    /// </summary>
    private async void OnBulkUninstallPluginsClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.BulkUninstallCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Handles selection changes in the plugin list. Updates the detail
    /// panel to reflect the newly selected plugin, or shows the empty
    /// state when nothing is selected.
    /// </summary>
    private void OnPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PluginListView.SelectedItem is PluginDisplayItem plugin)
        {
            if (_selectedPlugin?.IsFocused == true && _selectedPlugin.Id != plugin.Id)
            {
                ViewModel.DismissFocusedPluginLandingCommand.Execute(null);
            }

            _selectedPlugin = plugin;
            PopulateDetailPanel(plugin);
            DetailPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (_selectedPlugin?.IsFocused == true)
            {
                ViewModel.DismissFocusedPluginLandingCommand.Execute(null);
            }

            _selectedPlugin = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Populates all named elements in the detail panel with data from
    /// the given <see cref="PluginDisplayItem"/>.
    /// </summary>
    private void PopulateDetailPanel(PluginDisplayItem plugin)
    {
        // Header
        DetailIconGlyph.Glyph = plugin.TypeGlyph;
        DetailName.Text = plugin.Name;
        DetailAuthor.Text = plugin.Author;
        UpdateOperationsBadge(plugin);

        // Badges
        DetailVersionBadge.Text = $"v{plugin.Version}";
        DetailTypeBadgeIcon.Glyph = plugin.TypeGlyph;
        DetailTypeBadgeText.Text = plugin.TypeLabel;
        DetailPluginId.Text = plugin.PluginId;

        // Install path tooltip for truncated paths
        ToolTipService.SetToolTip(DetailInstallPath, plugin.InstallPath);

        // Status badge styling
        UpdateStatusBadge(plugin.IsEnabled);

        // Toggle switch - temporarily unhook the event to avoid re-triggering
        DetailToggle.Toggled -= OnPluginToggled;
        DetailToggle.IsOn = plugin.IsEnabled;
        DetailToggle.Tag = plugin.Id;
        DetailToggle.Toggled += OnPluginToggled;

        // Uninstall buttons (both the small icon button and the danger-zone button)
        DetailUninstallSmall.Tag = plugin.Id;
        DetailUninstallButton.Tag = plugin.Id;

        // Description
        DetailDescription.Text = !string.IsNullOrWhiteSpace(plugin.Description)
            ? plugin.Description
            : App.GetService<ILocalizationService>().GetString("Plugin_NoDescription");

        // Details card
        DetailInstallPath.Text = plugin.InstallPath;
        DetailInstalledAt.Text = plugin.InstalledAtFormatted;
        DetailLastActivated.Text = plugin.LastActivatedAtFormatted;

        // Configuration card (only shown if SettingsJson is non-empty)
        if (!string.IsNullOrWhiteSpace(plugin.SettingsJson))
        {
            DetailSettingsCard.Visibility = Visibility.Visible;
            DetailSettingsJson.Text = plugin.SettingsJson;
        }
        else
        {
            DetailSettingsCard.Visibility = Visibility.Collapsed;
        }

        // Documentation card (only shown if ReadmeContent is non-empty)
        if (!string.IsNullOrWhiteSpace(plugin.ReadmeContent))
        {
            DetailDocumentationCard.Visibility = Visibility.Visible;
            var segments = Helpers.MarkdownParser.Parse(plugin.ReadmeContent);
            DetailReadmeContent.Segments = segments;
        }
        else
        {
            DetailDocumentationCard.Visibility = Visibility.Collapsed;
            DetailReadmeContent.Segments = null;
        }
    }

    /// <summary>
    /// Updates the status badge background and text to reflect the
    /// enabled/disabled state.
    /// </summary>
    private void UpdateStatusBadge(bool isEnabled)
    {
        // Enabled plugins run: GO. Disabled plugins are parked, not faulted: STBY, unlit.
        DetailStatusLamp.Code = isEnabled ? "GO" : "STBY";
        DetailStatusLamp.State = isEnabled ? Controls.LampState.Go : Controls.LampState.Off;
    }

    private void UpdateOperationsBadge(PluginDisplayItem plugin)
    {
        if (plugin.IsFocused && !string.IsNullOrWhiteSpace(ViewModel.FocusedPluginSourceLabel))
        {
            DetailOperationsBadgeText.Text = ViewModel.FocusedPluginSourceLabel;
            DetailOperationsBadge.Visibility = Visibility.Visible;
            return;
        }

        DetailOperationsBadge.Visibility = Visibility.Collapsed;
        DetailOperationsBadgeText.Text = string.Empty;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginManagerViewModel.IsLoading))
        {
            if (!ViewModel.IsLoading)
            {
                SelectFocusedPluginFromViewModel();
                RefreshSelectedPluginOperationsBadge();
            }

            return;
        }

        if (e.PropertyName == nameof(PluginManagerViewModel.FocusedPluginId)
            || e.PropertyName == nameof(PluginManagerViewModel.FocusedPluginSourceLabel))
        {
            SelectFocusedPluginFromViewModel();
            RefreshSelectedPluginOperationsBadge();
        }
    }

    private void SelectFocusedPluginFromViewModel()
    {
        if (ViewModel.FocusedPluginId <= 0)
        {
            return;
        }

        var plugin = ViewModel.Plugins.FirstOrDefault(item => item.Id == ViewModel.FocusedPluginId);
        if (plugin is null)
        {
            return;
        }

        PluginListView.SelectedItem = plugin;
        PluginListView.ScrollIntoView(plugin);
    }

    private void RefreshSelectedPluginOperationsBadge()
    {
        if (_selectedPlugin is null)
        {
            return;
        }

        var currentPlugin = ViewModel.Plugins.FirstOrDefault(item => item.Id == _selectedPlugin.Id) ?? _selectedPlugin;
        _selectedPlugin = currentPlugin;
        UpdateOperationsBadge(currentPlugin);
    }

    // ===============================================================
    // PLUGIN ACTIONS - EVENT HANDLERS FOR DATA-TEMPLATE ITEMS
    // ===============================================================

    /// <summary>
    /// Handles the ToggleSwitch Toggled event. The plugin ID is stored in the Tag property so
    /// the correct command can be dispatched. Once the command has finished, the switch and the
    /// status lamp show the state the plugin is in: a failed enable leaves it disabled, so the
    /// switch flips back instead of reading Active over a plugin that is not running. One change
    /// runs at a time; a flip while one runs is undone when it settles.
    /// </summary>
    private async void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: long pluginId } toggle || _pluginChangeInFlight)
        {
            return;
        }

        _pluginChangeInFlight = true;
        try
        {
            if (toggle.IsOn)
            {
                await ViewModel.EnablePluginCommand.ExecuteAsync(pluginId);
            }
            else
            {
                await ViewModel.DisablePluginCommand.ExecuteAsync(pluginId);
            }
        }
        finally
        {
            _pluginChangeInFlight = false;
        }

        ShowSelectedPluginState();
    }

    /// <summary>
    /// Sets the detail switch, status lamp and last activation of the plugin on show from its
    /// real state, which may be another plugin than the one toggled if the selection changed
    /// meanwhile. Toggled is unhooked while the switch is set, so this starts no change.
    /// </summary>
    private void ShowSelectedPluginState()
    {
        if (_selectedPlugin is null)
        {
            return;
        }

        var plugin = ViewModel.Plugins.FirstOrDefault(item => item.Id == _selectedPlugin.Id) ?? _selectedPlugin;
        _selectedPlugin = plugin;

        UpdateStatusBadge(plugin.IsEnabled);
        DetailLastActivated.Text = plugin.LastActivatedAtFormatted;

        DetailToggle.Toggled -= OnPluginToggled;
        DetailToggle.IsOn = plugin.IsEnabled;
        DetailToggle.Toggled += OnPluginToggled;
    }

    /// <summary>
    /// Handles the Uninstall button click. The plugin ID is passed via
    /// the Button's Tag property. The view model asks for confirmation first.
    /// </summary>
    private async void OnUninstallPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long pluginId)
        {
            await ViewModel.UninstallPluginCommand.ExecuteAsync(pluginId);

            // If the selected plugin was uninstalled (not cancelled), clear the detail panel
            if (_selectedPlugin is not null && _selectedPlugin.Id == pluginId
                && ViewModel.Plugins.All(plugin => plugin.Id != pluginId))
            {
                _selectedPlugin = null;
                PluginListView.SelectedItem = null;
                DetailPanel.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }
    }

    // ===============================================================
    // HELPER - EMPTY STATE VISIBILITY
    // ===============================================================

    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when the plugin count is 0
    /// (and NOT loading), used by the sidebar empty state overlay.
    /// </summary>
    private Visibility HasNoPlugins(int pluginCount)
    {
        return pluginCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

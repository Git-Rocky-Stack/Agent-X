using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AgentX.App.ViewModels;
using Serilog;
using System.ComponentModel;
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
    // ═══════════════════════════════════════════════════════════════
    // BRUSHES — cached for status badge rendering
    // ═══════════════════════════════════════════════════════════════

    private static readonly SolidColorBrush ActiveBadgeBackground =
        new(Windows.UI.Color.FromArgb(0x1A, 0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush DisabledBadgeBackground =
        new(Windows.UI.Color.FromArgb(0x1A, 0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush ActiveBadgeForeground =
        new(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush DisabledBadgeForeground =
        new(Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));

    /// <summary>
    /// The currently selected plugin displayed in the detail panel.
    /// Tracked here because the ViewModel does not own selection state.
    /// </summary>
    private PluginDisplayItem? _selectedPlugin;

    public PluginManagerViewModel ViewModel { get; }

    public PluginManagerPage()
    {
        ViewModel = App.GetService<PluginManagerViewModel>();
        InitializeComponent();

        // Wire up the file-picker request from the ViewModel
        ViewModel.FilePickerRequested += OnFilePickerRequestedAsync;

        Loaded += async (_, _) =>
        {
            Log.Debug("PluginManagerPage loaded");
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

    // ═══════════════════════════════════════════════════════════════
    // FILE PICKER
    // ═══════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════
    // SELECTION — MASTER/DETAIL BINDING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles selection changes in the plugin list. Updates the detail
    /// panel to reflect the newly selected plugin, or shows the empty
    /// state when nothing is selected.
    /// </summary>
    private void OnPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PluginListView.SelectedItem is PluginDisplayItem plugin)
        {
            _selectedPlugin = plugin;
            PopulateDetailPanel(plugin);
            DetailPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
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

        // Toggle switch — temporarily unhook the event to avoid re-triggering
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
            : "No description provided.";

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
        if (isEnabled)
        {
            DetailStatusBadge.Background = ActiveBadgeBackground;
            DetailStatusText.Text = "ACTIVE";
            DetailStatusText.Foreground = ActiveBadgeForeground;
        }
        else
        {
            DetailStatusBadge.Background = DisabledBadgeBackground;
            DetailStatusText.Text = "DISABLED";
            DetailStatusText.Foreground = DisabledBadgeForeground;
        }
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
        if (e.PropertyName == nameof(PluginManagerViewModel.FocusedPluginId))
        {
            SelectFocusedPluginFromViewModel();
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

    // ═══════════════════════════════════════════════════════════════
    // PLUGIN ACTIONS — EVENT HANDLERS FOR DATA-TEMPLATE ITEMS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles the ToggleSwitch Toggled event. The plugin ID is stored
    /// in the Tag property so the correct command can be dispatched.
    /// After toggling, refreshes the detail panel to keep it in sync.
    /// </summary>
    private void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is long pluginId)
        {
            if (toggle.IsOn)
            {
                ViewModel.EnablePluginCommand.Execute(pluginId);
            }
            else
            {
                ViewModel.DisablePluginCommand.Execute(pluginId);
            }

            // Refresh the status badge in the detail panel if this is the selected plugin
            if (_selectedPlugin is not null && _selectedPlugin.Id == pluginId)
            {
                UpdateStatusBadge(toggle.IsOn);
            }
        }
    }

    /// <summary>
    /// Handles the Uninstall button click. The plugin ID is passed via
    /// the Button's Tag property.
    /// </summary>
    private void OnUninstallPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long pluginId)
        {
            ViewModel.UninstallPluginCommand.Execute(pluginId);

            // If the uninstalled plugin was selected, clear the detail panel
            if (_selectedPlugin is not null && _selectedPlugin.Id == pluginId)
            {
                _selectedPlugin = null;
                PluginListView.SelectedItem = null;
                DetailPanel.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER — EMPTY STATE VISIBILITY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when the plugin count is 0
    /// (and NOT loading), used by the sidebar empty state overlay.
    /// </summary>
    private Visibility HasNoPlugins(int pluginCount)
    {
        return pluginCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

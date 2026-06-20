using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

// =============================================================================
// SYNC SETTINGS PAGE (Code-Behind)
//
// Hosts the SyncSettingsPage XAML. Responsible for:
//   - Resolving the SyncSettingsViewModel from the DI container
//   - Triggering ViewModel initialization on page load
//   - Handling the folder picker dialog (requires WinRT interop / HWND)
//   - Providing x:Bind helper functions for multi-property visibility logic
//
// All business logic lives in the ViewModel. This code-behind only manages
// platform-specific UI interactions that cannot be expressed in XAML bindings.
// =============================================================================

public sealed partial class SyncSettingsPage : Page
{
    public SyncSettingsViewModel ViewModel { get; }

    public SyncSettingsPage()
    {
        ViewModel = App.GetService<SyncSettingsViewModel>();
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    // =========================================================================
    // PAGE LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Called when the page has loaded and the visual tree is available.
    /// Triggers ViewModel initialization to load configuration, status,
    /// and sync history data.
    /// </summary>
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("SyncSettingsPage loaded");
        await ViewModel.InitializeAsync();
    }

    // =========================================================================
    // FOLDER PICKER
    // =========================================================================

    /// <summary>
    /// Opens a native folder picker dialog for selecting the sync folder path.
    /// The folder picker requires WinRT interop to attach to the current window
    /// handle, which is why this logic lives in the code-behind rather than
    /// the ViewModel.
    /// </summary>
    private async void BrowseSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is not null)
            {
                ViewModel.SyncFolderPath = folder.Path;
                Log.Debug("Sync folder selected: {Path}", folder.Path);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder picker for sync folder");
        }
    }

    // =========================================================================
    // XAML BIND HELPER FUNCTIONS
    // =========================================================================

    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when the sync is not configured
    /// and the page is not currently loading. Used for the unconfigured empty state.
    /// This exists as an x:Bind function because multi-property visibility logic
    /// cannot be expressed with a single IValueConverter.
    /// </summary>
    public Visibility IsUnconfiguredAndNotLoading(bool isConfigured, bool isLoading)
    {
        return !isConfigured && !isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when the history list is empty
    /// and the page is not currently loading. Used for the empty-history placeholder.
    /// </summary>
    public Visibility IsHistoryEmpty(bool hasSyncHistory, bool isLoading)
    {
        return !hasSyncHistory && !isLoading ? Visibility.Visible : Visibility.Collapsed;
    }
}

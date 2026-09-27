using System.ComponentModel;
using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IShortcutRegistry _shortcutRegistry;
    private IDisposable? _shortcutScope;
    private bool _isLoaded;

    public SettingsViewModel ViewModel { get; }

    /// <summary>The Watch Folders section of the page.</summary>
    private WatchFolderSettingsViewModel WatchFolders => ViewModel.WatchFolders;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        _shortcutRegistry = App.GetService<IShortcutRegistry>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            await ViewModel.LoadEncryptionStatusAsync();
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncEncryptionToggle();
            _isLoaded = true;
        };
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _shortcutScope = _shortcutRegistry.RegisterShortcuts(
            new AgentX.Core.Services.Shortcuts.ShortcutDescriptor(
                "settings.save",
                "Save settings",
                new ShortcutScope(nameof(SettingsPage)),
                new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.S) },
                _ => ViewModel.SaveSettingsCommand.ExecuteAsync(null),
                "Settings"));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutScope?.Dispose();
        _shortcutScope = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.EncryptionEnabled))
            SyncEncryptionToggle();
    }

    /// <summary>
    /// Mirrors the view model's encryption state onto the toggle. Toggled also fires for
    /// programmatic IsOn changes, so the handler is unhooked while the state is written;
    /// otherwise every view-model update would re-enter the handler as a new user request.
    /// </summary>
    private void SyncEncryptionToggle()
    {
        EncryptionToggle.Toggled -= EncryptionToggle_Toggled;
        EncryptionToggle.IsOn = ViewModel.EncryptionEnabled;
        EncryptionToggle.Toggled += EncryptionToggle_Toggled;
    }

    private async void EncryptionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Suppress the event until the page has loaded the real encryption state.
        if (!_isLoaded) return;
        await ViewModel.RequestEncryptionStateAsync(EncryptionToggle.IsOn);

        // A declined or failed request can leave EncryptionEnabled unchanged (no PropertyChanged),
        // so always show the real state once the request settles.
        SyncEncryptionToggle();
    }

    /// <summary>
    /// Picks a folder to watch and adds it, including its subfolders when Include subfolders is on.
    /// </summary>
    private async void OnAddWatchFolderClick(object sender, RoutedEventArgs e)
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
                await WatchFolders.AddFolderCommand.ExecuteAsync(folder.Path);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Adding a watch folder failed");
        }
    }

    /// <summary>
    /// Stops watching the folder of the row and forgets it. No confirmation: documents already
    /// imported from it stay in the vault, and the folder can be added again.
    /// </summary>
    private async void OnRemoveWatchFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long watchFolderId })
        {
            await WatchFolders.RemoveFolderCommand.ExecuteAsync(watchFolderId);
        }
    }

    /// <summary>
    /// Confirms before resetting all settings to their defaults (discards the
    /// user's current configuration), then gates the existing reset command on
    /// the dialog's primary result.
    /// </summary>
    private async void OnResetToDefaultsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset to Defaults?",
            Content = "This restores every setting on this page to its default value. " +
                      "Your current configuration will be lost. Continue?",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetToDefaultsCommand.ExecuteAsync(null);
        }
    }
}

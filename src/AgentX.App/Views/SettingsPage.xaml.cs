using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgentX.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IShortcutRegistry _shortcutRegistry;
    private IDisposable? _shortcutScope;
    private bool _isLoaded;

    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        _shortcutRegistry = App.GetService<IShortcutRegistry>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            await ViewModel.LoadEncryptionStatusAsync();
            _isLoaded = true;
        };
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

    private async void EncryptionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Suppress the event while the toggle is being initialized by data binding.
        if (!_isLoaded) return;
        await ViewModel.OnEncryptionToggledAsync();
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

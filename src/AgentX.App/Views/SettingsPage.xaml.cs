using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;

namespace AgentX.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isLoaded;

    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            await ViewModel.LoadEncryptionStatusAsync();
            _isLoaded = true;
        };
    }

    private async void EncryptionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Suppress the event while the toggle is being initialized by data binding.
        if (!_isLoaded) return;
        await ViewModel.OnEncryptionToggledAsync();
    }
}

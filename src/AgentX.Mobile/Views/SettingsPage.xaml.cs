using AgentX.Mobile.ViewModels;

namespace AgentX.Mobile.Views;

/// <summary>
/// Settings page. Allows the user to configure the AgentX desktop API URL,
/// test the connection, and view basic app information.
/// </summary>
public sealed partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The bearer token is read from secure storage asynchronously.
        if (BindingContext is SettingsViewModel vm)
            _ = vm.LoadAsync();
    }
}

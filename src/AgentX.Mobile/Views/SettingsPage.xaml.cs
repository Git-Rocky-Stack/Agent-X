using CommunityToolkit.Maui.Converters;
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

        // Converters referenced by this page's XAML
        Resources.Add("InvertedBoolConverter", new InvertedBoolConverter());
        Resources.Add("StringNotEmptyConverter", new IsStringNotNullOrEmptyConverter());
        Resources.Add("IsNotNullConverter", new IsNotNullConverter());
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The bearer token is read from secure storage asynchronously.
        if (BindingContext is SettingsViewModel vm)
            _ = vm.LoadAsync();
    }
}

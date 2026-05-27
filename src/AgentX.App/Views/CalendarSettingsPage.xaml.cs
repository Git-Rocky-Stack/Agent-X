using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

/// <summary>
/// Calendar Connector Settings page. Allows users to connect/disconnect
/// Google Calendar and Microsoft Outlook, configure sync settings, and
/// view sync status.
/// </summary>
public sealed partial class CalendarSettingsPage : Page
{
    public CalendarSettingsViewModel ViewModel { get; }

    public CalendarSettingsPage()
    {
        ViewModel = App.GetService<CalendarSettingsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnDisconnectGoogleClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDisconnectAsync("Google Calendar"))
        {
            await ViewModel.DisconnectGoogleCommand.ExecuteAsync(null);
        }
    }

    private async void OnDisconnectMicrosoftClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDisconnectAsync("Outlook Calendar"))
        {
            await ViewModel.DisconnectMicrosoftCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Confirms before disconnecting a calendar account so synced credentials
    /// and state aren't removed on an accidental click.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> ConfirmDisconnectAsync(string accountName)
    {
        var dialog = new ContentDialog
        {
            Title = $"Disconnect {accountName}?",
            Content = $"This removes the {accountName} connection and stops syncing. " +
                      "You'll need to reconnect and re-authorize to use it again. Continue?",
            PrimaryButtonText = "Disconnect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
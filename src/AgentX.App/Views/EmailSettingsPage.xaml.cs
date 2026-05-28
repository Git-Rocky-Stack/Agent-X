using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

/// <summary>
/// Email Connector Settings page. Allows users to connect/disconnect
/// Gmail and Outlook, configure sync settings, and view sync status.
/// </summary>
public sealed partial class EmailSettingsPage : Page
{
    public EmailSettingsViewModel ViewModel { get; }

    public EmailSettingsPage()
    {
        ViewModel = App.GetService<EmailSettingsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnDisconnectGoogleClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDisconnectAsync("Gmail"))
        {
            await ViewModel.DisconnectGoogleCommand.ExecuteAsync(null);
        }
    }

    private async void OnDisconnectMicrosoftClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDisconnectAsync("Outlook Email"))
        {
            await ViewModel.DisconnectMicrosoftCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Confirms before disconnecting an email account so synced credentials and
    /// state aren't removed on an accidental click.
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
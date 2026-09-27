using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
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
        // Gmail is a product name, the same in every language.
        if (await ConfirmDisconnectAsync("Gmail"))
        {
            await ViewModel.DisconnectGoogleCommand.ExecuteAsync(null);
        }
    }

    private async void OnDisconnectMicrosoftClick(object sender, RoutedEventArgs e)
    {
        var accountName = App.GetService<ILocalizationService>().GetString("EmailSet_AccountOutlook");
        if (await ConfirmDisconnectAsync(accountName))
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
        var localization = App.GetService<ILocalizationService>();
        var dialog = new ContentDialog
        {
            Title = localization.GetString("EmailSet_DisconnectConfirmTitle", accountName),
            Content = localization.GetString("EmailSet_DisconnectConfirmMessage", accountName),
            PrimaryButtonText = localization.GetString("EmailSet_DisconnectConfirmButton"),
            CloseButtonText = localization.GetString("EmailSet_DisconnectCancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

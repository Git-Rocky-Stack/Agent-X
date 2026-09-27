using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
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
        var accountName = App.GetService<ILocalizationService>().GetString("CalSet_AccountGoogle");
        if (await ConfirmDisconnectAsync(accountName))
        {
            await ViewModel.DisconnectGoogleCommand.ExecuteAsync(null);
        }
    }

    private async void OnDisconnectMicrosoftClick(object sender, RoutedEventArgs e)
    {
        var accountName = App.GetService<ILocalizationService>().GetString("CalSet_AccountOutlook");
        if (await ConfirmDisconnectAsync(accountName))
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
        var localization = App.GetService<ILocalizationService>();
        var dialog = new ContentDialog
        {
            Title = localization.GetString("CalSet_DisconnectConfirmTitle", accountName),
            Content = localization.GetString("CalSet_DisconnectConfirmMessage", accountName),
            PrimaryButtonText = localization.GetString("CalSet_DisconnectConfirmButton"),
            CloseButtonText = localization.GetString("CalSet_DisconnectCancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

using AgentX.App.ViewModels;
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
}
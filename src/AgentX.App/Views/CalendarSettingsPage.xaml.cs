using AgentX.App.ViewModels;
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
}
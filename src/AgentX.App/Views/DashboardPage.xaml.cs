using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AgentX.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.GetService<DashboardViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Submits the dashboard search box on Enter. A search field that only stores text is
    /// a dead end, so this is the control's actual action.
    /// </summary>
    private void OnQuickSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        ViewModel.QuickSearchCommand.Execute(null);
    }

    private void NavigateToPage(string pageTag, object? parameter = null)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag, parameter);
        }
    }
}

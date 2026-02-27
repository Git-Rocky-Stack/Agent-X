using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;

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

    private void NavigateToPage(string pageTag)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag);
        }
    }
}

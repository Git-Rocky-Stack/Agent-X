using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class OperationsPage : Page
{
    public OperationsViewModel ViewModel { get; }

    public OperationsPage()
    {
        ViewModel = App.GetService<OperationsViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void NavigateToPage(string pageTag)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag);
        }
    }
}

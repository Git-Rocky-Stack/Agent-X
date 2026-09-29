using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class AnalyticsPage : Page
{
    public AnalyticsViewModel ViewModel { get; }

    public AnalyticsPage()
    {
        ViewModel = PageViewModelFactory.Create<AnalyticsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadDataAsync();
    }
}

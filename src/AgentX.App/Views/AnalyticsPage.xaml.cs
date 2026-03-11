using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;

namespace AgentX.App.Views;

public sealed partial class AnalyticsPage : Page
{
    public AnalyticsViewModel ViewModel { get; }

    public AnalyticsPage()
    {
        ViewModel = App.GetService<AnalyticsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadDataAsync();
    }
}

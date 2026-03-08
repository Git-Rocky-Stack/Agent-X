using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;
using Serilog;

namespace AgentX.App.Views;

public sealed partial class ComparisonPage : Page
{
    public ComparisonViewModel ViewModel { get; }

    public ComparisonPage()
    {
        ViewModel = App.GetService<ComparisonViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("ComparisonPage loaded");
        await ViewModel.InitializeAsync();
    }
}

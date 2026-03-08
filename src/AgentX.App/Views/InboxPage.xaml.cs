using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;
using Serilog;

namespace AgentX.App.Views;

public sealed partial class InboxPage : Page
{
    public InboxViewModel ViewModel { get; }

    public InboxPage()
    {
        ViewModel = App.GetService<InboxViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("InboxPage loaded");
        await ViewModel.InitializeAsync();
    }
}

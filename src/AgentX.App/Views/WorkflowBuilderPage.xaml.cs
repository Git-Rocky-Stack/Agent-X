using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;
using Serilog;
using Windows.ApplicationModel.DataTransfer;

namespace AgentX.App.Views;

public sealed partial class WorkflowBuilderPage : Page
{
    public WorkflowBuilderViewModel ViewModel { get; }

    public WorkflowBuilderPage()
    {
        ViewModel = App.GetService<WorkflowBuilderViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("WorkflowBuilderPage loaded");
        await ViewModel.InitializeAsync();
    }

    private void CopyOutputToClipboard(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.RunOutput))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(ViewModel.RunOutput);
            Clipboard.SetContent(dataPackage);
            ViewModel.StatusMessage = "Output copied to clipboard";
        }
    }

    /// <summary>
    /// Helper for DataTemplate visibility binding — shows element when int > 0.
    /// </summary>
    public static Visibility IntToVisibility(int value) =>
        value > 0 ? Visibility.Visible : Visibility.Collapsed;
}

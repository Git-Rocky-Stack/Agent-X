using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;
using Serilog;

namespace AgentX.App.Views;

/// <summary>
/// Weekly Digest page: displays activity summaries including document imports,
/// search trends, collection usage, and conversation highlights.
/// </summary>
public sealed partial class DigestPage : Page
{
    public DigestViewModel ViewModel { get; }

    public DigestPage()
    {
        ViewModel = App.GetService<DigestViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Handles report history item clicks in the sidebar.
    /// Routes the selected report to the ViewModel for display.
    /// </summary>
    private void OnReportHistoryItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DigestReportDisplay report)
        {
            ViewModel.SelectReportCommand.Execute(report);
        }
    }
}

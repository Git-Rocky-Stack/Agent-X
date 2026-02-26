using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;

namespace AgentX.App.Views;

public sealed partial class QuickActionsPage : Page
{
    public QuickActionsViewModel ViewModel { get; }

    public QuickActionsPage()
    {
        ViewModel = App.GetService<QuickActionsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    // ── Tab switching via RadioButton Checked events ─────────

    private void Tab_Summarize_Checked(object sender, RoutedEventArgs e)
    {
        SetActivePanel(PanelSummarize);
        ViewModel.SelectedTabIndex = 0;
    }

    private void Tab_KeyPoints_Checked(object sender, RoutedEventArgs e)
    {
        SetActivePanel(PanelKeyPoints);
        ViewModel.SelectedTabIndex = 1;
    }

    private void Tab_Translate_Checked(object sender, RoutedEventArgs e)
    {
        SetActivePanel(PanelTranslate);
        ViewModel.SelectedTabIndex = 2;
    }

    private void Tab_Duplicates_Checked(object sender, RoutedEventArgs e)
    {
        SetActivePanel(PanelDuplicates);
        ViewModel.SelectedTabIndex = 3;
    }

    private void Tab_Organize_Checked(object sender, RoutedEventArgs e)
    {
        SetActivePanel(PanelOrganize);
        ViewModel.SelectedTabIndex = 4;
    }

    /// <summary>
    /// Shows the specified panel and hides all others.
    /// </summary>
    private void SetActivePanel(StackPanel activePanel)
    {
        PanelSummarize.Visibility = Visibility.Collapsed;
        PanelKeyPoints.Visibility = Visibility.Collapsed;
        PanelTranslate.Visibility = Visibility.Collapsed;
        PanelDuplicates.Visibility = Visibility.Collapsed;
        PanelOrganize.Visibility = Visibility.Collapsed;

        activePanel.Visibility = Visibility.Visible;
    }
}

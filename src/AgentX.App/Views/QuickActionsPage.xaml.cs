using System.ComponentModel;
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class QuickActionsPage : Page
{
    public QuickActionsViewModel ViewModel { get; }

    public QuickActionsPage()
    {
        ViewModel = App.GetService<QuickActionsViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            ApplySelectedTab(ViewModel.SelectedTabIndex);
        };
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

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuickActionsViewModel.SelectedTabIndex))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => ApplySelectedTab(ViewModel.SelectedTabIndex));
    }

    /// <summary>
    /// Shows the specified panel and hides all others.
    /// </summary>
    private void SetActivePanel(StackPanel activePanel)
    {
        // IsChecked="True" on TabSummarize raises Checked mid-parse, before the
        // panel fields below are connected; bail out until Loaded re-applies the tab.
        if (PanelSummarize is null || PanelKeyPoints is null || PanelTranslate is null ||
            PanelDuplicates is null || PanelOrganize is null)
        {
            return;
        }

        PanelSummarize.Visibility = Visibility.Collapsed;
        PanelKeyPoints.Visibility = Visibility.Collapsed;
        PanelTranslate.Visibility = Visibility.Collapsed;
        PanelDuplicates.Visibility = Visibility.Collapsed;
        PanelOrganize.Visibility = Visibility.Collapsed;

        activePanel.Visibility = Visibility.Visible;
    }

    private void ApplySelectedTab(int tabIndex)
    {
        switch (tabIndex)
        {
            case 1:
                if (TabKeyPoints.IsChecked != true)
                {
                    TabKeyPoints.IsChecked = true;
                    return;
                }

                SetActivePanel(PanelKeyPoints);
                break;

            case 2:
                if (TabTranslate.IsChecked != true)
                {
                    TabTranslate.IsChecked = true;
                    return;
                }

                SetActivePanel(PanelTranslate);
                break;

            case 3:
                if (TabDuplicates.IsChecked != true)
                {
                    TabDuplicates.IsChecked = true;
                    return;
                }

                SetActivePanel(PanelDuplicates);
                break;

            case 4:
                if (TabOrganize.IsChecked != true)
                {
                    TabOrganize.IsChecked = true;
                    return;
                }

                SetActivePanel(PanelOrganize);
                break;

            default:
                if (TabSummarize.IsChecked != true)
                {
                    TabSummarize.IsChecked = true;
                    return;
                }

                SetActivePanel(PanelSummarize);
                break;
        }
    }

    private void NavigateToPage(string pageTag, object? parameter = null)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag, parameter);
        }
    }
}

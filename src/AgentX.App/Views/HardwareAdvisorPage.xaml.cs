using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class HardwareAdvisorPage : Page
{
    public HardwareAdvisorViewModel ViewModel { get; }

    public HardwareAdvisorPage()
    {
        ViewModel = App.GetService<HardwareAdvisorViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Helper for section visibility: returns Visible when count > 0.
    /// </summary>
    private Visibility HasItems(int count)
    {
        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the Install button click from within the recommended model DataTemplate.
    /// The model name is passed via the Button's Tag property.
    /// </summary>
    private void OnInstallModelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string modelName)
        {
            ViewModel.PullRecommendedModelCommand.Execute(modelName);
        }
    }
}

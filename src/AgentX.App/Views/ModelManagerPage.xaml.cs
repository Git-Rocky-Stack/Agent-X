using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class ModelManagerPage : Page
{
    public ModelManagerViewModel ViewModel { get; }

    public ModelManagerPage()
    {
        ViewModel = App.GetService<ModelManagerViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Helper for empty state visibility: returns Visible when model count is 0.
    /// </summary>
    private Visibility HasNoModels(int totalModels)
    {
        return totalModels == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the Set Active Model button click from within the ItemsRepeater DataTemplate.
    /// The model ID is passed via the Button's Tag property.
    /// </summary>
    private void OnSetActiveModelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string modelId)
        {
            ViewModel.SetActiveModelCommand.Execute(modelId);
        }
    }

    /// <summary>
    /// Handles the Copy Model Name button click from within the ItemsRepeater DataTemplate.
    /// The model name is passed via the Button's Tag property.
    /// </summary>
    private void OnCopyModelNameClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string modelName)
        {
            ViewModel.CopyModelNameCommand.Execute(modelName);
        }
    }

    /// <summary>
    /// Handles the Delete Model button click from within the ItemsRepeater DataTemplate.
    /// The model ID is passed via the Button's Tag property.
    /// </summary>
    private void OnDeleteModelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string modelId)
        {
            ViewModel.DeleteModelCommand.Execute(modelId);
        }
    }
}

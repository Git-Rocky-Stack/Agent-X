using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class ModelManagerPage : Page
{
    public ModelManagerViewModel ViewModel { get; }

    /// <summary>The Speech-to-Text Model section (row 5), bound by the XAML.</summary>
    public SpeechModelViewModel SpeechModel => ViewModel.SpeechModel;

    public ModelManagerPage()
    {
        ViewModel = PageViewModelFactory.Create<ModelManagerViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Stencil word of the speech-to-text lamp: HOLD while the model downloads, GO once it is
    /// installed, STBY while it is not. Equipment vocabulary, not localized.
    /// </summary>
    private string SpeechLampCode(bool isInstalled, bool isDownloading) =>
        isDownloading ? "HOLD" : isInstalled ? "GO" : "STBY";

    /// <summary>LED state matching <see cref="SpeechLampCode"/>: amber, green, or unlit.</summary>
    private Controls.LampState SpeechLampState(bool isInstalled, bool isDownloading) =>
        isDownloading ? Controls.LampState.Hold
        : isInstalled ? Controls.LampState.Go
        : Controls.LampState.Off;

    /// <summary>
    /// Helper for empty state visibility: returns Visible when model count is 0.
    /// </summary>
    private Visibility HasNoModels(int totalModels)
    {
        return totalModels == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// LED tone for the connection dot: GO when linked to Ollama, HOLD amber
    /// when not detected. Mirrors the MDL lamp on the instrument strip so the
    /// page indicator can never contradict the annunciator.
    /// </summary>
    private Microsoft.UI.Xaml.Media.Brush ConnectionBrush(bool isConnected)
    {
        return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isConnected ? "LedGoLampBrush" : "LedHoldLampBrush"];
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

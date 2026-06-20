using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace AgentX.App.Views;

public sealed partial class WebImportPage : Page
{
    public WebImportViewModel ViewModel { get; }

    public WebImportPage()
    {
        ViewModel = App.GetService<WebImportViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("WebImportPage loaded");
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Returns check or X glyph based on success state.
    /// </summary>
    public static string SuccessGlyph(bool success) =>
        success ? "\uE73E" : "\uE711";

    /// <summary>
    /// Returns green or red brush based on success state.
    /// </summary>
    public static SolidColorBrush SuccessBrush(bool success) =>
        success
            ? (SolidColorBrush)Application.Current.Resources["SuccessBrush"]
            : (SolidColorBrush)Application.Current.Resources["ErrorBrush"];

    /// <summary>
    /// Helper for DataTemplate visibility binding.
    /// </summary>
    public static Visibility IntToVisibility(int value) =>
        value > 0 ? Visibility.Visible : Visibility.Collapsed;
}

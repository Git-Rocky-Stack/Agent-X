using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AgentX.App.ViewModels;
using Serilog;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

public sealed partial class AnnotationsPage : Page
{
    public AnnotationsViewModel ViewModel { get; }

    public AnnotationsPage()
    {
        ViewModel = App.GetService<AnnotationsViewModel>();
        InitializeComponent();

        ViewModel.SaveMarkdownExportAsync = SaveMarkdownExportAsync;
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("AnnotationsPage loaded");
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Maps annotation color names to WinUI brushes for the color dots.
    /// </summary>
    public static SolidColorBrush ColorToBrush(string color) => color.ToLowerInvariant() switch
    {
        "yellow" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 204, 21)),
        "green" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 222, 128)),
        "blue" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250)),
        "red" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
        "purple" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 132, 252)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
    };

    private static async Task<AnnotationMarkdownExportResult> SaveMarkdownExportAsync(
        AnnotationMarkdownExportRequest request)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(request.SuggestedFileName),
        };
        picker.FileTypeChoices.Add("Markdown", [".md"]);

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return AnnotationMarkdownExportResult.Cancelled();
        }

        await FileIO.WriteTextAsync(file, request.Markdown);
        return AnnotationMarkdownExportResult.Saved(file.Path);
    }
}

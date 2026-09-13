using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    /// x:Bind entry point for the colour dots. The inks themselves live in
    /// <see cref="AnnotationInk"/>, the one file the palette hue guard exempts.
    /// </summary>
    public static SolidColorBrush ColorToBrush(string color) => AnnotationInk.BrushFor(color);

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

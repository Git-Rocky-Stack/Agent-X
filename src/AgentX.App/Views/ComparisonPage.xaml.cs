using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

public sealed partial class ComparisonPage : Page
{
    public ComparisonViewModel ViewModel { get; }

    public ComparisonPage()
    {
        ViewModel = App.GetService<ComparisonViewModel>();
        InitializeComponent();

        ViewModel.SaveReportExportAsync = SaveReportExportAsync;
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("ComparisonPage loaded");
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Lets the user choose where the exported Markdown report is written, then writes it.
    /// </summary>
    private static async Task<ComparisonReportExportResult> SaveReportExportAsync(
        ComparisonReportExportRequest request)
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
            return ComparisonReportExportResult.Cancelled();
        }

        await FileIO.WriteTextAsync(file, request.Markdown);
        return ComparisonReportExportResult.Saved(file.Path);
    }
}

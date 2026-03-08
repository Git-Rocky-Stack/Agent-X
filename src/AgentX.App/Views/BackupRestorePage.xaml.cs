using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;
using Serilog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

public sealed partial class BackupRestorePage : Page
{
    public BackupRestoreViewModel ViewModel { get; }

    public BackupRestorePage()
    {
        ViewModel = App.GetService<BackupRestoreViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("BackupRestorePage loaded");
        await ViewModel.InitializeAsync();
    }

    private async void BrowseBackupDestination(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        folderPicker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.BackupDestination = folder.Path;
        }
    }

    private async void BrowseRestoreFile(object sender, RoutedEventArgs e)
    {
        var filePicker = new FileOpenPicker();
        filePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        filePicker.FileTypeFilter.Add(".agentxbak");
        filePicker.FileTypeFilter.Add(".zip");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(filePicker, hwnd);

        var file = await filePicker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.RestoreFilePath = file.Path;
        }
    }
}

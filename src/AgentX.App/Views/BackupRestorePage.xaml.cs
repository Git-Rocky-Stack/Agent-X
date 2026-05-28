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

    /// <summary>
    /// Confirms before restoring — restore overwrites the entire knowledge base
    /// and is not reversible — then gates the existing restore command on the
    /// dialog's primary result.
    /// </summary>
    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.RestoreFilePath))
        {
            ViewModel.StatusMessage = "Please select a backup file to restore";
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore from Backup?",
            Content = "Restoring will overwrite your current knowledge base — " +
                      "documents, conversations, and workflows will be replaced with the " +
                      "backup's contents. This cannot be undone. Continue?",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RestoreFromBackupCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Confirms before permanently deleting a stored backup, then gates the
    /// existing delete command on the dialog's primary result.
    /// </summary>
    private async void OnDeleteBackupClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long backupId })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete Backup?",
            Content = "This permanently deletes the selected backup file. " +
                      "This cannot be undone. Continue?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteBackupCommand.ExecuteAsync(backupId);
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

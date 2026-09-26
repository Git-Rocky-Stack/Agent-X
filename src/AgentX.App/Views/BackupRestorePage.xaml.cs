using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Unloaded += (_, _) => ViewModel.BackupPasswordRequested -= PromptForBackupPasswordAsync;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("BackupRestorePage loaded");
        // The page is cached across navigations, so subscribe on every load (idempotently).
        ViewModel.BackupPasswordRequested -= PromptForBackupPasswordAsync;
        ViewModel.BackupPasswordRequested += PromptForBackupPasswordAsync;
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Asks for the password of an encrypted backup before it is restored. Returns null when
    /// the user cancels. Same ContentDialog + PasswordBox pattern as the database unlock prompt.
    /// </summary>
    private async Task<string?> PromptForBackupPasswordAsync()
    {
        var box = new PasswordBox
        {
            Header = "Backup password",
            PlaceholderText = "Password used when the backup was created"
        };

        var dialog = new ContentDialog
        {
            Title = "Encrypted Backup",
            Content = box,
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
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
                      "backup's contents. This cannot be undone, and Agent-X must be restarted " +
                      "afterwards. Continue?",
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
    /// Restores directly from a row in the backup history. Restore overwrites the whole
    /// knowledge base and cannot be undone, so this takes the same confirmation gate as
    /// the page-level restore rather than acting on a single click.
    /// </summary>
    private async void OnRestoreFromHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string filePath } || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore from Backup?",
            Content = "Restoring will overwrite your current knowledge base — " +
                      "documents, conversations, and workflows will be replaced with the " +
                      "backup's contents. This cannot be undone, and Agent-X must be restarted " +
                      "afterwards. Continue?",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RestoreFromHistoryCommand.ExecuteAsync(filePath);
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

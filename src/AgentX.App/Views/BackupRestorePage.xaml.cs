using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
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
        var localization = App.GetService<ILocalizationService>();
        var box = new PasswordBox
        {
            Header = localization.GetString("Backup_PasswordPromptHeader"),
            PlaceholderText = localization.GetString("Backup_PasswordPromptPlaceholder")
        };

        var dialog = new ContentDialog
        {
            Title = localization.GetString("Backup_PasswordPromptTitle"),
            Content = box,
            PrimaryButtonText = localization.GetString("Backup_RestoreButton"),
            CloseButtonText = localization.GetString("Backup_CancelButton"),
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

    /// <summary>Picks the folder scheduled backups are written to (saved with Save Schedule).</summary>
    private async void BrowseScheduleDestination(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is not null)
            {
                ViewModel.ScheduledBackupDestination = folder.Path;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not pick a folder for scheduled backups");
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
            ViewModel.StatusMessage = App.GetService<ILocalizationService>().GetString("Backup_SelectRestoreFile");
            return;
        }

        if (await ConfirmRestoreAsync())
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

        if (await ConfirmRestoreAsync())
        {
            await ViewModel.RestoreFromHistoryCommand.ExecuteAsync(filePath);
        }
    }

    /// <summary>
    /// Asks before a restore replaces the knowledge base. Cancel is the default button, so Enter
    /// never starts a restore by accident. True when the user chose Restore.
    /// </summary>
    private async Task<bool> ConfirmRestoreAsync()
    {
        var localization = App.GetService<ILocalizationService>();
        var dialog = new ContentDialog
        {
            Title = localization.GetString("Backup_RestoreConfirmTitle"),
            Content = localization.GetString("Backup_RestoreConfirmMessage"),
            PrimaryButtonText = localization.GetString("Backup_RestoreButton"),
            CloseButtonText = localization.GetString("Backup_CancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

        var localization = App.GetService<ILocalizationService>();
        var dialog = new ContentDialog
        {
            Title = localization.GetString("Backup_DeleteConfirmTitle"),
            Content = localization.GetString("Backup_DeleteConfirmMessage"),
            PrimaryButtonText = localization.GetString("Backup_DeleteButton"),
            CloseButtonText = localization.GetString("Backup_CancelButton"),
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

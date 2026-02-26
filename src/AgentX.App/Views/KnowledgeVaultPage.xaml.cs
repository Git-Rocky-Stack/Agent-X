using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;
using Serilog;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;

namespace AgentX.App.Views;

/// <summary>
/// Knowledge Vault page: Premium document management UI.
/// Handles file import (file picker, folder picker, drag-and-drop),
/// filter chip toggling, and document action button clicks.
/// </summary>
public sealed partial class KnowledgeVaultPage : Page
{
    public KnowledgeVaultViewModel ViewModel { get; }

    public KnowledgeVaultPage()
    {
        ViewModel = App.GetService<KnowledgeVaultViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // FILE IMPORT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens a file picker for selecting individual files to import.
    /// WinUI 3 requires the window handle for the picker.
    /// </summary>
    private async void OnImportFilesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            // Add supported file types
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".doc");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".html");
            picker.FileTypeFilter.Add(".htm");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".rtf");
            picker.FileTypeFilter.Add(".py");
            picker.FileTypeFilter.Add(".cs");
            picker.FileTypeFilter.Add(".js");
            picker.FileTypeFilter.Add(".ts");
            picker.FileTypeFilter.Add(".java");
            picker.FileTypeFilter.Add(".cpp");
            picker.FileTypeFilter.Add(".c");
            picker.FileTypeFilter.Add(".h");

            // Initialize the picker with the window handle
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files is not null && files.Count > 0)
            {
                var filePaths = files.Select(f => f.Path).ToList();
                await ViewModel.ImportFilesCommand.ExecuteAsync(filePaths);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open file picker");
        }
    }

    /// <summary>
    /// Opens a folder picker for importing all supported files from a directory.
    /// </summary>
    private async void OnImportFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            // Initialize the picker with the window handle
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                await ViewModel.ImportFolderCommand.ExecuteAsync(folder.Path);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder picker");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DRAG AND DROP HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop to import";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;

        // Visual feedback: apply active drop zone style
        if (sender is Border border)
        {
            border.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                (Windows.UI.Color)Application.Current.Resources["RedGlow10"]);
            border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                (Windows.UI.Color)Application.Current.Resources["Red500"]);
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        if (sender is Border border)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InputBackgroundBrush"];
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderMediumBrush"];
        }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        if (sender is Border border)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InputBackgroundBrush"];
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderMediumBrush"];
        }

        try
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var filePaths = new List<string>();

                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        filePaths.Add(file.Path);
                    }
                    else if (item is StorageFolder folder)
                    {
                        // For dropped folders, use the folder import path
                        await ViewModel.ImportFolderCommand.ExecuteAsync(folder.Path);
                        return;
                    }
                }

                if (filePaths.Count > 0)
                {
                    await ViewModel.HandleDroppedFilesAsync(filePaths);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle dropped files");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // FILTER CHIP HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private void OnFilterTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var type = button.Tag as string;
            var filter = string.IsNullOrEmpty(type) ? null : type;
            ViewModel.FilterByTypeCommand.Execute(filter);
        }
    }

    private void OnFilterStatusClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var status = button.Tag as string;
            var filter = string.IsNullOrEmpty(status) ? null : status;
            ViewModel.FilterByStatusCommand.Execute(filter);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DOCUMENT ACTION BUTTON HANDLERS
    // These bridge the DataTemplate button clicks to ViewModel commands,
    // since x:Bind with CommandParameter inside ItemsRepeater DataTemplates
    // does not support binding to ViewModel commands directly.
    // ═══════════════════════════════════════════════════════════════

    private void OnViewDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            ViewModel.ViewDocumentDetailCommand.Execute(id);
        }
    }

    private void OnReindexClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            ViewModel.ReindexDocumentCommand.Execute(id);
        }
    }

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string filePath)
        {
            ViewModel.OpenInExplorerCommand.Execute(filePath);
        }
    }

    private void OnDeleteDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            ViewModel.DeleteDocumentCommand.Execute(id);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // VISIBILITY HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns Visible when there are no documents and the drop zone is not
    /// already shown (to avoid duplicate empty states).
    /// </summary>
    private Visibility HasNoDocumentsVisible(long totalDocuments, bool showDropZone)
    {
        return totalDocuments == 0 && !showDropZone ? Visibility.Visible : Visibility.Collapsed;
    }
}

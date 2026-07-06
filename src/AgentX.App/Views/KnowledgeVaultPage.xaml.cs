using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

/// <summary>
/// Knowledge Vault page: Premium document management UI.
/// Handles file import (file picker, folder picker, drag-and-drop),
/// filter chip toggling, and document action button clicks.
/// </summary>
public sealed partial class KnowledgeVaultPage : Page
{
    private readonly IShortcutRegistry _shortcutRegistry;
    private IDisposable? _shortcutScope;

    public KnowledgeVaultViewModel ViewModel { get; }

    public KnowledgeVaultPage()
    {
        ViewModel = App.GetService<KnowledgeVaultViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        _shortcutRegistry = App.GetService<IShortcutRegistry>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _shortcutScope = _shortcutRegistry.RegisterShortcuts(
            new AgentX.Core.Services.Shortcuts.ShortcutDescriptor(
                "vault.refresh",
                "Refresh documents",
                new ShortcutScope(nameof(KnowledgeVaultPage)),
                new[] { new KeyChord(KeyModifiers.None, VirtualKeyCode.F5) },
                _ => ViewModel.RefreshCommand.ExecuteAsync(null),
                "Documents"));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutScope?.Dispose();
        _shortcutScope = null;
    }

    private void NavigateToPage(string pageTag)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag);
        }
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
                await ViewModel.ImportWithDedupCommand.ExecuteAsync(filePaths);
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
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"];
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderMediumBrush"];
        }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        if (sender is Border border)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"];
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
    // TAG FILTER HANDLER (Feature 7)
    // ═══════════════════════════════════════════════════════════════

    private void OnFilterTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var tagName = button.Tag as string;
            ViewModel.FilterByTagCommand.Execute(tagName);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // MULTI-SELECT HANDLER (Feature 8)
    // ═══════════════════════════════════════════════════════════════

    private void OnDocumentCheckToggle(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is long id)
        {
            ViewModel.ToggleDocumentSelectionCommand.Execute(id);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ADVANCED FILTER HANDLERS (Feature 9)
    // ═══════════════════════════════════════════════════════════════

    private void OnCollectionFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ViewModels.CollectionFilterItem item)
            {
                ViewModel.CollectionFilter = item.Id;
            }
            else
            {
                ViewModel.CollectionFilter = null;
            }
        }
    }

    private void OnDateAfterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            ViewModel.DateAfterFilter = args.NewDate.Value.DateTime;
        }
        else
        {
            ViewModel.DateAfterFilter = null;
        }
    }

    private void OnDateBeforeChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            ViewModel.DateBeforeFilter = args.NewDate.Value.DateTime;
        }
        else
        {
            ViewModel.DateBeforeFilter = null;
        }
    }

    private void OnSortByChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
        {
            var sort = item.Tag as string ?? "date";
            ViewModel.SortBy = sort;
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
            ViewModel.SelectDocumentCommand.Execute(id);
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

    private async void OnLaunchDocumentInWorkflowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            await ViewModel.LaunchDocumentInWorkflowCommand.ExecuteAsync(id);
        }
    }

    private void OnGenerateTitleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            ViewModel.GenerateTitleCommand.Execute(id);
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

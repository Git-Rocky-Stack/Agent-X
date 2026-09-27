using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
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
    // The frame caches this page (NavigationCacheMode="Enabled") and only builds a new instance
    // after it has evicted the previous one. The evicted page's view model is still subscribed
    // to the singleton indexing service's events, so it is released here.
    private static KnowledgeVaultViewModel? s_liveViewModel;

    private readonly IShortcutRegistry _shortcutRegistry;
    private IDisposable? _shortcutScope;

    public KnowledgeVaultViewModel ViewModel { get; }

    /// <summary>The preview's document text and annotations.</summary>
    private DocumentNotesViewModel Notes => ViewModel.Notes;

    public KnowledgeVaultPage()
    {
        ViewModel = PageViewModelFactory.Create<KnowledgeVaultViewModel>();
        Interlocked.Exchange(ref s_liveViewModel, ViewModel)?.Dispose();
        ViewModel.NavigateRequested = NavigateToPage;
        ViewModel.ConfirmDeleteAsync = ConfirmDeleteAsync;
        _shortcutRegistry = App.GetService<IShortcutRegistry>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // The previewed document counts as read while the page is on screen. Resume before the
        // navigation parameter can open another document, so that one is timed too.
        ViewModel.ResumeDocumentEngagement();

        // Honour the item the caller picked (Jump-To, command palette) rather than
        // dropping it and opening this page on whatever was last active.
        _ = ViewModel.ApplyNavigationParameterAsync(e.Parameter);

        // The palette's "Import Files" lands here with this intent. The picker needs
        // the window handle, which is why the intent is honoured in code-behind.
        if (e.Parameter is string intent && intent == NavigationIntents.ImportFiles)
        {
            OnImportFilesClick(this, new RoutedEventArgs());
        }

        var localization = App.GetService<ILocalizationService>();
        _shortcutScope = _shortcutRegistry.RegisterShortcuts(
            new AgentX.Core.Services.Shortcuts.ShortcutDescriptor(
                "vault.refresh",
                localization.GetString("Vault_ShortcutRefreshDocuments"),
                new ShortcutScope(nameof(KnowledgeVaultPage)),
                new[] { new KeyChord(KeyModifiers.None, VirtualKeyCode.F5) },
                _ => ViewModel.RefreshCommand.ExecuteAsync(null),
                localization.GetString("Vault_ShortcutCategory")));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutScope?.Dispose();
        _shortcutScope = null;
        _ = ViewModel.PauseDocumentEngagementAsync();
    }

    private void NavigateToPage(string pageTag, object? parameter = null)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag, parameter);
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

            // Every format a document processor reads, the built-in ones and those of active
            // plugins: the formats folder imports pick up, and nothing the import would reject.
            // Each entry starts with a dot and appears once, as the picker requires.
            foreach (var fileType in ViewModel.GetImportFileTypes())
            {
                picker.FileTypeFilter.Add(fileType);
            }

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
        e.DragUIOverride.Caption = App.GetService<ILocalizationService>().GetString("Vault_DropToImport");
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;

        // Visual feedback: apply the active drop zone style. This style already
        // existed and described exactly this state; the hand-rolled brushes that
        // used to live here were an unwired duplicate of it.
        if (sender is Border border)
        {
            border.Style = (Style)Application.Current.Resources["DropZoneActiveStyle"];
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        if (sender is Border border)
        {
            border.Style = (Style)Application.Current.Resources["DropZoneStyle"];
        }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        if (sender is Border border)
        {
            border.Style = (Style)Application.Current.Resources["DropZoneStyle"];
        }

        try
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var filePaths = new List<string>();
                var folderPaths = new List<string>();

                // Collect every dropped item; files and folders can be mixed in one drop.
                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        filePaths.Add(file.Path);
                    }
                    else if (item is StorageFolder folder)
                    {
                        folderPaths.Add(folder.Path);
                    }
                }

                await ViewModel.HandleDroppedItemsAsync(filePaths, folderPaths);
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

    /// <summary>
    /// Asks before documents are deleted: one by name, several by count. The view model
    /// deletes only on a yes; the files on disk are never touched.
    /// </summary>
    private async Task<bool> ConfirmDeleteAsync(DocumentDeletionRequest request)
    {
        var localization = App.GetService<ILocalizationService>();
        var (title, body) = request.DocumentName is { } name
            ? (localization.GetString("Vault_DeleteDocumentTitle"),
               localization.GetString("Vault_DeleteDocumentBody", name))
            : (localization.GetString("Vault_DeleteDocumentsTitle"),
               localization.GetString("Vault_DeleteDocumentsBody", request.Count));

        var dialog = new ContentDialog
        {
            Title = title,
            Content = body,
            PrimaryButtonText = localization.GetString("Vault_DeleteConfirm"),
            CloseButtonText = localization.GetString("Vault_DeleteCancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Preview annotations

    /// <summary>
    /// Hands the text selected in the preview's passage to the view model. The selection start
    /// is a text pointer whose offset also counts element boundaries, so it only tells the view
    /// model which occurrence of the selected text was meant.
    /// </summary>
    private void OnPassageSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock passage)
        {
            Notes.CaptureSelection(passage.SelectedText, passage.SelectionStart?.Offset ?? -1);
        }
    }

    private async void OnDeleteAnnotationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long annotationId)
        {
            await Notes.DeleteAnnotationCommand.ExecuteAsync(annotationId);
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

using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AgentX.App.Views;

/// <summary>
/// Collection Manager page: Two-panel layout with collection tree on the left
/// and selected collection detail on the right.
/// </summary>
public sealed partial class CollectionManagerPage : Page
{
    public CollectionManagerViewModel ViewModel { get; }

    public CollectionManagerPage()
    {
        ViewModel = PageViewModelFactory.Create<CollectionManagerViewModel>();
        ViewModel.ConfirmDestructiveActionAsync = request => ConfirmationDialog.ShowAsync(XamlRoot, request);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // COLLECTION TREE EVENT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles click on a collection item in the tree.
    /// Selects the collection and loads its documents.
    /// </summary>
    private void OnCollectionItemClick(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is CollectionDisplayItem collection)
        {
            ViewModel.SelectCollectionCommand.Execute(collection);
        }
    }

    /// <summary>
    /// Handles the delete button click for a collection. The view model asks for confirmation
    /// (through <see cref="CollectionManagerViewModel.ConfirmDestructiveActionAsync"/>) first.
    /// </summary>
    private void OnDeleteCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            ViewModel.DeleteCollectionCommand.Execute(id);
        }
    }

    /// <summary>
    /// Deletes the selected collections. The view model asks for confirmation first, the same
    /// way as for a single collection.
    /// </summary>
    private async void OnBulkDeleteCollectionsClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.BulkDeleteCollectionsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Exports every document in a collection through the shared export pipeline.
    /// </summary>
    private async void OnExportCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long collectionId } || collectionId <= 0)
        {
            return;
        }

        var exportViewModel = App.GetService<ExportViewModel>();
        await exportViewModel.ExportCollectionCommand.ExecuteAsync(
            new ExportCollectionRequest(collectionId));

        Log.Information("Collection {CollectionId} export finished: {Status}",
            collectionId, exportViewModel.StatusMessage);

        // The outcome used to reach only the log: tell the user where the file went, or why
        // there is none. The success text is one sentence in the resources, so each language
        // words the file name and the path together.
        var notifications = App.GetService<AgentX.App.Services.INotificationService>();
        var localization = App.GetService<ILocalizationService>();
        if (exportViewModel.LastExportSucceeded)
        {
            var savedPath = exportViewModel.LastExportPath ?? string.Empty;
            notifications.ShowSuccess(
                localization.GetString("Export_CompleteTitle"),
                localization.GetString("Export_CollectionSaved", Path.GetFileName(savedPath), savedPath),
                durationMs: 8000);
        }
        else
        {
            notifications.ShowError(localization.GetString("Export_FailedTitle"), exportViewModel.StatusMessage);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DOCUMENT MANAGEMENT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens a file picker and hands the picked files to the view model, which imports them,
    /// adds them (or the documents they duplicate) to the selected collection and reports the
    /// outcome.
    /// </summary>
    private async void OnAddDocumentsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            // No legacy binary Word files: the OpenXml reader behind DocxProcessor cannot open them.
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".html");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files is not null && files.Count > 0)
            {
                var filePaths = files.Select(f => f.Path).ToList();
                await ViewModel.AddFilesToCollectionCommand.ExecuteAsync(filePaths);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import and add documents to collection");
        }
    }

    /// <summary>
    /// Handles the remove document from collection button click.
    /// </summary>
    private void OnRemoveDocumentFromCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long docId)
        {
            ViewModel.RemoveDocumentFromCollectionCommand.Execute(docId);
        }
    }
}

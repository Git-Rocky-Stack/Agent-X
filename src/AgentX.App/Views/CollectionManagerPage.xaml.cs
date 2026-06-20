using AgentX.App.ViewModels;
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
        ViewModel = App.GetService<CollectionManagerViewModel>();
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
    /// Handles the delete button click for a collection.
    /// </summary>
    private void OnDeleteCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id)
        {
            ViewModel.DeleteCollectionCommand.Execute(id);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DOCUMENT MANAGEMENT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens a file picker to select files, imports them via IDocumentService,
    /// then associates the resulting document IDs with the selected collection.
    /// </summary>
    private async void OnAddDocumentsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".doc");
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
                // Import files first, then associate with collection
                var documentService = App.GetService<AgentX.Core.Documents.IDocumentService>();
                var filePaths = files.Select(f => f.Path).ToList();
                var importedDocs = await documentService.ImportFilesAsync(filePaths);
                var docIds = importedDocs.Select(d => d.Id).ToList();
                await ViewModel.AddDocumentsToCollectionCommand.ExecuteAsync(docIds);
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

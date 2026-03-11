using AgentX.Mobile.ViewModels;

namespace AgentX.Mobile.Views;

/// <summary>
/// Displays all documents from the AgentX knowledge vault via the REST API.
/// Refreshes when the page appears and when the user taps the toolbar refresh button.
/// </summary>
public sealed partial class DocumentsPage : ContentPage
{
    private readonly DocumentsViewModel _vm;

    public DocumentsPage(DocumentsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        // Register the inverted bool converter referenced in XAML
        Resources.Add("InvertedBoolConverter", new CommunityToolkit.Maui.Converters.InvertedBoolConverter());
    }

    /// <inheritdoc/>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Only load on first appearance or when the list is empty to avoid
        // redundant network calls every time the user switches tabs.
        if (_vm.Documents.Count == 0)
        {
            await _vm.LoadAsync().ConfigureAwait(false);
        }
    }
}

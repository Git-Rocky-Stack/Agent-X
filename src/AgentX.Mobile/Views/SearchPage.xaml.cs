using AgentX.Mobile.ViewModels;

namespace AgentX.Mobile.Views;

/// <summary>
/// Semantic search page. Accepts a natural language query, posts it to
/// POST /api/search on the AgentX desktop app, and displays ranked results.
/// </summary>
public sealed partial class SearchPage : ContentPage
{
    public SearchPage(SearchViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        Resources.Add("InvertedBoolConverter", new CommunityToolkit.Maui.Converters.InvertedBoolConverter());
    }
}

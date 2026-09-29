namespace AgentX.Core.Services.Search;

/// <summary>
/// The web search providers a user can pick in Settings, in picker order, the names the picker
/// shows for them, and the mapping between a picker index and the saved
/// <see cref="WebSearchProvider"/>.
/// </summary>
/// <remarks>
/// The picker listed the enum members themselves, so SearXNG read "SearXng". The names are
/// product names, so they are the same in every language. The setting is saved as the enum
/// value, never as the name, and a saved value that is not in the list is shown as "nothing
/// selected" and saved back unchanged.
/// </remarks>
public static class WebSearchProviderChoices
{
    /// <summary>Providers and display names, in picker order.</summary>
    public static IReadOnlyList<(WebSearchProvider Provider, string DisplayName)> All { get; } =
    [
        (WebSearchProvider.Brave, "Brave"),
        (WebSearchProvider.Serper, "Serper"),
        (WebSearchProvider.SearXng, "SearXNG"),
    ];

    /// <summary>Display names, in picker order.</summary>
    public static IReadOnlyList<string> DisplayNames { get; } = All.Select(choice => choice.DisplayName).ToList();

    /// <summary>Index of <paramref name="provider"/> in <see cref="All"/>, or -1 when it is not listed.</summary>
    public static int IndexOf(WebSearchProvider provider)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Provider == provider)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The provider to save for a picker selection. An index outside the list (nothing selected,
    /// which is how an unlisted saved value is shown) keeps <paramref name="savedProvider"/>.
    /// </summary>
    public static WebSearchProvider ResolveSelection(int selectedIndex, WebSearchProvider savedProvider) =>
        selectedIndex >= 0 && selectedIndex < All.Count ? All[selectedIndex].Provider : savedProvider;
}

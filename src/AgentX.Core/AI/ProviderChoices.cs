namespace AgentX.Core.AI;

/// <summary>
/// The providers a user can pick as the active AI provider, in picker order, and the mapping
/// between a picker index and the saved provider id.
/// </summary>
/// <remarks>
/// The built-in model is listed first because it is the default provider. A saved id that is
/// not in the list is shown as "nothing selected" and saved back unchanged, so opening and
/// saving the settings page never switches the provider on its own.
/// </remarks>
public static class ProviderChoices
{
    /// <summary>Provider ids and display names, in picker order.</summary>
    public static IReadOnlyList<(string Id, string DisplayName)> All { get; } =
    [
        ("local", "Built-in LLM (Local)"),
        ("ollama", "Ollama (Local)"),
        ("openai", "OpenAI"),
        ("anthropic", "Anthropic Claude"),
    ];

    /// <summary>Display names, in picker order.</summary>
    public static IReadOnlyList<string> DisplayNames { get; } = All.Select(choice => choice.DisplayName).ToList();

    /// <summary>Index of <paramref name="providerId"/> in <see cref="All"/>, or -1 when it is not listed.</summary>
    public static int IndexOf(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return -1;

        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Id, providerId.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The provider id to save for a picker selection. An index outside the list (nothing
    /// selected, which is how an unlisted saved id is shown) keeps <paramref name="savedProviderId"/>,
    /// or falls back to the built-in provider when nothing was saved.
    /// </summary>
    public static string ResolveSelection(int selectedIndex, string? savedProviderId)
    {
        if (selectedIndex >= 0 && selectedIndex < All.Count)
            return All[selectedIndex].Id;

        return string.IsNullOrWhiteSpace(savedProviderId) ? All[0].Id : savedProviderId.Trim();
    }
}

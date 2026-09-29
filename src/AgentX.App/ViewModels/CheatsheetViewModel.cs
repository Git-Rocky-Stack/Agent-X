using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels;

public sealed class CheatsheetGroup
{
    public required string Header { get; init; }
    public required IReadOnlyList<ShortcutDescriptor> Items { get; init; }
    public bool IsCurrentScope { get; init; }

    /// <summary>The "Current page" marker, in the user's language, on the group of the page in view.</summary>
    public string CurrentScopeLabel { get; init; } = string.Empty;
}

public partial class CheatsheetViewModel : ObservableObject
{
    /// <param name="localization">
    /// Words the "Current page" marker. Without it (or without the resource) the marker is English.
    /// </param>
    public CheatsheetViewModel(
        IShortcutRegistry registry,
        string? activeScopeName,
        ILocalizationService? localization = null)
    {
        var available = string.IsNullOrWhiteSpace(activeScopeName)
            ? registry.All().Where(d => d.Scope.IsGlobal)
            : registry.ForScope(activeScopeName);

        var currentPage = ProviderStatusText.Resolve(
            localization?.GetString("Cheatsheet_CurrentPage"), "Cheatsheet_CurrentPage", "Current page");

        Groups = new ObservableCollection<CheatsheetGroup>(
            available
                .GroupBy(d => d.Category ?? (d.Scope.IsGlobal ? ShortcutScope.Global.Name : d.Scope.Name))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var isCurrentScope = !string.IsNullOrWhiteSpace(activeScopeName)
                        && g.Any(d => !d.Scope.IsGlobal && d.Scope.Name == activeScopeName);
                    return new CheatsheetGroup
                    {
                        Header = g.Key,
                        Items = g.OrderBy(d => d.Label).ToArray(),
                        IsCurrentScope = isCurrentScope,
                        CurrentScopeLabel = isCurrentScope ? currentPage : string.Empty,
                    };
                }));
    }

    public ObservableCollection<CheatsheetGroup> Groups { get; }
}

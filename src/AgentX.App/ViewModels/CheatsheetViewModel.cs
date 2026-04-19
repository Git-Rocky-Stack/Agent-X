using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels;

public sealed class CheatsheetGroup
{
    public required string Header { get; init; }
    public required IReadOnlyList<ShortcutDescriptor> Items { get; init; }
    public bool IsCurrentScope { get; init; }
    public string CurrentScopeLabel => IsCurrentScope ? "Current page" : string.Empty;
}

public partial class CheatsheetViewModel : ObservableObject
{
    public CheatsheetViewModel(IShortcutRegistry registry, string? activeScopeName)
    {
        var available = string.IsNullOrWhiteSpace(activeScopeName)
            ? registry.All().Where(d => d.Scope.IsGlobal)
            : registry.ForScope(activeScopeName);

        Groups = new ObservableCollection<CheatsheetGroup>(
            available
                .GroupBy(d => d.Category ?? (d.Scope.IsGlobal ? ShortcutScope.Global.Name : d.Scope.Name))
                .OrderBy(g => g.Key)
                .Select(g => new CheatsheetGroup
                {
                    Header = g.Key,
                    Items = g.OrderBy(d => d.Label).ToArray(),
                    IsCurrentScope = !string.IsNullOrWhiteSpace(activeScopeName)
                        && g.Any(d => !d.Scope.IsGlobal && d.Scope.Name == activeScopeName),
                }));
    }

    public ObservableCollection<CheatsheetGroup> Groups { get; }
}

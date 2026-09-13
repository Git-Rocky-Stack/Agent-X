using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentX.App.ViewModels;

/// <summary>
/// Registry-backed half of the Command Palette. Sources its items from
/// <see cref="IShortcutRegistry"/> filtered to Global + the active page scope,
/// applies <see cref="FuzzyMatcher"/> ranking when <see cref="Query"/> is set, and
/// refreshes automatically when the registry fires <c>Changed</c>.
/// <para>
/// <c>Controls/CommandPalette</c> owns one instance for its lifetime, points
/// <see cref="ActiveScopeName"/> at the page currently in the content frame each time
/// it opens, and renders the non-global descriptors as the "On This Page" group.
/// Executing one of those rows routes through <see cref="ExecuteAsync"/>.
/// </para>
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IShortcutRegistry _registry;

    public CommandPaletteViewModel(IShortcutRegistry registry, string? activeScopeName)
    {
        _registry = registry;
        this.activeScopeName = activeScopeName;
        RefreshResults();
        _registry.Changed += (_, _) => RefreshResults();
    }

    [ObservableProperty] private string query = string.Empty;

    /// <summary>
    /// The page whose scoped shortcuts join the global ones, or null for global only.
    /// Settable because the palette outlives any one page: it is re-pointed at the
    /// current page each time it opens.
    /// </summary>
    [ObservableProperty] private string? activeScopeName;

    public ObservableCollection<ShortcutDescriptor> Results { get; } = new();

    partial void OnQueryChanged(string value) => RefreshResults();

    partial void OnActiveScopeNameChanged(string? value) => RefreshResults();

    private void RefreshResults()
    {
        var available = ActiveScopeName is null
            ? _registry.All().Where(d => d.Scope.IsGlobal)
            : _registry.ForScope(ActiveScopeName);

        var ordered = string.IsNullOrWhiteSpace(Query)
            ? available.OrderBy(d => d.Label).ToList()
            : FuzzyMatcher
                .Rank(available, d => d.Label, Query)
                .Select(s => s.Item)
                .ToList();

        Results.Clear();
        foreach (var r in ordered) Results.Add(r);
    }

    [RelayCommand]
    public async Task ExecuteAsync(ShortcutDescriptor descriptor)
    {
        if (descriptor is null) return;
        await descriptor.Handler(CancellationToken.None);
    }
}

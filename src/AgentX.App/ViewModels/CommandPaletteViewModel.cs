using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the Command Palette surface. Sources its items from
/// <see cref="IShortcutRegistry"/> filtered to Global + the active page scope,
/// and applies <see cref="FuzzyMatcher"/> ranking when <see cref="Query"/> is set.
/// Refreshes automatically when the registry fires <c>Changed</c>.
///
/// The XAML integration (binding the existing Controls/CommandPalette.xaml ListView
/// to <c>Results</c> and routing Enter through <c>ExecuteAsync</c>) lands in Task 10
/// (ShortcutCatalog seed) where the registry is populated with the descriptors the
/// palette currently hard-codes as callback action-IDs.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IShortcutRegistry _registry;
    private readonly string? _activeScopeName;

    public CommandPaletteViewModel(IShortcutRegistry registry, string? activeScopeName)
    {
        _registry = registry;
        _activeScopeName = activeScopeName;
        RefreshResults();
        _registry.Changed += (_, _) => RefreshResults();
    }

    [ObservableProperty] private string query = string.Empty;

    public ObservableCollection<ShortcutDescriptor> Results { get; } = new();

    partial void OnQueryChanged(string value) => RefreshResults();

    private void RefreshResults()
    {
        var available = _activeScopeName is null
            ? _registry.All().Where(d => d.Scope.IsGlobal)
            : _registry.ForScope(_activeScopeName);

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

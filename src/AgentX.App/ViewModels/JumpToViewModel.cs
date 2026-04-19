using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentX.App.ViewModels;

public enum JumpToItemKind
{
    Page,
    Document,
    Conversation,
}

public sealed record JumpToItem(
    string Id,
    string Label,
    string? Subtitle,
    JumpToItemKind Kind,
    Func<CancellationToken, Task> OpenAction)
{
    public string IconGlyph => Kind switch
    {
        JumpToItemKind.Conversation => "\uE90A",
        JumpToItemKind.Document => "\uE8A5",
        _ => "\uE8A5",
    };
}

public partial class JumpToViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<JumpToItem>>> _loadCandidates;
    private IReadOnlyList<JumpToItem> _allCandidates = Array.Empty<JumpToItem>();

    public JumpToViewModel(Func<CancellationToken, Task<IReadOnlyList<JumpToItem>>> loadCandidates)
    {
        _loadCandidates = loadCandidates;
    }

    [ObservableProperty] private string query = string.Empty;

    public ObservableCollection<JumpToItem> Results { get; } = new();

    partial void OnQueryChanged(string value) => Refresh();

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _allCandidates = await _loadCandidates(ct);
        Refresh();
    }

    [RelayCommand]
    public async Task ExecuteAsync(JumpToItem item)
    {
        if (item is null) return;
        await item.OpenAction(CancellationToken.None);
    }

    private void Refresh()
    {
        var ordered = string.IsNullOrWhiteSpace(Query)
            ? _allCandidates.OrderBy(c => c.Kind).ThenBy(c => c.Label).ToList()
            : FuzzyMatcher
                .Rank(_allCandidates, c => c.Label, Query)
                .Select(s => s.Item)
                .ToList();

        Results.Clear();
        foreach (var result in ordered) Results.Add(result);
    }
}

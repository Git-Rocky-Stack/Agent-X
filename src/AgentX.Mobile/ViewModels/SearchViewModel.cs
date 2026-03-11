using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Mobile.Models;
using AgentX.Mobile.Services;

namespace AgentX.Mobile.ViewModels;

/// <summary>
/// Backing view-model for <c>SearchPage</c>.
/// Manages the query entry, result list, and loading state for semantic search.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly AgentXApiClient _api;
    private CancellationTokenSource? _searchCts;

    public SearchViewModel(AgentXApiClient api)
    {
        _api = api;
    }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SearchResultDto> _results = [];

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasSearched;

    // ── Commands ──────────────────────────────────────────────────────────────

    private bool CanSearch => !string.IsNullOrWhiteSpace(Query) && !IsSearching;

    /// <summary>
    /// Executes a semantic search using the current <see cref="Query"/>.
    /// Cancels any in-flight search before starting a new one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        // Cancel previous request if still running
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        HasError = false;
        ErrorMessage = string.Empty;
        Results.Clear();
        HasSearched = false;

        try
        {
            var results = await _api.SearchAsync(Query, topK: 20, ct: ct).ConfigureAwait(true);

            foreach (var r in results)
                Results.Add(r);

            HasSearched = true;
            IsEmpty = Results.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // User navigated away or started a new search — ignore
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Clears the current query and results.</summary>
    [RelayCommand]
    private void Clear()
    {
        _searchCts?.Cancel();
        Query = string.Empty;
        Results.Clear();
        HasSearched = false;
        HasError = false;
        IsEmpty = false;
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using AgentX.App.Services;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

// =============================================================================
// SEARCH VIEW MODEL
//
// Drives the Semantic Search page: accepts user queries, performs vector
// similarity search via ISemanticSearchService, manages results display,
// search history, file-type filtering, saved filters, and latency reporting.
// =============================================================================

public partial class SearchViewModel : ObservableObject
{
    private readonly ISemanticSearchService _searchService;
    private readonly IHybridSearchOrchestrator _hybridSearchOrchestrator;
    private readonly IDocumentService _documentService;
    private readonly ICollectionService _collectionService;
    private readonly ILogger _logger;
    private readonly ILocalizationService _localization;
    private readonly IWorkflowLaunchService? _workflowLaunchService;

    // -- Search Input & State -------------------------------------
    [ObservableProperty] private string _queryText = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _showNoResults;
    [ObservableProperty] private int _totalResults;
    [ObservableProperty] private double _searchLatencyMs;
    [ObservableProperty] private string? _selectedFileTypeFilter;
    [ObservableProperty] private long? _selectedCollectionId;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private SearchMode _searchMode = SearchMode.Semantic;

    // -- Advanced Filters -----------------------------------------
    [ObservableProperty] private bool _isAdvancedFiltersOpen;
    [ObservableProperty] private double _minScoreFilter = 30;
    [ObservableProperty] private int _topKFilter = 20;
    [ObservableProperty] private DateTimeOffset? _createdAfterDate;
    [ObservableProperty] private DateTimeOffset? _createdBeforeDate;
    [ObservableProperty] private long _selectedCollectionFilterId;

    // -- Sort -----------------------------------------------------
    [ObservableProperty] private int _selectedSortIndex;

    // -- Saved Filters --------------------------------------------
    [ObservableProperty] private bool _hasSavedFilters;

    // -- Observable Collections ------------------------------------
    public ObservableCollection<SearchResultItem> Results { get; } = new();
    public ObservableCollection<SearchHistoryItem> SearchHistory { get; } = new();
    public ObservableCollection<SavedFilterItem> SavedFilters { get; } = new();
    public ObservableCollection<CollectionFilterItem> CollectionFilters { get; } = new();

    // -- Internal history storage ---------------------------------
    private readonly List<SearchHistoryItem> _historyStore = new();
    private long _historyIdCounter;

    // Concurrent searches
    // Each search claims the next generation; only the newest may update the results.
    private int _searchGeneration;
    private CancellationTokenSource? _searchCts;
    public NavigateHandler? NavigateRequested { get; set; }

    /// <summary>True when the current search mode is Semantic.</summary>
    public bool IsSemanticMode => SearchMode == SearchMode.Semantic;

    /// <summary>True when the current search mode is Keyword.</summary>
    public bool IsKeywordMode => SearchMode == SearchMode.Keyword;

    /// <summary>True when the current search mode is Hybrid.</summary>
    public bool IsHybridMode => SearchMode == SearchMode.Hybrid;

    public SearchViewModel(
        ISemanticSearchService searchService,
        IHybridSearchOrchestrator hybridSearchOrchestrator,
        IDocumentService documentService,
        ICollectionService collectionService,
        ILogger logger,
        ILocalizationService localization,
        IWorkflowLaunchService? workflowLaunchService = null)
    {
        _searchService = searchService;
        _hybridSearchOrchestrator = hybridSearchOrchestrator;
        _documentService = documentService;
        _collectionService = collectionService;
        _logger = logger;
        _localization = localization;
        _workflowLaunchService = workflowLaunchService;
        StatusMessage = _localization.GetString("Search_ReadyToSearch");
        _logger.Debug("SearchViewModel created with services");
    }

    /// <summary>
    /// Called by the generated code when SearchMode changes.
    /// Notifies computed property changes for mode-dependent UI bindings.
    /// </summary>
    partial void OnSearchModeChanged(SearchMode value)
    {
        OnPropertyChanged(nameof(IsSemanticMode));
        OnPropertyChanged(nameof(IsKeywordMode));
        OnPropertyChanged(nameof(IsHybridMode));
    }

    // =================================================================
    // INITIALIZATION
    // =================================================================

    public async Task InitializeAsync()
    {
        _logger.Information("SearchViewModel initializing...");

        try
        {
            await LoadSearchHistoryAsync();
            await LoadSavedFiltersAsync();
            await LoadCollectionFiltersAsync();
            StatusMessage = _localization.GetString("Search_ReadyToSearch");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to initialize SearchViewModel");
            StatusMessage = _localization.GetString("Search_ReadyToSearch");
        }
    }

    private async Task LoadSearchHistoryAsync()
    {
        try
        {
            var entries = await _searchService.GetSearchHistoryAsync(20);
            _historyStore.Clear();
            _historyIdCounter = 0;

            foreach (var entry in entries)
            {
                _historyIdCounter++;
                _historyStore.Add(new SearchHistoryItem
                {
                    Id = entry.Id,
                    QueryText = entry.QueryText,
                    ResultCount = entry.ResultCount,
                    SearchedAgo = FormatHelper.TimeAgoWithMonths(entry.SearchedAt)
                });
            }

            SyncHistoryToObservable();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load search history from database");
        }
    }

    // =================================================================
    // COMMANDS
    // =================================================================

    /// <summary>
    /// Applies a query handed over by whatever navigated here (the dashboard search box,
    /// the command palette) and runs it, so the user lands on results rather than an
    /// empty search page.
    /// </summary>
    public async Task ApplyNavigationParameterAsync(object? parameter)
    {
        if (parameter is not string query || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        QueryText = query;
        await SearchAsync();
    }

    /// <summary>
    /// Performs search using the current QueryText and SearchMode,
    /// applies any active file-type filter, updates Results,
    /// and records the search in history.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = QueryText?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        // A newer search supersedes one still running (a history click or a filter change
        // while results load). The older one is cancelled, and the generation check keeps
        // it from writing its results over the newer ones if it finishes anyway.
        var generation = Interlocked.Increment(ref _searchGeneration);
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCts, cts);
        CancelQuietly(previous);

        IsSearching = true;
        ShowNoResults = false;
        HasResults = false;
        StatusMessage = _localization.GetString("Search_Searching");

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Execute search via the hybrid orchestrator (handles Semantic, Keyword, and Hybrid modes)
            var effectiveCollectionId = SelectedCollectionFilterId > 0
                ? SelectedCollectionFilterId
                : SelectedCollectionId;

            // The date pickers yield local calendar days while ImportedAt is stored in UTC;
            // the "before" day is inclusive, so its bound is the end of that day.
            var searchQuery = new SearchQuery
            {
                QueryText = query,
                TopK = TopKFilter,
                MinScore = (float)(MinScoreFilter / 100.0),
                CollectionId = effectiveCollectionId,
                FileTypeFilter = ToServiceFileTypeFilter(SelectedFileTypeFilter),
                CreatedAfter = CreatedAfterDate.HasValue ? LocalDayRange.StartUtc(CreatedAfterDate.Value.DateTime) : null,
                CreatedBefore = CreatedBeforeDate.HasValue ? LocalDayRange.EndUtc(CreatedBeforeDate.Value.DateTime) : null,
                Mode = SearchMode
            };
            var rawResults = await _hybridSearchOrchestrator.SearchAsync(searchQuery, cts.Token);

            stopwatch.Stop();

            // Map raw results to display items, applying file type filter
            var displayResults = new List<SearchResultItem>();
            foreach (var r in rawResults)
            {
                // Prefer the stored type: connector items (calendar events, email) carry a
                // display name rather than a file name with an extension.
                var fileType = !string.IsNullOrWhiteSpace(r.FileType)
                    ? r.FileType.Trim().ToLowerInvariant()
                    : ExtractFileType(r.FileName);

                // Apply file type filter if active
                if (!string.IsNullOrEmpty(SelectedFileTypeFilter) &&
                    !MatchesFileTypeFilter(fileType, SelectedFileTypeFilter))
                {
                    continue;
                }

                // Look up document for collection names
                var collectionNames = new List<string>();
                try
                {
                    var doc = await _documentService.GetDocumentAsync(r.DocumentId);
                    if (doc?.DocumentCollections is not null)
                    {
                        foreach (var dc in doc.DocumentCollections)
                        {
                            if (dc.Collection is not null)
                                collectionNames.Add(dc.Collection.Name);
                        }
                    }
                }
                catch
                {
                    // Non-critical: collection names are supplementary
                }

                displayResults.Add(new SearchResultItem
                {
                    DocumentId = r.DocumentId,
                    ChunkId = r.ChunkId,
                    FileName = r.FileName,
                    FilePath = r.FilePath,
                    FileType = fileType,
                    Excerpt = !string.IsNullOrEmpty(r.Excerpt) ? r.Excerpt : TruncateExcerpt(r.MatchedText, 300),
                    RelevancePercent = r.RelevancePercent,
                    PageNumber = r.PageNumber,
                    CollectionNames = collectionNames
                });
            }

            if (generation != Volatile.Read(ref _searchGeneration))
            {
                return; // superseded while the results were being prepared
            }

            SearchLatencyMs = stopwatch.Elapsed.TotalMilliseconds;

            // Update observable collection, in the sort order the user picked
            Results.Clear();
            foreach (var item in ApplySort(displayResults))
                Results.Add(item);

            TotalResults = Results.Count;
            HasResults = Results.Count > 0;
            ShowNoResults = Results.Count == 0;

            var latency = SearchLatencyMs.ToString("F0");
            StatusMessage = Results.Count switch
            {
                0 => _localization.GetString("Search_NoResultsIn", latency),
                1 => _localization.GetString("Search_FoundResultsOne", Results.Count, latency),
                _ => _localization.GetString("Search_FoundResultsMany", Results.Count, latency)
            };

            // Save to history
            AddToHistory(query, Results.Count);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by a newer search, which owns the page state now.
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _searchGeneration))
            {
                _logger.Error(ex, "{SearchMode} search failed for query: {Query}", SearchMode, query);
                StatusMessage = _localization.GetString("Search_SearchFailed");
                ShowNoResults = true;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _searchGeneration))
            {
                IsSearching = false;
            }

            Interlocked.CompareExchange(ref _searchCts, null, cts);
            cts.Dispose();
        }
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That search already finished and released its token source.
        }
    }

    /// <summary>
    /// Clears the current query text and all results.
    /// </summary>
    [RelayCommand]
    private void ClearSearch()
    {
        QueryText = string.Empty;
        Results.Clear();
        HasResults = false;
        ShowNoResults = false;
        TotalResults = 0;
        SearchLatencyMs = 0;
        StatusMessage = _localization.GetString("Search_ReadyToSearch");
    }

    /// <summary>
    /// Fills QueryText with a history item and re-executes the search.
    /// </summary>
    [RelayCommand]
    private async Task SelectHistoryItem(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return;

        QueryText = queryText;
        await SearchAsync();
    }

    /// <summary>
    /// Clears all search history entries.
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        _historyStore.Clear();
        SearchHistory.Clear();

        try
        {
            await _searchService.ClearSearchHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to clear search history from database");
        }
    }

    /// <summary>
    /// Sets the active file-type filter and re-executes the search
    /// if a query is present.
    /// </summary>
    [RelayCommand]
    private async Task FilterByFileType(string? fileType)
    {
        SelectedFileTypeFilter = fileType;

        // Re-execute search with new filter if we have a query
        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            await SearchAsync();
        }
    }

    // =================================================================
    // SAVED FILTER COMMANDS
    // =================================================================

    /// <summary>
    /// Saves the current query and search mode as a reusable saved filter.
    /// </summary>
    [RelayCommand]
    private async Task SaveCurrentFilterAsync()
    {
        var query = QueryText?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        try
        {
            // Map the current sort index to a persistable string identifier
            string? sortOrder = SelectedSortIndex switch
            {
                1 => "newest",
                2 => "oldest",
                3 => "name",
                _ => "relevance"
            };

            await _searchService.SaveSearchHistoryAsync(
                query,
                TotalResults,
                minScore: MinScoreFilter,
                maxResults: TopKFilter,
                dateAfter: CreatedAfterDate?.DateTime,
                dateBefore: CreatedBeforeDate?.DateTime,
                sortOrder: sortOrder,
                searchType: SearchTypeName(SearchMode));

            var history = await _searchService.GetSearchHistoryAsync(50);
            var entry = history.FirstOrDefault(h =>
                h.QueryText.Equals(query, StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
            {
                await _searchService.SaveSearchFilterAsync(entry.Id);
                await LoadSavedFiltersAsync();
                StatusMessage = _localization.GetString("Search_FilterSaved");
                _logger.Information("Search filter saved: Query={Query}", query);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save current filter");
            StatusMessage = _localization.GetString("Search_FilterSaveFailed");
        }
    }

    /// <summary>
    /// Removes a saved filter by unsaving the underlying search history entry.
    /// </summary>
    [RelayCommand]
    private async Task RemoveSavedFilterAsync(long filterId)
    {
        try
        {
            await _searchService.UnsaveSearchFilterAsync(filterId);
            await LoadSavedFiltersAsync();
            StatusMessage = _localization.GetString("Search_FilterRemoved");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove saved filter {Id}", filterId);
            StatusMessage = _localization.GetString("Search_FilterRemoveFailed");
        }
    }

    /// <summary>
    /// Restores the query text and search mode from a saved filter and executes the search.
    /// </summary>
    [RelayCommand]
    private async Task ApplySavedFilterAsync(SavedFilterItem? filter)
    {
        if (filter is null)
            return;

        // Restore query text and search mode
        QueryText = filter.QueryText;
        SearchMode = filter.Mode;

        // Restore advanced filter settings
        MinScoreFilter = filter.MinScore ?? 30;
        TopKFilter = filter.MaxResults ?? 20;
        CreatedAfterDate = filter.DateAfter.HasValue
            ? new DateTimeOffset(filter.DateAfter.Value)
            : null;
        CreatedBeforeDate = filter.DateBefore.HasValue
            ? new DateTimeOffset(filter.DateBefore.Value)
            : null;

        // Restore sort order
        SelectedSortIndex = filter.SortOrder?.ToLowerInvariant() switch
        {
            "newest" => 1,
            "oldest" => 2,
            "name" => 3,
            _ => 0 // "relevance" or null
        };

        // Open the advanced filters panel so the user can see the restored settings
        if (filter.MinScore.HasValue || filter.MaxResults.HasValue ||
            filter.DateAfter.HasValue || filter.DateBefore.HasValue)
        {
            IsAdvancedFiltersOpen = true;
        }

        await SearchAsync();
    }

    // =================================================================
    // ADVANCED FILTER COMMANDS
    // =================================================================

    [RelayCommand]
    private void ToggleAdvancedFilters()
    {
        IsAdvancedFiltersOpen = !IsAdvancedFiltersOpen;
    }

    [RelayCommand]
    private void ClearAdvancedFilters()
    {
        MinScoreFilter = 30;
        TopKFilter = 20;
        CreatedAfterDate = null;
        CreatedBeforeDate = null;
        SelectedCollectionFilterId = 0;
    }

    // =================================================================
    // SORT
    // =================================================================

    partial void OnSelectedSortIndexChanged(int value)
    {
        SortResults();
    }

    private void SortResults()
    {
        if (Results.Count == 0) return;

        var sorted = ApplySort(Results);

        Results.Clear();
        foreach (var item in sorted)
            Results.Add(item);
    }

    /// <summary>
    /// Orders results by the selected sort option. Used both when new results arrive and
    /// when the user changes the sort, so a fresh search honors the current choice.
    /// </summary>
    private List<SearchResultItem> ApplySort(IEnumerable<SearchResultItem> items) => SelectedSortIndex switch
    {
        1 => items.OrderByDescending(r => r.DocumentId).ToList(),
        2 => items.OrderBy(r => r.DocumentId).ToList(),
        3 => items.OrderBy(r => r.FileName).ToList(),
        _ => items.OrderByDescending(r => r.RelevancePercent).ToList(),
    };

    // =================================================================
    // DOCUMENT ACTIONS
    // =================================================================

    /// <summary>
    /// Opens the source document in Windows Explorer, selecting the file.
    /// </summary>
    [RelayCommand]
    private void OpenDocument(long documentId)
    {
        try
        {
            var result = Results.FirstOrDefault(r => r.DocumentId == documentId);
            if (result is null || string.IsNullOrWhiteSpace(result.FilePath))
            {
                _logger.Warning("Cannot open document {Id}: not found in results", documentId);
                return;
            }

            if (File.Exists(result.FilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{result.FilePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                _logger.Warning("File not found at path: {Path}", result.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open document {Id}", documentId);
        }
    }

    [RelayCommand]
    private void LaunchResultIntoWorkflow(SearchResultItem? result)
    {
        if (_workflowLaunchService is null || result is null)
        {
            return;
        }

        // The input lands in the workflow page's input box, so its labels are in the UI language.
        var lines = new List<string>
        {
            _localization.GetString("Search_WorkflowInputSource")
        };

        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            lines.Add(_localization.GetString("Search_WorkflowInputQuery", QueryText.Trim()));
        }

        lines.Add(_localization.GetString("Search_WorkflowInputDocument", result.FileName));
        lines.Add(_localization.GetString("Search_WorkflowInputRelevance", result.RelevancePercent));

        if (result.PageNumber.HasValue)
        {
            lines.Add(_localization.GetString("Search_WorkflowInputPage", result.PageNumber.Value));
        }

        var excerptHeading = _localization.GetString("Search_WorkflowInputExcerpt");
        lines.Add(string.Empty);
        lines.Add(excerptHeading);
        lines.Add(new string('-', excerptHeading.Length));
        lines.Add(string.IsNullOrWhiteSpace(result.Excerpt)
            ? _localization.GetString("Search_WorkflowInputNoExcerpt")
            : result.Excerpt.Trim());

        _workflowLaunchService.StageRequest(new WorkflowLaunchRequest
        {
            InputText = string.Join(Environment.NewLine, lines),
            SourceLabel = _localization.GetString("Search_WorkflowSourceLabel", result.FileName),
            RecommendedWorkflowName = "Research Brief"
        });

        NavigateRequested?.Invoke("Workflows");
    }

    // =================================================================
    // PRIVATE HELPERS
    // =================================================================

    /// <summary>
    /// The name stored with history entries and saved filters, which
    /// <see cref="ToSearchMode"/> maps back to a <see cref="SearchMode"/>.
    /// </summary>
    private static string SearchTypeName(SearchMode mode) => mode switch
    {
        SearchMode.Keyword => "keyword",
        SearchMode.Hybrid => "hybrid",
        _ => "semantic"
    };

    /// <summary>The <see cref="SearchMode"/> a stored search type name stands for.</summary>
    private static SearchMode ToSearchMode(string? searchType) => searchType?.ToLowerInvariant() switch
    {
        "keyword" => SearchMode.Keyword,
        "hybrid" => SearchMode.Hybrid,
        _ => SearchMode.Semantic
    };

    /// <summary>The mode's name as a saved filter's badge shows it.</summary>
    private string SearchModeLabel(SearchMode mode) => mode switch
    {
        SearchMode.Keyword => _localization.GetString("Search_SavedFilterModeKeyword"),
        SearchMode.Hybrid => _localization.GetString("Search_SavedFilterModeHybrid"),
        _ => _localization.GetString("Search_SavedFilterModeSemantic")
    };

    /// <summary>
    /// The file type the search services filter on before the top-K cut. A chip that stands
    /// for one stored file type is passed through; a category chip such as "code" spans
    /// several stored types, so it is not sent (an exact match on "code" finds nothing) and
    /// <see cref="MatchesFileTypeFilter"/> applies it to the results instead.
    /// </summary>
    internal static string? ToServiceFileTypeFilter(string? chip)
    {
        if (string.IsNullOrWhiteSpace(chip))
        {
            return null;
        }

        return chip.Equals("code", StringComparison.OrdinalIgnoreCase) ? null : chip;
    }

    private static string ExtractFileType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? "unknown" : ext.TrimStart('.').ToLowerInvariant();
    }

    private static bool MatchesFileTypeFilter(string fileType, string filter)
    {
        return filter.ToLowerInvariant() switch
        {
            "pdf" => fileType == "pdf",
            "docx" => fileType is "docx" or "doc",
            "txt" => fileType is "txt" or "text",
            "code" => fileType is "cs" or "py" or "js" or "ts" or "java" or "cpp" or "c" or "go" or "rs" or "rb" or "php" or "swift" or "kt",
            "md" => fileType is "md" or "markdown",
            "calendarevent" => fileType is "calendarevent",
            "emailmessage" => fileType is "emailmessage",
            _ => true
        };
    }

    private static string TruncateExcerpt(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Clean up whitespace
        var cleaned = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        cleaned = cleaned.Trim();

        if (cleaned.Length <= maxLength)
            return cleaned;

        // Truncate at the last word boundary before maxLength
        var truncated = cleaned[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.6)
            truncated = truncated[..lastSpace];

        return truncated + "...";
    }

    private void AddToHistory(string query, int resultCount)
    {
        var searchType = SearchTypeName(SearchMode);

        // Remove duplicate if exists
        _historyStore.RemoveAll(h =>
            h.QueryText.Equals(query, StringComparison.OrdinalIgnoreCase));

        _historyIdCounter++;
        _historyStore.Insert(0, new SearchHistoryItem
        {
            Id = _historyIdCounter,
            QueryText = query,
            ResultCount = resultCount,
            SearchedAgo = _localization.GetString("Search_HistoryJustNow")
        });

        // Keep only the most recent 20 entries
        while (_historyStore.Count > 20)
            _historyStore.RemoveAt(_historyStore.Count - 1);

        SyncHistoryToObservable();

        // Persist to database (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _searchService.SaveSearchHistoryAsync(query, resultCount, searchType: searchType);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to persist search history entry");
            }
        });
    }

    private void SyncHistoryToObservable()
    {
        SearchHistory.Clear();
        foreach (var item in _historyStore)
            SearchHistory.Add(item);
    }

    private async Task LoadSavedFiltersAsync()
    {
        try
        {
            var entries = await _searchService.GetSavedFiltersAsync();

            SavedFilters.Clear();
            foreach (var entry in entries)
            {
                var mode = ToSearchMode(entry.SearchType);
                SavedFilters.Add(new SavedFilterItem
                {
                    Id = entry.Id,
                    QueryText = entry.QueryText,
                    Mode = mode,
                    SearchType = SearchModeLabel(mode),
                    SavedAt = FormatHelper.TimeAgoWithMonths(entry.SearchedAt),
                    MinScore = entry.MinScore,
                    MaxResults = entry.MaxResults,
                    DateAfter = entry.DateAfter,
                    DateBefore = entry.DateBefore,
                    SortOrder = entry.SortOrder
                });
            }

            HasSavedFilters = SavedFilters.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load saved filters");
        }
    }

    private async Task LoadCollectionFiltersAsync()
    {
        try
        {
            var collections = await _collectionService.GetAllCollectionsAsync();

            CollectionFilters.Clear();
            CollectionFilters.Add(new CollectionFilterItem { Id = 0, Name = _localization.GetString("Search_AllCollections"), DocumentCount = 0 });
            foreach (var col in collections.OrderBy(c => c.Name))
            {
                CollectionFilters.Add(new CollectionFilterItem { Id = col.Id, Name = col.Name, DocumentCount = col.DocumentCount });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load collection filters");
        }
    }
}

// =============================================================================
// SEARCH RESULT ITEM - Display model for a single search result
// =============================================================================

public partial class SearchResultItem : ObservableObject
{
    public long DocumentId { get; init; }
    public long ChunkId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public string Excerpt { get; init; } = string.Empty;
    public int RelevancePercent { get; init; }
    public int? PageNumber { get; init; }
    public List<string> CollectionNames { get; init; } = new();

    /// <summary>
    /// Returns a Segoe MDL2 glyph appropriate for the file type.
    /// </summary>
    public string FileTypeIcon => FileType.ToLowerInvariant() switch
    {
        "pdf" => "\uEA90",
        "docx" or "doc" => "\uE8A5",
        "txt" => "\uE8A4",
        "md" => "\uE943",
        "cs" or "py" or "js" or "ts" => "\uE943",
        "calendarevent" => "\uE787",
        "emailmessage" => "\uE715",
        _ => "\uE7C3"
    };

}

// =============================================================================
// SEARCH HISTORY ITEM - Display model for a recent search entry
// =============================================================================

public class SearchHistoryItem
{
    public long Id { get; init; }
    public string QueryText { get; init; } = string.Empty;
    public int ResultCount { get; init; }
    public string SearchedAgo { get; init; } = string.Empty;
}

// =============================================================================
// SAVED FILTER ITEM - Display model for a bookmarked search filter
// =============================================================================

public class SavedFilterItem
{
    public long Id { get; init; }
    public string QueryText { get; init; } = string.Empty;

    /// <summary>The search mode the filter restores.</summary>
    public SearchMode Mode { get; init; } = SearchMode.Semantic;

    /// <summary>The name of <see cref="Mode"/> shown on the filter's badge, in the UI language.</summary>
    public string SearchType { get; init; } = string.Empty;

    public string SavedAt { get; init; } = string.Empty;

    // -- Advanced filter settings ---------------------------------
    public double? MinScore { get; init; }
    public int? MaxResults { get; init; }
    public DateTime? DateAfter { get; init; }
    public DateTime? DateBefore { get; init; }
    public string? SortOrder { get; init; }
}

// NOTE: CollectionFilterItem is defined in KnowledgeVaultViewModel.cs

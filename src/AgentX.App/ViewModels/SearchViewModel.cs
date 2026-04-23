using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Collections;
using AgentX.App.Services;
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
    private readonly IWorkflowLaunchService? _workflowLaunchService;

    // ── Search Input & State ─────────────────────────────────────
    [ObservableProperty] private string _queryText = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _showNoResults;
    [ObservableProperty] private int _totalResults;
    [ObservableProperty] private double _searchLatencyMs;
    [ObservableProperty] private string? _selectedFileTypeFilter;
    [ObservableProperty] private long? _selectedCollectionId;
    [ObservableProperty] private string _statusMessage = "Ready to search";
    [ObservableProperty] private SearchMode _searchMode = SearchMode.Semantic;

    // ── Advanced Filters ─────────────────────────────────────────
    [ObservableProperty] private bool _isAdvancedFiltersOpen;
    [ObservableProperty] private double _minScoreFilter = 30;
    [ObservableProperty] private int _topKFilter = 20;
    [ObservableProperty] private DateTimeOffset? _createdAfterDate;
    [ObservableProperty] private DateTimeOffset? _createdBeforeDate;
    [ObservableProperty] private long _selectedCollectionFilterId;

    // ── Sort ─────────────────────────────────────────────────────
    [ObservableProperty] private int _selectedSortIndex;

    // ── Saved Filters ────────────────────────────────────────────
    [ObservableProperty] private bool _hasSavedFilters;

    // ── Observable Collections ────────────────────────────────────
    public ObservableCollection<SearchResultItem> Results { get; } = new();
    public ObservableCollection<SearchHistoryItem> SearchHistory { get; } = new();
    public ObservableCollection<SavedFilterItem> SavedFilters { get; } = new();
    public ObservableCollection<CollectionFilterItem> CollectionFilters { get; } = new();

    // ── Internal history storage ─────────────────────────────────
    private readonly List<SearchHistoryItem> _historyStore = new();
    private long _historyIdCounter;
    public Action<string>? NavigateRequested { get; set; }

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
        IWorkflowLaunchService? workflowLaunchService = null)
    {
        _searchService = searchService;
        _hybridSearchOrchestrator = hybridSearchOrchestrator;
        _documentService = documentService;
        _collectionService = collectionService;
        _logger = logger;
        _workflowLaunchService = workflowLaunchService;
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
            StatusMessage = "Ready to search";
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to initialize SearchViewModel");
            StatusMessage = "Ready to search";
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

        IsSearching = true;
        ShowNoResults = false;
        HasResults = false;
        StatusMessage = "Searching...";

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Execute search via the hybrid orchestrator (handles Semantic, Keyword, and Hybrid modes)
            var effectiveCollectionId = SelectedCollectionFilterId > 0
                ? SelectedCollectionFilterId
                : SelectedCollectionId;

            var searchQuery = new SearchQuery
            {
                QueryText = query,
                TopK = TopKFilter,
                MinScore = (float)(MinScoreFilter / 100.0),
                CollectionId = effectiveCollectionId,
                FileTypeFilter = SelectedFileTypeFilter,
                CreatedAfter = CreatedAfterDate?.DateTime,
                CreatedBefore = CreatedBeforeDate?.DateTime,
                Mode = SearchMode
            };
            var rawResults = await _hybridSearchOrchestrator.SearchAsync(searchQuery);

            stopwatch.Stop();
            SearchLatencyMs = stopwatch.Elapsed.TotalMilliseconds;

            // Map raw results to display items, applying file type filter
            var displayResults = new List<SearchResultItem>();
            foreach (var r in rawResults)
            {
                var fileType = ExtractFileType(r.FileName);

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

            // Update observable collection
            Results.Clear();
            foreach (var item in displayResults)
                Results.Add(item);

            TotalResults = Results.Count;
            HasResults = Results.Count > 0;
            ShowNoResults = Results.Count == 0;

            StatusMessage = Results.Count > 0
                ? $"Found {Results.Count} result{(Results.Count != 1 ? "s" : "")} in {SearchLatencyMs:F0}ms"
                : $"No results found in {SearchLatencyMs:F0}ms";

            // Save to history
            AddToHistory(query, Results.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{SearchMode} search failed for query: {Query}", SearchMode, query);
            StatusMessage = "Search failed. Please try again.";
            ShowNoResults = true;
        }
        finally
        {
            IsSearching = false;
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
        StatusMessage = "Ready to search";
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
                sortOrder: sortOrder);

            var history = await _searchService.GetSearchHistoryAsync(50);
            var entry = history.FirstOrDefault(h =>
                h.QueryText.Equals(query, StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
            {
                await _searchService.SaveSearchFilterAsync(entry.Id);
                await LoadSavedFiltersAsync();
                StatusMessage = "Filter saved";
                _logger.Information("Search filter saved: Query={Query}", query);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save current filter");
            StatusMessage = "Failed to save filter";
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
            StatusMessage = "Filter removed";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove saved filter {Id}", filterId);
            StatusMessage = "Failed to remove filter";
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
        SearchMode = filter.SearchType?.ToLowerInvariant() switch
        {
            "keyword" => SearchMode.Keyword,
            "hybrid" => SearchMode.Hybrid,
            _ => SearchMode.Semantic
        };

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

        var sorted = SelectedSortIndex switch
        {
            1 => Results.OrderByDescending(r => r.DocumentId).ToList(),
            2 => Results.OrderBy(r => r.DocumentId).ToList(),
            3 => Results.OrderBy(r => r.FileName).ToList(),
            _ => Results.OrderByDescending(r => r.RelevancePercent).ToList(),
        };

        Results.Clear();
        foreach (var item in sorted)
            Results.Add(item);
    }

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

        var lines = new List<string>
        {
            "Source: Search result"
        };

        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            lines.Add($"Query: {QueryText.Trim()}");
        }

        lines.Add($"Document: {result.FileName}");
        lines.Add($"Relevance: {result.RelevancePercent}%");

        if (result.PageNumber.HasValue)
        {
            lines.Add($"Page: {result.PageNumber.Value}");
        }

        lines.Add(string.Empty);
        lines.Add("Excerpt");
        lines.Add("-------");
        lines.Add(string.IsNullOrWhiteSpace(result.Excerpt)
            ? "No excerpt available."
            : result.Excerpt.Trim());

        _workflowLaunchService.StageRequest(new WorkflowLaunchRequest
        {
            InputText = string.Join(Environment.NewLine, lines),
            SourceLabel = $"Loaded search context from \"{result.FileName}\"",
            RecommendedWorkflowName = "Research Brief"
        });

        NavigateRequested?.Invoke("Workflows");
    }

    // =================================================================
    // PRIVATE HELPERS
    // =================================================================

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
        // Remove duplicate if exists
        _historyStore.RemoveAll(h =>
            h.QueryText.Equals(query, StringComparison.OrdinalIgnoreCase));

        _historyIdCounter++;
        _historyStore.Insert(0, new SearchHistoryItem
        {
            Id = _historyIdCounter,
            QueryText = query,
            ResultCount = resultCount,
            SearchedAgo = "just now"
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
                await _searchService.SaveSearchHistoryAsync(query, resultCount);
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
                SavedFilters.Add(new SavedFilterItem
                {
                    Id = entry.Id,
                    QueryText = entry.QueryText,
                    SearchType = entry.SearchType,
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
            CollectionFilters.Add(new CollectionFilterItem { Id = 0, Name = "All Collections", DocumentCount = 0 });
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
// SEARCH RESULT ITEM — Display model for a single search result
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

    /// <summary>
    /// Returns a hex color string based on relevance tier:
    /// High (>=80): green, Medium (>=60): yellow, Low (>=40): orange, Poor: red.
    /// </summary>
    public string ScoreColor => RelevancePercent switch
    {
        >= 80 => "#4CAF50",
        >= 60 => "#FFC107",
        >= 40 => "#FF9800",
        _ => "#F44336"
    };
}

// =============================================================================
// SEARCH HISTORY ITEM — Display model for a recent search entry
// =============================================================================

public class SearchHistoryItem
{
    public long Id { get; init; }
    public string QueryText { get; init; } = string.Empty;
    public int ResultCount { get; init; }
    public string SearchedAgo { get; init; } = string.Empty;
}

// =============================================================================
// SAVED FILTER ITEM — Display model for a bookmarked search filter
// =============================================================================

public class SavedFilterItem
{
    public long Id { get; init; }
    public string QueryText { get; init; } = string.Empty;
    public string SearchType { get; init; } = "semantic";
    public string SavedAt { get; init; } = string.Empty;

    // ── Advanced filter settings ─────────────────────────────────
    public double? MinScore { get; init; }
    public int? MaxResults { get; init; }
    public DateTime? DateAfter { get; init; }
    public DateTime? DateBefore { get; init; }
    public string? SortOrder { get; init; }
}

// NOTE: CollectionFilterItem is defined in KnowledgeVaultViewModel.cs

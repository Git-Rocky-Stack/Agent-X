using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using Serilog;

namespace AgentX.App.ViewModels;

// =============================================================================
// SEARCH VIEW MODEL
//
// Drives the Semantic Search page: accepts user queries, performs vector
// similarity search via ISemanticSearchService, manages results display,
// search history, file-type filtering, and latency reporting.
// =============================================================================

public partial class SearchViewModel : ObservableObject
{
    private readonly ISemanticSearchService _searchService;
    private readonly IHybridSearchOrchestrator _hybridSearchOrchestrator;
    private readonly IDocumentService _documentService;
    private readonly ILogger _logger;

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

    // ── Collections ──────────────────────────────────────────────
    public ObservableCollection<SearchResultItem> Results { get; } = new();
    public ObservableCollection<SearchHistoryItem> SearchHistory { get; } = new();

    // ── Internal history storage ─────────────────────────────────
    private readonly List<SearchHistoryItem> _historyStore = new();
    private long _historyIdCounter;

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
        ILogger logger)
    {
        _searchService = searchService;
        _hybridSearchOrchestrator = hybridSearchOrchestrator;
        _documentService = documentService;
        _logger = logger;
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
                    SearchedAgo = FormatTimeAgo(entry.SearchedAt)
                });
            }

            SyncHistoryToObservable();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load search history from database");
        }
    }

    private static string FormatTimeAgo(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime.ToUniversalTime();
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 43200 => $"{(int)span.TotalDays}d ago",
            _ => dateTime.ToLocalTime().ToString("MMM d")
        };
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
            var searchQuery = new SearchQuery
            {
                QueryText = query,
                TopK = 20,
                CollectionId = SelectedCollectionId,
                FileTypeFilter = SelectedFileTypeFilter,
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

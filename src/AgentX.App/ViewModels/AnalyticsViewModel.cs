using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Intelligence;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class AnalyticsViewModel : ObservableObject, IDisposable
{
    private readonly IAnalyticsService _analyticsService;
    private readonly IConversationRecallService _conversationRecallService;
    private readonly IConversationSummaryService _conversationSummaryService;
    private readonly IConversationThemeClusterService _conversationThemeClusterService;
    private readonly ILogger _log;

    // ── Loading State ────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    // ── Summary Card Values ──────────────────────────────────────────────────

    [ObservableProperty] private string _totalConversations = "0";
    [ObservableProperty] private string _totalMessages = "0";
    [ObservableProperty] private string _totalTokensUsed = "0";
    [ObservableProperty] private string _totalDocuments = "0";
    [ObservableProperty] private string _totalSearches = "0";
    [ObservableProperty] private string _totalWorkflowRuns = "0";
    [ObservableProperty] private string _averageResponseTime = "0 ms";
    [ObservableProperty] private string _averageTokensPerMessage = "0";
    [ObservableProperty] private string _documentsIndexed = "0";
    [ObservableProperty] private string _documentsPending = "0";

    // ── Indexing Progress ────────────────────────────────────────────────────

    /// <summary>Fraction of documents that are indexed (0.0–1.0) for the progress indicator.</summary>
    [ObservableProperty] private double _indexingCompletionFraction;
    [ObservableProperty] private string _indexingCompletionLabel = "0%";

    // ── Daily Activity Trends ────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<AnalyticsDailyItem> _dailyConversations = new();
    [ObservableProperty] private ObservableCollection<AnalyticsDailyItem> _dailyDocuments = new();
    [ObservableProperty] private ObservableCollection<AnalyticsDailyItem> _dailySearches = new();

    [ObservableProperty] private bool _hasDailyConversationData;
    [ObservableProperty] private bool _hasDailyDocumentData;
    [ObservableProperty] private bool _hasDailySearchData;

    // ── Model Usage ──────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<AnalyticsModelItem> _modelUsage = new();
    [ObservableProperty] private bool _hasModelData;

    // ── File Type Distribution ───────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<AnalyticsFileTypeItem> _fileTypeDistribution = new();
    [ObservableProperty] private bool _hasFileTypeData;

    // ── Performance Metrics ──────────────────────────────────────────────────

    [ObservableProperty] private string _perfAverage = "— ms";
    [ObservableProperty] private string _perfMedian = "— ms";
    [ObservableProperty] private string _perfP95 = "— ms";
    [ObservableProperty] private string _perfFastest = "— ms";
    [ObservableProperty] private string _perfSlowest = "— ms";
    [ObservableProperty] private string _perfTotalInference = "—";
    [ObservableProperty] private string _perfTokensPerSecond = "—";
    [ObservableProperty] private bool _hasPerformanceData;

    // ── Conversation Intelligence ───────────────────────────────────────────

    [ObservableProperty] private string _summarizedConversations = "0";
    [ObservableProperty] private string _currentSummarySnapshots = "0";
    [ObservableProperty] private string _staleConversationSummaries = "0";
    [ObservableProperty] private string _pendingSummaryRefreshes = "0";
    [ObservableProperty] private ObservableCollection<AnalyticsConversationSummaryItem> _recentConversationSummaries = new();
    [ObservableProperty] private bool _hasConversationIntelligence;
    [ObservableProperty] private bool _hasRecentConversationSummaries;

    // ── Conversation Recall ─────────────────────────────────────────────────

    [ObservableProperty] private string _embeddedMessages = "0";
    [ObservableProperty] private string _pendingMessageEmbeddings = "0";
    [ObservableProperty] private string _recallReadyConversations = "0";
    [ObservableProperty] private string _lastMessageEmbeddingRefresh = "No embeddings yet";
    [ObservableProperty] private string _recallQuery = string.Empty;
    [ObservableProperty] private bool _isRecallRunning;
    [ObservableProperty] private string _recallStatusMessage = "Semantic recall searches durable message embeddings across past conversations.";
    [ObservableProperty] private ObservableCollection<AnalyticsConversationRecallItem> _conversationRecallResults = new();
    [ObservableProperty] private bool _hasConversationRecallCoverage;
    [ObservableProperty] private bool _hasConversationRecallResults;

    // ── Conversation Themes ─────────────────────────────────────────────────

    [ObservableProperty] private string _activeThemeClusters = "0";
    [ObservableProperty] private string _clusteredThemeConversations = "0";
    [ObservableProperty] private string _newThemeClusters7d = "0";
    [ObservableProperty] private string _lastThemeMaterialized = "No clusters yet";
    [ObservableProperty] private ObservableCollection<AnalyticsConversationThemeItem> _conversationThemeClusters = new();
    [ObservableProperty] private bool _hasConversationThemes;
    [ObservableProperty] private bool _hasConversationThemeClusters;

    // ── Computed Insights ────────────────────────────────────────────────────

    /// <summary>Formatted tokens per conversation (TotalTokens / TotalConversations).</summary>
    [ObservableProperty] private string _tokensPerConversation = "0";

    public AnalyticsViewModel(
        IAnalyticsService analyticsService,
        IConversationRecallService conversationRecallService,
        IConversationSummaryService conversationSummaryService,
        IConversationThemeClusterService conversationThemeClusterService,
        ILogger logger)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _conversationRecallService = conversationRecallService ?? throw new ArgumentNullException(nameof(conversationRecallService));
        _conversationSummaryService = conversationSummaryService ?? throw new ArgumentNullException(nameof(conversationSummaryService));
        _conversationThemeClusterService = conversationThemeClusterService ?? throw new ArgumentNullException(nameof(conversationThemeClusterService));
        _log = logger?.ForContext<AnalyticsViewModel>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Data Loading ─────────────────────────────────────────────────────────

    public async Task LoadDataAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        HasError  = false;

        try
        {
            _log.Information("Analytics: loading all metrics");

            await RefreshConversationSummariesAsync(ct);
            await RefreshConversationRecallCoverageAsync(ct);
            await RefreshConversationThemesAsync(ct);

            await Task.WhenAll(
                LoadSummaryAsync(ct),
                LoadDailyTrendsAsync(ct),
                LoadModelUsageAsync(ct),
                LoadFileTypeDistributionAsync(ct),
                LoadPerformanceAsync(ct),
                LoadConversationIntelligenceAsync(ct),
                LoadConversationRecallAsync(ct),
                LoadConversationThemesAsync(ct));

            _log.Information("Analytics: all metrics loaded");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Analytics: unexpected failure during full load");
            HasError     = true;
            ErrorMessage = "Failed to load analytics data. Please try refreshing.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSummaryAsync(CancellationToken ct)
    {
        try
        {
            var summary = await _analyticsService.GetSummaryAsync(ct);

            TotalConversations    = FormatNumber(summary.TotalConversations);
            TotalMessages         = FormatNumber(summary.TotalMessages);
            TotalTokensUsed       = FormatTokens(summary.TotalTokensUsed);
            TotalDocuments        = FormatNumber(summary.TotalDocuments);
            TotalSearches         = FormatNumber(summary.TotalSearches);
            TotalWorkflowRuns     = FormatNumber(summary.TotalWorkflowRuns);
            AverageResponseTime   = summary.AverageResponseTimeMs > 0
                ? $"{summary.AverageResponseTimeMs:F0} ms"
                : "N/A";
            AverageTokensPerMessage = summary.AverageTokensPerMessage > 0
                ? $"{summary.AverageTokensPerMessage:F0}"
                : "0";
            DocumentsIndexed = FormatNumber(summary.DocumentsIndexedCount);
            DocumentsPending = FormatNumber(summary.DocumentsPendingCount);

            // Indexing completion fraction
            var totalDocs = summary.DocumentsIndexedCount + summary.DocumentsPendingCount;
            if (totalDocs > 0)
            {
                IndexingCompletionFraction = (double)summary.DocumentsIndexedCount / totalDocs;
                var pct = (int)Math.Round(IndexingCompletionFraction * 100.0);
                IndexingCompletionLabel = $"{pct}%";
            }
            else
            {
                IndexingCompletionFraction = 1.0;
                IndexingCompletionLabel    = "100%";
            }

            // Tokens per conversation insight
            if (summary.TotalConversations > 0 && summary.TotalTokensUsed > 0)
            {
                var tpc = summary.TotalTokensUsed / (double)summary.TotalConversations;
                TokensPerConversation = FormatTokens((long)Math.Round(tpc));
            }
            else
            {
                TokensPerConversation = "0";
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load summary");
        }
    }

    private async Task LoadDailyTrendsAsync(CancellationToken ct)
    {
        try
        {
            var (convMetrics, docMetrics, searchMetrics) = await (
                _analyticsService.GetDailyConversationMetricsAsync(30, ct),
                _analyticsService.GetDailyDocumentMetricsAsync(30, ct),
                _analyticsService.GetDailySearchMetricsAsync(30, ct)
            ).WhenAll();

            DailyConversations       = BuildDailyItems(convMetrics, "#C41E3A");
            HasDailyConversationData = convMetrics.Any(m => m.Count > 0);

            DailyDocuments       = BuildDailyItems(docMetrics, "#3B82F6");
            HasDailyDocumentData = docMetrics.Any(m => m.Count > 0);

            DailySearches       = BuildDailyItems(searchMetrics, "#22C55E");
            HasDailySearchData  = searchMetrics.Any(m => m.Count > 0);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load daily trends");
            DailyConversations = new ObservableCollection<AnalyticsDailyItem>();
            DailyDocuments     = new ObservableCollection<AnalyticsDailyItem>();
            DailySearches      = new ObservableCollection<AnalyticsDailyItem>();
        }
    }

    private async Task LoadModelUsageAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await _analyticsService.GetModelUsageAsync(ct);
            HasModelData = metrics.Count > 0;

            // Color palette cycles through brand-consistent colors
            var colors = new[] { "#C41E3A", "#3B82F6", "#22C55E", "#F59E0B", "#A855F7", "#EC4899", "#06B6D4", "#F97316" };

            ModelUsage = new ObservableCollection<AnalyticsModelItem>(
                metrics.Select((m, i) => new AnalyticsModelItem
                {
                    ModelId              = m.ModelId,
                    ConversationCount    = m.ConversationCount,
                    TotalTokens          = FormatTokens(m.TotalTokens),
                    Percentage           = m.Percentage,
                    // BarWidthFraction: 0.0–1.0 for PercentToWidthConverter
                    BarWidthFraction     = m.Percentage / 100.0,
                    Color                = colors[i % colors.Length],
                    PercentageLabel      = $"{m.Percentage:F1}%",
                    CountLabel           = $"{m.ConversationCount} conv.",
                }));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load model usage");
            ModelUsage   = new ObservableCollection<AnalyticsModelItem>();
            HasModelData = false;
        }
    }

    private async Task LoadFileTypeDistributionAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await _analyticsService.GetFileTypeDistributionAsync(ct);
            HasFileTypeData = metrics.Count > 0;

            var colors = new[] { "#C41E3A", "#3B82F6", "#22C55E", "#F59E0B", "#A855F7", "#EC4899", "#06B6D4", "#F97316" };

            FileTypeDistribution = new ObservableCollection<AnalyticsFileTypeItem>(
                metrics.Select((m, i) => new AnalyticsFileTypeItem
                {
                    FileType         = m.FileType.ToUpperInvariant(),
                    Count            = m.Count,
                    TotalSize        = FormatHelper.FormatBytes(m.TotalSizeBytes),
                    Percentage       = m.Percentage,
                    // BarWidthFraction: 0.0–1.0 for PercentToWidthConverter
                    BarWidthFraction = m.Percentage / 100.0,
                    Color            = colors[i % colors.Length],
                    PercentageLabel  = $"{m.Percentage:F1}%",
                    CountLabel       = m.Count == 1 ? "1 file" : $"{m.Count} files",
                }));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load file type distribution");
            FileTypeDistribution = new ObservableCollection<AnalyticsFileTypeItem>();
            HasFileTypeData      = false;
        }
    }

    private async Task LoadPerformanceAsync(CancellationToken ct)
    {
        try
        {
            var perf = await _analyticsService.GetPerformanceMetricsAsync(ct);

            HasPerformanceData = perf.AverageResponseTimeMs > 0;

            if (HasPerformanceData)
            {
                PerfAverage        = FormatMs(perf.AverageResponseTimeMs);
                PerfMedian         = FormatMs(perf.MedianResponseTimeMs);
                PerfP95            = FormatMs(perf.P95ResponseTimeMs);
                PerfFastest        = FormatMs(perf.FastestResponseMs);
                PerfSlowest        = FormatMs(perf.SlowestResponseMs);
                PerfTotalInference = FormatMs(perf.TotalInferenceTimeMs);
                PerfTokensPerSecond = perf.AverageTokensPerSecond > 0
                    ? $"{perf.AverageTokensPerSecond:F1} tok/s"
                    : "N/A";
            }
            else
            {
                PerfAverage = PerfMedian = PerfP95 = PerfFastest =
                    PerfSlowest = PerfTotalInference = PerfTokensPerSecond = "N/A";
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load performance metrics");
            HasPerformanceData = false;
            PerfAverage = PerfMedian = PerfP95 = PerfFastest =
                PerfSlowest = PerfTotalInference = PerfTokensPerSecond = "N/A";
        }
    }

    private async Task RefreshConversationSummariesAsync(CancellationToken ct)
    {
        try
        {
            var refreshed = await _conversationSummaryService
                .RefreshStaleSummariesAsync(4, ct)
                .ConfigureAwait(false);

            _log.Debug("Analytics: refreshed {Count} durable conversation summaries", refreshed);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: durable conversation summary refresh failed");
        }
    }

    private async Task RefreshConversationRecallCoverageAsync(CancellationToken ct)
    {
        try
        {
            var refreshed = await _conversationRecallService
                .RefreshRecentConversationEmbeddingsAsync(4, ct)
                .ConfigureAwait(false);

            _log.Debug("Analytics: refreshed {Count} durable message embeddings", refreshed);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: durable message embedding refresh failed");
        }
    }

    private async Task RefreshConversationThemesAsync(CancellationToken ct)
    {
        try
        {
            var refreshed = await _conversationThemeClusterService
                .RefreshStaleClustersAsync(4, ct)
                .ConfigureAwait(false);

            _log.Debug("Analytics: refreshed {Count} durable conversation theme clusters", refreshed);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: durable conversation theme refresh failed");
        }
    }

    private async Task LoadConversationIntelligenceAsync(CancellationToken ct)
    {
        try
        {
            var overview = await _analyticsService.GetConversationIntelligenceAsync(ct: ct);

            SummarizedConversations = FormatNumber(overview.SummarizedConversations);
            CurrentSummarySnapshots = FormatNumber(overview.CurrentSnapshots);
            StaleConversationSummaries = FormatNumber(overview.StaleConversations);
            PendingSummaryRefreshes = FormatNumber(overview.PendingRefreshes);

            RecentConversationSummaries = new ObservableCollection<AnalyticsConversationSummaryItem>(
                overview.RecentSummaries.Select(summary => new AnalyticsConversationSummaryItem
                {
                    ConversationId = summary.ConversationId,
                    Title = summary.Title,
                    PreviewText = summary.PreviewText,
                    KeyPoints = summary.KeyPoints.ToList(),
                    CoveredMessageCount = summary.CoveredMessageCount,
                    GeneratedAt = summary.GeneratedAt,
                    StatusLabel = BuildConversationSummaryStatusLabel(summary),
                    StatusColor = summary.HasRefreshError
                        ? "#F59E0B"
                        : summary.IsStale
                            ? "#C41E3A"
                            : "#22C55E",
                    GeneratedAtLabel = BuildRelativeTimeLabel(summary.GeneratedAt)
                }));

            HasRecentConversationSummaries = RecentConversationSummaries.Count > 0;
            HasConversationIntelligence = overview.SummarizedConversations > 0
                || overview.CurrentSnapshots > 0
                || overview.StaleConversations > 0
                || overview.PendingRefreshes > 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load conversation intelligence");
            RecentConversationSummaries = new ObservableCollection<AnalyticsConversationSummaryItem>();
            HasRecentConversationSummaries = false;
            HasConversationIntelligence = false;
        }
    }

    private async Task LoadConversationRecallAsync(CancellationToken ct)
    {
        try
        {
            var overview = await _analyticsService.GetConversationRecallOverviewAsync(ct);

            EmbeddedMessages = FormatNumber(overview.EmbeddedMessages);
            PendingMessageEmbeddings = FormatNumber(overview.PendingMessageEmbeddings);
            RecallReadyConversations = FormatNumber(overview.RecallReadyConversations);
            LastMessageEmbeddingRefresh = overview.LastEmbeddedAt.HasValue
                ? BuildRelativeTimeLabel(overview.LastEmbeddedAt.Value)
                : "No embeddings yet";

            HasConversationRecallCoverage = overview.EmbeddedMessages > 0
                || overview.PendingMessageEmbeddings > 0
                || overview.RecallReadyConversations > 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load conversation recall overview");
            HasConversationRecallCoverage = false;
            LastMessageEmbeddingRefresh = "No embeddings yet";
        }
    }

    private async Task LoadConversationThemesAsync(CancellationToken ct)
    {
        try
        {
            var overview = await _analyticsService.GetConversationThemeOverviewAsync(ct: ct);

            ActiveThemeClusters = FormatNumber(overview.ActiveThemeClusters);
            ClusteredThemeConversations = FormatNumber(overview.ClusteredConversations);
            NewThemeClusters7d = FormatNumber(overview.NewThemes7d);
            LastThemeMaterialized = overview.LastMaterializedAt.HasValue
                ? BuildRelativeTimeLabel(overview.LastMaterializedAt.Value)
                : "No clusters yet";

            ConversationThemeClusters = new ObservableCollection<AnalyticsConversationThemeItem>(
                overview.Clusters.Select(cluster => new AnalyticsConversationThemeItem
                {
                    ClusterId = cluster.ClusterId,
                    Label = cluster.Label,
                    PreviewText = cluster.PreviewText,
                    KeyPoints = cluster.KeyPoints.ToList(),
                    ConversationCount = cluster.ConversationCount,
                    ActiveConversationCount7d = cluster.ActiveConversationCount7d,
                    ActiveConversationCount30d = cluster.ActiveConversationCount30d,
                    LastActiveAtLabel = BuildRelativeTimeLabel(cluster.LastActiveAt),
                    RecentConversationTitles = cluster.RecentConversationTitles.ToList()
                }));

            HasConversationThemeClusters = ConversationThemeClusters.Count > 0;
            HasConversationThemes = overview.ActiveThemeClusters > 0
                || overview.ClusteredConversations > 0
                || overview.NewThemes7d > 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load conversation themes");
            ConversationThemeClusters = new ObservableCollection<AnalyticsConversationThemeItem>();
            HasConversationThemeClusters = false;
            HasConversationThemes = false;
            LastThemeMaterialized = "No clusters yet";
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    partial void OnRecallQueryChanged(string value)
    {
        RunConversationRecallCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRecallRunningChanged(bool value)
    {
        RunConversationRecallCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _log.Debug("Analytics: manual refresh requested");
        await LoadDataAsync();
    }

    private bool CanRunConversationRecall() =>
        !IsRecallRunning && !string.IsNullOrWhiteSpace(RecallQuery);

    [RelayCommand(CanExecute = nameof(CanRunConversationRecall))]
    private async Task RunConversationRecallAsync(CancellationToken ct = default)
    {
        if (!CanRunConversationRecall())
        {
            return;
        }

        IsRecallRunning = true;
        RecallStatusMessage = "Refreshing recent message embeddings and running recall...";

        try
        {
            await _conversationRecallService
                .RefreshRecentConversationEmbeddingsAsync(6, ct)
                .ConfigureAwait(false);

            var results = await _conversationRecallService
                .SearchRelevantMessagesAsync(RecallQuery, maxResults: 6, minSimilarity: 0.68f, ct: ct)
                .ConfigureAwait(false);

            ConversationRecallResults = new ObservableCollection<AnalyticsConversationRecallItem>(
                results.Select(result => new AnalyticsConversationRecallItem
                {
                    ConversationId = result.ConversationId,
                    MessageId = result.MessageId,
                    ConversationTitle = result.ConversationTitle,
                    Role = result.Role,
                    RoleLabel = result.Role == "assistant" ? "Assistant" : "User",
                    PreviewText = result.ContentPreview,
                    Similarity = result.Similarity,
                    SimilarityLabel = $"{Math.Round(result.Similarity * 100)}% match",
                    Timestamp = result.Timestamp,
                    TimestampLabel = BuildRelativeTimeLabel(result.Timestamp)
                }));

            HasConversationRecallResults = ConversationRecallResults.Count > 0;
            RecallStatusMessage = ConversationRecallResults.Count == 0
                ? "No durable recall matches cleared the current similarity threshold."
                : ConversationRecallResults.Count == 1
                    ? "1 durable recall match found."
                    : $"{ConversationRecallResults.Count} durable recall matches found.";

            await LoadConversationRecallAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: conversation recall query failed");
            ConversationRecallResults = new ObservableCollection<AnalyticsConversationRecallItem>();
            HasConversationRecallResults = false;
            RecallStatusMessage = "Conversation recall failed. Check embedding availability and try again.";
        }
        finally
        {
            IsRecallRunning = false;
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts a list of <see cref="DailyMetric"/> records into display items with
    /// bar heights normalized relative to the maximum count in the series.
    /// </summary>
    private static ObservableCollection<AnalyticsDailyItem> BuildDailyItems(
        IReadOnlyList<DailyMetric> metrics,
        string color)
    {
        var max = metrics.Count > 0 ? metrics.Max(m => m.Count) : 0;
        if (max == 0) max = 1; // avoid division by zero

        return new ObservableCollection<AnalyticsDailyItem>(
            metrics.Select(m => new AnalyticsDailyItem
            {
                Date            = m.Date,
                Count           = m.Count,
                Label           = m.Label,
                Color           = color,
                // BarHeightPercent: 0–100 relative to the series maximum
                BarHeightPercent = m.Count * 100.0 / max,
                // Clamp minimum bar height so zero days are visually distinguishable
                BarHeight        = m.Count > 0 ? Math.Max(2.0, m.Count * 60.0 / max) : 1.0,
            }));
    }

    private static string FormatNumber(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M"
        : value >= 1_000   ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private static string FormatNumber(int value) => FormatNumber((long)value);

    private static string FormatTokens(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F2}M"
        : value >= 1_000   ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private static string FormatMs(double ms) =>
        ms >= 60_000 ? $"{ms / 60_000.0:F1} min"
        : ms >= 1_000 ? $"{ms / 1_000.0:F2} s"
        : $"{ms:F0} ms";

    private static string BuildConversationSummaryStatusLabel(ConversationSummaryMetric summary)
    {
        if (summary.HasRefreshError)
        {
            return "Refresh issue";
        }

        if (summary.IsStale && summary.PendingMessageCount > 0)
        {
            return summary.PendingMessageCount == 1
                ? "1 new message"
                : $"{summary.PendingMessageCount} new messages";
        }

        return "Current";
    }

    private static string BuildRelativeTimeLabel(DateTime generatedAt)
    {
        var elapsed = DateTime.UtcNow - generatedAt;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} hr ago";
        }

        var days = Math.Max(1, (int)elapsed.TotalDays);
        return days == 1 ? "1 day ago" : $"{days} days ago";
    }

    public void Dispose()
    {
        _log.Debug("AnalyticsViewModel disposed");
    }
}

// ═══════════════════════════════════════════════════════════════════
//  DISPLAY ITEM CLASSES (top-level for x:Bind DataTemplate support)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a single day's activity for display in a bar-chart row.
/// </summary>
public sealed class AnalyticsDailyItem
{
    public DateTime Date             { get; init; }
    public int      Count            { get; init; }
    public string   Label            { get; init; } = string.Empty;
    public string   Color            { get; init; } = "#C41E3A";
    public double   BarHeightPercent { get; init; }
    public double   BarHeight        { get; init; }
    public string   CountLabel       => Count.ToString("N0");
    public string   Tooltip          => $"{Label}: {Count:N0}";
}

/// <summary>
/// Represents a single AI model's usage share for display in a horizontal bar chart.
/// </summary>
public sealed class AnalyticsModelItem
{
    public string ModelId           { get; init; } = string.Empty;
    public int    ConversationCount { get; init; }
    public string TotalTokens       { get; init; } = string.Empty;
    public double Percentage        { get; init; }
    /// <summary>Bar fill fraction in [0.0, 1.0] for use with <c>PercentToWidthConverter</c>.</summary>
    public double BarWidthFraction  { get; init; }
    public string Color             { get; init; } = "#C41E3A";
    public string PercentageLabel   { get; init; } = string.Empty;
    public string CountLabel        { get; init; } = string.Empty;
    public string DisplayName       => string.IsNullOrEmpty(ModelId) ? "Unknown" : ModelId;
}

/// <summary>
/// Represents a single file type's document share for display in a horizontal bar chart.
/// </summary>
public sealed class AnalyticsFileTypeItem
{
    public string FileType          { get; init; } = string.Empty;
    public int    Count             { get; init; }
    public string TotalSize         { get; init; } = string.Empty;
    public double Percentage        { get; init; }
    /// <summary>Bar fill fraction in [0.0, 1.0] for use with <c>PercentToWidthConverter</c>.</summary>
    public double BarWidthFraction  { get; init; }
    public string Color             { get; init; } = "#3B82F6";
    public string PercentageLabel   { get; init; } = string.Empty;
    public string CountLabel        { get; init; } = string.Empty;
}

/// <summary>
/// Represents one persisted conversation summary preview for the Analytics page.
/// </summary>
public sealed class AnalyticsConversationSummaryItem
{
    public long ConversationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public IReadOnlyList<string> KeyPoints { get; init; } = Array.Empty<string>();
    public int CoveredMessageCount { get; init; }
    public DateTime GeneratedAt { get; init; }
    public string GeneratedAtLabel { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string StatusColor { get; init; } = "#22C55E";
    public bool HasKeyPoints => KeyPoints.Count > 0;
    public string KeyPointsPreview => string.Join(" · ", KeyPoints);
    public string CoverageLabel => CoveredMessageCount == 1
        ? "1 message covered"
        : $"{CoveredMessageCount} messages covered";
}

/// <summary>
/// Represents one semantic recall match across persisted conversation messages.
/// </summary>
public sealed class AnalyticsConversationRecallItem
{
    public long ConversationId { get; init; }
    public long MessageId { get; init; }
    public string ConversationTitle { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string RoleLabel { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public float Similarity { get; init; }
    public string SimilarityLabel { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string TimestampLabel { get; init; } = string.Empty;
    public string ConversationLabel => $"{ConversationTitle} · {RoleLabel}";
}

/// <summary>
/// Represents one durable conversation theme cluster for Analytics.
/// </summary>
public sealed class AnalyticsConversationThemeItem
{
    public long ClusterId { get; init; }
    public string Label { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public IReadOnlyList<string> KeyPoints { get; init; } = Array.Empty<string>();
    public int ConversationCount { get; init; }
    public int ActiveConversationCount7d { get; init; }
    public int ActiveConversationCount30d { get; init; }
    public string LastActiveAtLabel { get; init; } = string.Empty;
    public IReadOnlyList<string> RecentConversationTitles { get; init; } = Array.Empty<string>();
    public bool HasKeyPoints => KeyPoints.Count > 0;
    public bool HasRecentConversations => RecentConversationTitles.Count > 0;
    public string KeyPointsPreview => string.Join(" · ", KeyPoints);
    public string RecentConversationsPreview => string.Join(" · ", RecentConversationTitles);
    public string ActivityLabel => $"{ConversationCount} conversations · {ActiveConversationCount7d} active / 7d · {ActiveConversationCount30d} active / 30d";
}

// ─── Task tuple extension ────────────────────────────────────────────────────
// Allows awaiting a ValueTuple of Tasks elegantly in LoadDailyTrendsAsync.

file static class TaskTupleExtensions
{
    public static async Task<(T1, T2, T3)> WhenAll<T1, T2, T3>(
        this (Task<T1> t1, Task<T2> t2, Task<T3> t3) tasks)
    {
        await Task.WhenAll(tasks.t1, tasks.t2, tasks.t3);
        return (await tasks.t1, await tasks.t2, await tasks.t3);
    }
}

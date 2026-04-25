using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.App.Services;
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
    private readonly IConversationThemeTrendService _conversationThemeTrendService;
    private readonly IOperationsDrillInService? _operationsDrillInService;
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

    // ── Workflow Intelligence ──────────────────────────────────────────────

    [ObservableProperty] private string _workflowRunsTotal = "0";
    [ObservableProperty] private string _workflowSuccessRate = "—";
    [ObservableProperty] private string _workflowAverageRunDuration = "—";
    [ObservableProperty] private string _workflowActiveRecently = "0";
    [ObservableProperty] private string _workflowIntelligenceStatusMessage = "No workflow runs yet. Run a workflow to seed this section.";
    [ObservableProperty] private ObservableCollection<AnalyticsDailyItem> _dailyWorkflowRuns = new();
    [ObservableProperty] private ObservableCollection<AnalyticsWorkflowTopItem> _topWorkflows = new();
    [ObservableProperty] private ObservableCollection<AnalyticsWorkflowRecentRunItem> _recentWorkflowRuns = new();
    [ObservableProperty] private bool _hasWorkflowIntelligence;
    [ObservableProperty] private bool _hasWorkflowTrendData;
    [ObservableProperty] private bool _hasTopWorkflows;
    [ObservableProperty] private bool _hasRecentWorkflowRuns;

    // ── Conversation Intelligence ───────────────────────────────────────────

    [ObservableProperty] private string _summarizedConversations = "0";
    [ObservableProperty] private string _currentSummarySnapshots = "0";
    [ObservableProperty] private string _staleConversationSummaries = "0";
    [ObservableProperty] private string _pendingSummaryRefreshes = "0";
    [ObservableProperty] private ObservableCollection<AnalyticsConversationSummaryItem> _recentConversationSummaries = new();
    [ObservableProperty] private bool _hasConversationIntelligence;
    [ObservableProperty] private bool _hasRecentConversationSummaries;
    [ObservableProperty] private string _conversationIntelligenceStatusMessage = string.Empty;
    [ObservableProperty] private long _focusedConversationSummaryId;
    [ObservableProperty] private string _focusedConversationSourceLabel = string.Empty;
    public bool HasFocusedConversationLanding => !string.IsNullOrWhiteSpace(FocusedConversationSourceLabel);
    public bool HasConversationIntelligenceStatusMessage => !string.IsNullOrWhiteSpace(ConversationIntelligenceStatusMessage);

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

    // ── Theme Trends ────────────────────────────────────────────────────────

    [ObservableProperty] private string _trendingThemes = "0";
    [ObservableProperty] private string _newThemeEntries7d = "0";
    [ObservableProperty] private string _mostActiveTheme = "No trend data yet";
    [ObservableProperty] private string _lastThemeTrendRefresh = "No trends yet";
    [ObservableProperty] private ObservableCollection<AnalyticsConversationThemeTrendItem> _conversationThemeTrends = new();
    [ObservableProperty] private bool _hasConversationThemeTrends;

    // ── Computed Insights ────────────────────────────────────────────────────

    /// <summary>Formatted tokens per conversation (TotalTokens / TotalConversations).</summary>
    [ObservableProperty] private string _tokensPerConversation = "0";

    public AnalyticsViewModel(
        IAnalyticsService analyticsService,
        IConversationRecallService conversationRecallService,
        IConversationSummaryService conversationSummaryService,
        IConversationThemeClusterService conversationThemeClusterService,
        IConversationThemeTrendService conversationThemeTrendService,
        ILogger logger,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _conversationRecallService = conversationRecallService ?? throw new ArgumentNullException(nameof(conversationRecallService));
        _conversationSummaryService = conversationSummaryService ?? throw new ArgumentNullException(nameof(conversationSummaryService));
        _conversationThemeClusterService = conversationThemeClusterService ?? throw new ArgumentNullException(nameof(conversationThemeClusterService));
        _conversationThemeTrendService = conversationThemeTrendService ?? throw new ArgumentNullException(nameof(conversationThemeTrendService));
        _operationsDrillInService = operationsDrillInService;
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
            await RefreshConversationThemeTrendsAsync(ct);

            await Task.WhenAll(
                LoadSummaryAsync(ct),
                LoadDailyTrendsAsync(ct),
                LoadModelUsageAsync(ct),
                LoadFileTypeDistributionAsync(ct),
                LoadPerformanceAsync(ct),
                LoadWorkflowIntelligenceAsync(ct),
                LoadConversationIntelligenceAsync(ct),
                LoadConversationRecallAsync(ct),
                LoadConversationThemesAsync(ct),
                LoadConversationThemeTrendsAsync(ct));

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

    private async Task LoadWorkflowIntelligenceAsync(CancellationToken ct)
    {
        try
        {
            var overviewTask = _analyticsService.GetWorkflowIntelligenceOverviewAsync(ct: ct);
            var dailyTask = _analyticsService.GetDailyWorkflowRunMetricsAsync(30, ct);

            await Task.WhenAll(overviewTask, dailyTask);

            var overview = await overviewTask;
            var dailyMetrics = await dailyTask;
            var completedOutcomes = overview.SuccessfulRuns + overview.FailedOrCancelledRuns;

            WorkflowRunsTotal = FormatNumber(overview.TotalRuns);
            WorkflowSuccessRate = completedOutcomes > 0
                ? $"{overview.SuccessRate:F1}%"
                : "—";
            WorkflowAverageRunDuration = overview.AverageRunDurationMs > 0
                ? FormatMs(overview.AverageRunDurationMs)
                : "—";
            WorkflowActiveRecently = FormatNumber(overview.ActiveWorkflowsRecently);

            DailyWorkflowRuns = BuildDailyItems(dailyMetrics, "#F97316");
            HasWorkflowTrendData = dailyMetrics.Any(metric => metric.Count > 0);

            TopWorkflows = new ObservableCollection<AnalyticsWorkflowTopItem>(
                overview.TopWorkflows.Select(workflow => new AnalyticsWorkflowTopItem
                {
                    WorkflowId = workflow.WorkflowId,
                    WorkflowName = workflow.WorkflowName,
                    Category = workflow.Category,
                    RunVolumeLabel = BuildWorkflowRunVolumeLabel(workflow.RunCount),
                    SuccessRateLabel = BuildWorkflowSuccessRateLabel(workflow.SuccessRate, workflow.SuccessfulRuns, workflow.FailedOrCancelledRuns),
                    ReliabilityLabel = BuildWorkflowReliabilityLabel(workflow.SuccessfulRuns, workflow.FailedOrCancelledRuns),
                    LastRunLabel = BuildRelativeTimeLabel(workflow.LastRunAt)
                }));
            HasTopWorkflows = TopWorkflows.Count > 0;

            RecentWorkflowRuns = new ObservableCollection<AnalyticsWorkflowRecentRunItem>(
                overview.RecentRuns.Select(run => new AnalyticsWorkflowRecentRunItem
                {
                    WorkflowRunId = run.WorkflowRunId,
                    WorkflowId = run.WorkflowId,
                    WorkflowName = run.WorkflowName,
                    StatusLabel = BuildWorkflowStatusLabel(run.Status),
                    StartedAtLabel = BuildRelativeTimeLabel(run.StartedAt),
                    DurationLabel = BuildWorkflowRunDurationLabel(run.Status, run.DurationMs),
                    PreviewText = run.PreviewText
                }));
            HasRecentWorkflowRuns = RecentWorkflowRuns.Count > 0;

            HasWorkflowIntelligence = overview.TotalRuns > 0
                || overview.TopWorkflows.Count > 0
                || overview.RecentRuns.Count > 0;

            WorkflowIntelligenceStatusMessage = HasWorkflowIntelligence
                ? string.Empty
                : "No workflow runs yet. Run a workflow to seed reliability, trend, and result analytics.";
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load workflow intelligence");
            WorkflowRunsTotal = "0";
            WorkflowSuccessRate = "—";
            WorkflowAverageRunDuration = "—";
            WorkflowActiveRecently = "0";
            DailyWorkflowRuns = new ObservableCollection<AnalyticsDailyItem>();
            TopWorkflows = new ObservableCollection<AnalyticsWorkflowTopItem>();
            RecentWorkflowRuns = new ObservableCollection<AnalyticsWorkflowRecentRunItem>();
            HasWorkflowTrendData = false;
            HasTopWorkflows = false;
            HasRecentWorkflowRuns = false;
            HasWorkflowIntelligence = false;
            WorkflowIntelligenceStatusMessage = "Workflow analytics are unavailable right now. Refresh and try again.";
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

    private async Task RefreshConversationThemeTrendsAsync(CancellationToken ct)
    {
        try
        {
            var refreshed = await _conversationThemeTrendService
                .RefreshRecentClusterTrendsAsync(4, 30, ct)
                .ConfigureAwait(false);

            _log.Debug("Analytics: refreshed {Count} durable conversation theme trend windows", refreshed);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: durable conversation theme trend refresh failed");
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

            var recentSummaries = overview.RecentSummaries.Select(summary => new AnalyticsConversationSummaryItem
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
                })
                .ToList();

            ApplyConversationSummaryFocus(recentSummaries);

            RecentConversationSummaries = new ObservableCollection<AnalyticsConversationSummaryItem>(recentSummaries);

            HasRecentConversationSummaries = RecentConversationSummaries.Count > 0;
            HasConversationIntelligence = overview.SummarizedConversations > 0
                || overview.CurrentSnapshots > 0
                || overview.StaleConversations > 0
                || overview.PendingRefreshes > 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load conversation intelligence");
            FocusedConversationSummaryId = 0;
            FocusedConversationSourceLabel = string.Empty;
            RecentConversationSummaries = new ObservableCollection<AnalyticsConversationSummaryItem>();
            HasRecentConversationSummaries = false;
            HasConversationIntelligence = false;
        }
    }

    private void ApplyConversationSummaryFocus(List<AnalyticsConversationSummaryItem> items)
    {
        var request = _operationsDrillInService?.ConsumePendingConversationRequest();
        if (request is not null && request.ConversationId > 0)
        {
            FocusedConversationSummaryId = request.ConversationId;
            FocusedConversationSourceLabel = request.SourceLabel;
            ConversationIntelligenceStatusMessage = string.Empty;
        }

        if (FocusedConversationSummaryId <= 0 || string.IsNullOrWhiteSpace(FocusedConversationSourceLabel) || items.Count == 0)
        {
            if (items.Count == 0)
            {
                FocusedConversationSummaryId = 0;
                FocusedConversationSourceLabel = string.Empty;
            }

            return;
        }

        var index = items.FindIndex(item => item.ConversationId == FocusedConversationSummaryId);
        if (index < 0)
        {
            FocusedConversationSummaryId = 0;
            FocusedConversationSourceLabel = string.Empty;
            return;
        }

        var target = items[index];
        items[index] = CloneConversationSummaryItem(target, true, FocusedConversationSourceLabel);

        var focused = items[index];
        items.RemoveAt(index);
        items.Insert(0, focused);
    }

    private static AnalyticsConversationSummaryItem CloneConversationSummaryItem(
        AnalyticsConversationSummaryItem item,
        bool isFocused,
        string? sourceLabel = null) => new()
    {
        ConversationId = item.ConversationId,
        Title = item.Title,
        PreviewText = item.PreviewText,
        KeyPoints = item.KeyPoints,
        CoveredMessageCount = item.CoveredMessageCount,
        GeneratedAt = item.GeneratedAt,
        GeneratedAtLabel = item.GeneratedAtLabel,
        StatusLabel = item.StatusLabel,
        StatusColor = item.StatusColor,
        IsFocused = isFocused,
        SourceLabel = isFocused ? sourceLabel ?? item.SourceLabel : string.Empty
    };

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

    private async Task LoadConversationThemeTrendsAsync(CancellationToken ct)
    {
        try
        {
            var overview = await _analyticsService.GetConversationThemeTrendOverviewAsync(ct: ct);

            TrendingThemes = FormatNumber(overview.TrendingThemes);
            NewThemeEntries7d = FormatNumber(overview.NewThemeEntries7d);
            MostActiveTheme = string.IsNullOrWhiteSpace(overview.MostActiveThemeLabel)
                ? "No trend data yet"
                : overview.MostActiveThemeLabel;
            LastThemeTrendRefresh = overview.LastTrendRefresh.HasValue
                ? BuildRelativeTimeLabel(overview.LastTrendRefresh.Value)
                : "No trends yet";

            ConversationThemeTrends = new ObservableCollection<AnalyticsConversationThemeTrendItem>(
                overview.Trends.Select(metric =>
                {
                    var recent30DayActivity = metric.DailySeries.Sum(point => point.ActiveConversationCount);
                    return new AnalyticsConversationThemeTrendItem
                    {
                        ClusterId = metric.ClusterId,
                        Label = metric.Label,
                        PreviewText = metric.PreviewText,
                        ActivitySummary = $"{metric.Recent7DayActivity} active / 7d · {recent30DayActivity} active / 30d",
                        MomentumLabel = BuildThemeTrendMomentumLabel(metric.Recent7DayActivity, metric.Previous7DayActivity),
                        NewEntriesLabel = BuildThemeTrendNewEntriesLabel(metric.Recent7DayNewEntries),
                        LastActiveAtLabel = BuildRelativeTimeLabel(metric.LastActiveAt),
                        Bars = BuildThemeTrendBars(metric.DailySeries)
                    };
                }));

            HasConversationThemeTrends = ConversationThemeTrends.Count > 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load conversation theme trends");
            ConversationThemeTrends = new ObservableCollection<AnalyticsConversationThemeTrendItem>();
            HasConversationThemeTrends = false;
            MostActiveTheme = "No trend data yet";
            LastThemeTrendRefresh = "No trends yet";
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    partial void OnRecallQueryChanged(string value)
    {
        RunConversationRecallCommand.NotifyCanExecuteChanged();
    }

    partial void OnConversationIntelligenceStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasConversationIntelligenceStatusMessage));

    partial void OnFocusedConversationSummaryIdChanged(long value) =>
        RefreshFocusedConversationSummaryCommand.NotifyCanExecuteChanged();

    partial void OnIsRecallRunningChanged(bool value)
    {
        RunConversationRecallCommand.NotifyCanExecuteChanged();
    }

    partial void OnFocusedConversationSourceLabelChanged(string value)
    {
        OnPropertyChanged(nameof(HasFocusedConversationLanding));
        RefreshFocusedConversationSummaryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _log.Debug("Analytics: manual refresh requested");
        await LoadDataAsync();
    }

    [RelayCommand]
    private void DismissFocusedConversationLanding()
    {
        ClearFocusedConversationLanding();
    }

    private bool CanRefreshFocusedConversationSummary() =>
        FocusedConversationSummaryId > 0 && !string.IsNullOrWhiteSpace(FocusedConversationSourceLabel);

    [RelayCommand(CanExecute = nameof(CanRefreshFocusedConversationSummary))]
    private async Task RefreshFocusedConversationSummaryAsync(CancellationToken ct = default)
    {
        if (!CanRefreshFocusedConversationSummary())
        {
            return;
        }

        var targetConversationId = FocusedConversationSummaryId;
        var targetTitle = RecentConversationSummaries
            .FirstOrDefault(item => item.ConversationId == targetConversationId)?
            .Title;

        try
        {
            var refreshed = await _conversationSummaryService
                .RefreshConversationSummaryAsync(targetConversationId, ct)
                .ConfigureAwait(false);

            if (!refreshed)
            {
                ConversationIntelligenceStatusMessage = BuildConversationSummaryRefreshUnchangedMessage(targetTitle);
                return;
            }

            ClearFocusedConversationLanding();
            await LoadConversationIntelligenceAsync(ct).ConfigureAwait(false);
            ConversationIntelligenceStatusMessage = BuildConversationSummaryResolutionMessage(targetTitle);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: focused durable summary refresh failed for conversation {ConversationId}", targetConversationId);
            ConversationIntelligenceStatusMessage = "Focused durable summary refresh failed. Check AI connectivity and try again.";
        }
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

    private void ClearFocusedConversationLanding()
    {
        FocusedConversationSummaryId = 0;
        FocusedConversationSourceLabel = string.Empty;

        if (RecentConversationSummaries.Count == 0)
        {
            return;
        }

        RecentConversationSummaries = new ObservableCollection<AnalyticsConversationSummaryItem>(
            RecentConversationSummaries.Select(item => CloneConversationSummaryItem(item, false)));
    }

    private static string BuildConversationSummaryResolutionMessage(string? title)
    {
        var resolvedLabel = !string.IsNullOrWhiteSpace(title)
            ? $"\"{title}\""
            : "the focused conversation summary";
        return $"Resolved {resolvedLabel} by refreshing its durable summary.";
    }

    private static string BuildConversationSummaryRefreshUnchangedMessage(string? title)
    {
        var resolvedLabel = !string.IsNullOrWhiteSpace(title)
            ? $"\"{title}\""
            : "the focused conversation summary";
        return $"No refreshed durable summary was generated for {resolvedLabel}. The current state was kept.";
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

    private static IReadOnlyList<AnalyticsConversationThemeTrendBarItem> BuildThemeTrendBars(
        IReadOnlyList<ConversationThemeDailyPoint> points)
    {
        if (points.Count == 0)
        {
            return Array.Empty<AnalyticsConversationThemeTrendBarItem>();
        }

        var totals = points
            .Select(point => point.ActiveConversationCount + point.NewConversationCount + point.SnapshotRefreshCount)
            .ToList();
        var max = Math.Max(1, totals.Max());

        return points.Select(point =>
        {
            var total = point.ActiveConversationCount + point.NewConversationCount + point.SnapshotRefreshCount;
            return new AnalyticsConversationThemeTrendBarItem
            {
                Date = point.Date,
                BarHeight = total > 0 ? Math.Max(4.0, total * 44.0 / max) : 2.0,
                Tooltip = $"{point.Date:MMM d}: {point.ActiveConversationCount} active, {point.NewConversationCount} new, {point.SnapshotRefreshCount} snapshots"
            };
        }).ToList();
    }

    private static string BuildThemeTrendMomentumLabel(int recent7DayActivity, int previous7DayActivity)
    {
        var delta = recent7DayActivity - previous7DayActivity;
        if (recent7DayActivity == 0 && previous7DayActivity == 0)
        {
            return "No recent movement";
        }

        if (delta > 0)
        {
            return $"+{delta} vs prior 7d";
        }

        if (delta < 0)
        {
            return $"{delta} vs prior 7d";
        }

        return "Flat vs prior 7d";
    }

    private static string BuildThemeTrendNewEntriesLabel(int recent7DayNewEntries)
    {
        if (recent7DayNewEntries <= 0)
        {
            return string.Empty;
        }

        return recent7DayNewEntries == 1
            ? "1 new theme entry this week"
            : $"{recent7DayNewEntries} new theme entries this week";
    }

    private static string BuildWorkflowRunVolumeLabel(int runCount) =>
        runCount == 1 ? "1 run" : $"{runCount} runs";

    private static string BuildWorkflowSuccessRateLabel(
        double successRate,
        int successfulRuns,
        int failedOrCancelledRuns)
    {
        var outcomeRuns = successfulRuns + failedOrCancelledRuns;
        return outcomeRuns > 0
            ? $"{successRate:F1}% success"
            : "No completed outcomes yet";
    }

    private static string BuildWorkflowReliabilityLabel(int successfulRuns, int failedOrCancelledRuns)
    {
        var outcomeRuns = successfulRuns + failedOrCancelledRuns;
        if (outcomeRuns == 0)
        {
            return "No completed outcomes yet";
        }

        if (failedOrCancelledRuns == 0)
        {
            return successfulRuns == 1
                ? "1 successful run"
                : $"{successfulRuns} successful runs";
        }

        return $"{successfulRuns} succeeded · {failedOrCancelledRuns} failed/cancelled";
    }

    private static string BuildWorkflowStatusLabel(string status) => status switch
    {
        "completed" => "Completed",
        "failed" => "Failed",
        "cancelled" => "Cancelled",
        "running" => "Running",
        "pending" => "Pending",
        _ => "Unknown"
    };

    private static string BuildWorkflowRunDurationLabel(string status, long? durationMs)
    {
        if (durationMs.HasValue && durationMs.Value > 0)
        {
            return FormatMs(durationMs.Value);
        }

        return status switch
        {
            "running" => "In progress",
            "pending" => "Queued",
            _ => "No duration"
        };
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
/// Represents one workflow rollup row for the Analytics workflow intelligence section.
/// </summary>
public sealed class AnalyticsWorkflowTopItem
{
    public long WorkflowId { get; init; }
    public string WorkflowName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RunVolumeLabel { get; init; } = string.Empty;
    public string SuccessRateLabel { get; init; } = string.Empty;
    public string ReliabilityLabel { get; init; } = string.Empty;
    public string LastRunLabel { get; init; } = string.Empty;
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);
}

/// <summary>
/// Represents one recent workflow run projection for the Analytics workflow intelligence section.
/// </summary>
public sealed class AnalyticsWorkflowRecentRunItem
{
    public long WorkflowRunId { get; init; }
    public long WorkflowId { get; init; }
    public string WorkflowName { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string StartedAtLabel { get; init; } = string.Empty;
    public string DurationLabel { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string TimelineLabel => string.IsNullOrWhiteSpace(DurationLabel)
        ? StartedAtLabel
        : $"{StartedAtLabel} · {DurationLabel}";
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
    public bool IsFocused { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
    public bool HasKeyPoints => KeyPoints.Count > 0;
    public bool HasSourceLabel => !string.IsNullOrWhiteSpace(SourceLabel);
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

/// <summary>
/// Represents one durable theme trend row for Analytics.
/// </summary>
public sealed class AnalyticsConversationThemeTrendItem
{
    public long ClusterId { get; init; }
    public string Label { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string ActivitySummary { get; init; } = string.Empty;
    public string MomentumLabel { get; init; } = string.Empty;
    public string NewEntriesLabel { get; init; } = string.Empty;
    public string LastActiveAtLabel { get; init; } = string.Empty;
    public IReadOnlyList<AnalyticsConversationThemeTrendBarItem> Bars { get; init; } = Array.Empty<AnalyticsConversationThemeTrendBarItem>();
    public bool HasNewEntries => !string.IsNullOrWhiteSpace(NewEntriesLabel);
}

/// <summary>
/// Represents one bar in the persisted 30-day theme trend strip.
/// </summary>
public sealed class AnalyticsConversationThemeTrendBarItem
{
    public DateTime Date { get; init; }
    public double BarHeight { get; init; }
    public string Tooltip { get; init; } = string.Empty;
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

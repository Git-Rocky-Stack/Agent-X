using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ILocalizationService _localization;
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
    [ObservableProperty] private string _averageResponseTime;
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

    [ObservableProperty] private string _perfAverage;
    [ObservableProperty] private string _perfMedian;
    [ObservableProperty] private string _perfP95;
    [ObservableProperty] private string _perfFastest;
    [ObservableProperty] private string _perfSlowest;
    [ObservableProperty] private string _perfTotalInference = "—";
    [ObservableProperty] private string _perfTokensPerSecond = "—";
    [ObservableProperty] private bool _hasPerformanceData;

    // ── Workflow Intelligence ──────────────────────────────────────────────

    [ObservableProperty] private string _workflowRunsTotal = "0";
    [ObservableProperty] private string _workflowSuccessRate = "—";
    [ObservableProperty] private string _workflowAverageRunDuration = "—";
    [ObservableProperty] private string _workflowActiveRecently = "0";
    [ObservableProperty] private string _workflowIntelligenceStatusMessage;
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
    [ObservableProperty] private string _lastMessageEmbeddingRefresh;
    [ObservableProperty] private string _recallQuery = string.Empty;
    [ObservableProperty] private bool _isRecallRunning;
    [ObservableProperty] private string _recallStatusMessage;
    [ObservableProperty] private ObservableCollection<AnalyticsConversationRecallItem> _conversationRecallResults = new();
    [ObservableProperty] private bool _hasConversationRecallCoverage;
    [ObservableProperty] private bool _hasConversationRecallResults;

    // ── Conversation Themes ─────────────────────────────────────────────────

    [ObservableProperty] private string _activeThemeClusters = "0";
    [ObservableProperty] private string _clusteredThemeConversations = "0";
    [ObservableProperty] private string _newThemeClusters7d = "0";
    [ObservableProperty] private string _lastThemeMaterialized;
    [ObservableProperty] private ObservableCollection<AnalyticsConversationThemeItem> _conversationThemeClusters = new();
    [ObservableProperty] private bool _hasConversationThemes;
    [ObservableProperty] private bool _hasConversationThemeClusters;

    // ── Theme Trends ────────────────────────────────────────────────────────

    [ObservableProperty] private string _trendingThemes = "0";
    [ObservableProperty] private string _newThemeEntries7d = "0";
    [ObservableProperty] private string _mostActiveTheme;
    [ObservableProperty] private string _lastThemeTrendRefresh;
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
        ILocalizationService localization,
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
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        _averageResponseTime = FormatMs(0);
        var noTiming = _localization.GetString("Ana_DurationMilliseconds", "\u2014");
        _perfAverage = noTiming;
        _perfMedian = noTiming;
        _perfP95 = noTiming;
        _perfFastest = noTiming;
        _perfSlowest = noTiming;
        _workflowIntelligenceStatusMessage = _localization.GetString("Ana_WorkflowNoRunsYet");
        _lastMessageEmbeddingRefresh = _localization.GetString("Ana_NoEmbeddingsYet");
        _recallStatusMessage = _localization.GetString("Ana_RecallIntro");
        _lastThemeMaterialized = _localization.GetString("Ana_NoClustersYet");
        _mostActiveTheme = _localization.GetString("Ana_NoTrendDataYet");
        _lastThemeTrendRefresh = _localization.GetString("Ana_NoTrendsYet");
    }

    // ── Data Loading ─────────────────────────────────────────────────────────

    public async Task LoadDataAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        HasError = false;

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
            HasError = true;
            ErrorMessage = _localization.GetString("Ana_LoadFailed");
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

            TotalConversations = FormatNumber(summary.TotalConversations);
            TotalMessages = FormatNumber(summary.TotalMessages);
            TotalTokensUsed = FormatTokens(summary.TotalTokensUsed);
            TotalDocuments = FormatNumber(summary.TotalDocuments);
            TotalSearches = FormatNumber(summary.TotalSearches);
            TotalWorkflowRuns = FormatNumber(summary.TotalWorkflowRuns);
            AverageResponseTime = summary.AverageResponseTimeMs > 0
                ? _localization.GetString("Ana_DurationMilliseconds", summary.AverageResponseTimeMs.ToString("F0"))
                : _localization.GetString("Ana_NotAvailable");
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
                IndexingCompletionLabel = "100%";
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

            DailyConversations = BuildDailyItems(convMetrics, "#AA2024");
            HasDailyConversationData = convMetrics.Any(m => m.Count > 0);

            DailyDocuments = BuildDailyItems(docMetrics, "#58C4BC");
            HasDailyDocumentData = docMetrics.Any(m => m.Count > 0);

            DailySearches = BuildDailyItems(searchMetrics, "#41E25E");
            HasDailySearchData = searchMetrics.Any(m => m.Count > 0);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load daily trends");
            DailyConversations = new ObservableCollection<AnalyticsDailyItem>();
            DailyDocuments = new ObservableCollection<AnalyticsDailyItem>();
            DailySearches = new ObservableCollection<AnalyticsDailyItem>();
        }
    }

    private async Task LoadModelUsageAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await _analyticsService.GetModelUsageAsync(ct);
            HasModelData = metrics.Count > 0;

            // Color palette cycles through brand-consistent colors
            var colors = new[] { "#AA2024", "#58C4BC", "#41E25E", "#FFB000", "#E6E6E6", "#E0252B", "#B3B3B3", "#7F171A" };

            ModelUsage = new ObservableCollection<AnalyticsModelItem>(
                metrics.Select((m, i) => new AnalyticsModelItem
                {
                    ModelId = m.ModelId,
                    DisplayName = string.IsNullOrEmpty(m.ModelId) ? _localization.GetString("Ana_UnknownModel") : m.ModelId,
                    ConversationCount = m.ConversationCount,
                    TotalTokens = FormatTokens(m.TotalTokens),
                    Percentage = m.Percentage,
                    // BarWidthFraction: 0.0–1.0 for PercentToWidthConverter
                    BarWidthFraction = m.Percentage / 100.0,
                    Color = colors[i % colors.Length],
                    PercentageLabel = $"{m.Percentage:F1}%",
                    CountLabel = _localization.GetString("Ana_ModelConversationCount", m.ConversationCount),
                }));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load model usage");
            ModelUsage = new ObservableCollection<AnalyticsModelItem>();
            HasModelData = false;
        }
    }

    private async Task LoadFileTypeDistributionAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await _analyticsService.GetFileTypeDistributionAsync(ct);
            HasFileTypeData = metrics.Count > 0;

            var colors = new[] { "#AA2024", "#58C4BC", "#41E25E", "#FFB000", "#E6E6E6", "#E0252B", "#B3B3B3", "#7F171A" };

            FileTypeDistribution = new ObservableCollection<AnalyticsFileTypeItem>(
                metrics.Select((m, i) => new AnalyticsFileTypeItem
                {
                    FileType = m.FileType.ToUpperInvariant(),
                    Count = m.Count,
                    TotalSize = FormatHelper.FormatBytes(m.TotalSizeBytes),
                    Percentage = m.Percentage,
                    // BarWidthFraction: 0.0–1.0 for PercentToWidthConverter
                    BarWidthFraction = m.Percentage / 100.0,
                    Color = colors[i % colors.Length],
                    PercentageLabel = $"{m.Percentage:F1}%",
                    CountLabel = m.Count == 1
                        ? _localization.GetString("Ana_FileCountOne")
                        : _localization.GetString("Ana_FileCountMany", m.Count),
                }));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load file type distribution");
            FileTypeDistribution = new ObservableCollection<AnalyticsFileTypeItem>();
            HasFileTypeData = false;
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
                PerfAverage = FormatMs(perf.AverageResponseTimeMs);
                PerfMedian = FormatMs(perf.MedianResponseTimeMs);
                PerfP95 = FormatMs(perf.P95ResponseTimeMs);
                PerfFastest = FormatMs(perf.FastestResponseMs);
                PerfSlowest = FormatMs(perf.SlowestResponseMs);
                PerfTotalInference = FormatMs(perf.TotalInferenceTimeMs);
                PerfTokensPerSecond = perf.AverageTokensPerSecond > 0
                    ? _localization.GetString("Ana_TokensPerSecond", perf.AverageTokensPerSecond.ToString("F1"))
                    : _localization.GetString("Ana_NotAvailable");
            }
            else
            {
                PerfAverage = PerfMedian = PerfP95 = PerfFastest =
                    PerfSlowest = PerfTotalInference = PerfTokensPerSecond = _localization.GetString("Ana_NotAvailable");
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load performance metrics");
            HasPerformanceData = false;
            PerfAverage = PerfMedian = PerfP95 = PerfFastest =
            PerfSlowest = PerfTotalInference = PerfTokensPerSecond = _localization.GetString("Ana_NotAvailable");
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

            DailyWorkflowRuns = BuildDailyItems(dailyMetrics, "#FFB000");
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
                : _localization.GetString("Ana_WorkflowNoRuns");
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
            WorkflowIntelligenceStatusMessage = _localization.GetString("Ana_WorkflowUnavailable");
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
                CoverageLabel = summary.CoveredMessageCount == 1
                    ? _localization.GetString("Ana_MessagesCoveredOne")
                    : _localization.GetString("Ana_MessagesCoveredMany", summary.CoveredMessageCount),
                GeneratedAt = summary.GeneratedAt,
                StatusLabel = BuildConversationSummaryStatusLabel(summary),
                StatusColor = summary.HasRefreshError
                        ? "#FFB000"
                        : summary.IsStale
                            ? "#AA2024"
                            : "#41E25E",
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
            CoverageLabel = item.CoverageLabel,
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
                : _localization.GetString("Ana_NoEmbeddingsYet");

            HasConversationRecallCoverage = overview.EmbeddedMessages > 0
                || overview.PendingMessageEmbeddings > 0
                || overview.RecallReadyConversations > 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: failed to load conversation recall overview");
            HasConversationRecallCoverage = false;
            LastMessageEmbeddingRefresh = _localization.GetString("Ana_NoEmbeddingsYet");
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
                : _localization.GetString("Ana_NoClustersYet");

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
                    ActivityLabel = _localization.GetString(
                        "Ana_ThemeClusterActivity",
                        cluster.ConversationCount,
                        cluster.ActiveConversationCount7d,
                        cluster.ActiveConversationCount30d),
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
            LastThemeMaterialized = _localization.GetString("Ana_NoClustersYet");
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
                ? _localization.GetString("Ana_NoTrendDataYet")
                : overview.MostActiveThemeLabel;
            LastThemeTrendRefresh = overview.LastTrendRefresh.HasValue
                ? BuildRelativeTimeLabel(overview.LastTrendRefresh.Value)
                : _localization.GetString("Ana_NoTrendsYet");

            ConversationThemeTrends = new ObservableCollection<AnalyticsConversationThemeTrendItem>(
                overview.Trends.Select(metric =>
                {
                    var recent30DayActivity = metric.DailySeries.Sum(point => point.ActiveConversationCount);
                    return new AnalyticsConversationThemeTrendItem
                    {
                        ClusterId = metric.ClusterId,
                        Label = metric.Label,
                        PreviewText = metric.PreviewText,
                        ActivitySummary = _localization.GetString(
                            "Ana_ThemeTrendActivity", metric.Recent7DayActivity, recent30DayActivity),
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
            MostActiveTheme = _localization.GetString("Ana_NoTrendDataYet");
            LastThemeTrendRefresh = _localization.GetString("Ana_NoTrendsYet");
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
                .RefreshConversationSummaryAsync(targetConversationId, ct);

            if (!refreshed)
            {
                ConversationIntelligenceStatusMessage = BuildConversationSummaryRefreshUnchangedMessage(targetTitle);
                return;
            }

            ClearFocusedConversationLanding();
            await LoadConversationIntelligenceAsync(ct);
            ConversationIntelligenceStatusMessage = BuildConversationSummaryResolutionMessage(targetTitle);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: focused durable summary refresh failed for conversation {ConversationId}", targetConversationId);
            ConversationIntelligenceStatusMessage = _localization.GetString("Ana_SummaryRefreshFailed");
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
        RecallStatusMessage = _localization.GetString("Ana_RecallRunning");

        try
        {
            await _conversationRecallService
                .RefreshRecentConversationEmbeddingsAsync(6, ct);

            var results = await _conversationRecallService
                .SearchRelevantMessagesAsync(RecallQuery, maxResults: 6, minSimilarity: 0.68f, ct: ct);

            ConversationRecallResults = new ObservableCollection<AnalyticsConversationRecallItem>(
                results.Select(result => new AnalyticsConversationRecallItem
                {
                    ConversationId = result.ConversationId,
                    MessageId = result.MessageId,
                    ConversationTitle = result.ConversationTitle,
                    Role = result.Role,
                    RoleLabel = result.Role == "assistant"
                        ? _localization.GetString("Ana_RoleAssistant")
                        : _localization.GetString("Ana_RoleUser"),
                    PreviewText = result.ContentPreview,
                    Similarity = result.Similarity,
                    SimilarityLabel = _localization.GetString("Ana_RecallMatchPercent", Math.Round(result.Similarity * 100)),
                    Timestamp = result.Timestamp,
                    TimestampLabel = BuildRelativeTimeLabel(result.Timestamp)
                }));

            HasConversationRecallResults = ConversationRecallResults.Count > 0;
            RecallStatusMessage = ConversationRecallResults.Count == 0
                ? _localization.GetString("Ana_RecallNoMatches")
                : ConversationRecallResults.Count == 1
                    ? _localization.GetString("Ana_RecallMatchesOne")
                    : _localization.GetString("Ana_RecallMatchesMany", ConversationRecallResults.Count);

            await LoadConversationRecallAsync(ct);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Analytics: conversation recall query failed");
            ConversationRecallResults = new ObservableCollection<AnalyticsConversationRecallItem>();
            HasConversationRecallResults = false;
            RecallStatusMessage = _localization.GetString("Ana_RecallFailed");
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

    private string BuildConversationSummaryResolutionMessage(string? title) =>
        !string.IsNullOrWhiteSpace(title)
            ? _localization.GetString("Ana_SummaryResolved", title)
            : _localization.GetString("Ana_SummaryResolvedUntitled");

    private string BuildConversationSummaryRefreshUnchangedMessage(string? title) =>
        !string.IsNullOrWhiteSpace(title)
            ? _localization.GetString("Ana_SummaryUnchanged", title)
            : _localization.GetString("Ana_SummaryUnchangedUntitled");

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
                Date = m.Date,
                Count = m.Count,
                Label = m.Label,
                Color = color,
                // BarHeightPercent: 0–100 relative to the series maximum
                BarHeightPercent = m.Count * 100.0 / max,
                // Clamp minimum bar height so zero days are visually distinguishable
                BarHeight = m.Count > 0 ? Math.Max(2.0, m.Count * 60.0 / max) : 1.0,
            }));
    }

    private IReadOnlyList<AnalyticsConversationThemeTrendBarItem> BuildThemeTrendBars(
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
                Tooltip = _localization.GetString(
                    "Ana_ThemeTrendBarTooltip",
                    point.Date.ToString("MMM d"),
                    point.ActiveConversationCount,
                    point.NewConversationCount,
                    point.SnapshotRefreshCount)
            };
        }).ToList();
    }

    private string BuildThemeTrendMomentumLabel(int recent7DayActivity, int previous7DayActivity)
    {
        var delta = recent7DayActivity - previous7DayActivity;
        if (recent7DayActivity == 0 && previous7DayActivity == 0)
        {
            return _localization.GetString("Ana_TrendNoMovement");
        }

        if (delta > 0)
        {
            return _localization.GetString("Ana_TrendVsPrior", $"+{delta}");
        }

        if (delta < 0)
        {
            return _localization.GetString("Ana_TrendVsPrior", delta);
        }

        return _localization.GetString("Ana_TrendFlat");
    }

    private string BuildThemeTrendNewEntriesLabel(int recent7DayNewEntries)
    {
        if (recent7DayNewEntries <= 0)
        {
            return string.Empty;
        }

        return recent7DayNewEntries == 1
            ? _localization.GetString("Ana_NewThemeEntriesOne")
            : _localization.GetString("Ana_NewThemeEntriesMany", recent7DayNewEntries);
    }

    private string BuildWorkflowRunVolumeLabel(int runCount) =>
        runCount == 1
            ? _localization.GetString("Ana_RunCountOne")
            : _localization.GetString("Ana_RunCountMany", runCount);

    private string BuildWorkflowSuccessRateLabel(
        double successRate,
        int successfulRuns,
        int failedOrCancelledRuns)
    {
        var outcomeRuns = successfulRuns + failedOrCancelledRuns;
        return outcomeRuns > 0
            ? _localization.GetString("Ana_SuccessRate", successRate.ToString("F1"))
            : _localization.GetString("Ana_NoCompletedOutcomes");
    }

    private string BuildWorkflowReliabilityLabel(int successfulRuns, int failedOrCancelledRuns)
    {
        var outcomeRuns = successfulRuns + failedOrCancelledRuns;
        if (outcomeRuns == 0)
        {
            return _localization.GetString("Ana_NoCompletedOutcomes");
        }

        if (failedOrCancelledRuns == 0)
        {
            return successfulRuns == 1
                ? _localization.GetString("Ana_SuccessfulRunsOne")
                : _localization.GetString("Ana_SuccessfulRunsMany", successfulRuns);
        }

        return _localization.GetString("Ana_RunsSucceededAndFailed", successfulRuns, failedOrCancelledRuns);
    }

    private string BuildWorkflowStatusLabel(string status) => status switch
    {
        "completed" => _localization.GetString("Ana_RunStatusCompleted"),
        "failed" => _localization.GetString("Ana_RunStatusFailed"),
        "cancelled" => _localization.GetString("Ana_RunStatusCancelled"),
        "running" => _localization.GetString("Ana_RunStatusRunning"),
        "pending" => _localization.GetString("Ana_RunStatusPending"),
        _ => _localization.GetString("Ana_RunStatusUnknown")
    };

    private string BuildWorkflowRunDurationLabel(string status, long? durationMs)
    {
        if (durationMs.HasValue && durationMs.Value > 0)
        {
            return FormatMs(durationMs.Value);
        }

        return status switch
        {
            "running" => _localization.GetString("Ana_DurationInProgress"),
            "pending" => _localization.GetString("Ana_DurationQueued"),
            _ => _localization.GetString("Ana_NoDuration")
        };
    }

    private static string FormatNumber(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M"
        : value >= 1_000 ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private static string FormatNumber(int value) => FormatNumber((long)value);

    private static string FormatTokens(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F2}M"
        : value >= 1_000 ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private string FormatMs(double ms) =>
        ms >= 60_000 ? _localization.GetString("Ana_DurationMinutes", (ms / 60_000.0).ToString("F1"))
        : ms >= 1_000 ? _localization.GetString("Ana_DurationSeconds", (ms / 1_000.0).ToString("F2"))
        : _localization.GetString("Ana_DurationMilliseconds", ms.ToString("F0"));

    private string BuildConversationSummaryStatusLabel(ConversationSummaryMetric summary)
    {
        if (summary.HasRefreshError)
        {
            return _localization.GetString("Ana_SummaryRefreshIssue");
        }

        if (summary.IsStale && summary.PendingMessageCount > 0)
        {
            return summary.PendingMessageCount == 1
                ? _localization.GetString("Ana_NewMessagesOne")
                : _localization.GetString("Ana_NewMessagesMany", summary.PendingMessageCount);
        }

        return _localization.GetString("Ana_SummaryCurrent");
    }

    private string BuildRelativeTimeLabel(DateTime generatedAt)
    {
        var elapsed = DateTime.UtcNow - generatedAt;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return _localization.GetString("Ana_TimeJustNow");
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return _localization.GetString("Ana_TimeMinutesAgo", Math.Max(1, (int)elapsed.TotalMinutes));
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return _localization.GetString("Ana_TimeHoursAgo", Math.Max(1, (int)elapsed.TotalHours));
        }

        var days = Math.Max(1, (int)elapsed.TotalDays);
        return days == 1
            ? _localization.GetString("Ana_TimeOneDayAgo")
            : _localization.GetString("Ana_TimeDaysAgo", days);
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
    public DateTime Date { get; init; }
    public int Count { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Color { get; init; } = "#AA2024";
    public double BarHeightPercent { get; init; }
    public double BarHeight { get; init; }
    public string CountLabel => Count.ToString("N0");
    public string Tooltip => $"{Label}: {Count:N0}";
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
    public string ModelId { get; init; } = string.Empty;
    public int ConversationCount { get; init; }
    public string TotalTokens { get; init; } = string.Empty;
    public double Percentage { get; init; }
    /// <summary>Bar fill fraction in [0.0, 1.0] for use with <c>PercentToWidthConverter</c>.</summary>
    public double BarWidthFraction { get; init; }
    public string Color { get; init; } = "#AA2024";
    public string PercentageLabel { get; init; } = string.Empty;
    public string CountLabel { get; init; } = string.Empty;

    /// <summary>The model id, or "Unknown" in the user's language when the usage row has none.</summary>
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Represents a single file type's document share for display in a horizontal bar chart.
/// </summary>
public sealed class AnalyticsFileTypeItem
{
    public string FileType { get; init; } = string.Empty;
    public int Count { get; init; }
    public string TotalSize { get; init; } = string.Empty;
    public double Percentage { get; init; }
    /// <summary>Bar fill fraction in [0.0, 1.0] for use with <c>PercentToWidthConverter</c>.</summary>
    public double BarWidthFraction { get; init; }
    public string Color { get; init; } = "#58C4BC";
    public string PercentageLabel { get; init; } = string.Empty;
    public string CountLabel { get; init; } = string.Empty;
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
    public string StatusColor { get; init; } = "#41E25E";
    public bool IsFocused { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
    public bool HasKeyPoints => KeyPoints.Count > 0;
    public bool HasSourceLabel => !string.IsNullOrWhiteSpace(SourceLabel);
    public string KeyPointsPreview => string.Join(" · ", KeyPoints);

    /// <summary>How many messages the summary covers, e.g. "6 messages covered".</summary>
    public string CoverageLabel { get; init; } = string.Empty;
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

    /// <summary>The cluster's size and recent activity: conversations, and how many were active in 7 and 30 days.</summary>
    public string ActivityLabel { get; init; } = string.Empty;
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

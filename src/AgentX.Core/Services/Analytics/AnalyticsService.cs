using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Services.Analytics.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Analytics;

/// <summary>
/// EF Core-backed implementation of <see cref="IAnalyticsService"/>.
/// All queries use AsNoTracking for read-only performance.
/// Gap-filling for daily metrics is performed in-process after a single database round-trip.
/// </summary>
public sealed class AnalyticsService : IAnalyticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AgentXDbContext _db;
    private readonly ILogger _log;

    public AnalyticsService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<AnalyticsService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Summary ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AnalyticsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            // Run independent scalar queries in parallel for minimum latency
            var conversationCountTask = _db.Conversations.AsNoTracking().CountAsync(ct);
            var messageCountTask = _db.Messages.AsNoTracking().CountAsync(ct);
            var totalTokensTask = _db.Conversations.AsNoTracking().SumAsync(c => c.TokensUsed, ct);
            var documentCountTask = _db.Documents.AsNoTracking().CountAsync(ct);
            var searchCountTask = _db.SearchHistory.AsNoTracking().CountAsync(ct);
            var workflowRunCountTask = _db.WorkflowRuns.AsNoTracking().CountAsync(ct);

            var indexedCountTask = _db.Documents.AsNoTracking()
                .CountAsync(d => d.IndexingStatus == "completed", ct);

            var pendingCountTask = _db.Documents.AsNoTracking()
                .CountAsync(d => d.IndexingStatus == "pending" || d.IndexingStatus == "processing", ct);

            // Average response time — only assistant messages with timing data
            var avgResponseTask = _db.Messages.AsNoTracking()
                .Where(m => m.Role == "assistant" && m.GenerationTimeMs != null && m.GenerationTimeMs > 0)
                .Select(m => m.GenerationTimeMs)
                .AverageAsync(ms => (double?)ms, ct);

            // Average tokens per assistant message
            var avgTokensTask = _db.Messages.AsNoTracking()
                .Where(m => m.Role == "assistant" && m.TokenCount > 0)
                .Select(m => (double?)m.TokenCount)
                .AverageAsync(ct);

            await Task.WhenAll(
                conversationCountTask, messageCountTask, totalTokensTask,
                documentCountTask, searchCountTask, workflowRunCountTask,
                indexedCountTask, pendingCountTask, avgResponseTask, avgTokensTask);

            return new AnalyticsSummary
            {
                TotalConversations = await conversationCountTask,
                TotalMessages = await messageCountTask,
                TotalTokensUsed = await totalTokensTask,
                TotalDocuments = await documentCountTask,
                TotalSearches = await searchCountTask,
                TotalWorkflowRuns = await workflowRunCountTask,
                DocumentsIndexedCount = await indexedCountTask,
                DocumentsPendingCount = await pendingCountTask,
                AverageResponseTimeMs = await avgResponseTask ?? 0.0,
                AverageTokensPerMessage = await avgTokensTask ?? 0.0,
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load analytics summary");
            return new AnalyticsSummary();
        }
    }

    // ── Daily Metrics ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyMetric>> GetDailyConversationMetricsAsync(
        int days = 30, CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var raw = await _db.Conversations.AsNoTracking()
                .Where(c => c.CreatedAt >= cutoff)
                .GroupBy(c => c.CreatedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            return FillGaps(raw.Select(r => (r.Date, r.Count)), days);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load daily conversation metrics");
            return Array.Empty<DailyMetric>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyMetric>> GetDailyDocumentMetricsAsync(
        int days = 30, CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var raw = await _db.Documents.AsNoTracking()
                .Where(d => d.ImportedAt >= cutoff)
                .GroupBy(d => d.ImportedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            return FillGaps(raw.Select(r => (r.Date, r.Count)), days);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load daily document metrics");
            return Array.Empty<DailyMetric>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyMetric>> GetDailySearchMetricsAsync(
        int days = 30, CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var raw = await _db.SearchHistory.AsNoTracking()
                .Where(s => s.SearchedAt >= cutoff)
                .GroupBy(s => s.SearchedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            return FillGaps(raw.Select(r => (r.Date, r.Count)), days);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load daily search metrics");
            return Array.Empty<DailyMetric>();
        }
    }

    // ── Model Usage ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelUsageMetric>> GetModelUsageAsync(CancellationToken ct = default)
    {
        try
        {
            // Group conversations by ModelId for conversation counts
            var conversationGroups = await _db.Conversations.AsNoTracking()
                .Where(c => c.ModelId != null && c.ModelId != string.Empty)
                .GroupBy(c => c.ModelId)
                .Select(g => new
                {
                    ModelId = g.Key,
                    ConversationCount = g.Count(),
                    TotalTokens = g.Sum(c => c.TokensUsed)
                })
                .OrderByDescending(g => g.ConversationCount)
                .ToListAsync(ct);

            if (conversationGroups.Count == 0)
                return Array.Empty<ModelUsageMetric>();

            var totalConversations = conversationGroups.Sum(g => g.ConversationCount);

            return conversationGroups.Select(g => new ModelUsageMetric
            {
                ModelId = g.ModelId,
                ConversationCount = g.ConversationCount,
                TotalTokens = g.TotalTokens,
                Percentage = totalConversations > 0
                    ? Math.Round(g.ConversationCount * 100.0 / totalConversations, 1)
                    : 0.0
            }).ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load model usage metrics");
            return Array.Empty<ModelUsageMetric>();
        }
    }

    // ── File Type Distribution ───────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileTypeMetric>> GetFileTypeDistributionAsync(CancellationToken ct = default)
    {
        try
        {
            var groups = await _db.Documents.AsNoTracking()
                .Where(d => d.FileType != null && d.FileType != string.Empty)
                .GroupBy(d => d.FileType)
                .Select(g => new
                {
                    FileType = g.Key,
                    Count = g.Count(),
                    TotalSizeBytes = g.Sum(d => d.FileSizeBytes)
                })
                .OrderByDescending(g => g.Count)
                .ToListAsync(ct);

            if (groups.Count == 0)
                return Array.Empty<FileTypeMetric>();

            var totalCount = groups.Sum(g => g.Count);

            return groups.Select(g => new FileTypeMetric
            {
                FileType = g.FileType,
                Count = g.Count,
                TotalSizeBytes = g.TotalSizeBytes,
                Percentage = totalCount > 0
                    ? Math.Round(g.Count * 100.0 / totalCount, 1)
                    : 0.0
            }).ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load file type distribution");
            return Array.Empty<FileTypeMetric>();
        }
    }

    // ── Performance Metrics ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            // Pull all non-null generation times for assistant messages in a single query.
            // We need the full list in-process to compute median and P95.
            var timings = await _db.Messages.AsNoTracking()
                .Where(m => m.Role == "assistant"
                         && m.GenerationTimeMs != null
                         && m.GenerationTimeMs > 0)
                .OrderBy(m => m.GenerationTimeMs)
                .Select(m => m.GenerationTimeMs!.Value)
                .ToListAsync(ct);

            if (timings.Count == 0)
                return new PerformanceMetrics();

            // Also grab token counts for the same messages to compute tokens/sec
            var tokenTimingPairs = await _db.Messages.AsNoTracking()
                .Where(m => m.Role == "assistant"
                         && m.GenerationTimeMs != null
                         && m.GenerationTimeMs > 0
                         && m.TokenCount > 0)
                .Select(m => new { Tokens = (double)m.TokenCount, Ms = m.GenerationTimeMs!.Value })
                .ToListAsync(ct);

            var sortedMs = timings; // already ordered ascending
            var count = sortedMs.Count;

            var average = sortedMs.Average();
            var median = ComputePercentile(sortedMs, 50);
            var p95 = ComputePercentile(sortedMs, 95);
            var fastest = sortedMs[0];
            var slowest = sortedMs[count - 1];
            var totalMs = sortedMs.Sum();

            // tokens/sec: sum(tokens) / sum(seconds)
            double avgTokensPerSec = 0.0;
            if (tokenTimingPairs.Count > 0)
            {
                var totalTokens = tokenTimingPairs.Sum(p => p.Tokens);
                var totalSeconds = tokenTimingPairs.Sum(p => p.Ms) / 1000.0;
                avgTokensPerSec = totalSeconds > 0 ? Math.Round(totalTokens / totalSeconds, 1) : 0.0;
            }

            return new PerformanceMetrics
            {
                AverageResponseTimeMs = Math.Round(average, 1),
                MedianResponseTimeMs = Math.Round(median, 1),
                P95ResponseTimeMs = Math.Round(p95, 1),
                FastestResponseMs = Math.Round(fastest, 1),
                SlowestResponseMs = Math.Round(slowest, 1),
                TotalInferenceTimeMs = Math.Round(totalMs, 0),
                AverageTokensPerSecond = avgTokensPerSec,
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load performance metrics");
            return new PerformanceMetrics();
        }
    }

    // ── Workflow Intelligence ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<WorkflowIntelligenceOverview> GetWorkflowIntelligenceOverviewAsync(
        int maxRecentRuns = 6,
        int maxTopWorkflows = 5,
        int recentActivityDays = 30,
        CancellationToken ct = default)
    {
        try
        {
            maxRecentRuns = Math.Max(0, maxRecentRuns);
            maxTopWorkflows = Math.Max(0, maxTopWorkflows);
            recentActivityDays = Math.Max(1, recentActivityDays);

            var recentCutoff = DateTime.UtcNow.Date.AddDays(-(recentActivityDays - 1));

            var totalRunsTask = _db.WorkflowRuns.AsNoTracking()
                .CountAsync(ct);

            var successfulRunsTask = _db.WorkflowRuns.AsNoTracking()
                .CountAsync(run => run.Status == "completed", ct);

            var failedOrCancelledRunsTask = _db.WorkflowRuns.AsNoTracking()
                .CountAsync(run => run.Status == "failed" || run.Status == "cancelled", ct);

            var activeWorkflowsTask = _db.WorkflowRuns.AsNoTracking()
                .Where(run => run.StartedAt >= recentCutoff)
                .Select(run => run.WorkflowId)
                .Distinct()
                .CountAsync(ct);

            var durationRowsTask = _db.WorkflowRuns.AsNoTracking()
                .Where(run => run.CompletedAt != null && run.CompletedAt > run.StartedAt)
                .Select(run => new
                {
                    run.StartedAt,
                    CompletedAt = run.CompletedAt!.Value
                })
                .ToListAsync(ct);

            var topWorkflowRowsTask = maxTopWorkflows == 0
                ? Task.FromResult(new List<TopWorkflowAggregateRow>())
                : (
                    from run in _db.WorkflowRuns.AsNoTracking()
                    join workflow in _db.Workflows.AsNoTracking()
                        on run.WorkflowId equals workflow.Id
                    group new { run, workflow } by new
                    {
                        run.WorkflowId,
                        workflow.Name,
                        workflow.Category
                    }
                    into grouped
                    orderby grouped.Count() descending,
                            grouped.Count(entry => entry.run.Status == "completed") descending,
                            grouped.Max(entry => entry.run.StartedAt) descending
                    select new TopWorkflowAggregateRow
                    {
                        WorkflowId = grouped.Key.WorkflowId,
                        WorkflowName = grouped.Key.Name,
                        Category = grouped.Key.Category,
                        RunCount = grouped.Count(),
                        SuccessfulRuns = grouped.Count(entry => entry.run.Status == "completed"),
                        FailedOrCancelledRuns = grouped.Count(entry => entry.run.Status == "failed" || entry.run.Status == "cancelled"),
                        LastRunAt = grouped.Max(entry => entry.run.StartedAt)
                    })
                .Take(maxTopWorkflows)
                .ToListAsync(ct);

            var recentRunRowsTask = maxRecentRuns == 0
                ? Task.FromResult(new List<RecentWorkflowRunRow>())
                : (
                    from run in _db.WorkflowRuns.AsNoTracking()
                    join workflow in _db.Workflows.AsNoTracking()
                        on run.WorkflowId equals workflow.Id
                    orderby run.StartedAt descending
                    select new RecentWorkflowRunRow
                    {
                        WorkflowRunId = run.Id,
                        WorkflowId = workflow.Id,
                        WorkflowName = workflow.Name,
                        Status = run.Status,
                        StartedAt = run.StartedAt,
                        CompletedAt = run.CompletedAt,
                        FinalOutput = run.FinalOutput,
                        ErrorMessage = run.ErrorMessage
                    })
                .Take(maxRecentRuns)
                .ToListAsync(ct);

            await Task.WhenAll(
                totalRunsTask,
                successfulRunsTask,
                failedOrCancelledRunsTask,
                activeWorkflowsTask,
                durationRowsTask,
                topWorkflowRowsTask,
                recentRunRowsTask);

            var successfulRuns = await successfulRunsTask;
            var failedOrCancelledRuns = await failedOrCancelledRunsTask;
            var finishedRuns = successfulRuns + failedOrCancelledRuns;
            var durations = (await durationRowsTask)
                .Select(row => (row.CompletedAt - row.StartedAt).TotalMilliseconds)
                .Where(durationMs => durationMs > 0)
                .ToList();

            var topWorkflows = (await topWorkflowRowsTask)
                .Select(row =>
                {
                    var outcomeRuns = row.SuccessfulRuns + row.FailedOrCancelledRuns;
                    return new WorkflowTopWorkflowMetric
                    {
                        WorkflowId = row.WorkflowId,
                        WorkflowName = NormalizeWorkflowName(row.WorkflowName),
                        Category = row.Category ?? string.Empty,
                        RunCount = row.RunCount,
                        SuccessfulRuns = row.SuccessfulRuns,
                        FailedOrCancelledRuns = row.FailedOrCancelledRuns,
                        SuccessRate = outcomeRuns > 0
                            ? Math.Round(row.SuccessfulRuns * 100.0 / outcomeRuns, 1)
                            : 0.0,
                        LastRunAt = row.LastRunAt
                    };
                })
                .ToList();

            var recentRuns = (await recentRunRowsTask)
                .Select(row => new WorkflowRecentRunMetric
                {
                    WorkflowRunId = row.WorkflowRunId,
                    WorkflowId = row.WorkflowId,
                    WorkflowName = NormalizeWorkflowName(row.WorkflowName),
                    Status = NormalizeWorkflowStatus(row.Status),
                    StartedAt = row.StartedAt,
                    CompletedAt = row.CompletedAt,
                    DurationMs = ComputeDurationMs(row.StartedAt, row.CompletedAt),
                    PreviewText = BuildWorkflowRunPreview(row.FinalOutput, row.ErrorMessage, row.Status),
                    HasErrorPreview = !string.IsNullOrWhiteSpace(row.ErrorMessage)
                })
                .ToList();

            return new WorkflowIntelligenceOverview
            {
                TotalRuns = await totalRunsTask,
                SuccessfulRuns = successfulRuns,
                FailedOrCancelledRuns = failedOrCancelledRuns,
                SuccessRate = finishedRuns > 0
                    ? Math.Round(successfulRuns * 100.0 / finishedRuns, 1)
                    : 0.0,
                AverageRunDurationMs = durations.Count > 0
                    ? Math.Round(durations.Average(), 1)
                    : 0.0,
                ActiveWorkflowsRecently = await activeWorkflowsTask,
                TopWorkflows = topWorkflows,
                RecentRuns = recentRuns
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load workflow intelligence overview");
            return new WorkflowIntelligenceOverview();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyMetric>> GetDailyWorkflowRunMetricsAsync(
        int days = 30,
        CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var raw = await _db.WorkflowRuns.AsNoTracking()
                .Where(run => run.StartedAt >= cutoff)
                .GroupBy(run => run.StartedAt.Date)
                .Select(group => new { Date = group.Key, Count = group.Count() })
                .ToListAsync(ct);

            return FillGaps(raw.Select(row => (row.Date, row.Count)), days);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load daily workflow metrics");
            return Array.Empty<DailyMetric>();
        }
    }

    // ── Conversation Intelligence ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConversationIntelligenceOverview> GetConversationIntelligenceAsync(
        int maxRecent = 6,
        CancellationToken ct = default)
    {
        try
        {
            var summarizedConversationsTask = _db.ConversationSummarySnapshots.AsNoTracking()
                .Select(s => s.ConversationId)
                .Distinct()
                .CountAsync(ct);

            var currentSnapshotsTask = _db.ConversationSummarySnapshots.AsNoTracking()
                .CountAsync(ct);

            var staleConversationsTask = _db.ConversationSummaryStates.AsNoTracking()
                .CountAsync(s => s.LatestSnapshotId != null && s.IsStale, ct);

            var pendingRefreshesTask = (
                from conversation in _db.Conversations.AsNoTracking()
                join state in _db.ConversationSummaryStates.AsNoTracking()
                    on conversation.Id equals state.ConversationId into stateGroup
                from state in stateGroup.DefaultIfEmpty()
                where conversation.MessageCount > 0
                   && (state == null || state.LatestSnapshotId == null || state.IsStale)
                select conversation.Id)
                .CountAsync(ct);

            var recentRowsTask = (
                from state in _db.ConversationSummaryStates.AsNoTracking()
                join conversation in _db.Conversations.AsNoTracking()
                    on state.ConversationId equals conversation.Id
                join snapshot in _db.ConversationSummarySnapshots.AsNoTracking()
                    on state.LatestSnapshotId equals snapshot.Id
                orderby snapshot.GeneratedAt descending
                select new
                {
                    conversation.Id,
                    conversation.Title,
                    snapshot.PreviewText,
                    snapshot.KeyPointsJson,
                    snapshot.GeneratedAt,
                    snapshot.CoveredMessageCount,
                    state.PendingMessageCount,
                    state.IsStale,
                    state.LastRefreshedAt,
                    state.LastError
                })
                .Take(maxRecent)
                .ToListAsync(ct);

            await Task.WhenAll(
                summarizedConversationsTask,
                currentSnapshotsTask,
                staleConversationsTask,
                pendingRefreshesTask,
                recentRowsTask);

            var recentSummaries = (await recentRowsTask)
                .Select(row => new ConversationSummaryMetric
                {
                    ConversationId = row.Id,
                    Title = string.IsNullOrWhiteSpace(row.Title) ? "Untitled conversation" : row.Title,
                    PreviewText = row.PreviewText,
                    KeyPoints = ParseKeyPoints(row.KeyPointsJson),
                    GeneratedAt = row.GeneratedAt,
                    LastRefreshedAt = row.LastRefreshedAt,
                    CoveredMessageCount = row.CoveredMessageCount,
                    PendingMessageCount = row.PendingMessageCount,
                    IsStale = row.IsStale,
                    HasRefreshError = !string.IsNullOrWhiteSpace(row.LastError),
                    LastError = row.LastError
                })
                .ToList();

            return new ConversationIntelligenceOverview
            {
                SummarizedConversations = await summarizedConversationsTask,
                CurrentSnapshots = await currentSnapshotsTask,
                StaleConversations = await staleConversationsTask,
                PendingRefreshes = await pendingRefreshesTask,
                RecentSummaries = recentSummaries
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load conversation intelligence metrics");
            return new ConversationIntelligenceOverview();
        }
    }

    /// <inheritdoc />
    public async Task<ConversationRecallOverview> GetConversationRecallOverviewAsync(CancellationToken ct = default)
    {
        try
        {
            var embeddedMessagesTask = _db.Messages.AsNoTracking()
                .CountAsync(m => (m.Role == "user" || m.Role == "assistant") && m.Embedding != null, ct);

            var pendingEmbeddingsTask = _db.Messages.AsNoTracking()
                .CountAsync(m =>
                    (m.Role == "user" || m.Role == "assistant")
                    && m.Content != string.Empty
                    && m.Embedding == null, ct);

            var recallReadyConversationsTask = _db.Messages.AsNoTracking()
                .Where(m => (m.Role == "user" || m.Role == "assistant") && m.Embedding != null)
                .Select(m => m.ConversationId)
                .Distinct()
                .CountAsync(ct);

            var lastEmbeddedAtTask = _db.Messages.AsNoTracking()
                .Where(m => (m.Role == "user" || m.Role == "assistant") && m.EmbeddedAt != null)
                .MaxAsync(m => (DateTime?)m.EmbeddedAt, ct);

            await Task.WhenAll(
                embeddedMessagesTask,
                pendingEmbeddingsTask,
                recallReadyConversationsTask,
                lastEmbeddedAtTask);

            return new ConversationRecallOverview
            {
                EmbeddedMessages = await embeddedMessagesTask,
                PendingMessageEmbeddings = await pendingEmbeddingsTask,
                RecallReadyConversations = await recallReadyConversationsTask,
                LastEmbeddedAt = await lastEmbeddedAtTask
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load conversation recall overview");
            return new ConversationRecallOverview();
        }
    }

    /// <inheritdoc />
    public async Task<ConversationThemeOverview> GetConversationThemeOverviewAsync(
        int maxClusters = 6,
        CancellationToken ct = default)
    {
        try
        {
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

            var activeThemeClustersTask = _db.ConversationThemeClusters.AsNoTracking()
                .CountAsync(ct);

            var clusteredConversationsTask = _db.ConversationThemeMemberships.AsNoTracking()
                .CountAsync(ct);

            var newThemesTask = _db.ConversationThemeClusters.AsNoTracking()
                .CountAsync(cluster => cluster.FirstSeenAt >= sevenDaysAgo, ct);

            var lastMaterializedTask = _db.ConversationThemeClusters.AsNoTracking()
                .MaxAsync(cluster => (DateTime?)cluster.MaterializedAt, ct);

            var clusterRows = await _db.ConversationThemeClusters.AsNoTracking()
                .OrderByDescending(cluster => cluster.ActiveConversationCount7d)
                .ThenByDescending(cluster => cluster.ConversationCount)
                .ThenByDescending(cluster => cluster.LastActiveAt)
                .Take(maxClusters)
                .Select(cluster => new
                {
                    cluster.Id,
                    cluster.Label,
                    cluster.PreviewText,
                    cluster.KeyPointsJson,
                    cluster.ConversationCount,
                    cluster.ActiveConversationCount7d,
                    cluster.ActiveConversationCount30d,
                    cluster.FirstSeenAt,
                    cluster.LastActiveAt,
                    cluster.MaterializedAt
                })
                .ToListAsync(ct);

            var clusterIds = clusterRows.Select(cluster => cluster.Id).ToList();
            var recentConversationRows = clusterIds.Count == 0
                ? []
                : await (
                    from membership in _db.ConversationThemeMemberships.AsNoTracking()
                    join conversation in _db.Conversations.AsNoTracking()
                        on membership.ConversationId equals conversation.Id
                    where clusterIds.Contains(membership.ClusterId)
                    orderby conversation.UpdatedAt descending
                    select new
                    {
                        membership.ClusterId,
                        conversation.Title,
                        conversation.UpdatedAt
                    })
                    .ToListAsync(ct);

            await Task.WhenAll(
                activeThemeClustersTask,
                clusteredConversationsTask,
                newThemesTask,
                lastMaterializedTask);

            var recentConversationLookup = recentConversationRows
                .GroupBy(row => row.ClusterId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(row => string.IsNullOrWhiteSpace(row.Title) ? "Untitled conversation" : row.Title)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToList());

            var clusters = clusterRows
                .Select(cluster => new ConversationThemeClusterMetric
                {
                    ClusterId = cluster.Id,
                    Label = cluster.Label,
                    PreviewText = cluster.PreviewText,
                    KeyPoints = ParseKeyPoints(cluster.KeyPointsJson),
                    ConversationCount = cluster.ConversationCount,
                    ActiveConversationCount7d = cluster.ActiveConversationCount7d,
                    ActiveConversationCount30d = cluster.ActiveConversationCount30d,
                    FirstSeenAt = cluster.FirstSeenAt,
                    LastActiveAt = cluster.LastActiveAt,
                    MaterializedAt = cluster.MaterializedAt,
                    RecentConversationTitles = recentConversationLookup.GetValueOrDefault(cluster.Id, Array.Empty<string>())
                })
                .ToList();

            return new ConversationThemeOverview
            {
                ActiveThemeClusters = await activeThemeClustersTask,
                ClusteredConversations = await clusteredConversationsTask,
                NewThemes7d = await newThemesTask,
                LastMaterializedAt = await lastMaterializedTask,
                Clusters = clusters
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load conversation theme overview");
            return new ConversationThemeOverview();
        }
    }

    /// <inheritdoc />
    public async Task<ConversationThemeTrendOverview> GetConversationThemeTrendOverviewAsync(
        int maxThemes = 5,
        int days = 30,
        CancellationToken ct = default)
    {
        try
        {
            if (maxThemes <= 0 || days <= 0)
            {
                return new ConversationThemeTrendOverview();
            }

            var windowStart = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var clusters = await _db.ConversationThemeClusters.AsNoTracking()
                .Select(cluster => new
                {
                    cluster.Id,
                    cluster.Label,
                    cluster.PreviewText,
                    cluster.LastActiveAt
                })
                .ToListAsync(ct);

            if (clusters.Count == 0)
            {
                return new ConversationThemeTrendOverview();
            }

            var clusterIds = clusters.Select(cluster => cluster.Id).ToList();
            var rows = await _db.ConversationThemeDailyMetrics.AsNoTracking()
                .Where(metric => clusterIds.Contains(metric.ClusterId)
                              && metric.Date >= windowStart)
                .Select(metric => new ThemeTrendRow
                {
                    ClusterId = metric.ClusterId,
                    Date = metric.Date,
                    ActiveConversationCount = metric.ActiveConversationCount,
                    NewConversationCount = metric.NewConversationCount,
                    SnapshotRefreshCount = metric.SnapshotRefreshCount,
                    MaterializedAt = metric.MaterializedAt
                })
                .ToListAsync(ct);

            var materializedClusterIds = rows
                .Select(row => row.ClusterId)
                .ToHashSet();
            if (materializedClusterIds.Count == 0)
            {
                return new ConversationThemeTrendOverview();
            }

            var metrics = clusters
                .Where(cluster => materializedClusterIds.Contains(cluster.Id))
                .Select(cluster =>
                {
                    var series = BuildThemeTrendSeries(
                        rows.Where(row => row.ClusterId == cluster.Id),
                        windowStart,
                        days);
                    var recent7 = series.TakeLast(Math.Min(7, series.Count)).Sum(point => point.ActiveConversationCount);
                    var previous7 = series.Count <= 7
                        ? 0
                        : series.Skip(Math.Max(0, series.Count - 14)).Take(7).Sum(point => point.ActiveConversationCount);
                    var recent7NewEntries = series.TakeLast(Math.Min(7, series.Count)).Sum(point => point.NewConversationCount);

                    return new ConversationThemeTrendMetric
                    {
                        ClusterId = cluster.Id,
                        Label = cluster.Label,
                        PreviewText = cluster.PreviewText,
                        Recent7DayActivity = recent7,
                        Previous7DayActivity = previous7,
                        Recent7DayNewEntries = recent7NewEntries,
                        LastActiveAt = cluster.LastActiveAt,
                        DailySeries = series
                    };
                })
                .OrderByDescending(metric => metric.Recent7DayActivity)
                .ThenByDescending(metric => metric.Recent7DayNewEntries)
                .ThenByDescending(metric => metric.LastActiveAt)
                .ToList();

            var trendingThemes = metrics.Count(metric =>
                metric.Recent7DayActivity > 0
                && metric.Recent7DayActivity > metric.Previous7DayActivity);
            var newThemeEntries7d = metrics.Sum(metric => metric.Recent7DayNewEntries);
            var mostActiveTheme = metrics
                .OrderByDescending(metric => metric.Recent7DayActivity)
                .ThenByDescending(metric => metric.LastActiveAt)
                .Select(metric => metric.Label)
                .FirstOrDefault()
                ?? string.Empty;
            var lastTrendRefresh = rows.Count == 0
                ? (DateTime?)null
                : rows.Max(row => row.MaterializedAt);

            return new ConversationThemeTrendOverview
            {
                TrendingThemes = trendingThemes,
                NewThemeEntries7d = newThemeEntries7d,
                MostActiveThemeLabel = mostActiveTheme,
                LastTrendRefresh = lastTrendRefresh,
                Trends = metrics.Take(maxThemes).ToList()
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load conversation theme trend overview");
            return new ConversationThemeTrendOverview();
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Produces a contiguous list of <see cref="DailyMetric"/> records covering the last
    /// <paramref name="days"/> calendar days (today inclusive), filling missing dates with
    /// a count of zero. Input sequence is not required to be sorted.
    /// </summary>
    private static IReadOnlyList<DailyMetric> FillGaps(
        IEnumerable<(DateTime Date, int Count)> raw,
        int days)
    {
        var lookup = raw.ToDictionary(r => r.Date.Date, r => r.Count);
        var today = DateTime.UtcNow.Date;
        var result = new List<DailyMetric>(days);

        for (var i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var count = lookup.GetValueOrDefault(date, 0);

            result.Add(new DailyMetric
            {
                Date = date,
                Count = count,
                Label = date.ToString("MMM d"),
            });
        }

        return result;
    }

    private static IReadOnlyList<ConversationThemeDailyPoint> BuildThemeTrendSeries(
        IEnumerable<ThemeTrendRow> rows,
        DateTime windowStart,
        int days)
    {
        var lookup = rows.ToDictionary(
            row => row.Date.Date,
            row => new ConversationThemeDailyPoint
            {
                Date = row.Date.Date,
                ActiveConversationCount = row.ActiveConversationCount,
                NewConversationCount = row.NewConversationCount,
                SnapshotRefreshCount = row.SnapshotRefreshCount
            });

        var series = new List<ConversationThemeDailyPoint>(days);
        for (var offset = 0; offset < days; offset++)
        {
            var date = windowStart.AddDays(offset);
            series.Add(lookup.GetValueOrDefault(date, new ConversationThemeDailyPoint
            {
                Date = date,
                ActiveConversationCount = 0,
                NewConversationCount = 0,
                SnapshotRefreshCount = 0
            }));
        }

        return series;
    }

    private sealed record ThemeTrendRow
    {
        public long ClusterId { get; init; }
        public DateTime Date { get; init; }
        public int ActiveConversationCount { get; init; }
        public int NewConversationCount { get; init; }
        public int SnapshotRefreshCount { get; init; }
        public DateTime MaterializedAt { get; init; }
    }

    private sealed record TopWorkflowAggregateRow
    {
        public long WorkflowId { get; init; }
        public string? WorkflowName { get; init; }
        public string? Category { get; init; }
        public int RunCount { get; init; }
        public int SuccessfulRuns { get; init; }
        public int FailedOrCancelledRuns { get; init; }
        public DateTime LastRunAt { get; init; }
    }

    private sealed record RecentWorkflowRunRow
    {
        public long WorkflowRunId { get; init; }
        public long WorkflowId { get; init; }
        public string? WorkflowName { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public string? FinalOutput { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Computes the <paramref name="percentile"/>-th percentile (0–100) from an ascending-sorted
    /// list using the nearest-rank method.
    /// </summary>
    private static double ComputePercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0.0;
        if (sortedValues.Count == 1) return sortedValues[0];

        // Nearest-rank: ceil(p/100 * n) gives the 1-based index
        var index = (int)Math.Ceiling(percentile / 100.0 * sortedValues.Count);
        index = Math.Clamp(index, 1, sortedValues.Count);
        return sortedValues[index - 1];
    }

    private static IReadOnlyList<string> ParseKeyPoints(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var points = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            return points is null
                ? Array.Empty<string>()
                : points.Where(point => !string.IsNullOrWhiteSpace(point)).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeWorkflowName(string? workflowName) =>
        string.IsNullOrWhiteSpace(workflowName)
            ? "Untitled workflow"
            : workflowName.Trim();

    private static string NormalizeWorkflowStatus(string status) =>
        string.IsNullOrWhiteSpace(status)
            ? "pending"
            : status.Trim().ToLowerInvariant();

    private static long? ComputeDurationMs(DateTime startedAt, DateTime? completedAt)
    {
        if (!completedAt.HasValue || completedAt <= startedAt)
        {
            return null;
        }

        return (long)Math.Round((completedAt.Value - startedAt).TotalMilliseconds);
    }

    private static string BuildWorkflowRunPreview(
        string? finalOutput,
        string? errorMessage,
        string status)
    {
        var source = !string.IsNullOrWhiteSpace(errorMessage)
            ? errorMessage
            : !string.IsNullOrWhiteSpace(finalOutput)
                ? finalOutput
                : NormalizeWorkflowStatus(status) switch
                {
                    "running" => "Run still in progress.",
                    "pending" => "Run queued and waiting to start.",
                    "cancelled" => "Run was cancelled before a final output was stored.",
                    _ => "No stored output preview."
                };

        var compact = source
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return compact.Length <= 160
            ? compact
            : $"{compact[..159].TrimEnd()}…";
    }
}

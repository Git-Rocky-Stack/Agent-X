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
            var messageCountTask      = _db.Messages.AsNoTracking().CountAsync(ct);
            var totalTokensTask       = _db.Conversations.AsNoTracking().SumAsync(c => c.TokensUsed, ct);
            var documentCountTask     = _db.Documents.AsNoTracking().CountAsync(ct);
            var searchCountTask       = _db.SearchHistory.AsNoTracking().CountAsync(ct);
            var workflowRunCountTask  = _db.WorkflowRuns.AsNoTracking().CountAsync(ct);

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
                TotalConversations    = await conversationCountTask,
                TotalMessages         = await messageCountTask,
                TotalTokensUsed       = await totalTokensTask,
                TotalDocuments        = await documentCountTask,
                TotalSearches         = await searchCountTask,
                TotalWorkflowRuns     = await workflowRunCountTask,
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
                    ModelId           = g.Key,
                    ConversationCount = g.Count(),
                    TotalTokens       = g.Sum(c => c.TokensUsed)
                })
                .OrderByDescending(g => g.ConversationCount)
                .ToListAsync(ct);

            if (conversationGroups.Count == 0)
                return Array.Empty<ModelUsageMetric>();

            var totalConversations = conversationGroups.Sum(g => g.ConversationCount);

            return conversationGroups.Select(g => new ModelUsageMetric
            {
                ModelId           = g.ModelId,
                ConversationCount = g.ConversationCount,
                TotalTokens       = g.TotalTokens,
                Percentage        = totalConversations > 0
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
                    FileType       = g.Key,
                    Count          = g.Count(),
                    TotalSizeBytes = g.Sum(d => d.FileSizeBytes)
                })
                .OrderByDescending(g => g.Count)
                .ToListAsync(ct);

            if (groups.Count == 0)
                return Array.Empty<FileTypeMetric>();

            var totalCount = groups.Sum(g => g.Count);

            return groups.Select(g => new FileTypeMetric
            {
                FileType       = g.FileType,
                Count          = g.Count,
                TotalSizeBytes = g.TotalSizeBytes,
                Percentage     = totalCount > 0
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
            var count    = sortedMs.Count;

            var average      = sortedMs.Average();
            var median       = ComputePercentile(sortedMs, 50);
            var p95          = ComputePercentile(sortedMs, 95);
            var fastest      = sortedMs[0];
            var slowest      = sortedMs[count - 1];
            var totalMs      = sortedMs.Sum();

            // tokens/sec: sum(tokens) / sum(seconds)
            double avgTokensPerSec = 0.0;
            if (tokenTimingPairs.Count > 0)
            {
                var totalTokens  = tokenTimingPairs.Sum(p => p.Tokens);
                var totalSeconds = tokenTimingPairs.Sum(p => p.Ms) / 1000.0;
                avgTokensPerSec  = totalSeconds > 0 ? Math.Round(totalTokens / totalSeconds, 1) : 0.0;
            }

            return new PerformanceMetrics
            {
                AverageResponseTimeMs  = Math.Round(average, 1),
                MedianResponseTimeMs   = Math.Round(median, 1),
                P95ResponseTimeMs      = Math.Round(p95, 1),
                FastestResponseMs      = Math.Round(fastest, 1),
                SlowestResponseMs      = Math.Round(slowest, 1),
                TotalInferenceTimeMs   = Math.Round(totalMs, 0),
                AverageTokensPerSecond = avgTokensPerSec,
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load performance metrics");
            return new PerformanceMetrics();
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
        var today  = DateTime.UtcNow.Date;
        var result = new List<DailyMetric>(days);

        for (var i = days - 1; i >= 0; i--)
        {
            var date  = today.AddDays(-i);
            var count = lookup.GetValueOrDefault(date, 0);

            result.Add(new DailyMetric
            {
                Date  = date,
                Count = count,
                Label = date.ToString("MMM d"),
            });
        }

        return result;
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
}

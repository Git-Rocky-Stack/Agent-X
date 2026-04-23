using AgentX.Core.Services.Analytics.Models;

namespace AgentX.Core.Services.Analytics;

/// <summary>
/// Provides aggregated analytics and usage metrics derived from the Agent-X SQLite database.
/// All methods are read-only and use AsNoTracking for maximum query performance.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Returns a top-level summary of all activity in the application.
    /// </summary>
    Task<AnalyticsSummary> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns conversation counts grouped by day for the specified number of trailing days.
    /// Days with no activity are included with a count of zero.
    /// </summary>
    /// <param name="days">Number of trailing days to include (default: 30).</param>
    Task<IReadOnlyList<DailyMetric>> GetDailyConversationMetricsAsync(int days = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns document import counts grouped by day for the specified number of trailing days.
    /// Days with no imports are included with a count of zero.
    /// </summary>
    /// <param name="days">Number of trailing days to include (default: 30).</param>
    Task<IReadOnlyList<DailyMetric>> GetDailyDocumentMetricsAsync(int days = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns search counts grouped by day for the specified number of trailing days.
    /// Days with no searches are included with a count of zero.
    /// </summary>
    /// <param name="days">Number of trailing days to include (default: 30).</param>
    Task<IReadOnlyList<DailyMetric>> GetDailySearchMetricsAsync(int days = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns per-model usage aggregates ordered by conversation count descending.
    /// </summary>
    Task<IReadOnlyList<ModelUsageMetric>> GetModelUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns per-file-type document distribution ordered by document count descending.
    /// </summary>
    Task<IReadOnlyList<FileTypeMetric>> GetFileTypeDistributionAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns inference performance statistics derived from message GenerationTimeMs values.
    /// Returns a zeroed-out <see cref="PerformanceMetrics"/> when no timed messages exist.
    /// </summary>
    Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns durable conversation-summary coverage metrics and recent summary previews.
    /// </summary>
    Task<ConversationIntelligenceOverview> GetConversationIntelligenceAsync(
        int maxRecent = 6,
        CancellationToken ct = default);
}

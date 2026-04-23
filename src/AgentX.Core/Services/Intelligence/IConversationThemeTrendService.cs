namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Materializes bounded daily activity rows for durable conversation theme
/// clusters so Analytics can read trend data without query-time recomputation.
/// </summary>
public interface IConversationThemeTrendService
{
    /// <summary>
    /// Upserts a trailing daily trend window for a single cluster.
    /// Returns the number of daily rows materialized.
    /// </summary>
    Task<int> RefreshClusterTrendWindowAsync(
        long clusterId,
        int days = 30,
        CancellationToken ct = default);

    /// <summary>
    /// Refreshes a bounded set of recently touched clusters whose daily trend
    /// rows are missing or stale relative to cluster materialization.
    /// Returns the number of clusters refreshed.
    /// </summary>
    Task<int> RefreshRecentClusterTrendsAsync(
        int maxClusters = 4,
        int days = 30,
        CancellationToken ct = default);
}

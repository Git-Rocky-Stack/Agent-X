using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

public interface IDigestInsightService
{
    Task<IReadOnlyList<DigestSearchTrend>> BuildSearchTrendsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default);

    Task<IReadOnlyList<DigestCollectionTrend>> BuildCollectionTrendsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default);

    Task<IReadOnlyList<DigestFileTypeTrend>> BuildFileTypeTrendsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default);
}

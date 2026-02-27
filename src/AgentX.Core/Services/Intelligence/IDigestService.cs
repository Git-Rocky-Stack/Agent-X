using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Generates and manages weekly digest reports summarizing knowledge vault activity.
/// Reports include document imports, search trends, collection usage, and conversation highlights.
/// </summary>
public interface IDigestService
{
    /// <summary>
    /// Generates a digest report for the specified period. If no dates are provided,
    /// defaults to the past 7 days ending at the current time.
    /// </summary>
    /// <param name="periodStart">Start of the reporting period (inclusive). Defaults to 7 days ago.</param>
    /// <param name="periodEnd">End of the reporting period (inclusive). Defaults to now.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated and persisted <see cref="DigestReportEntity"/>.</returns>
    Task<DigestReportEntity> GenerateDigestAsync(
        DateTime? periodStart = null,
        DateTime? periodEnd = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent digest reports, ordered by generation date descending.
    /// </summary>
    /// <param name="limit">Maximum number of reports to return. Defaults to 10.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<DigestReportEntity>> GetReportHistoryAsync(int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recently generated digest report, or null if none exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<DigestReportEntity?> GetLatestReportAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks a specific report as read by the user.
    /// </summary>
    /// <param name="reportId">The ID of the report to mark as read.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkAsReadAsync(long reportId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether there are any unread digest reports.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> HasUnreadReportsAsync(CancellationToken ct = default);
}

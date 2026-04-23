using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

public interface IHierarchicalSummaryService
{
    Task<HierarchicalSummaryResult> BuildSummaryAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default);

    Task<string> SummarizeAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ExtractKeyPointsAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default);
}

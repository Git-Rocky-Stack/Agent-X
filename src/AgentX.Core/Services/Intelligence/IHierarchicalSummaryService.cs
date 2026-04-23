namespace AgentX.Core.Services.Intelligence;

public interface IHierarchicalSummaryService
{
    Task<string> SummarizeAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ExtractKeyPointsAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default);
}

namespace AgentX.App.Services;

/// <summary>
/// Produces a coherent cross-surface operations snapshot for dashboard and future
/// intelligence-operations surfaces.
/// </summary>
public interface IOperationsOverviewService
{
    Task<OperationsOverviewSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

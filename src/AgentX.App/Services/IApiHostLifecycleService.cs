namespace AgentX.App.Services;

/// <summary>
/// Owns the desktop REST API lifecycle used by the browser extension and mobile companion.
/// </summary>
public interface IApiHostLifecycleService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

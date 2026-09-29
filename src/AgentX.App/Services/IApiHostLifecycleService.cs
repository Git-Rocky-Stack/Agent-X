namespace AgentX.App.Services;

/// <summary>
/// Owns the desktop REST API lifecycle used by the browser extension and mobile companion.
/// </summary>
public interface IApiHostLifecycleService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-reads the Local API settings and applies them to the running process without a restart:
    /// stops the listener when the API is disabled, starts it when enabled, and hands the current
    /// token to a running listener, so a regenerated token works at once and the previous token
    /// stops working at once.
    /// </summary>
    Task ApplySettingsAsync(CancellationToken ct = default);
}

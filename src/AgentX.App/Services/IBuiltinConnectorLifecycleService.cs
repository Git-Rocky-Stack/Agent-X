namespace AgentX.App.Services;

/// <summary>
/// Owns first-party connector plugin initialization and timer activation.
/// </summary>
public interface IBuiltinConnectorLifecycleService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task RefreshAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

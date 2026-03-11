namespace AgentX.Core.Services.Api;

/// <summary>
/// Defines the contract for the embedded local REST API host.
/// The host exposes AgentX core functionality over HTTP for external tool
/// integration and the mobile companion app.
/// </summary>
public interface IApiHostService
{
    /// <summary>
    /// Indicates whether the HTTP listener is currently running and accepting requests.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// The port number the listener is bound to.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// The full base URL of the API (e.g., http://localhost:9846/).
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Starts the HTTP listener on the specified port and begins processing requests.
    /// Idempotent — calling Start when already running is a no-op.
    /// </summary>
    /// <param name="port">TCP port to listen on. Defaults to 9846.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(int port = 9846, CancellationToken ct = default);

    /// <summary>
    /// Stops the HTTP listener gracefully, draining in-flight requests.
    /// Idempotent — calling Stop when already stopped is a no-op.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StopAsync(CancellationToken ct = default);
}

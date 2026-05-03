using AgentX.Core.Services.Api;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Starts and stops the local REST API on the stable browser-extension port.
/// </summary>
public sealed class ApiHostLifecycleService : IApiHostLifecycleService
{
    public const int DefaultPort = 9846;

    private readonly IApiHostService _apiHost;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ApiHostLifecycleService(IApiHostService apiHost, ILogger logger)
    {
        _apiHost = apiHost ?? throw new ArgumentNullException(nameof(apiHost));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<ApiHostLifecycleService>();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_apiHost.IsRunning)
            {
                _log.Debug("REST API startup skipped because it is already running on port {Port}", _apiHost.Port);
                return;
            }

            await _apiHost.StartAsync(DefaultPort, ct).ConfigureAwait(false);
            _log.Information("REST API lifecycle started on {BaseUrl}", _apiHost.BaseUrl);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_apiHost.IsRunning)
            {
                return;
            }

            await _apiHost.StopAsync(ct).ConfigureAwait(false);
            _log.Information("REST API lifecycle stopped");
        }
        finally
        {
            _gate.Release();
        }
    }
}

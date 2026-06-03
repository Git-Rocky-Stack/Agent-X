using AgentX.Core.Services.Api;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Starts and stops the local REST API on the stable browser-extension port. Honors the
/// <see cref="AppSettings.LocalApiEnabled"/> toggle and provisions the per-install bearer token
/// (<see cref="AppSettings.LocalApiToken"/>) on first start.
/// </summary>
public sealed class ApiHostLifecycleService : IApiHostLifecycleService
{
    public const int DefaultPort = 9846;

    private readonly IApiHostService _apiHost;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ApiHostLifecycleService(IApiHostService apiHost, ISettingsService settingsService, ILogger logger)
    {
        _apiHost = apiHost ?? throw new ArgumentNullException(nameof(apiHost));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
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

            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

            if (!settings.LocalApiEnabled)
            {
                _log.Information("Local REST API is disabled in settings — listener not started");
                return;
            }

            // Provision a per-install token on first start so the extension has something to pair with.
            if (string.IsNullOrEmpty(settings.LocalApiToken))
            {
                settings.LocalApiToken = LocalApiSecurity.GenerateToken();
                await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);
                _log.Information("Generated a new local REST API token (first run)");
            }

            await _apiHost.StartAsync(DefaultPort, settings.LocalApiToken, ct).ConfigureAwait(false);
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

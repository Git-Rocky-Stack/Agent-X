using AgentX.Core.Services.Api;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Starts and stops the local REST API on the stable browser-extension port. Honors the
/// <see cref="AppSettings.LocalApiEnabled"/> toggle and provisions the per-install bearer token
/// (<see cref="AppSettings.LocalApiToken"/>) on first start. <see cref="ApplySettingsAsync"/>
/// re-applies both at runtime when the user saves settings or regenerates the token.
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
                _log.Information("Local REST API is disabled in settings - listener not started");
                return;
            }

            var token = await EnsureTokenAsync(settings).ConfigureAwait(false);

            await _apiHost.StartAsync(DefaultPort, token, ct).ConfigureAwait(false);
            _log.Information("REST API lifecycle started on {BaseUrl}", _apiHost.BaseUrl);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplySettingsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

            if (!settings.LocalApiEnabled)
            {
                if (_apiHost.IsRunning)
                {
                    await _apiHost.StopAsync(ct).ConfigureAwait(false);
                    _log.Information("Local REST API disabled in settings; listener stopped");
                }

                return;
            }

            var token = await EnsureTokenAsync(settings).ConfigureAwait(false);

            if (_apiHost.IsRunning)
            {
                // Swap the token on the live listener: the new token is accepted from the next
                // request on and the previous one is rejected, so regenerating really revokes.
                _apiHost.SetAuthToken(token);
                _log.Information("Applied the current local REST API token to the running listener");
                return;
            }

            await _apiHost.StartAsync(DefaultPort, token, ct).ConfigureAwait(false);
            _log.Information("Local REST API enabled in settings; listening on {BaseUrl}", _apiHost.BaseUrl);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns the persisted token, provisioning and saving a new one when none exists yet (first
    /// start, or a settings file whose token was cleared) so clients always have something to pair with.
    /// </summary>
    private async Task<string> EnsureTokenAsync(AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.LocalApiToken))
        {
            settings.LocalApiToken = LocalApiSecurity.GenerateToken();
            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);
            _log.Information("Generated a new local REST API token");
        }

        return settings.LocalApiToken;
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

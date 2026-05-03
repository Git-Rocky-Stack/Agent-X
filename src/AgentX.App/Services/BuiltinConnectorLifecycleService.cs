using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Initializes built-in data connector plugins with the same scoped host services
/// that external plugin packages receive, then activates their timers only when
/// the corresponding app setting is enabled.
/// </summary>
public sealed class BuiltinConnectorLifecycleService : IBuiltinConnectorLifecycleService, IDisposable
{
    private readonly CalendarPlugin _calendarPlugin;
    private readonly EmailPlugin _emailPlugin;
    private readonly IServiceProvider _rootServices;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ServiceProvider? _pluginServices;
    private bool _calendarActive;
    private bool _emailActive;
    private bool _disposed;

    public BuiltinConnectorLifecycleService(
        CalendarPlugin calendarPlugin,
        EmailPlugin emailPlugin,
        IServiceProvider rootServices,
        ISettingsService settingsService,
        ILogger logger)
    {
        _calendarPlugin = calendarPlugin ?? throw new ArgumentNullException(nameof(calendarPlugin));
        _emailPlugin = emailPlugin ?? throw new ArgumentNullException(nameof(emailPlugin));
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<BuiltinConnectorLifecycleService>();
    }

    public Task InitializeAsync(CancellationToken ct = default) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopPluginsAsync().ConfigureAwait(false);
            _pluginServices?.Dispose();
            _pluginServices = BuildPluginServiceProvider();

            await _calendarPlugin.InitializeAsync(CreateContext(_calendarPlugin)).ConfigureAwait(false);
            await _emailPlugin.InitializeAsync(CreateContext(_emailPlugin)).ConfigureAwait(false);

            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            if (settings.CalendarConnector.EnableCalendarSync)
            {
                await _calendarPlugin.ActivateAsync().ConfigureAwait(false);
                _calendarActive = true;
            }

            if (settings.EmailConnector.EnableEmailSync)
            {
                await _emailPlugin.ActivateAsync().ConfigureAwait(false);
                _emailActive = true;
            }

            _log.Information(
                "Built-in connectors refreshed. CalendarActive={CalendarActive} EmailActive={EmailActive}",
                _calendarActive,
                _emailActive);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopPluginsAsync().ConfigureAwait(false);
            _pluginServices?.Dispose();
            _pluginServices = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pluginServices?.Dispose();
        _gate.Dispose();
    }

    private ServiceProvider BuildPluginServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_rootServices.GetRequiredService<IOAuthService>());
        services.AddSingleton(_rootServices.GetRequiredService<IInboxService>());
        return services.BuildServiceProvider();
    }

    private async Task StopPluginsAsync()
    {
        if (_calendarActive)
        {
            await _calendarPlugin.DeactivateAsync().ConfigureAwait(false);
            _calendarActive = false;
        }

        if (_emailActive)
        {
            await _emailPlugin.DeactivateAsync().ConfigureAwait(false);
            _emailActive = false;
        }
    }

    private BuiltinPluginContext CreateContext(IPlugin plugin)
    {
        if (_pluginServices is null)
            throw new InvalidOperationException("Plugin services have not been initialized.");

        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX",
            "Plugins",
            SanitizeDirectorySegment(plugin.Id),
            "data");

        Directory.CreateDirectory(dataPath);

        return new BuiltinPluginContext(
            _pluginServices,
            dataPath,
            _log.ForContext("PluginId", plugin.Id).ForContext("PluginVersion", plugin.Version));
    }

    private static string SanitizeDirectorySegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private sealed class BuiltinPluginContext : IPluginContext
    {
        public BuiltinPluginContext(IServiceProvider services, string pluginDataPath, ILogger logger)
        {
            Services = services;
            PluginDataPath = pluginDataPath;
            Logger = logger;
        }

        public IServiceProvider Services { get; }
        public string PluginDataPath { get; }
        public ILogger Logger { get; }
    }
}

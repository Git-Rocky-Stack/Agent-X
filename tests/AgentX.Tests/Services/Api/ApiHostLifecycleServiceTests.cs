using AgentX.App.Services;
using AgentX.Core.Services.Api;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services.Api;

public sealed class ApiHostLifecycleServiceTests
{
    [Fact]
    public async Task StartAsync_StartsApiHostOnBrowserExtensionPort()
    {
        var apiHost = new RecordingApiHostService();
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.StartAsync();

        apiHost.StartCount.Should().Be(1);
        apiHost.StartedPort.Should().Be(9846);
        apiHost.StartedToken.Should().NotBeNullOrEmpty("a bearer token must be supplied to the host");
    }

    [Fact]
    public async Task StartAsync_IsIdempotentWhenApiHostIsAlreadyRunning()
    {
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.StartAsync();

        apiHost.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_DoesNotStart_WhenApiDisabled()
    {
        var apiHost = new RecordingApiHostService();
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = false });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.StartAsync();

        apiHost.StartCount.Should().Be(0, "the listener must not start when the toggle is off");
    }

    [Fact]
    public async Task StartAsync_GeneratesAndPersistsToken_WhenMissing()
    {
        var apiHost = new RecordingApiHostService();
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true, LocalApiToken = null });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.StartAsync();

        settings.Current.LocalApiToken.Should().NotBeNullOrEmpty();
        settings.SaveCount.Should().Be(1, "a freshly generated token must be persisted");
        apiHost.StartedToken.Should().Be(settings.Current.LocalApiToken);
    }

    [Fact]
    public async Task StartAsync_ReusesExistingToken_WithoutResaving()
    {
        var apiHost = new RecordingApiHostService();
        const string existing = "EXISTINGTOKEN1234567890";
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true, LocalApiToken = existing });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.StartAsync();

        apiHost.StartedToken.Should().Be(existing);
        settings.SaveCount.Should().Be(0, "an existing token must not be regenerated or re-saved");
    }

    [Fact]
    public async Task StopAsync_StopsApiHostWhenRunning()
    {
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.StopAsync();

        apiHost.StopCount.Should().Be(1);
        apiHost.IsRunning.Should().BeFalse();
    }

    // ── Test doubles ─────────────────────────────────────────────────────────

    private sealed class RecordingApiHostService : IApiHostService
    {
        public bool IsRunning { get; set; }
        public int Port { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public int? StartedPort { get; private set; }
        public string? StartedToken { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(int port = 9846, string? authToken = null, CancellationToken ct = default)
        {
            StartCount++;
            StartedPort = port;
            StartedToken = authToken;
            Port = port;
            BaseUrl = $"http://localhost:{port}/";
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; }
        public int SaveCount { get; private set; }

        public FakeSettingsService(AppSettings settings) => Current = settings;

        public Task<AppSettings> GetSettingsAsync() => Task.FromResult(Current);

        public Task SaveSettingsAsync(AppSettings settings)
        {
            Current = settings;
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<T?> GetValueAsync<T>(string key) => Task.FromResult(default(T));

        public Task SetValueAsync<T>(string key, T value) => Task.CompletedTask;
    }
}

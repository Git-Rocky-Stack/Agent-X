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

    // --- ApplySettingsAsync: token rotation and the enable toggle at runtime ---

    [Fact]
    public async Task ApplySettingsAsync_WhenRunning_HandsTheRegeneratedTokenToTheLiveListener()
    {
        // Regenerating the token in Settings used to only save it: the listener kept the token it
        // captured at startup, so the old (possibly leaked) token kept working and the new one got
        // 401 until the app restarted.
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true, LocalApiToken = "OLD" });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        settings.Current.LocalApiToken = "REGENERATED";
        await lifecycle.ApplySettingsAsync();

        apiHost.AppliedTokens.Should().Equal("REGENERATED");
        apiHost.StartCount.Should().Be(0, "a running listener adopts the token in place, without a restart");
        apiHost.StopCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplySettingsAsync_WhenApiDisabled_StopsTheRunningListener()
    {
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = false, LocalApiToken = "T" });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.ApplySettingsAsync();

        apiHost.StopCount.Should().Be(1, "turning the API off must stop the listener, not wait for a restart");
        apiHost.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ApplySettingsAsync_WhenApiDisabledAndStopped_IsANoOp()
    {
        var apiHost = new RecordingApiHostService();
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = false });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.ApplySettingsAsync();

        apiHost.StartCount.Should().Be(0);
        apiHost.StopCount.Should().Be(0);
        settings.SaveCount.Should().Be(0, "a disabled API must not provision a token");
    }

    [Fact]
    public async Task ApplySettingsAsync_WhenApiEnabledAndStopped_StartsTheListenerWithTheSavedToken()
    {
        var apiHost = new RecordingApiHostService();
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true, LocalApiToken = "SAVED" });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.ApplySettingsAsync();

        apiHost.StartCount.Should().Be(1, "turning the API on must start the listener, not wait for a restart");
        apiHost.StartedPort.Should().Be(9846);
        apiHost.StartedToken.Should().Be("SAVED");
    }

    [Fact]
    public async Task ApplySettingsAsync_WhenTokenMissing_ProvisionsPersistsAndAppliesOne()
    {
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var settings = new FakeSettingsService(new AppSettings { LocalApiEnabled = true, LocalApiToken = null });
        var lifecycle = new ApiHostLifecycleService(apiHost, settings, Logger.None);

        await lifecycle.ApplySettingsAsync();

        settings.Current.LocalApiToken.Should().NotBeNullOrEmpty();
        settings.SaveCount.Should().Be(1, "a freshly generated token must be persisted");
        apiHost.AppliedTokens.Should().Equal(settings.Current.LocalApiToken);
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
        public List<string?> AppliedTokens { get; } = new();

        public void SetAuthToken(string? authToken) => AppliedTokens.Add(authToken);

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

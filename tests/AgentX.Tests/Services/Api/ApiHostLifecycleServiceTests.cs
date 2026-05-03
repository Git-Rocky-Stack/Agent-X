using AgentX.App.Services;
using AgentX.Core.Services.Api;
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
        var lifecycle = new ApiHostLifecycleService(apiHost, Logger.None);

        await lifecycle.StartAsync();

        apiHost.StartCount.Should().Be(1);
        apiHost.StartedPort.Should().Be(9846);
    }

    [Fact]
    public async Task StartAsync_IsIdempotentWhenApiHostIsAlreadyRunning()
    {
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var lifecycle = new ApiHostLifecycleService(apiHost, Logger.None);

        await lifecycle.StartAsync();

        apiHost.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_StopsApiHostWhenRunning()
    {
        var apiHost = new RecordingApiHostService { IsRunning = true, Port = 9846 };
        var lifecycle = new ApiHostLifecycleService(apiHost, Logger.None);

        await lifecycle.StopAsync();

        apiHost.StopCount.Should().Be(1);
        apiHost.IsRunning.Should().BeFalse();
    }

    private sealed class RecordingApiHostService : IApiHostService
    {
        public bool IsRunning { get; set; }
        public int Port { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public int? StartedPort { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(int port = 9846, CancellationToken ct = default)
        {
            StartCount++;
            StartedPort = port;
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
}

using System.Net;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class BuiltInModelBootstrapTests : IDisposable
{
    private readonly string _dir;

    public BuiltInModelBootstrapTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "agentx-modeltest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private BuiltInModelBootstrap CreateBootstrap(HttpClient client, long minValid = 8)
        => new(
            client,
            _dir,
            Log.Logger,
            modelFileName: "test-model.gguf",
            downloadUrl: "https://example.invalid/test-model.gguf",
            expectedSizeBytes: 64,
            minimumValidBytes: minValid);

    [Fact]
    public async Task DownloadAsync_writes_file_atomically_and_reports_completion()
    {
        var body = RandomBytes(64);
        using var client = new HttpClient(new StubHandler(body));
        var bootstrap = CreateBootstrap(client);

        string? finalStatus = null;
        var progress = new SyncProgress<ModelDownloadProgress>(p => finalStatus = p.Status);

        await bootstrap.DownloadAsync(progress);

        File.Exists(bootstrap.ModelPath).Should().BeTrue();
        File.ReadAllBytes(bootstrap.ModelPath).Should().Equal(body);
        File.Exists(bootstrap.ModelPath + ".part").Should().BeFalse("the temp file is moved on success");
        bootstrap.IsInstalled().Should().BeTrue();
        finalStatus.Should().Be("Complete");
    }

    [Fact]
    public void IsInstalled_reflects_presence_and_rejects_truncated_files()
    {
        using var client = new HttpClient(new StubHandler(RandomBytes(8)));
        var bootstrap = CreateBootstrap(client, minValid: 1000);

        bootstrap.IsInstalled().Should().BeFalse("no file exists yet");

        File.WriteAllBytes(bootstrap.ModelPath, RandomBytes(1500));
        bootstrap.IsInstalled().Should().BeTrue("a file above the validity floor is installed");

        File.WriteAllBytes(bootstrap.ModelPath, RandomBytes(10));
        bootstrap.IsInstalled().Should().BeFalse("a file below the floor is a truncated leftover");
    }

    [Fact]
    public async Task EnsureInstalledAsync_skips_download_when_already_present()
    {
        var handler = new StubHandler(RandomBytes(64)) { ThrowIfCalled = true };
        using var client = new HttpClient(handler);
        var bootstrap = CreateBootstrap(client, minValid: 100);

        File.WriteAllBytes(bootstrap.ModelPath, RandomBytes(2048)); // already installed

        var act = async () => await bootstrap.EnsureInstalledAsync();

        await act.Should().NotThrowAsync("an installed model must not trigger a network call");
        handler.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_rejects_incomplete_transfer_and_leaves_no_files()
    {
        // Server advertises more than it delivers — the transfer is incomplete and must be rejected.
        var body = RandomBytes(200);
        using var client = new HttpClient(new StubHandler(body) { AdvertisedLength = 1000 });
        var bootstrap = CreateBootstrap(client, minValid: 8);

        var act = async () => await bootstrap.DownloadAsync();

        await act.Should().ThrowAsync<IOException>();
        File.Exists(bootstrap.ModelPath).Should().BeFalse("an incomplete download must not be published");
        File.Exists(bootstrap.ModelPath + ".part").Should().BeFalse("the partial must be cleaned up");
    }

    [Fact]
    public async Task DownloadAsync_overwrites_an_existing_truncated_file()
    {
        File.WriteAllBytes(Path.Combine(_dir, "test-model.gguf"), RandomBytes(5)); // stale truncated file
        var body = RandomBytes(64);
        using var client = new HttpClient(new StubHandler(body));
        var bootstrap = CreateBootstrap(client);

        await bootstrap.DownloadAsync();

        File.ReadAllBytes(bootstrap.ModelPath).Should().Equal(body);
    }

    [Fact]
    public async Task DownloadAsync_with_precancelled_token_does_not_publish_a_file()
    {
        using var client = new HttpClient(new StubHandler(RandomBytes(64)));
        var bootstrap = CreateBootstrap(client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await bootstrap.DownloadAsync(null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(bootstrap.ModelPath).Should().BeFalse();
        File.Exists(bootstrap.ModelPath + ".part").Should().BeFalse();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++) bytes[i] = (byte)((i * 31 + 7) & 0xFF);
        return bytes;
    }

    /// <summary>Inline (synchronous) IProgress so assertions are deterministic in unit tests.</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;
        public SyncProgress(Action<T> onReport) => _onReport = onReport;
        public void Report(T value) => _onReport(value);
    }

    /// <summary>Stub handler returning a fixed body, with optional advertised length / call tripwire.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public long? AdvertisedLength { get; init; }
        public bool ThrowIfCalled { get; init; }
        public bool WasCalled { get; private set; }

        public StubHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            if (ThrowIfCalled)
                throw new InvalidOperationException("Network call was not expected.");

            cancellationToken.ThrowIfCancellationRequested();

            var content = new ByteArrayContent(_body);
            content.Headers.ContentLength = AdvertisedLength ?? _body.Length;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            return Task.FromResult(response);
        }
    }
}

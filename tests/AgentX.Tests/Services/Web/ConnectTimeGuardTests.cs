using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AgentX.Core.Services.Web;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Web;

/// <summary>
/// Server-side request forgery checks where connections are opened: the addresses a check
/// resolved are pinned and connected to (so DNS rebinding cannot reach a local host after the
/// check passed), cloud metadata endpoints are refused in every form, <see cref="GuardedWebHandler"/>
/// applies both through a real HttpClient, and page WebSockets follow the private network rule.
/// Host names are unique per test because pins are shared by the whole process.
/// </summary>
public sealed class ConnectTimeGuardTests
{
    private static readonly IPAddress Public = IPAddress.Parse("93.184.216.34");

    private static string UniqueHost() => $"guard-{Guid.NewGuid():N}.example";

    // -- Cloud metadata ----------------------------------------------------------------

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.170.2")]
    [InlineData("100.100.100.200")]
    [InlineData("192.0.0.192")]
    [InlineData("168.63.129.16")]
    [InlineData("fd00:ec2::254")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:a9fe:a9fe::1")]
    public void IsCloudMetadata_flags_metadata_endpoints_in_every_form(string address)
    {
        PrivateNetworkGuard.IsCloudMetadata(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("::1")]
    [InlineData("100.100.100.201")]
    [InlineData("fd00:ec2::253")]
    [InlineData("2002:0808:0808::1")]
    public void IsCloudMetadata_leaves_other_addresses_alone(string address)
    {
        PrivateNetworkGuard.IsCloudMetadata(IPAddress.Parse(address)).Should().BeFalse();
    }

    // -- Pinning -------------------------------------------------------------------------

    [Fact]
    public async Task A_name_checked_as_public_is_connected_at_the_addresses_the_check_saw()
    {
        var host = UniqueHost();
        var lookups = 0;

        // DNS rebinding: the first answer is public, every later one is this computer.
        Task<IPAddress[]> Resolve(string name, CancellationToken _) =>
            Task.FromResult(++lookups == 1 ? new[] { Public } : new[] { IPAddress.Loopback });

        var firstCheck = await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(new Uri($"https://{host}/"), Resolve, default);
        var connectTo = await PrivateNetworkGuard.ResolveForConnectAsync(host, allowPrivate: false, Resolve, default);
        var laterCheck = await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(new Uri($"https://{host}/other"), Resolve, default);

        firstCheck.Should().BeFalse();
        connectTo.Should().Equal(Public);
        laterCheck.Should().BeFalse("a check within the pin's lifetime gets the pinned verdict");
        lookups.Should().Be(1, "DNS is asked once; the connection uses the pinned answer");
    }

    [Fact]
    public async Task A_name_that_did_not_resolve_at_the_check_may_only_connect_to_public_addresses()
    {
        var host = UniqueHost();
        var lookups = 0;
        Task<IPAddress[]> Resolve(string name, CancellationToken _) => ++lookups == 1
            ? Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound))
            : Task.FromResult(new[] { IPAddress.Parse("10.0.0.8") });

        var isPrivate = await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(new Uri($"http://{host}/"), Resolve, default);
        var connect = () => PrivateNetworkGuard.ResolveForConnectAsync(host, allowPrivate: false, Resolve, default);

        isPrivate.Should().BeFalse();
        await connect.Should().ThrowAsync<HttpRequestException>().WithMessage("*Blocked a connection*");
    }

    [Fact]
    public void The_synchronous_check_pins_like_the_asynchronous_one()
    {
        var host = UniqueHost();
        var lookups = 0;
        IPAddress[] Resolve(string name) => ++lookups == 1 ? [IPAddress.Parse("10.1.2.3")] : [Public];

        PrivateNetworkGuard.IsPrivateOrLocalHost(new Uri($"wss://{host}/live"), Resolve).Should().BeTrue();
        PrivateNetworkGuard.IsPrivateOrLocalHost(new Uri($"wss://{host}/live"), Resolve).Should().BeTrue();
        lookups.Should().Be(1);
    }

    // -- Which addresses a connection may use ----------------------------------------------

    [Fact]
    public async Task A_public_host_connects_only_to_its_public_addresses()
    {
        var host = UniqueHost();
        Task<IPAddress[]> Resolve(string name, CancellationToken _) =>
            Task.FromResult(new[] { IPAddress.Parse("10.0.0.1"), Public });

        var connectTo = await PrivateNetworkGuard.ResolveForConnectAsync(host, allowPrivate: false, Resolve, default);

        connectTo.Should().Equal(Public);
    }

    [Fact]
    public async Task A_private_host_may_connect_privately_but_never_to_a_metadata_endpoint()
    {
        var host = UniqueHost();
        Task<IPAddress[]> Resolve(string name, CancellationToken _) =>
            Task.FromResult(new[] { IPAddress.Parse("169.254.169.254"), IPAddress.Parse("10.0.0.1") });

        var connectTo = await PrivateNetworkGuard.ResolveForConnectAsync(host, allowPrivate: true, Resolve, default);

        connectTo.Should().Equal(IPAddress.Parse("10.0.0.1"));
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("[::ffff:169.254.169.254]")]
    [InlineData("[64:ff9b::a9fe:a9fe]")]
    [InlineData("[fd00:ec2::254]")]
    public async Task A_metadata_literal_is_refused_even_where_private_addresses_are_allowed(string host)
    {
        var connect = () => PrivateNetworkGuard.ResolveForConnectAsync(
            host, allowPrivate: true, (_, _) => throw new InvalidOperationException("a literal needs no DNS"), default);

        await connect.Should().ThrowAsync<HttpRequestException>().WithMessage("*cloud metadata*");
    }

    // -- Through an HttpClient -------------------------------------------------------------

    [Fact]
    public async Task Handler_fetches_an_intranet_address_the_user_entered()
    {
        await using var server = LoopbackServer.Start("intranet page");
        using var client = new HttpClient(new GuardedWebHandler(useProxy: false, proxy: null, resolve: null, connect: null));

        var body = await client.GetStringAsync($"http://127.0.0.1:{server.Port}/wiki");

        body.Should().Be("intranet page");
        server.Connections.Should().Be(1);
    }

    [Fact]
    public async Task Handler_connects_a_rebinding_name_to_its_pinned_public_address_never_the_local_one()
    {
        await using var server = LoopbackServer.Start("local secret");
        var host = UniqueHost();
        var lookups = 0;
        var attempted = new ConcurrentQueue<IPAddress>();
        Task<IPAddress[]> Resolve(string name, CancellationToken _) =>
            Task.FromResult(++lookups == 1 ? new[] { Public } : new[] { IPAddress.Loopback });
        ValueTask<Stream> Connect(IPAddress[] addresses, int port, CancellationToken _)
        {
            foreach (var address in addresses)
                attempted.Enqueue(address);
            throw new SocketException((int)SocketError.ConnectionRefused); // the public host is not reachable from a test
        }

        using var client = new HttpClient(new GuardedWebHandler(useProxy: false, proxy: null, Resolve, Connect));
        var fetch = () => client.GetStringAsync($"http://{host}:{server.Port}/admin");

        await fetch.Should().ThrowAsync<HttpRequestException>();
        attempted.Should().Equal(Public);
        server.Connections.Should().Be(0);
    }

    [Fact]
    public async Task Handler_refuses_a_name_that_resolves_locally_only_when_connecting()
    {
        await using var server = LoopbackServer.Start("local secret");
        var host = UniqueHost();
        var lookups = 0;
        Task<IPAddress[]> Resolve(string name, CancellationToken _) => ++lookups == 1
            ? Task.FromException<IPAddress[]>(new SocketException((int)SocketError.TryAgain))
            : Task.FromResult(new[] { IPAddress.Loopback });

        using var client = new HttpClient(new GuardedWebHandler(useProxy: false, proxy: null, Resolve, connect: null));
        var fetch = () => client.GetStringAsync($"http://{host}:{server.Port}/");

        (await fetch.Should().ThrowAsync<HttpRequestException>()).Which.ToString().Should().Contain("Blocked a connection");
        server.Connections.Should().Be(0);
    }

    [Fact]
    public async Task Handler_refuses_a_metadata_endpoint_even_when_entered_directly()
    {
        var attempted = new ConcurrentQueue<IPAddress>();
        ValueTask<Stream> Connect(IPAddress[] addresses, int port, CancellationToken _)
        {
            foreach (var address in addresses)
                attempted.Enqueue(address);
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        using var client = new HttpClient(new GuardedWebHandler(useProxy: false, proxy: null, resolve: null, Connect));
        var fetch = () => client.GetStringAsync("http://169.254.169.254/latest/meta-data/iam/security-credentials/");

        (await fetch.Should().ThrowAsync<HttpRequestException>()).Which.ToString().Should().Contain("cloud metadata");
        attempted.Should().BeEmpty("no socket is opened");
    }

    [Fact]
    public async Task Handler_still_sends_through_a_configured_proxy()
    {
        // The proxy is on this computer; the connection to it must not be taken for a request
        // to a local address.
        await using var proxy = LoopbackServer.Start("via proxy");
        var host = UniqueHost();
        Task<IPAddress[]> Resolve(string name, CancellationToken _) => Task.FromResult(new[] { Public });

        using var client = new HttpClient(new GuardedWebHandler(
            useProxy: true, new WebProxy($"http://127.0.0.1:{proxy.Port}"), Resolve, connect: null));
        var body = await client.GetStringAsync($"http://{host}/page");

        body.Should().Be("via proxy");
        proxy.RequestLines.Should().ContainSingle().Which.Should().StartWith($"GET http://{host}/page ");
    }

    // -- Page WebSockets -----------------------------------------------------------------------

    [Theory]
    [InlineData("ws://127.0.0.1:9000/socket", false)]
    [InlineData("wss://printer.local/live", false)]
    [InlineData("ws://[::1]/", false)]
    [InlineData("ws://169.254.169.254/", false)]
    [InlineData("wss://93.184.216.34/feed", true)]
    [InlineData("ftp://93.184.216.34/", false)]
    [InlineData("not a url", false)]
    public void WebSocket_targets_follow_the_private_network_rule(string url, bool allowed)
    {
        static bool NoDns(Uri uri) =>
            PrivateNetworkGuard.IsPrivateOrLocalHost(uri, _ => throw new InvalidOperationException("literals and local names need no DNS"));

        JsRenderingService.IsAllowedWebSocketTarget(url, NoDns).Should().Be(allowed);
    }

    [Fact]
    public void WebSocket_to_a_name_that_resolves_privately_is_refused()
    {
        var host = UniqueHost();

        var allowed = JsRenderingService.IsAllowedWebSocketTarget(
            $"wss://{host}/updates",
            uri => PrivateNetworkGuard.IsPrivateOrLocalHost(uri, _ => [IPAddress.Parse("192.168.0.20")]));

        allowed.Should().BeFalse();
    }

    /// <summary>
    /// A loopback HTTP/1.1 server that answers every request with the same body, counts the
    /// connections it accepted and records each request line.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _response;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private int _connections;

        private LoopbackServer(string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            _response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n")
                .Concat(bytes)
                .ToArray();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public static LoopbackServer Start(string body) => new(body);

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Connections => Volatile.Read(ref _connections);

        public ConcurrentQueue<string> RequestLines { get; } = new();

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                Interlocked.Increment(ref _connections);
                _ = Task.Run(() => AnswerAsync(connection));
            }
        }

        private async Task AnswerAsync(TcpClient connection)
        {
            using (connection)
            {
                try
                {
                    var stream = connection.GetStream();
                    var received = new StringBuilder();
                    var buffer = new byte[4096];
                    while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer, _stop.Token);
                        if (read == 0)
                            return;
                        received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    RequestLines.Enqueue(received.ToString().Split("\r\n")[0]);
                    await stream.WriteAsync(_response, _stop.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
                {
                    // The client went away.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException)
            {
                // Stopping.
            }

            _stop.Dispose();
        }
    }
}

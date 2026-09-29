using System.Net;
using System.Net.Sockets;

namespace AgentX.Core.Services.Web;

/// <summary>
/// The HTTP handler the page fetcher, the feed reader and the sitemap parser send through. It
/// checks what every connection actually reaches, where the socket is opened, so a host that
/// passed <see cref="PrivateNetworkGuard"/> as public cannot be reached at a private address.
/// </summary>
/// <remarks>
/// <para>
/// Each request is routed by its host's verdict from <see cref="PrivateNetworkGuard"/>, the same
/// pinned verdict the fetchers' own checks saw. A host that is public is connected to at the
/// addresses its check resolved (or, without a live pin, only at public addresses a fresh lookup
/// returns), so DNS rebinding between the check and the connection gets nowhere. A host that is
/// private or local, which the fetchers only request where their rules allow it (a URL the user
/// entered, or one found in content that was itself private), is connected to as resolved. Cloud
/// metadata endpoints are refused in both cases, so even a URL the user entered cannot read them.
/// </para>
/// <para>
/// The two kinds of host use separate connection pools: a pooled connection is not checked
/// again, so one opened to a private address must never serve a host that is public now.
/// </para>
/// <para>
/// Through a proxy configured on the computer, the connection goes to the proxy, which resolves
/// the target itself; the checks made before sending are then the only guard.
/// </para>
/// </remarks>
internal sealed class GuardedWebHandler : HttpMessageHandler
{
    private readonly HttpMessageInvoker _publicOnly;
    private readonly HttpMessageInvoker _privateAllowed;
    private readonly bool _useProxy;
    private readonly IWebProxy? _proxy;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress[], int, CancellationToken, ValueTask<Stream>> _connect;

    /// <summary>
    /// Creates the handler used in production: the computer's proxy settings, system DNS and
    /// plain sockets. Redirects are not followed (<see cref="WebHttp"/> follows them hop by hop);
    /// gzip, deflate and brotli responses are decompressed.
    /// </summary>
    public static GuardedWebHandler Create() => new(useProxy: true, proxy: null, resolve: null, connect: null);

    /// <summary>
    /// Test seam: <paramref name="resolve"/> replaces DNS, <paramref name="connect"/> replaces
    /// opening the socket, and the proxy can be turned off or given.
    /// </summary>
    internal GuardedWebHandler(
        bool useProxy,
        IWebProxy? proxy,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve,
        Func<IPAddress[], int, CancellationToken, ValueTask<Stream>>? connect)
    {
        _useProxy = useProxy;
        _proxy = proxy;
        _resolve = resolve ?? Dns.GetHostAddressesAsync;
        _connect = connect ?? ConnectSocketAsync;
        _publicOnly = new HttpMessageInvoker(CreateZoneHandler(allowPrivate: false), disposeHandler: true);
        _privateAllowed = new HttpMessageInvoker(CreateZoneHandler(allowPrivate: true), disposeHandler: true);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isPrivate = request.RequestUri is { IsAbsoluteUri: true } uri
                        && await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(uri, _resolve, cancellationToken).ConfigureAwait(false);

        var zone = isPrivate ? _privateAllowed : _publicOnly;
        return await zone.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publicOnly.Dispose();
            _privateAllowed.Dispose();
        }

        base.Dispose(disposing);
    }

    private SocketsHttpHandler CreateZoneHandler(bool allowPrivate)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = false,
            UseProxy = _useProxy,
            ConnectCallback = (context, ct) => ConnectAsync(context, allowPrivate, ct),
        };

        if (_proxy is not null)
        {
            handler.Proxy = _proxy;
        }

        return handler;
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, bool allowPrivate, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;

        // Every connection is held to the rules for the host it actually goes to, except one to
        // the proxy configured for this request.
        var addresses = IsConnectionToProxy(context)
            ? await ResolveProxyAsync(endpoint.Host, ct).ConfigureAwait(false)
            : await PrivateNetworkGuard.ResolveForConnectAsync(endpoint.Host, allowPrivate, _resolve, ct).ConfigureAwait(false);

        return await _connect(addresses, endpoint.Port, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the connection goes to the proxy the handler uses for this request (the one given
    /// to the handler, or the computer's), rather than to the request's own host.
    /// </summary>
    private bool IsConnectionToProxy(SocketsHttpConnectionContext context)
    {
        var proxy = _useProxy ? _proxy ?? HttpClient.DefaultProxy : null;
        if (proxy is null || context.InitialRequestMessage.RequestUri is not { IsAbsoluteUri: true } target)
        {
            return false;
        }

        try
        {
            if (proxy.IsBypassed(target) || proxy.GetProxy(target) is not { } proxyUri)
            {
                return false;
            }

            return proxyUri.Port == context.DnsEndPoint.Port
                   && string.Equals(
                       PrivateNetworkGuard.NormalizeHost(proxyUri.IdnHost),
                       PrivateNetworkGuard.NormalizeHost(context.DnsEndPoint.Host),
                       StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A proxy setting that cannot be evaluated leaves the connection under the rules.
            return false;
        }
    }

    private async Task<IPAddress[]> ResolveProxyAsync(string host, CancellationToken ct)
    {
        var name = PrivateNetworkGuard.NormalizeHost(host);
        return IPAddress.TryParse(name, out var literal)
            ? [literal]
            : await _resolve(name, ct).ConfigureAwait(false);
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPAddress[] addresses, int port, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

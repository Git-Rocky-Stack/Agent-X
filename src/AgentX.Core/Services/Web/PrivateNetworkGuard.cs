using System.Net;
using System.Net.Sockets;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Decides whether a URL points at this machine or a private network, to stop server-side
/// request forgery through web import. The rule applied by the fetchers: a URL that Agent-X
/// discovers in remote content (a redirect, a feed item, a sitemap entry, a resource requested
/// by a page in the headless browser) may reach a private or local address only when the
/// content it came from was itself served from such an address. A URL the user types or drops
/// in directly may still target any host, so intranet pages keep importing as before.
/// </summary>
internal static class PrivateNetworkGuard
{
    /// <summary>
    /// True when the URL's host is "localhost" (or a *.localhost / *.local name), a private or
    /// local IP literal, or a name that resolves to at least one private or local address. A
    /// name that does not resolve is not reported as private; the request fails on its own.
    /// </summary>
    public static Task<bool> IsPrivateOrLocalHostAsync(Uri uri, CancellationToken ct = default) =>
        IsPrivateOrLocalHostAsync(uri, Dns.GetHostAddressesAsync, ct);

    /// <summary>Same as the public overload, with the DNS lookup supplied by the caller (tests).</summary>
    internal static async Task<bool> IsPrivateOrLocalHostAsync(
        Uri uri,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken ct)
    {
        var host = uri.Host.Trim('[', ']').TrimEnd('.');
        if (host.Length == 0)
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return IsPrivateOrLocal(literal);
        }

        var name = host.ToLowerInvariant();
        if (name == "localhost" || name.EndsWith(".localhost", StringComparison.Ordinal)
                                || name.EndsWith(".local", StringComparison.Ordinal))
        {
            return true;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolve(name, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return false;
        }

        return addresses.Any(IsPrivateOrLocal);
    }

    /// <summary>
    /// True for loopback, unspecified, "this network", link-local, private (RFC 1918), shared
    /// carrier-grade NAT (RFC 6598), benchmarking, multicast and reserved IPv4 addresses; for
    /// IPv6 loopback, unspecified, link-local, site-local, unique-local (fc00::/7) and multicast
    /// addresses; and for IPv6 forms that embed an IPv4 address (IPv4-mapped, IPv4-compatible,
    /// NAT64 64:ff9b::/96 and 6to4 2002::/16) whose embedded address is one of those.
    /// </summary>
    public static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 => true,                                       // "this network" 0.0.0.0/8
                10 => true,                                      // RFC 1918
                100 => bytes[1] >= 64 && bytes[1] <= 127,        // RFC 6598 shared address space
                127 => true,                                     // loopback
                169 => bytes[1] == 254,                          // link-local
                172 => bytes[1] >= 16 && bytes[1] <= 31,         // RFC 1918
                192 => bytes[1] == 168                           // RFC 1918
                       || (bytes[1] == 0 && bytes[2] == 0),      // IETF protocol assignments
                198 => bytes[1] == 18 || bytes[1] == 19,         // benchmarking 198.18.0.0/15
                >= 224 => true,                                  // multicast, reserved, broadcast
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true; // unique local fc00::/7
            }

            // ::/96 (unspecified, loopback and deprecated IPv4-compatible addresses)
            if (bytes.Take(12).All(b => b == 0))
            {
                return IsPrivateOrLocal(new IPAddress(bytes[12..16]));
            }

            // NAT64 64:ff9b::/96
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
                && bytes.Skip(4).Take(8).All(b => b == 0))
            {
                return IsPrivateOrLocal(new IPAddress(bytes[12..16]));
            }

            // 6to4 2002::/16 carries an IPv4 address in bytes 2 to 5
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return IsPrivateOrLocal(new IPAddress(bytes[2..6]));
            }

            return false;
        }

        // Any other address family is not something a web fetch should reach.
        return true;
    }

    /// <summary>
    /// Throws when a redirect leaves a public site for a private or local address. A chain that
    /// started on a private host (an intranet page the user asked for) may stay private.
    /// </summary>
    /// <param name="origin">The URL the request chain started from.</param>
    /// <param name="from">The URL that answered with the redirect.</param>
    /// <param name="to">The redirect target.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="HttpRequestException">Thrown when the redirect is refused.</exception>
    public static async Task EnsureRedirectAllowedAsync(Uri origin, Uri from, Uri to, CancellationToken ct)
    {
        if (!await IsPrivateOrLocalHostAsync(to, ct).ConfigureAwait(false))
        {
            return;
        }

        if (await IsPrivateOrLocalHostAsync(origin, ct).ConfigureAwait(false))
        {
            return;
        }

        throw new HttpRequestException(
            $"Blocked a redirect from '{from}' to '{to}': a public site may not send Agent-X to a private or local network address.");
    }
}

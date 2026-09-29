using System.Collections.Concurrent;
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
/// <remarks>
/// <para>
/// A check of a host name pins what it found: the resolved addresses and the verdict are kept for
/// <see cref="PinLifetime"/>, later checks of the name within that time return the same verdict,
/// and <see cref="GuardedWebHandler"/> connects to exactly those addresses. A name whose DNS
/// answer changes between the check and the connection (DNS rebinding) therefore cannot turn a
/// host that passed as public into a local one.
/// </para>
/// <para>
/// Cloud metadata endpoints (<see cref="IsCloudMetadata"/>) are never connected to, whatever the
/// rule allows otherwise.
/// </para>
/// </remarks>
internal static class PrivateNetworkGuard
{
    /// <summary>
    /// How long the result of checking a host name is kept: later checks of the name return the
    /// same verdict, and connections go to the addresses the check resolved.
    /// </summary>
    internal static readonly TimeSpan PinLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Above this many pinned names, expired pins are dropped when a new one is added.</summary>
    private const int PinPruneThreshold = 512;

    private static readonly ConcurrentDictionary<string, HostPin> Pins = new(StringComparer.Ordinal);

    // Cloud instance metadata and platform endpoints outside 169.254.0.0/16.
    private static readonly IPAddress AlibabaCloudMetadata = IPAddress.Parse("100.100.100.200");
    private static readonly IPAddress OracleCloudLegacyMetadata = IPAddress.Parse("192.0.0.192");
    private static readonly IPAddress AzurePlatformAddress = IPAddress.Parse("168.63.129.16");
    private static readonly IPAddress AwsIPv6Metadata = IPAddress.Parse("fd00:ec2::254");

    /// <summary>What a check found for a host name.</summary>
    /// <param name="Addresses">The addresses the name resolved to (never empty).</param>
    /// <param name="IsPrivate">Whether any of them is private or local.</param>
    /// <param name="ExpiresUtc">When the pin stops being used.</param>
    private sealed record HostPin(IPAddress[] Addresses, bool IsPrivate, DateTime ExpiresUtc);

    /// <summary>
    /// True when the URL's host is "localhost" (or a *.localhost / *.local name), a private or
    /// local IP literal, or a name that resolves to at least one private or local address. A
    /// name that does not resolve is not reported as private; the request fails on its own.
    /// A name that was checked in the last <see cref="PinLifetime"/> gets the same answer.
    /// </summary>
    public static Task<bool> IsPrivateOrLocalHostAsync(Uri uri, CancellationToken ct = default) =>
        IsPrivateOrLocalHostAsync(uri, Dns.GetHostAddressesAsync, ct);

    /// <summary>Same as the public overload, with the DNS lookup supplied by the caller (tests).</summary>
    internal static async Task<bool> IsPrivateOrLocalHostAsync(
        Uri uri,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken ct)
    {
        var name = HostOf(uri);
        if (ClassifyWithoutLookup(name) is { } decided)
        {
            return decided;
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

        return Pin(name, addresses);
    }

    /// <summary>
    /// Synchronous form of <see cref="IsPrivateOrLocalHostAsync(Uri, CancellationToken)"/>, for a
    /// decision that has to be made inside a synchronous callback (a Playwright WebSocket route).
    /// </summary>
    public static bool IsPrivateOrLocalHost(Uri uri) => IsPrivateOrLocalHost(uri, Dns.GetHostAddresses);

    /// <summary>Same as the public overload, with the DNS lookup supplied by the caller (tests).</summary>
    internal static bool IsPrivateOrLocalHost(Uri uri, Func<string, IPAddress[]> resolve)
    {
        var name = HostOf(uri);
        if (ClassifyWithoutLookup(name) is { } decided)
        {
            return decided;
        }

        IPAddress[] addresses;
        try
        {
            addresses = resolve(name);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return false;
        }

        return Pin(name, addresses);
    }

    /// <summary>
    /// The addresses a connection to <paramref name="host"/> may use, for the connect callback of
    /// <see cref="GuardedWebHandler"/>: the addresses pinned by the host's check while the pin
    /// lives, otherwise a fresh lookup. Cloud metadata endpoints are always removed; private and
    /// local addresses are removed too unless <paramref name="allowPrivate"/>, which is the case
    /// only for a host that was itself checked as private or local.
    /// </summary>
    /// <exception cref="HttpRequestException">No address is left to connect to.</exception>
    internal static async Task<IPAddress[]> ResolveForConnectAsync(
        string host,
        bool allowPrivate,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken ct)
    {
        var name = NormalizeHost(host);
        IPAddress[] candidates;
        if (IPAddress.TryParse(name, out var literal))
        {
            candidates = [literal];
        }
        else if (TryGetLivePin(name, out var pin))
        {
            candidates = pin.Addresses;
        }
        else
        {
            candidates = await resolve(name, ct).ConfigureAwait(false);
        }

        var allowed = candidates
            .Where(address => !IsCloudMetadata(address) && (allowPrivate || !IsPrivateOrLocal(address)))
            .ToArray();

        if (allowed.Length == 0)
        {
            throw new HttpRequestException(allowPrivate
                ? $"Blocked a connection to '{host}': it points to a cloud metadata endpoint, which web import never reads."
                : $"Blocked a connection to '{host}': when connecting, it pointed to this computer, a private network or a cloud metadata endpoint, which this request may not reach.");
        }

        return allowed;
    }

    /// <summary>
    /// The form in which host names are compared and pinned: no IPv6 brackets, no trailing dot,
    /// lower case.
    /// </summary>
    internal static string NormalizeHost(string host) => host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();

    private static string HostOf(Uri uri) => NormalizeHost(uri.IdnHost);

    /// <summary>
    /// Decides a host that needs no lookup: an empty host, an IP literal, a local name, or a name
    /// with a live pin. Null when the name has to be resolved.
    /// </summary>
    private static bool? ClassifyWithoutLookup(string name)
    {
        if (name.Length == 0)
        {
            return true;
        }

        if (IPAddress.TryParse(name, out var literal))
        {
            return IsPrivateOrLocal(literal);
        }

        if (name == "localhost" || name.EndsWith(".localhost", StringComparison.Ordinal)
                                || name.EndsWith(".local", StringComparison.Ordinal))
        {
            return true;
        }

        return TryGetLivePin(name, out var pin) ? pin.IsPrivate : null;
    }

    /// <summary>
    /// Pins what a lookup of <paramref name="name"/> found and returns the pinned verdict. A live
    /// pin stored meanwhile by a concurrent check wins, so every caller gets one answer. A lookup
    /// that found nothing is not pinned and counts as public.
    /// </summary>
    private static bool Pin(string name, IPAddress[] addresses)
    {
        if (addresses.Length == 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var found = new HostPin(addresses, addresses.Any(IsPrivateOrLocal), now + PinLifetime);
        var pinned = Pins.AddOrUpdate(name, found, (_, existing) => existing.ExpiresUtc > now ? existing : found);

        if (Pins.Count > PinPruneThreshold)
        {
            foreach (var entry in Pins)
            {
                if (entry.Value.ExpiresUtc <= now)
                {
                    Pins.TryRemove(entry);
                }
            }
        }

        return pinned.IsPrivate;
    }

    private static bool TryGetLivePin(string name, out HostPin pin)
    {
        if (Pins.TryGetValue(name, out var found) && found.ExpiresUtc > DateTime.UtcNow)
        {
            pin = found;
            return true;
        }

        pin = null!;
        return false;
    }

    /// <summary>
    /// Cloud instance metadata and platform endpoints, which serve credentials and are never web
    /// content: the whole IPv4 link-local range 169.254.0.0/16 (the metadata service of AWS,
    /// Google Cloud, Azure, Oracle Cloud, OpenStack and DigitalOcean at 169.254.169.254, the AWS
    /// container endpoint 169.254.170.2), Alibaba Cloud's 100.100.100.200, Oracle Cloud's legacy
    /// 192.0.0.192, the Azure platform address 168.63.129.16 and the AWS IPv6 endpoint
    /// fd00:ec2::254, in any IPv6 form that embeds an IPv4 address (IPv4-mapped, IPv4-compatible,
    /// NAT64 and 6to4).
    /// </summary>
    public static bool IsCloudMetadata(IPAddress address)
    {
        if (EmbeddedIPv4(address) is { } embedded)
        {
            address = embedded;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] == 169 && bytes[1] == 254)
                   || address.Equals(AlibabaCloudMetadata)
                   || address.Equals(OracleCloudLegacyMetadata)
                   || address.Equals(AzurePlatformAddress);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
               && new IPAddress(address.GetAddressBytes()).Equals(AwsIPv6Metadata);
    }

    /// <summary>
    /// The IPv4 address carried by an IPv6 address of a form that embeds one: IPv4-mapped
    /// (::ffff:0:0/96), IPv4-compatible (::/96, which covers :: and ::1), NAT64 (64:ff9b::/96)
    /// and 6to4 (2002::/16). Null for any other address.
    /// </summary>
    private static IPAddress? EmbeddedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();

        // ::/96 (unspecified, loopback and deprecated IPv4-compatible addresses)
        if (bytes.Take(12).All(b => b == 0))
        {
            return new IPAddress(bytes[12..16]);
        }

        // NAT64 64:ff9b::/96
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
            && bytes.Skip(4).Take(8).All(b => b == 0))
        {
            return new IPAddress(bytes[12..16]);
        }

        // 6to4 2002::/16 carries an IPv4 address in bytes 2 to 5
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return new IPAddress(bytes[2..6]);
        }

        return null;
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
        if (EmbeddedIPv4(address) is { } embedded)
        {
            return IsPrivateOrLocal(embedded);
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

            // unique local fc00::/7; the forms that embed an IPv4 address were decided above
            return (bytes[0] & 0xFE) == 0xFC;
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

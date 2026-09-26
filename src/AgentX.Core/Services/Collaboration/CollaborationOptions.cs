namespace AgentX.Core.Services.Collaboration;

/// <summary>
/// Options for <see cref="CollaborationService"/>. The defaults are the safe ones: the host
/// accepts connections from this machine only, and every request must carry the access token.
/// </summary>
/// <remarks>
/// The collaboration feature is unfinished and nothing in the app starts it. These options
/// exist so that wiring it up later cannot silently expose an unauthenticated endpoint on
/// the local network, which is what the original <c>http://+:{port}/</c> binding did.
/// </remarks>
public sealed class CollaborationOptions
{
    /// <summary>Shortest accepted <see cref="AccessToken"/>.</summary>
    public const int MinimumAccessTokenLength = 32;

    /// <summary>
    /// When <c>false</c> (the default) the host listens on <c>http://localhost:{port}/</c> and
    /// rejects any request whose remote address is not a loopback address. Set to <c>true</c>
    /// only for a trusted network: the host then listens on every interface
    /// (<c>http://+:{port}/</c>, which needs a URL ACL reservation on Windows).
    /// </summary>
    public bool AllowRemotePeers { get; init; }

    /// <summary>
    /// Shared secret that every request must present as <c>Authorization: Bearer {token}</c>.
    /// When null or empty, a random token is generated for the process, so only this process
    /// can talk to its own host. Every participant of a LAN session must be configured with
    /// the same token, at least <see cref="MinimumAccessTokenLength"/> characters long.
    /// </summary>
    public string? AccessToken { get; init; }
}

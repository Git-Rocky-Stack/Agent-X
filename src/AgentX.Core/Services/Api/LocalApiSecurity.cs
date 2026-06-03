using System.Security.Cryptography;
using System.Text;

namespace AgentX.Core.Services.Api;

/// <summary>
/// Pure authorization and CORS-origin policy for the embedded local REST API. Kept free of
/// <see cref="System.Net.HttpListener"/> types so the security decisions are deterministic and
/// directly unit-testable.
/// </summary>
/// <remarks>
/// Policy summary:
/// <list type="bullet">
///   <item>Every route except the lightweight extension health probe requires a bearer token
///         that matches the per-install API token (constant-time comparison).</item>
///   <item>Cross-origin reads are permitted only for browser-extension origins
///         (<c>chrome-extension://</c>, <c>moz-extension://</c>); ordinary web pages receive no
///         <c>Access-Control-Allow-Origin</c> header and therefore cannot read responses.</item>
/// </list>
/// </remarks>
public static class LocalApiSecurity
{
    private const string BearerScheme = "Bearer ";

    /// <summary>
    /// Routes reachable without authentication. Only the extension health probe qualifies — it
    /// returns no user data and lets the extension detect whether AgentX is running before pairing.
    /// </summary>
    public static bool IsPublicPath(string path) =>
        string.Equals(path, "/api/extension/health", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="authorizationHeader"/> carries a bearer token that
    /// matches <paramref name="expectedToken"/>. Fails closed when the expected token is missing,
    /// and uses a constant-time comparison to avoid leaking the token via timing.
    /// </summary>
    public static bool IsAuthorized(string? authorizationHeader, string? expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken))
            return false; // no token provisioned → deny everything (fail closed)

        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return false;

        if (!authorizationHeader.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var provided = authorizationHeader[BearerScheme.Length..].Trim();
        if (provided.Length == 0)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expectedToken));
    }

    /// <summary>
    /// Resolves the value to echo in <c>Access-Control-Allow-Origin</c> for the given request
    /// <paramref name="origin"/>, or <c>null</c> when the origin must not receive a CORS grant.
    /// Only browser-extension origins are allowed; web origins (http/https) get <c>null</c>.
    /// </summary>
    public static string? ResolveAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return null;

        return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("ms-browser-extension://", StringComparison.OrdinalIgnoreCase)
            ? origin
            : null;
    }

    /// <summary>
    /// Generates a new cryptographically-random 256-bit API token, hex-encoded for safe transport
    /// in an <c>Authorization</c> header and easy copy/paste during extension pairing.
    /// </summary>
    public static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

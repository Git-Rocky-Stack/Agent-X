using AgentX.Core.Services.Api;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Api;

/// <summary>
/// Tests for <see cref="LocalApiSecurity"/> — the bearer-token authorization and CORS-origin
/// policy that closes the unauthenticated-API / wildcard-CORS vulnerability.
/// </summary>
public sealed class LocalApiSecurityTests
{
    private const string Token = "A1B2C3D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2";

    // ── Public paths ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/extension/health", true)]
    [InlineData("/api/health", false)]
    [InlineData("/api/documents", false)]
    [InlineData("/api/inbox/clip", false)]
    [InlineData("/api/search", false)]
    public void IsPublicPath_OnlyExtensionHealthIsPublic(string path, bool expected)
        => LocalApiSecurity.IsPublicPath(path).Should().Be(expected);

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public void IsAuthorized_ValidBearerToken_Succeeds()
        => LocalApiSecurity.IsAuthorized($"Bearer {Token}", Token).Should().BeTrue();

    [Fact]
    public void IsAuthorized_IsSchemeCaseInsensitive()
        => LocalApiSecurity.IsAuthorized($"bearer {Token}", Token).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WRONGTOKEN")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong-token")]
    [InlineData("Basic A1B2C3")]
    public void IsAuthorized_RejectsMissingOrWrongTokens(string? header)
        => LocalApiSecurity.IsAuthorized(header, Token).Should().BeFalse();

    [Fact]
    public void IsAuthorized_FailsClosed_WhenNoTokenProvisioned()
    {
        // Even a syntactically valid header must be rejected when the server has no token.
        LocalApiSecurity.IsAuthorized($"Bearer {Token}", null).Should().BeFalse();
        LocalApiSecurity.IsAuthorized($"Bearer {Token}", "").Should().BeFalse();
    }

    // ── CORS origin policy ───────────────────────────────────────────────────

    [Theory]
    [InlineData("chrome-extension://abcdefghijklmnop")]
    [InlineData("moz-extension://1234-5678")]
    public void ResolveAllowedOrigin_AllowsExtensionOrigins(string origin)
        => LocalApiSecurity.ResolveAllowedOrigin(origin).Should().Be(origin);

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://localhost:3000")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveAllowedOrigin_DeniesWebOrigins(string? origin)
        => LocalApiSecurity.ResolveAllowedOrigin(origin).Should().BeNull();

    // ── Token generation ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateToken_ProducesDistinctHighEntropyTokens()
    {
        var a = LocalApiSecurity.GenerateToken();
        var b = LocalApiSecurity.GenerateToken();

        a.Should().HaveLength(64, "256 bits hex-encoded is 64 characters");
        a.Should().NotBe(b);
        a.Should().MatchRegex("^[0-9A-F]+$");
    }
}

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Security;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.OAuth;

/// <summary>
/// Coverage for the HTTP-dependent and browser-walled paths of <see cref="OAuthService"/>
/// that the base <c>OAuthServiceTests</c> cannot reach (AX-QA-009 — lifting the binding
/// OAuth critical namespace so the global coverage floor can ratchet further).
///
/// The service constructs its own <see cref="HttpClient"/> with no injection seam, so the
/// token-exchange / refresh / revocation paths are exercised against a real in-process
/// <see cref="HttpListener"/> stub bound to a free localhost port (the provider config's
/// endpoints point at it). The token-exchange, credential-persistence, scope-building, PKCE
/// and authorization-URL helpers are only reachable through <c>AuthorizeAsync</c>, which
/// launches the system browser via <c>Process.Start</c> (untestable in CI), so they are
/// invoked directly by reflection — the same approach used for other unreachable-by-public-API
/// internals in this initiative.
/// </summary>
public sealed class OAuthServiceHttpFlowTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IDpapiEncryptionService> _encryption;
    private readonly ILogger _logger;

    public OAuthServiceHttpFlowTests()
    {
        _factory = new TestDbContextFactory();
        _encryption = new Mock<IDpapiEncryptionService>();
        // Default symmetric-ish stubs: Encrypt(x) -> "ENC:x". Tests add Decrypt setups.
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => "ENC:" + s);
        _logger = Log.ForContext<OAuthServiceHttpFlowTests>();
    }

    public void Dispose() => _factory.Dispose();

    private OAuthService CreateService(AgentXDbContext? db = null) =>
        new(db ?? _factory.CreateContext(), _encryption.Object, _logger);

    private static OAuthProviderConfig ProviderConfig(
        string tokenEndpoint, string? revocationEndpoint = null) => new()
        {
            ProviderId = "google",
            DisplayName = "Google",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = tokenEndpoint,
            RevocationEndpoint = revocationEndpoint,
            Scopes = "openid profile email",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "http://localhost:8080/callback",
        };

    private void SeedCredential(
        string accessCipher = "DPAPI:access",
        string refreshCipher = "DPAPI:refresh",
        DateTime? expiry = null)
    {
        using var ctx = _factory.CreateContext();
        ctx.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            ProviderId = "google",
            AccessToken = accessCipher,
            RefreshToken = refreshCipher,
            TokenExpiry = expiry ?? DateTime.UtcNow.AddHours(1),
            Scopes = "openid",
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        ctx.SaveChanges();
    }

    private async Task<OAuthCredentialEntity?> ReadCredentialAsync()
    {
        using var ctx = _factory.CreateContext();
        return await ctx.OAuthCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.ProviderId == "google");
    }

    // ── Reflection bridges for the browser-walled internals ─────────────────────

    private static async Task<object?> InvokePrivateAsync(OAuthService svc, string method, params object?[] args)
    {
        var mi = typeof(OAuthService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(OAuthService), method);
        var task = (Task)mi.Invoke(svc, args)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static object? InvokeStatic(string method, params object?[] args)
    {
        var mi = typeof(OAuthService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(OAuthService), method);
        return mi.Invoke(null, args);
    }

    private static object NewTokenResponse(string access, string? refresh, int expiresIn, string? userId = null)
    {
        var t = typeof(OAuthService).GetNestedType("TokenResponse", BindingFlags.NonPublic)!;
        var tr = Activator.CreateInstance(t)!;
        t.GetProperty("AccessToken")!.SetValue(tr, access);
        t.GetProperty("RefreshToken")!.SetValue(tr, refresh);
        t.GetProperty("ExpiresInSeconds")!.SetValue(tr, expiresIn);
        t.GetProperty("UserId")!.SetValue(tr, userId);
        return tr;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  In-process token/revocation stub server
    // ══════════════════════════════════════════════════════════════════════════

    private sealed class StubHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly List<string> _requests = new();
        private readonly object _gate = new();

        public string BaseUrl { get; }
        public string TokenEndpoint => BaseUrl + "token";
        public string RevocationEndpoint => BaseUrl + "revoke";

        /// <summary>Maps a received request body to an (HTTP status, JSON body) response.</summary>
        public Func<string, (int Status, string Body)> Handler { get; set; } = _ => (200, "{}");

        public IReadOnlyList<string> Requests
        {
            get { lock (_gate) return _requests.ToList(); }
        }

        public StubHttpServer()
        {
            BaseUrl = $"http://localhost:{FreePort()}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch { return; } // listener stopped/disposed

                try
                {
                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                        body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    lock (_gate) _requests.Add(body);

                    var (status, responseBody) = Handler(body);
                    var bytes = Encoding.UTF8.GetBytes(responseBody);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                    ctx.Response.Close();
                }
                catch { /* best-effort stub */ }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch { /* already stopped */ }
            _cts.Dispose();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RefreshTokenAsync — success + branch coverage (real HTTP)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RefreshTokenAsync_Success_UpdatesCredentialAndReturnsTrue()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (200,
            """{"access_token":"new-access-token","refresh_token":"new-refresh-token","expires_in":7200,"token_type":"Bearer"}""");

        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("old-refresh-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig(server.TokenEndpoint));

        var result = await service.RefreshTokenAsync("google");

        result.Should().BeTrue();
        var entity = await ReadCredentialAsync();
        entity!.AccessToken.Should().Be("ENC:new-access-token");
        entity.RefreshToken.Should().Be("ENC:new-refresh-token");
        entity.TokenExpiry.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(7200), TimeSpan.FromMinutes(1));
        // The decrypted old refresh token must have been sent to the token endpoint.
        server.Requests.Should().ContainSingle()
            .Which.Should().Contain("grant_type=refresh_token").And.Contain("old-refresh-plain");
    }

    [Fact]
    public async Task RefreshTokenAsync_Success_KeepsOldRefreshToken_AndDefaultsExpiry_WhenServerOmitsThem()
    {
        using var server = new StubHttpServer();
        // No refresh_token and no expires_in → keep old refresh cipher, default 1-hour expiry.
        server.Handler = _ => (200, """{"access_token":"new-access-token","token_type":"Bearer"}""");

        SeedCredential(refreshCipher: "DPAPI:original-refresh");
        _encryption.Setup(e => e.Decrypt("DPAPI:original-refresh")).Returns("original-refresh-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig(server.TokenEndpoint));

        var result = await service.RefreshTokenAsync("google");

        result.Should().BeTrue();
        var entity = await ReadCredentialAsync();
        entity!.AccessToken.Should().Be("ENC:new-access-token");
        entity.RefreshToken.Should().Be("DPAPI:original-refresh"); // unchanged
        entity.TokenExpiry.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(3600), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_OnNonSuccessStatus()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (400, """{"error":"invalid_grant"}""");

        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("refresh-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig(server.TokenEndpoint));

        (await service.RefreshTokenAsync("google")).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_WhenResponseHasNoAccessToken()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (200, """{"token_type":"Bearer","expires_in":3600}""");

        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("refresh-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig(server.TokenEndpoint));

        (await service.RefreshTokenAsync("google")).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_WhenRefreshTokenDecryptThrows()
    {
        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh"))
            .Throws(new InvalidOperationException("decrypt failure"));

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig("http://localhost:1/token"));

        (await service.RefreshTokenAsync("google")).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_WhenDecryptedRefreshTokenIsEmpty()
    {
        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns(string.Empty);

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig("http://localhost:1/token"));

        (await service.RefreshTokenAsync("google")).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_OnNetworkError()
    {
        // Endpoint that refuses the connection → HttpRequestException caught → false.
        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("refresh-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig("http://localhost:9/token"));

        (await service.RefreshTokenAsync("google")).Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GetAccessTokenAsync — successful auto-refresh path (real HTTP)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAccessTokenAsync_RefreshesExpiredToken_AndReturnsNewAccessToken()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (200, """{"access_token":"refreshed-access","token_type":"Bearer","expires_in":3600}""");

        // Expires within the 5-minute refresh buffer → triggers auto-refresh.
        SeedCredential(expiry: DateTime.UtcNow.AddMinutes(2));
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("refresh-plain");
        _encryption.Setup(e => e.Decrypt("ENC:refreshed-access")).Returns("refreshed-access-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig(server.TokenEndpoint));

        var token = await service.GetAccessTokenAsync("google");

        token.Should().Be("refreshed-access-plain");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RevokeAsync — server-side revocation success (real HTTP)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeAsync_PerformsServerSideRevocation_ThenDeletesCredential()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (200, "{}");

        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:access")).Returns("access-plain");

        using var service = CreateService();
        service.RegisterProvider(ProviderConfig(server.TokenEndpoint, revocationEndpoint: server.RevocationEndpoint));

        await service.RevokeAsync("google");

        (await ReadCredentialAsync()).Should().BeNull();
        server.Requests.Should().ContainSingle().Which.Should().Contain("token=access-plain");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ExchangeCodeForTokensAsync (reflection — walled behind AuthorizeAsync)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExchangeCodeForTokens_Success_ReturnsTokenResponse()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (200,
            """{"access_token":"exchanged-access","refresh_token":"exchanged-refresh","expires_in":3600,"user_id":"u-9"}""");

        using var service = CreateService();
        var config = ProviderConfig(server.TokenEndpoint);

        var result = await InvokePrivateAsync(
            service, "ExchangeCodeForTokensAsync", config, "auth-code", config.RedirectUri, "verifier-xyz");

        result.Should().NotBeNull();
        var access = result!.GetType().GetProperty("AccessToken")!.GetValue(result);
        access.Should().Be("exchanged-access");
        // The PKCE verifier and authorization code must reach the token endpoint.
        server.Requests.Should().ContainSingle()
            .Which.Should().Contain("grant_type=authorization_code").And.Contain("code_verifier=verifier-xyz");
    }

    [Fact]
    public async Task ExchangeCodeForTokens_Throws_OnNonSuccessStatus()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (400, """{"error":"invalid_grant"}""");

        using var service = CreateService();
        var config = ProviderConfig(server.TokenEndpoint);

        Func<Task> act = () => InvokePrivateAsync(
            service, "ExchangeCodeForTokensAsync", config, "bad-code", config.RedirectUri, "verifier");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Token exchange failed");
    }

    [Fact]
    public async Task ExchangeCodeForTokens_Throws_WhenResponseHasNoAccessToken()
    {
        using var server = new StubHttpServer();
        server.Handler = _ => (200, """{"token_type":"Bearer"}""");

        using var service = CreateService();
        var config = ProviderConfig(server.TokenEndpoint);

        Func<Task> act = () => InvokePrivateAsync(
            service, "ExchangeCodeForTokensAsync", config, "code", config.RedirectUri, "verifier");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("did not contain an access token");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PersistCredentialAsync (reflection — walled behind AuthorizeAsync)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PersistCredential_InsertsNewCredential()
    {
        using var service = CreateService();
        var tokenResponse = NewTokenResponse("access-plain", "refresh-plain", 7200, userId: "user-xyz");

        var result = (OAuthCredential)(await InvokePrivateAsync(
            service, "PersistCredentialAsync", "google", tokenResponse, "openid profile"))!;

        result.ProviderId.Should().Be("google");
        result.AccessToken.Should().Be("access-plain");
        result.UserId.Should().Be("user-xyz");

        var entity = await ReadCredentialAsync();
        entity.Should().NotBeNull();
        entity!.AccessToken.Should().Be("ENC:access-plain");
        entity.RefreshToken.Should().Be("ENC:refresh-plain");
        entity.Scopes.Should().Be("openid profile");
        entity.UserId.Should().Be("user-xyz");
        entity.TokenExpiry.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(7200), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task PersistCredential_UpdatesExistingCredential_PreservingCreatedAt()
    {
        var originalCreatedAt = DateTime.UtcNow.AddDays(-3);
        using (var ctx = _factory.CreateContext())
        {
            ctx.OAuthCredentials.Add(new OAuthCredentialEntity
            {
                ProviderId = "google",
                AccessToken = "DPAPI:old-access",
                RefreshToken = "DPAPI:old-refresh",
                TokenExpiry = DateTime.UtcNow.AddHours(-1),
                Scopes = "openid",
                UserId = "old-user",
                CreatedAt = originalCreatedAt,
                UpdatedAt = originalCreatedAt,
            });
            await ctx.SaveChangesAsync();
        }

        using var service = CreateService();
        // No refresh token in the response → encrypts an empty string for the refresh slot.
        var tokenResponse = NewTokenResponse("new-access", refresh: null, expiresIn: 0, userId: "new-user");

        var result = (OAuthCredential)(await InvokePrivateAsync(
            service, "PersistCredentialAsync", "google", tokenResponse, "openid email"))!;

        result.CreatedAt.Should().BeCloseTo(originalCreatedAt, TimeSpan.FromSeconds(1));

        using var verify = _factory.CreateContext();
        (await verify.OAuthCredentials.CountAsync(c => c.ProviderId == "google")).Should().Be(1);
        var entity = await verify.OAuthCredentials.AsNoTracking().FirstAsync(c => c.ProviderId == "google");
        entity.AccessToken.Should().Be("ENC:new-access");
        entity.RefreshToken.Should().Be("ENC:"); // Encrypt(string.Empty)
        entity.Scopes.Should().Be("openid email");
        entity.UserId.Should().Be("new-user");
        entity.CreatedAt.Should().BeCloseTo(originalCreatedAt, TimeSpan.FromSeconds(1)); // preserved
        entity.TokenExpiry.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(3600), TimeSpan.FromMinutes(1)); // default
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DecryptEntity failure branches (via GetCredentialAsync)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCredentialAsync_Throws_WhenAccessTokenDecryptFails()
    {
        SeedCredential(accessCipher: "DPAPI:bad-access");
        _encryption.Setup(e => e.Decrypt("DPAPI:bad-access"))
            .Throws(new CryptographicExceptionStub());

        using var service = CreateService();

        Func<Task> act = () => service.GetCredentialAsync("google");
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("decrypt the access token");
    }

    [Fact]
    public async Task GetCredentialAsync_Throws_WhenRefreshTokenDecryptFails()
    {
        SeedCredential(accessCipher: "DPAPI:ok-access", refreshCipher: "DPAPI:bad-refresh");
        _encryption.Setup(e => e.Decrypt("DPAPI:ok-access")).Returns("access-plain");
        _encryption.Setup(e => e.Decrypt("DPAPI:bad-refresh"))
            .Throws(new CryptographicExceptionStub());

        using var service = CreateService();

        Func<Task> act = () => service.GetCredentialAsync("google");
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("decrypt the refresh token");
    }

    private sealed class CryptographicExceptionStub : Exception { }

    // ══════════════════════════════════════════════════════════════════════════
    //  AuthorizeAsync — early validation (runs before the browser launch)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuthorizeAsync_Throws_WhenRedirectUriIsNotLocalhost()
    {
        using var service = CreateService();
        service.RegisterProvider(ProviderConfig("https://oauth2.googleapis.com/token"));

        Func<Task> act = () => service.AuthorizeAsync("google", redirectUri: "https://evil.example.com/callback");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("redirectUri");
    }

    [Fact]
    public async Task AuthorizeAsync_Throws_WhenProviderNotRegistered()
    {
        using var service = CreateService();

        Func<Task> act = () => service.AuthorizeAsync("not-registered");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("No OAuth provider configuration");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BuildScopes (reflection — static helper)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildScopes_ReturnsDefault_WhenAdditionalIsBlank()
    {
        var result = (string)InvokeStatic("BuildScopes", "openid profile", "   ")!;
        result.Should().Be("openid profile");
    }

    [Fact]
    public void BuildScopes_ReturnsAdditional_WhenDefaultIsBlank()
    {
        var result = (string)InvokeStatic("BuildScopes", "", "calendar.read")!;
        result.Should().Be("calendar.read");
    }

    [Fact]
    public void BuildScopes_MergesAndDeduplicates()
    {
        var result = (string)InvokeStatic("BuildScopes", "openid, profile", "profile, email")!;
        var scopes = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        scopes.Should().BeEquivalentTo("openid", "profile", "email");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BuildAuthorizationUrl (reflection — static helper)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildAuthorizationUrl_IncludesPkceStateAndExtraParameters()
    {
        var config = new OAuthProviderConfig
        {
            ProviderId = "google",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            ClientId = "client-123",
            ExtraAuthParameters = new Dictionary<string, string>
            {
                ["access_type"] = "offline",
                ["prompt"] = "consent",
            },
        };

        var url = (string)InvokeStatic(
            "BuildAuthorizationUrl", config, "openid profile", "http://localhost:8080/callback",
            "state-value", "challenge-value")!;

        url.Should().StartWith("https://accounts.google.com/o/oauth2/v2/auth?");
        url.Should().Contain("client_id=client-123");
        url.Should().Contain("response_type=code");
        url.Should().Contain("code_challenge=challenge-value");
        url.Should().Contain("code_challenge_method=S256");
        url.Should().Contain("state=state-value");
        url.Should().Contain("access_type=offline");
        url.Should().Contain("prompt=consent");
    }

    [Fact]
    public void BuildAuthorizationUrl_OmitsExtraParameters_WhenNull()
    {
        var config = new OAuthProviderConfig
        {
            ProviderId = "microsoft",
            AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            ClientId = "ms-client",
            ExtraAuthParameters = null,
        };

        var url = (string)InvokeStatic(
            "BuildAuthorizationUrl", config, "openid", "http://localhost:8080/callback",
            "s", "c")!;

        url.Should().Contain("client_id=ms-client");
        url.Should().Contain("code_challenge_method=S256");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CSRF / PKCE helpers (reflection — static)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateState_ProducesDecodable32ByteValue()
    {
        var state = (string)InvokeStatic("GenerateState")!;
        state.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(state).Should().HaveCount(32);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesBase64UrlValue()
    {
        var verifier = (string)InvokeStatic("GenerateCodeVerifier")!;
        verifier.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        verifier.Should().HaveLength(43); // 32 bytes Base64Url, unpadded
    }

    [Fact]
    public void ComputeCodeChallenge_IsBase64UrlSha256OfVerifier()
    {
        const string verifier = "test-verifier-value";
        var challenge = (string)InvokeStatic("ComputeCodeChallenge", verifier)!;

        var expected = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        challenge.Should().Be(expected);
    }

    [Fact]
    public void Base64UrlEncode_StripsPaddingAndReplacesUrlUnsafeChars()
    {
        // Bytes chosen so standard Base64 yields '+' and '/' and padding.
        var bytes = new byte[] { 0xFB, 0xFF, 0xFE, 0xFF, 0xFF };
        var encoded = (string)InvokeStatic("Base64UrlEncode", bytes)!;

        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Dispose
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Dispose_IsIdempotent_AndDisposesRefreshLocks()
    {
        var service = CreateService();
        service.RegisterProvider(ProviderConfig("http://localhost:9/token"));

        // Create a per-provider refresh lock so the disposal loop has an entry to dispose.
        SeedCredential();
        _encryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("refresh-plain");
        await service.RefreshTokenAsync("google");

        service.Dispose();
        Action second = () => service.Dispose(); // guard: _isDisposed short-circuits
        second.Should().NotThrow();
    }
}

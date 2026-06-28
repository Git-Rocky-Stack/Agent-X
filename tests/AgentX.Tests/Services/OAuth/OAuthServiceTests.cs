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
/// Unit tests for <see cref="OAuthService"/>.
/// Uses an in-memory SQLite database via <see cref="TestDbContextFactory"/>
/// and mocks <see cref="IDpapiEncryptionService"/> to isolate service logic
/// from DPAPI platform dependencies.
/// </summary>
public sealed class OAuthServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IDpapiEncryptionService> _mockEncryption;
    private readonly ILogger _logger;

    public OAuthServiceTests()
    {
        _factory = new TestDbContextFactory();
        _mockEncryption = new Mock<IDpapiEncryptionService>();
        _logger = Log.ForContext<OAuthServiceTests>();
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Creates an OAuthService with a fresh database context and the mocked encryption service.
    /// Provider configs are NOT registered by default — tests that need them must call
    /// <see cref="OAuthService.RegisterProvider"/> explicitly.
    /// </summary>
    private OAuthService CreateService(AgentXDbContext? db = null)
    {
        var context = db ?? _factory.CreateContext();
        return new OAuthService(context, _mockEncryption.Object, _logger);
    }

    /// <summary>
    /// Registers a Google provider config on the service for tests that need
    /// a provider registered (e.g. refresh, authorize).
    /// </summary>
    private static void RegisterGoogleProvider(OAuthService service)
    {
        service.RegisterProvider(new OAuthProviderConfig
        {
            ProviderId = "google",
            DisplayName = "Google",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            RevocationEndpoint = "https://oauth2.googleapis.com/revoke",
            Scopes = "openid profile email",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "http://localhost:8080/callback"
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Constructor argument guards
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_Throws_WhenDbIsNull()
    {
        var act = () => new OAuthService(null!, _mockEncryption.Object, _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Constructor_Throws_WhenEncryptionIsNull()
    {
        using var context = _factory.CreateContext();

        var act = () => new OAuthService(context, null!, _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("encryption");
    }

    [Fact]
    public void Constructor_Throws_WhenLoggerIsNull()
    {
        using var context = _factory.CreateContext();

        var act = () => new OAuthService(context, _mockEncryption.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetCredentialAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCredentialAsync_ReturnsNull_WhenNoCredentialExists()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var result = await service.GetCredentialAsync("google");

        // Assert
        result.Should().BeNull("no credential has been stored for the provider");
    }

    [Fact]
    public async Task GetCredentialAsync_ReturnsDecryptedCredential_WhenExists()
    {
        // Arrange
        var db = _factory.CreateContext();
        var expiry = DateTime.UtcNow.AddHours(1);
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        var updatedAt = DateTime.UtcNow;

        db.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            ProviderId = "google",
            AccessToken = "DPAPI:encrypted-access",
            RefreshToken = "DPAPI:encrypted-refresh",
            TokenExpiry = expiry,
            Scopes = "openid profile email",
            UserId = "google-sub-123",
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        });
        await db.SaveChangesAsync();

        // Mock decryption to return plaintext values
        _mockEncryption.Setup(e => e.Decrypt("DPAPI:encrypted-access")).Returns("plain-access-token");
        _mockEncryption.Setup(e => e.Decrypt("DPAPI:encrypted-refresh")).Returns("plain-refresh-token");

        using var service = CreateService(db);

        // Act
        var result = await service.GetCredentialAsync("google");

        // Assert
        result.Should().NotBeNull();
        result!.ProviderId.Should().Be("google");
        result.AccessToken.Should().Be("plain-access-token");
        result.RefreshToken.Should().Be("plain-refresh-token");
        result.TokenExpiry.Should().Be(expiry);
        result.Scopes.Should().Be("openid profile email");
        result.UserId.Should().Be("google-sub-123");
        result.CreatedAt.Should().Be(createdAt);
        result.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public async Task GetCredentialAsync_ThrowsArgumentException_WhenProviderIsWhitespace()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var act = () => service.GetCredentialAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("provider");
    }

    [Fact]
    public async Task GetCredentialAsync_ThrowsArgumentException_WhenProviderIsNull()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var act = () => service.GetCredentialAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetAccessTokenAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAccessTokenAsync_ThrowsInvalidOperationException_WhenNoCredentialExists()
    {
        // Arrange
        using var service = CreateService();

        // Act & Assert
        var act = () => service.GetAccessTokenAsync("google");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No OAuth credential stored*");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsDecryptedToken_WhenCredentialIsValidAndNotExpired()
    {
        // Arrange
        var db = _factory.CreateContext();
        var expiry = DateTime.UtcNow.AddHours(1); // Not expired, not within buffer

        db.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            ProviderId = "google",
            AccessToken = "DPAPI:encrypted-access",
            RefreshToken = "DPAPI:encrypted-refresh",
            TokenExpiry = expiry,
            Scopes = "openid",
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _mockEncryption.Setup(e => e.Decrypt("DPAPI:encrypted-access")).Returns("valid-access-token");
        _mockEncryption.Setup(e => e.Decrypt("DPAPI:encrypted-refresh")).Returns("valid-refresh-token");

        using var service = CreateService(db);

        // Act
        var token = await service.GetAccessTokenAsync("google");

        // Assert
        token.Should().Be("valid-access-token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_AutoRefreshes_WhenTokenIsWithinFiveMinuteBuffer()
    {
        // Arrange — token expires in 3 minutes, which is within the 5-minute refresh buffer.
        // Auto-refresh will attempt to call the token endpoint, which will fail (no HTTP server).
        // However, GetAccessTokenAsync should at least attempt the refresh before throwing.
        var db = _factory.CreateContext();
        var expiry = DateTime.UtcNow.AddMinutes(3); // Within 5-minute buffer

        db.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            ProviderId = "google",
            AccessToken = "DPAPI:encrypted-access",
            RefreshToken = "DPAPI:encrypted-refresh",
            TokenExpiry = expiry,
            Scopes = "openid",
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _mockEncryption.Setup(e => e.Decrypt("DPAPI:encrypted-access")).Returns("expiring-access-token");
        _mockEncryption.Setup(e => e.Decrypt("DPAPI:encrypted-refresh")).Returns("refresh-token");

        using var service = CreateService(db);
        RegisterGoogleProvider(service);

        // Act & Assert — The refresh will fail (no HTTP server), so GetAccessTokenAsync
        // should throw an InvalidOperationException indicating refresh failure.
        var act = () => service.GetAccessTokenAsync("google");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to refresh*");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RefreshTokenAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_WhenNoCredentialExists()
    {
        // Arrange
        using var service = CreateService();
        RegisterGoogleProvider(service);

        // Act
        var result = await service.RefreshTokenAsync("google");

        // Assert
        result.Should().BeFalse("no credential exists to refresh");
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsFalse_WhenNoProviderConfigRegistered()
    {
        // Arrange — insert a credential but don't register a provider config
        var db = _factory.CreateContext();
        db.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            ProviderId = "google",
            AccessToken = "DPAPI:access",
            RefreshToken = "DPAPI:refresh",
            TokenExpiry = DateTime.UtcNow.AddHours(1),
            Scopes = "openid",
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _mockEncryption.Setup(e => e.Decrypt("DPAPI:access")).Returns("access-token");
        _mockEncryption.Setup(e => e.Decrypt("DPAPI:refresh")).Returns("refresh-token");

        using var service = CreateService(db);
        // NOT registering provider — this should make refresh fail gracefully

        // Act
        var result = await service.RefreshTokenAsync("google");

        // Assert
        result.Should().BeFalse("no provider config means refresh cannot proceed");
    }

    [Fact]
    public async Task RefreshTokenAsync_ThrowsArgumentException_WhenProviderIsWhitespace()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var act = () => service.RefreshTokenAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("provider");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RevokeAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeAsync_RemovesCredentialFromDatabase_EvenIfServerRevocationFails()
    {
        // Arrange — insert a credential
        var db = _factory.CreateContext();
        db.OAuthCredentials.Add(new OAuthCredentialEntity
        {
            ProviderId = "google",
            AccessToken = "DPAPI:access",
            RefreshToken = "DPAPI:refresh",
            TokenExpiry = DateTime.UtcNow.AddHours(1),
            Scopes = "openid",
            UserId = "user-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Verify the credential exists
        var before = await db.OAuthCredentials.FirstOrDefaultAsync(c => c.ProviderId == "google");
        before.Should().NotBeNull();

        _mockEncryption.Setup(e => e.Decrypt("DPAPI:access")).Returns("plain-access-token");

        // Use a fresh context for the service (the service creates its own context)
        using var service = CreateService(_factory.CreateContext());
        RegisterGoogleProvider(service);

        // Act — RevokeAsync will try server-side revocation (which will fail because
        // there's no HTTP server), but it should still remove the local credential.
        await service.RevokeAsync("google");

        // Assert — credential should be removed from the database
        var verificationDb = _factory.CreateContext();
        var after = await verificationDb.OAuthCredentials.FirstOrDefaultAsync(c => c.ProviderId == "google");
        after.Should().BeNull("the local credential should be deleted even if server-side revocation fails");
    }

    [Fact]
    public async Task RevokeAsync_DoesNotThrow_WhenNoCredentialExists()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var act = () => service.RevokeAsync("google");

        // Assert — should not throw even with no credential in the database
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAsync_ThrowsArgumentException_WhenProviderIsWhitespace()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var act = () => service.RevokeAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("provider");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RegisterProvider / GetRegisteredProviders
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterProvider_AddsProviderToRegistry()
    {
        // Arrange
        using var service = CreateService();
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Act
        service.RegisterProvider(config);

        // Assert
        var providers = service.GetRegisteredProviders();
        providers.Should().ContainKey("google");
        providers["google"].Should().BeSameAs(config);
    }

    [Fact]
    public void RegisterProvider_ThrowsArgumentNullException_WhenConfigIsNull()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var act = () => service.RegisterProvider(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterProvider_ThrowsArgumentException_WhenProviderIdIsEmpty()
    {
        // Arrange
        using var service = CreateService();
        var config = new OAuthProviderConfig { ProviderId = string.Empty };

        // Act
        var act = () => service.RegisterProvider(config);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ProviderId*");
    }

    [Fact]
    public void RegisterProvider_OverwritesExistingProvider()
    {
        // Arrange
        using var service = CreateService();

        var config1 = new OAuthProviderConfig
        {
            ProviderId = "google",
            DisplayName = "Google Old",
            AuthorizationEndpoint = "https://old.example.com/auth",
            TokenEndpoint = "https://old.example.com/token",
            ClientId = "old-client-id",
            ClientSecret = "old-secret",
            RedirectUri = "http://localhost:8080/callback"
        };

        var config2 = new OAuthProviderConfig
        {
            ProviderId = "google",
            DisplayName = "Google New",
            AuthorizationEndpoint = "https://new.example.com/auth",
            TokenEndpoint = "https://new.example.com/token",
            ClientId = "new-client-id",
            ClientSecret = "new-secret",
            RedirectUri = "http://localhost:8080/callback"
        };

        // Act
        service.RegisterProvider(config1);
        service.RegisterProvider(config2);

        // Assert — second registration should overwrite the first
        var providers = service.GetRegisteredProviders();
        providers["google"].DisplayName.Should().Be("Google New");
        providers["google"].ClientId.Should().Be("new-client-id");
    }
}

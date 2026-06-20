using AgentX.Core.Services.OAuth;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.OAuth;

/// <summary>
/// Unit tests for <see cref="OAuthProviderRegistry"/>.
/// Validates that the factory methods produce correctly configured
/// <see cref="OAuthProviderConfig"/> instances with the expected endpoints,
/// scopes, provider IDs, and extra auth parameters.
/// </summary>
public sealed class OAuthProviderRegistryTests
{
    // ══════════════════════════════════════════════════════════════════════
    //  Provider ID constants
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProviderIdGoogle_IsGoogle()
    {
        OAuthProviderRegistry.ProviderIdGoogle.Should().Be("google");
    }

    [Fact]
    public void ProviderIdMicrosoft_IsMicrosoft()
    {
        OAuthProviderRegistry.ProviderIdMicrosoft.Should().Be("microsoft");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Google provider configuration
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Google_SetsProviderIdToGoogle()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.ProviderId.Should().Be("google");
    }

    [Fact]
    public void Google_SetsDisplayNameToGoogle()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.DisplayName.Should().Be("Google");
    }

    [Fact]
    public void Google_SetsCorrectAuthorizationEndpoint()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.AuthorizationEndpoint.Should().Be("https://accounts.google.com/o/oauth2/v2/auth");
    }

    [Fact]
    public void Google_SetsCorrectTokenEndpoint()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.TokenEndpoint.Should().Be("https://oauth2.googleapis.com/token");
    }

    [Fact]
    public void Google_SetsCorrectRevocationEndpoint()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.RevocationEndpoint.Should().Be("https://oauth2.googleapis.com/revoke");
    }

    [Fact]
    public void Google_SetsClientIdAndSecret()
    {
        // Act
        var config = OAuthProviderRegistry.Google("my-client-id", "my-client-secret", "http://localhost:8080/callback");

        // Assert
        config.ClientId.Should().Be("my-client-id");
        config.ClientSecret.Should().Be("my-client-secret");
    }

    [Fact]
    public void Google_SetsRedirectUri()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:9999/callback");

        // Assert
        config.RedirectUri.Should().Be("http://localhost:9999/callback");
    }

    [Fact]
    public void Google_IncludesScopes()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.Scopes.Should().Contain("openid");
        config.Scopes.Should().Contain("profile");
        config.Scopes.Should().Contain("email");
        config.Scopes.Should().Contain("calendar.readonly");
        config.Scopes.Should().Contain("gmail.readonly");
    }

    [Fact]
    public void Google_IncludesExtraAuthParameters()
    {
        // Act
        var config = OAuthProviderRegistry.Google("client-id", "client-secret", "http://localhost:8080/callback");

        // Assert
        config.ExtraAuthParameters.Should().NotBeNull();
        config.ExtraAuthParameters.Should().ContainKey("access_type");
        config.ExtraAuthParameters!["access_type"].Should().Be("offline");
        config.ExtraAuthParameters.Should().ContainKey("prompt");
        config.ExtraAuthParameters["prompt"].Should().Be("consent");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Microsoft provider configuration
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Microsoft_SetsProviderIdToMicrosoft()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.ProviderId.Should().Be("microsoft");
    }

    [Fact]
    public void Microsoft_SetsDisplayNameToMicrosoft()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.DisplayName.Should().Be("Microsoft");
    }

    [Fact]
    public void Microsoft_SetsCorrectAuthorizationEndpoint()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.AuthorizationEndpoint.Should().Be("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
    }

    [Fact]
    public void Microsoft_SetsCorrectTokenEndpoint()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.TokenEndpoint.Should().Be("https://login.microsoftonline.com/common/oauth2/v2.0/token");
    }

    [Fact]
    public void Microsoft_SetsEmptyRevocationEndpoint()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert — Microsoft does not expose a standard revocation endpoint
        config.RevocationEndpoint.Should().BeEmpty();
    }

    [Fact]
    public void Microsoft_SetsClientIdAndSecret()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("ms-client-id", "ms-client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.ClientId.Should().Be("ms-client-id");
        config.ClientSecret.Should().Be("ms-client-secret");
    }

    [Fact]
    public void Microsoft_SetsRedirectUri()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:9999/callback");

        // Assert
        config.RedirectUri.Should().Be("http://localhost:9999/callback");
    }

    [Fact]
    public void Microsoft_IncludesScopes()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.Scopes.Should().Contain("openid");
        config.Scopes.Should().Contain("profile");
        config.Scopes.Should().Contain("email");
        config.Scopes.Should().Contain("Calendars.Read");
        config.Scopes.Should().Contain("Mail.Read");
        config.Scopes.Should().Contain("User.Read");
    }

    [Fact]
    public void Microsoft_IncludesExtraAuthParameters_WithSelectAccount()
    {
        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", "common", "http://localhost:8080/callback");

        // Assert
        config.ExtraAuthParameters.Should().NotBeNull();
        config.ExtraAuthParameters.Should().ContainKey("prompt");
        config.ExtraAuthParameters!["prompt"].Should().Be("select_account");
    }

    [Fact]
    public void Microsoft_UsesCustomTenantId()
    {
        // Arrange
        var tenantId = "9188040d-6c67-4c5b-b112-36a35b8fbbbe";

        // Act
        var config = OAuthProviderRegistry.Microsoft("client-id", "client-secret", tenantId, "http://localhost:8080/callback");

        // Assert
        config.AuthorizationEndpoint.Should().Contain(tenantId);
        config.TokenEndpoint.Should().Contain(tenantId);
    }
}

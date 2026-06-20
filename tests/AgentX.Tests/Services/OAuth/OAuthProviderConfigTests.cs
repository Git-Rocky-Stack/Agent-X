using AgentX.Core.Services.OAuth;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.OAuth;

/// <summary>
/// Unit tests for <see cref="OAuthProviderConfig"/>.
/// Validates construction, default values, and property assignment.
/// </summary>
public sealed class OAuthProviderConfigTests
{
    // ══════════════════════════════════════════════════════════════════════
    //  Default values
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Default_ProviderId_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.ProviderId.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_DisplayName_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.DisplayName.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_AuthorizationEndpoint_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.AuthorizationEndpoint.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_TokenEndpoint_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.TokenEndpoint.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_RevocationEndpoint_IsNull()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.RevocationEndpoint.Should().BeNull();
    }

    [Fact]
    public void Default_Scopes_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.Scopes.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_ClientId_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.ClientId.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_ClientSecret_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.ClientSecret.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_RedirectUri_IsEmptyString()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.RedirectUri.Should().Be(string.Empty);
    }

    [Fact]
    public void Default_ExtraAuthParameters_IsNull()
    {
        // Arrange & Act
        var config = new OAuthProviderConfig();

        // Assert
        config.ExtraAuthParameters.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Construction with all fields
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CanCreate_WithAllFieldsPopulated()
    {
        // Arrange
        var extraParams = new Dictionary<string, string>
        {
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        // Act
        var config = new OAuthProviderConfig
        {
            ProviderId = "google",
            DisplayName = "Google Calendar",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            RevocationEndpoint = "https://oauth2.googleapis.com/revoke",
            Scopes = "openid profile email",
            ClientId = "my-client-id.apps.googleusercontent.com",
            ClientSecret = "my-client-secret",
            RedirectUri = "http://localhost:8080/callback",
            ExtraAuthParameters = extraParams
        };

        // Assert — every field should retain its assigned value
        config.ProviderId.Should().Be("google");
        config.DisplayName.Should().Be("Google Calendar");
        config.AuthorizationEndpoint.Should().Be("https://accounts.google.com/o/oauth2/v2/auth");
        config.TokenEndpoint.Should().Be("https://oauth2.googleapis.com/token");
        config.RevocationEndpoint.Should().Be("https://oauth2.googleapis.com/revoke");
        config.Scopes.Should().Be("openid profile email");
        config.ClientId.Should().Be("my-client-id.apps.googleusercontent.com");
        config.ClientSecret.Should().Be("my-client-secret");
        config.RedirectUri.Should().Be("http://localhost:8080/callback");
        config.ExtraAuthParameters.Should().NotBeNull();
        config.ExtraAuthParameters.Should().HaveCount(2);
        config.ExtraAuthParameters!["access_type"].Should().Be("offline");
        config.ExtraAuthParameters["prompt"].Should().Be("consent");
    }

    [Fact]
    public void CanCreate_WithMinimalFields()
    {
        // Act — only ProviderId is required for RegisterProvider validation
        var config = new OAuthProviderConfig
        {
            ProviderId = "custom-provider"
        };

        // Assert
        config.ProviderId.Should().Be("custom-provider");
        config.DisplayName.Should().Be(string.Empty);
        config.AuthorizationEndpoint.Should().Be(string.Empty);
        config.TokenEndpoint.Should().Be(string.Empty);
        config.RevocationEndpoint.Should().BeNull();
        config.Scopes.Should().Be(string.Empty);
        config.ClientId.Should().Be(string.Empty);
        config.ClientSecret.Should().Be(string.Empty);
        config.RedirectUri.Should().Be(string.Empty);
        config.ExtraAuthParameters.Should().BeNull();
    }

    [Fact]
    public void CanCreate_WithNullRevocationEndpoint()
    {
        // Act
        var config = new OAuthProviderConfig
        {
            ProviderId = "microsoft",
            RevocationEndpoint = null
        };

        // Assert — Microsoft provider does not support server-side revocation
        config.RevocationEndpoint.Should().BeNull();
    }

    [Fact]
    public void CanCreate_WithEmptyRevocationEndpoint()
    {
        // Act — Some providers use empty string instead of null for "no revocation"
        var config = new OAuthProviderConfig
        {
            ProviderId = "microsoft",
            RevocationEndpoint = string.Empty
        };

        // Assert
        config.RevocationEndpoint.Should().BeEmpty();
    }
}

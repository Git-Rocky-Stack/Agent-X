using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Calendar;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Unit tests for <see cref="GoogleCalendarProvider"/> and
/// <see cref="OutlookCalendarProvider"/> — validates construction,
/// property defaults, and provider ID values.
/// Full API integration tests require live OAuth credentials and are
/// covered by the manual test scenarios in the spec.
/// </summary>
public sealed class CalendarProviderTests : IDisposable
{
    private readonly Mock<IOAuthService> _mockOAuth;
    private readonly ILogger _logger;

    public CalendarProviderTests()
    {
        _mockOAuth = new Mock<IOAuthService>();
        _logger = new LoggerConfiguration().CreateLogger();
    }

    public void Dispose()
    {
        // ILogger from LoggerConfiguration is IDisposable.
        (_logger as IDisposable)?.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GoogleCalendarProvider
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GoogleCalendarProvider_ProviderId_ReturnsGoogle()
    {
        var provider = new GoogleCalendarProvider(_mockOAuth.Object, _logger);
        provider.ProviderId.Should().Be("google");
    }

    [Fact]
    public void GoogleCalendarProvider_ThrowsOnNullOAuthService()
    {
        var act = () => new GoogleCalendarProvider(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GoogleCalendarProvider_ThrowsOnNullLogger()
    {
        var act = () => new GoogleCalendarProvider(_mockOAuth.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  OutlookCalendarProvider
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OutlookCalendarProvider_ProviderId_ReturnsMicrosoft()
    {
        var provider = new OutlookCalendarProvider(_mockOAuth.Object, _logger);
        provider.ProviderId.Should().Be("microsoft");
    }

    [Fact]
    public void OutlookCalendarProvider_ThrowsOnNullOAuthService()
    {
        var act = () => new OutlookCalendarProvider(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OutlookCalendarProvider_ThrowsOnNullLogger()
    {
        var act = () => new OutlookCalendarProvider(_mockOAuth.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CalendarPlugin:RegisterProvidersAsync integration
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CalendarPlugin_RegisterProvidersAsync_WithGoogleCredentials_RegistersGoogleProvider()
    {
        // Arrange
        var plugin = new CalendarPlugin();
        var services = new ServiceCollection();
        services.AddSingleton(_mockOAuth.Object);
        var sp = services.BuildServiceProvider();

        var mockContext = new Mock<IPluginContext>();
        mockContext.Setup(c => c.Services).Returns(sp);
        mockContext.Setup(c => c.PluginDataPath).Returns(Path.Combine(Path.GetTempPath(), $"agentx-test-{Guid.NewGuid():N}"));
        mockContext.Setup(c => c.Logger).Returns(_logger);
        Directory.CreateDirectory(mockContext.Object.PluginDataPath);

        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync(new OAuthCredential
            {
                ProviderId = "google",
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                TokenExpiry = DateTime.UtcNow.AddHours(1),
            });
        _mockOAuth.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        // Act
        await plugin.InitializeAsync(mockContext.Object);

        // Assert
        var providers = plugin.GetProviders();
        providers.Should().HaveCount(1);
        providers[0].ProviderId.Should().Be("google");

        // Cleanup
        plugin.Dispose();
        sp.Dispose();
        try { Directory.Delete(mockContext.Object.PluginDataPath, true); } catch { }
    }

    [Fact]
    public async Task CalendarPlugin_RegisterProvidersAsync_WithMicrosoftCredentials_RegistersOutlookProvider()
    {
        // Arrange
        var plugin = new CalendarPlugin();
        var services = new ServiceCollection();
        services.AddSingleton(_mockOAuth.Object);
        var sp = services.BuildServiceProvider();

        var mockContext = new Mock<IPluginContext>();
        mockContext.Setup(c => c.Services).Returns(sp);
        mockContext.Setup(c => c.PluginDataPath).Returns(Path.Combine(Path.GetTempPath(), $"agentx-test-{Guid.NewGuid():N}"));
        mockContext.Setup(c => c.Logger).Returns(_logger);
        Directory.CreateDirectory(mockContext.Object.PluginDataPath);

        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync((OAuthCredential?)null);
        _mockOAuth.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync(new OAuthCredential
            {
                ProviderId = "microsoft",
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                TokenExpiry = DateTime.UtcNow.AddHours(1),
            });

        // Act
        await plugin.InitializeAsync(mockContext.Object);

        // Assert
        var providers = plugin.GetProviders();
        providers.Should().HaveCount(1);
        providers[0].ProviderId.Should().Be("microsoft");

        // Cleanup
        plugin.Dispose();
        sp.Dispose();
        try { Directory.Delete(mockContext.Object.PluginDataPath, true); } catch { }
    }

    [Fact]
    public async Task CalendarPlugin_RegisterProvidersAsync_WithBothCredentials_RegistersBothProviders()
    {
        // Arrange
        var plugin = new CalendarPlugin();
        var services = new ServiceCollection();
        services.AddSingleton(_mockOAuth.Object);
        var sp = services.BuildServiceProvider();

        var mockContext = new Mock<IPluginContext>();
        mockContext.Setup(c => c.Services).Returns(sp);
        mockContext.Setup(c => c.PluginDataPath).Returns(Path.Combine(Path.GetTempPath(), $"agentx-test-{Guid.NewGuid():N}"));
        mockContext.Setup(c => c.Logger).Returns(_logger);
        Directory.CreateDirectory(mockContext.Object.PluginDataPath);

        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync(new OAuthCredential
            {
                ProviderId = "google",
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                TokenExpiry = DateTime.UtcNow.AddHours(1),
            });
        _mockOAuth.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync(new OAuthCredential
            {
                ProviderId = "microsoft",
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                TokenExpiry = DateTime.UtcNow.AddHours(1),
            });

        // Act
        await plugin.InitializeAsync(mockContext.Object);

        // Assert
        var providers = plugin.GetProviders();
        providers.Should().HaveCount(2);
        providers.Should().Contain(p => p.ProviderId == "google");
        providers.Should().Contain(p => p.ProviderId == "microsoft");

        // Cleanup
        plugin.Dispose();
        sp.Dispose();
        try { Directory.Delete(mockContext.Object.PluginDataPath, true); } catch { }
    }

    [Fact]
    public async Task CalendarPlugin_RegisterProvidersAsync_WithNoCredentials_RegistersNoProviders()
    {
        // Arrange
        var plugin = new CalendarPlugin();
        var services = new ServiceCollection();
        services.AddSingleton(_mockOAuth.Object);
        var sp = services.BuildServiceProvider();

        var mockContext = new Mock<IPluginContext>();
        mockContext.Setup(c => c.Services).Returns(sp);
        mockContext.Setup(c => c.PluginDataPath).Returns(Path.Combine(Path.GetTempPath(), $"agentx-test-{Guid.NewGuid():N}"));
        mockContext.Setup(c => c.Logger).Returns(_logger);
        Directory.CreateDirectory(mockContext.Object.PluginDataPath);

        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync((OAuthCredential?)null);
        _mockOAuth.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        // Act
        await plugin.InitializeAsync(mockContext.Object);

        // Assert
        plugin.GetProviders().Should().BeEmpty();

        // Cleanup
        plugin.Dispose();
        sp.Dispose();
        try { Directory.Delete(mockContext.Object.PluginDataPath, true); } catch { }
    }
}
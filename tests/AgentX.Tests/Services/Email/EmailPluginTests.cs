using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Email;

/// <summary>
/// Unit tests for EmailPlugin lifecycle and EmailProvider construction.
/// </summary>
public sealed class EmailPluginTests : IDisposable
{
    private readonly Mock<IOAuthService> _oauthService;
    private readonly Mock<IPluginContext> _mockContext;
    private readonly ILogger _logger;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDataPath;
    private readonly EmailPlugin _plugin;

    public EmailPluginTests()
    {
        _oauthService = new Mock<IOAuthService>(MockBehavior.Loose);
        _logger = new LoggerConfiguration().CreateLogger();
        _tempDataPath = Path.Combine(Path.GetTempPath(), $"agentx-email-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataPath);

        var services = new ServiceCollection();
        services.AddSingleton(_oauthService.Object);
        _serviceProvider = services.BuildServiceProvider();

        _mockContext = new Mock<IPluginContext>();
        _mockContext.Setup(c => c.Services).Returns(_serviceProvider);
        _mockContext.Setup(c => c.PluginDataPath).Returns(_tempDataPath);
        _mockContext.Setup(c => c.Logger).Returns(_logger);

        _plugin = new EmailPlugin();
    }

    public void Dispose()
    {
        _plugin.Dispose();
        _serviceProvider.Dispose();
        try { if (Directory.Exists(_tempDataPath)) Directory.Delete(_tempDataPath, true); }
        catch { /* best effort */ }
    }

    // ── Plugin properties ────────────────────────────────────────────────────

    [Fact]
    public void Id_ReturnsCorrectValue()
    {
        _plugin.Id.Should().Be("com.agentx.email");
    }

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        _plugin.Name.Should().Be("Email Connector");
    }

    [Fact]
    public void Type_IsDataConnector()
    {
        _plugin.Type.Should().Be(PluginType.DataConnector);
    }

    [Fact]
    public void Version_IsSet()
    {
        _plugin.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Author_IsSet()
    {
        _plugin.Author.Should().NotBeNullOrEmpty();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_SetsUpContext()
    {
        await _plugin.InitializeAsync(_mockContext.Object);
        // No exception = success
    }

    [Fact]
    public async Task InitializeAsync_NullContext_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _plugin.InitializeAsync(null!));
    }

    [Fact]
    public async Task ActivateAsync_WithoutInitialize_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _plugin.ActivateAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task ActivateAsync_WithNoProviders_StartsTimer()
    {
        _oauthService.Setup(o => o.GetCredentialAsync("google")).ReturnsAsync((OAuthCredential?)null);
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft")).ReturnsAsync((OAuthCredential?)null);

        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();

        _plugin.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivateAsync_WithGoogleCredential_RegistersGmailProvider()
    {
        _oauthService.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync(new OAuthCredential { AccessToken = "test-token", TokenExpiry = DateTime.UtcNow.AddHours(1) });
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();

        _plugin.Providers.Should().HaveCount(1);
        _plugin.Providers[0].ProviderId.Should().Be("google");
    }

    [Fact]
    public async Task ActivateAsync_WithBothCredentials_RegistersBothProviders()
    {
        _oauthService.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync(new OAuthCredential { AccessToken = "g-token", TokenExpiry = DateTime.UtcNow.AddHours(1) });
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync(new OAuthCredential { AccessToken = "m-token", TokenExpiry = DateTime.UtcNow.AddHours(1) });

        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();

        _plugin.Providers.Should().HaveCount(2);
        _plugin.Providers.Select(p => p.ProviderId).Should().Contain(["google", "microsoft"]);
    }

    [Fact]
    public async Task DeactivateAsync_StopsSync()
    {
        _oauthService.Setup(o => o.GetCredentialAsync("google")).ReturnsAsync((OAuthCredential?)null);
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft")).ReturnsAsync((OAuthCredential?)null);

        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();
        await _plugin.DeactivateAsync();

        // No exception = success
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _plugin.Dispose();
        _plugin.Dispose(); // second call should not throw
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSettings_ReturnsDefaultWhenNotInitialized()
    {
        // Default settings before initialization
        var settings = _plugin.GetSettings();
        settings.SyncIntervalMinutes.Should().Be(10);
    }

    [Fact]
    public async Task UpdateSettings_PersistsToDisk()
    {
        _oauthService.Setup(o => o.GetCredentialAsync("google")).ReturnsAsync((OAuthCredential?)null);
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft")).ReturnsAsync((OAuthCredential?)null);

        await _plugin.InitializeAsync(_mockContext.Object);

        var newSettings = new EmailSyncSettings { SyncIntervalMinutes = 30 };
        _plugin.UpdateSettings(newSettings);

        var loaded = _plugin.GetSettings();
        loaded.SyncIntervalMinutes.Should().Be(30);

        // Verify file on disk
        var settingsPath = Path.Combine(_tempDataPath, "email-sync-settings.json");
        File.Exists(settingsPath).Should().BeTrue();
    }

    [Fact]
    public void UpdateSettings_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _plugin.UpdateSettings(null!));
    }

    // ── Provider construction ──────────────────────────────────────────────────

    [Fact]
    public void GmailProvider_Constructs_WithValidArgs()
    {
        var oauth = new Mock<IOAuthService>(MockBehavior.Loose);
        var logger = new LoggerConfiguration().CreateLogger();
        var provider = new GmailProvider(oauth.Object, logger, "gmail.readonly");

        provider.ProviderId.Should().Be("google");
    }

    [Fact]
    public void GmailProvider_NullOAuth_Throws()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        Assert.Throws<ArgumentNullException>(() => new GmailProvider(null!, logger, "scope"));
    }

    [Fact]
    public void OutlookEmailProvider_Constructs_WithValidArgs()
    {
        var oauth = new Mock<IOAuthService>(MockBehavior.Loose);
        var logger = new LoggerConfiguration().CreateLogger();
        var provider = new OutlookEmailProvider(oauth.Object, logger, "Mail.Read");

        provider.ProviderId.Should().Be("microsoft");
    }

    [Fact]
    public void OutlookEmailProvider_NullOAuth_Throws()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        Assert.Throws<ArgumentNullException>(() => new OutlookEmailProvider(null!, logger, "scope"));
    }
}
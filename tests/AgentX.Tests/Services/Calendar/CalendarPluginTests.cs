using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Unit tests for <see cref="CalendarPlugin"/> lifecycle and provider management.
/// Tests initialization, activation, deactivation, disposal, provider registration,
/// and sync settings management.
/// </summary>
public sealed class CalendarPluginTests : IDisposable
{
    private readonly CalendarPlugin _plugin;
    private readonly Mock<IOAuthService> _mockOAuth;
    private readonly Mock<IPluginContext> _mockContext;
    private readonly ILogger _logger;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDataPath;

    public CalendarPluginTests()
    {
        _plugin = new CalendarPlugin();
        _mockOAuth = new Mock<IOAuthService>();
        _logger = new LoggerConfiguration().CreateLogger();

        // Create a temporary directory for plugin data.
        _tempDataPath = Path.Combine(Path.GetTempPath(), $"agentx-calendar-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataPath);

        // Set up the scoped service provider with IOAuthService.
        var services = new ServiceCollection();
        services.AddSingleton(_mockOAuth.Object);
        _serviceProvider = services.BuildServiceProvider();

        // Set up the plugin context.
        _mockContext = new Mock<IPluginContext>();
        _mockContext.Setup(c => c.Services).Returns(_serviceProvider);
        _mockContext.Setup(c => c.PluginDataPath).Returns(_tempDataPath);
        _mockContext.Setup(c => c.Logger).Returns(_logger);
    }

    public void Dispose()
    {
        _plugin.Dispose();
        _serviceProvider.Dispose();

        // Clean up temp directory.
        try
        {
            if (Directory.Exists(_tempDataPath))
                Directory.Delete(_tempDataPath, true);
        }
        catch
        {
            // Swallow cleanup failures in tests.
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  IPlugin metadata
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Id_ReturnsComAgentXCalendar()
    {
        _plugin.Id.Should().Be("com.agentx.calendar");
    }

    [Fact]
    public void Name_ReturnsCalendarConnector()
    {
        _plugin.Name.Should().Be("Calendar Connector");
    }

    [Fact]
    public void Version_Returns1_0_0()
    {
        _plugin.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void Author_ReturnsAgentX()
    {
        _plugin.Author.Should().Be("AgentX");
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        _plugin.Description.Should().NotBeEmpty();
    }

    [Fact]
    public void Type_ReturnsDataConnector()
    {
        _plugin.Type.Should().Be(PluginType.DataConnector);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  InitializeAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InitializeAsync_WithValidContext_ResolvesOAuthService()
    {
        // Act
        await _plugin.InitializeAsync(_mockContext.Object);

        // Assert — OAuth service should be resolved (accessible through internal method)
        _plugin.GetOAuthService().Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_WithNoOAuthService_StillSucceeds()
    {
        // Arrange — context with no IOAuthService
        var emptyServices = new ServiceCollection().BuildServiceProvider();
        _mockContext.Setup(c => c.Services).Returns(emptyServices);

        // Act
        await _plugin.InitializeAsync(_mockContext.Object);

        // Assert — OAuth service is null but plugin still initializes
        _plugin.GetOAuthService().Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_ThrowsOnNullContext()
    {
        // Act
        var act = () => _plugin.InitializeAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InitializeAsync_LoadsDefaultSyncSettings_WhenNoFileExists()
    {
        // Act
        await _plugin.InitializeAsync(_mockContext.Object);

        // Assert
        var settings = _plugin.GetSettings();
        settings.Should().NotBeNull();
        settings.SyncIntervalMinutes.Should().Be(15);
        settings.DaysFutureToSync.Should().Be(30);
        settings.DaysPastToSync.Should().Be(90);
    }

    [Fact]
    public async Task InitializeAsync_ChecksGoogleCredentials()
    {
        // Arrange
        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync(new OAuthCredential
            {
                ProviderId = "google",
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                TokenExpiry = DateTime.UtcNow.AddHours(1),
                Scopes = "calendar.readonly",
                UserId = "user-123",
            });

        // Act
        await _plugin.InitializeAsync(_mockContext.Object);

        // Assert — OAuth service was queried for Google credentials
        _mockOAuth.Verify(o => o.GetCredentialAsync("google"), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_ChecksMicrosoftCredentials()
    {
        // Arrange
        _mockOAuth.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        // Act
        await _plugin.InitializeAsync(_mockContext.Object);

        // Assert — OAuth service was queried for Microsoft credentials
        _mockOAuth.Verify(o => o.GetCredentialAsync("microsoft"), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ActivateAsync / DeactivateAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActivateAsync_AfterInitialize_Succeeds()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        // Act
        await _plugin.ActivateAsync();

        // Assert — no exception means success
    }

    [Fact]
    public async Task ActivateAsync_IsIdempotent()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        // Act — activate twice
        await _plugin.ActivateAsync();
        await _plugin.ActivateAsync();

        // Assert — no exception means success
    }

    [Fact]
    public async Task DeactivateAsync_AfterActivate_Succeeds()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();

        // Act
        await _plugin.DeactivateAsync();

        // Assert — no exception means success
    }

    [Fact]
    public async Task DeactivateAsync_WhenNotActivated_IsNoOp()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        // Act — deactivate without activating first
        await _plugin.DeactivateAsync();

        // Assert — no exception means success
    }

    [Fact]
    public async Task FullLifecycle_InitializeActivateDeactivate_Succeeds()
    {
        // Act
        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();
        await _plugin.DeactivateAsync();

        // Assert — no exception means full lifecycle works
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Dispose
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_IsIdempotent()
    {
        // Act — dispose twice
        _plugin.Dispose();
        _plugin.Dispose();

        // Assert — no exception means success
    }

    [Fact]
    public async Task Dispose_AfterFullLifecycle_Succeeds()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();
        await _plugin.DeactivateAsync();

        // Act
        _plugin.Dispose();

        // Assert — no exception means success
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Provider management
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddProvider_AddsToProviderList()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var mockProvider = new Mock<ICalendarProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns("google");

        // Act
        _plugin.AddProvider(mockProvider.Object);

        // Assert
        _plugin.GetProviders().Should().HaveCount(1);
        _plugin.GetProviders()[0].ProviderId.Should().Be("google");
    }

    [Fact]
    public async Task AddProvider_ReplacesExistingProviderWithSameId()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var provider1 = new Mock<ICalendarProvider>();
        provider1.Setup(p => p.ProviderId).Returns("google");
        var provider2 = new Mock<ICalendarProvider>();
        provider2.Setup(p => p.ProviderId).Returns("google");

        // Act
        _plugin.AddProvider(provider1.Object);
        _plugin.AddProvider(provider2.Object);

        // Assert — should replace, not duplicate
        _plugin.GetProviders().Should().HaveCount(1);
    }

    [Fact]
    public async Task AddProvider_ThrowsOnNullProvider()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        // Act
        var act = () => _plugin.AddProvider(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveProvider_RemovesExistingProvider()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var mockProvider = new Mock<ICalendarProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns("google");
        _plugin.AddProvider(mockProvider.Object);

        // Act
        _plugin.RemoveProvider("google");

        // Assert
        _plugin.GetProviders().Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveProvider_WithNonExistentId_IsNoOp()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        // Act — remove provider that doesn't exist
        _plugin.RemoveProvider("nonexistent");

        // Assert — no exception, empty provider list
        _plugin.GetProviders().Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Settings management
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateSettingsAsync_UpdatesSettingsAndPersists()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var newSettings = new CalendarSyncSettings
        {
            SyncIntervalMinutes = 30,
            DaysFutureToSync = 60,
            DaysPastToSync = 180,
        };

        // Act
        await _plugin.UpdateSettingsAsync(newSettings);

        // Assert
        var settings = _plugin.GetSettings();
        settings.SyncIntervalMinutes.Should().Be(30);
        settings.DaysFutureToSync.Should().Be(60);
        settings.DaysPastToSync.Should().Be(180);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ThrowsOnNullSettings()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        // Act
        var act = () => _plugin.UpdateSettingsAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SettingsPersistence_RoundTripsToFile()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var customSettings = new CalendarSyncSettings
        {
            SyncIntervalMinutes = 45,
            ConflictResolution = "LocalWins",
            EnabledCalendars = new Dictionary<string, bool> { ["cal-1"] = true },
        };
        await _plugin.UpdateSettingsAsync(customSettings);

        // Act — create a new plugin and initialize (should load from file)
        var plugin2 = new CalendarPlugin();
        await plugin2.InitializeAsync(_mockContext.Object);

        // Assert
        var loaded = plugin2.GetSettings();
        loaded.SyncIntervalMinutes.Should().Be(45);
        loaded.ConflictResolution.Should().Be("LocalWins");
        loaded.EnabledCalendars.Should().ContainKey("cal-1");

        // Cleanup
        plugin2.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SyncCompleted event
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TriggerSyncAsync_WithNoProviders_ReturnsZeroResult()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        await _plugin.ActivateAsync();

        // Act
        var result = await _plugin.TriggerSyncAsync();

        // Assert
        result.Should().NotBeNull();
        result!.ItemsAdded.Should().Be(0);
        result.ItemsFailed.Should().Be(0);
    }

    [Fact]
    public async Task TriggerSyncAsync_WithProvider_FetchesEvents()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);

        var mockProvider = new Mock<ICalendarProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns("google");
        mockProvider.Setup(p => p.ListCalendarsAsync(default))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new() { Id = "cal-1", Name = "Work", SourceProvider = "google" },
            });
        mockProvider.Setup(p => p.GetEventsAsync("cal-1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default))
            .ReturnsAsync((new List<CalEvent>
            {
                new() { Id = "evt-1", Title = "Test Event", SourceProvider = "google", CalendarId = "cal-1" },
            }, (string?)null));

        _plugin.AddProvider(mockProvider.Object);

        // Enable the calendar in settings
        var settings = _plugin.GetSettings();
        settings.EnabledCalendars["cal-1"] = true;
        await _plugin.UpdateSettingsAsync(settings);

        // Act
        var result = await _plugin.TriggerSyncAsync();

        // Assert
        result.Should().NotBeNull();
        // Events are counted as "skipped" until CalendarEventProcessor is implemented
        result!.ItemsSkipped.Should().Be(1);
        result.ItemsAdded.Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CalendarService (ICalendarService implementation)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CalendarService_IsConnectedAsync_WithNoCredentials_ReturnsFalse()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var service = new CalendarService(_plugin, _logger);
        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync((OAuthCredential?)null);
        _mockOAuth.Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        // Act
        var connected = await service.IsConnectedAsync();

        // Assert
        connected.Should().BeFalse();
    }

    [Fact]
    public async Task CalendarService_IsConnectedAsync_WithGoogleCredentials_ReturnsTrue()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var service = new CalendarService(_plugin, _logger);
        _mockOAuth.Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync(new OAuthCredential
            {
                ProviderId = "google",
                AccessToken = "test",
                RefreshToken = "test",
                TokenExpiry = DateTime.UtcNow.AddHours(1),
            });

        // Act
        var connected = await service.IsConnectedAsync();

        // Assert
        connected.Should().BeTrue();
    }

    [Fact]
    public async Task CalendarService_GetSyncSettingsAsync_ReturnsCurrentSettings()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var service = new CalendarService(_plugin, _logger);

        // Act
        var settings = await service.GetSyncSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        settings.SyncIntervalMinutes.Should().Be(15);
    }

    [Fact]
    public async Task CalendarService_UpdateSyncSettingsAsync_UpdatesPluginSettings()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var service = new CalendarService(_plugin, _logger);
        var newSettings = new CalendarSyncSettings { SyncIntervalMinutes = 60 };

        // Act
        await service.UpdateSyncSettingsAsync(newSettings);

        // Assert
        var loaded = await service.GetSyncSettingsAsync();
        loaded.SyncIntervalMinutes.Should().Be(60);
    }

    [Fact]
    public async Task CalendarService_ListAvailableCalendarsAsync_WithNoProviders_ReturnsEmpty()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var service = new CalendarService(_plugin, _logger);

        // Act
        var calendars = await service.ListAvailableCalendarsAsync();

        // Assert
        calendars.Should().BeEmpty();
    }

    [Fact]
    public async Task CalendarService_UpdateSyncSettingsAsync_ThrowsOnNull()
    {
        // Arrange
        await _plugin.InitializeAsync(_mockContext.Object);
        var service = new CalendarService(_plugin, _logger);

        // Act
        var act = () => service.UpdateSyncSettingsAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

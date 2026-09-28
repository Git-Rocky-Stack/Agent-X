using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Inbox;
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
/// Integration tests verifying the full Calendar sync pipeline:
/// CalendarSyncService -> CalendarEventProcessor -> IInboxService.UpsertExternalAsync
/// and the CalendarPlugin orchestrating providers + sync together.
/// </summary>
public sealed class CalendarIntegrationTests : IDisposable
{
    private readonly Mock<IInboxService> _inboxService;
    private readonly Mock<IOAuthService> _oauthService;
    private readonly Mock<ICalendarProvider> _googleProvider;
    private readonly Mock<ICalendarProvider> _outlookProvider;
    private readonly CalendarEventProcessor _processor;
    private readonly CalendarSyncService _syncService;
    private readonly string _tempDir;
    private readonly ILogger _logger;

    public CalendarIntegrationTests()
    {
        _inboxService = new Mock<IInboxService>(MockBehavior.Strict);
        _oauthService = new Mock<IOAuthService>(MockBehavior.Loose);
        _googleProvider = new Mock<ICalendarProvider>(MockBehavior.Strict);
        _outlookProvider = new Mock<ICalendarProvider>(MockBehavior.Strict);
        _logger = new LoggerConfiguration().CreateLogger();

        _processor = new CalendarEventProcessor(_logger);
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentx-calendar-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _syncService = new CalendarSyncService(
            _inboxService.Object, _processor, _logger, _tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    // -- Helpers ------------------------------------------------------------------

    private static CalEvent CreateEvent(
        string id = "evt-1",
        string title = "Sprint Planning",
        string calendarId = "cal-primary",
        string sourceProvider = "google")
    {
        return new CalEvent
        {
            Id = id,
            Title = title,
            Start = DateTime.UtcNow.AddDays(1),
            End = DateTime.UtcNow.AddDays(1).AddHours(1),
            CalendarId = calendarId,
            SourceProvider = sourceProvider,
            Description = "Weekly sprint planning meeting",
            Location = "Room 42",
            Organizer = "boss@example.com",
            Attendees =
            [
                new() { Email = "alice@example.com", DisplayName = "Alice", ResponseStatus = "accepted", IsOrganizer = false },
                new() { Email = "bob@example.com", DisplayName = "Bob", ResponseStatus = "declined", IsOrganizer = false },
            ],
        };
    }

    private static CalendarSyncSettings DefaultSettings(params string[] enabledCalendarIds)
    {
        var settings = new CalendarSyncSettings();
        foreach (var id in enabledCalendarIds)
            settings.EnabledCalendars[id] = true;
        return settings;
    }

    private static ExternalTriageResult Created(InboxItemEntity item) =>
        new(item, ExternalTriageOutcome.Created);

    private InboxItemEntity CreateInboxItem(long id = 1, DateTime? addedAt = null, DateTime? processedAt = null)
    {
        return new InboxItemEntity
        {
            Id = id,
            FilePath = $@"C:\Temp\AgentX\ExternalItems\com.agentx.calendar\event-{id}.txt",
            Status = "accepted",
            AddedAt = addedAt ?? DateTime.UtcNow,
            ProcessedAt = processedAt ?? DateTime.UtcNow,
            SourcePluginId = "com.agentx.calendar",
            SourceCategory = "calendar_event",
            ExternalId = $"google:cal-primary:evt-{id}",
        };
    }

    private void SetupGoogleProvider(params CalEvent[] events)
    {
        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new() { Id = "cal-primary", Name = "Primary" },
            });
        _googleProvider
            .Setup(p => p.GetEventsAsync("cal-primary", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((events.ToList() as IReadOnlyList<CalEvent>, (string?)"delta-token-1"));
    }

    private void SetupOutlookProvider(params CalEvent[] events)
    {
        _outlookProvider.SetupGet(p => p.ProviderId).Returns("microsoft");
        _outlookProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new() { Id = "outlook-cal-1", Name = "Calendar" },
            });
        _outlookProvider
            .Setup(p => p.GetEventsAsync("outlook-cal-1", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((events.ToList() as IReadOnlyList<CalEvent>, (string?)"ms-delta-1"));
    }

    // -- CalendarSyncService integration tests ------------------------------------

    [Fact]
    public async Task SyncAsync_SingleProvider_ProcessesAllEventsThroughInbox()
    {
        // Arrange
        var evt1 = CreateEvent("evt-1", "Sprint Planning");
        var evt2 = CreateEvent("evt-2", "Code Review");
        SetupGoogleProvider(evt1, evt2);

        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync((string fn, string ft, string st, string? su, string sp, string? sc,
                           string eid, string? cp, string ct) =>
                Created(CreateInboxItem(long.Parse(eid[^1].ToString()), processedAt: DateTime.UtcNow)));

        var settings = DefaultSettings("cal-primary");

        // Act
        var result = await _syncService.SyncAsync([_googleProvider.Object], settings);

        // Assert
        result.Should().NotBeNull();
        result.ItemsFailed.Should().Be(0);
        result.IsSuccess.Should().BeTrue();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);

        // Verify UpsertExternalAsync was called for each event
        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), "calendar-connector",
            It.IsAny<string?>(), "com.agentx.calendar", "calendar_event",
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAsync_MultiProvider_AggregatesResults()
    {
        // Arrange
        var googleEvent = CreateEvent("g-1", "Google Meeting", "cal-primary", "google");
        var outlookEvent = CreateEvent("o-1", "Outlook Meeting", "outlook-cal-1", "microsoft");
        SetupGoogleProvider(googleEvent);
        SetupOutlookProvider(outlookEvent);

        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(Created(CreateInboxItem()));

        var settings = DefaultSettings("cal-primary", "outlook-cal-1");

        // Act
        var result = await _syncService.SyncAsync(
            [_googleProvider.Object, _outlookProvider.Object], settings);

        // Assert
        result.Should().NotBeNull();
        result.ItemsFailed.Should().Be(0);
        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAsync_NoEnabledCalendars_SkipsProvider()
    {
        // Arrange
        SetupGoogleProvider(CreateEvent());

        var settings = new CalendarSyncSettings(); // no enabled calendars

        // Act
        var result = await _syncService.SyncAsync([_googleProvider.Object], settings);

        // Assert
        result.ItemsAdded.Should().Be(0);
        result.ItemsSkipped.Should().Be(0);
        result.ItemsFailed.Should().Be(0);
        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_DeltaTokens_PersistedAcrossSyncs()
    {
        // Arrange
        SetupGoogleProvider(CreateEvent());
        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(Created(CreateInboxItem()));

        var settings = DefaultSettings("cal-primary");

        // Act - first sync
        await _syncService.SyncAsync([_googleProvider.Object], settings);

        // Assert - delta token file created
        var deltaPath = Path.Combine(_tempDir, "calendar-delta-tokens.json");
        File.Exists(deltaPath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(deltaPath);
        content.Should().Contain("google:cal-primary");
        content.Should().Contain("delta-token-1");
    }

    [Fact]
    public async Task SyncAsync_ProviderError_ContinuesToNextProvider()
    {
        // Arrange
        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API error"));

        var outlookEvent = CreateEvent("o-1", "Outlook Meeting", "outlook-cal-1", "microsoft");
        SetupOutlookProvider(outlookEvent);

        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(Created(CreateInboxItem()));

        var settings = DefaultSettings("cal-primary", "outlook-cal-1");

        // Act
        var result = await _syncService.SyncAsync(
            [_googleProvider.Object, _outlookProvider.Object], settings);

        // Assert - Outlook still processed despite Google failure
        result.ItemsFailed.Should().Be(1); // Google provider failure
        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Once); // Outlook event
    }

    [Fact]
    public async Task SyncAsync_CancellationRequested_StopsProcessing()
    {
        // Arrange
        SetupGoogleProvider(CreateEvent(), CreateEvent("evt-2", "Standup"));
        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(Created(CreateInboxItem()));

        var settings = DefaultSettings("cal-primary");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _syncService.SyncAsync([_googleProvider.Object], settings, cts.Token));
    }

    [Fact]
    public async Task SyncAsync_EventProcessingError_ContinuesToNextEvent()
    {
        // Arrange
        var evt1 = CreateEvent("evt-1", "Sprint Planning");
        var evt2 = CreateEvent("evt-2", "Code Review");
        SetupGoogleProvider(evt1, evt2);

        var callCount = 0;
        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("DB error on first event");
                return Created(CreateInboxItem(2));
            });

        var settings = DefaultSettings("cal-primary");

        // Act
        var result = await _syncService.SyncAsync([_googleProvider.Object], settings);

        // Assert - first event failed, second processed
        result.ItemsFailed.Should().Be(1);
        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    // -- CalendarEventProcessor + InboxService pipeline tests ---------------------

    [Fact]
    public void Processor_ProducesCorrectExternalId_ForGoogleEvent()
    {
        var evt = CreateEvent("abc123", "Meeting", "work-cal", "google");
        var (fileName, fileType, sourceType, sourceUrl, sourcePluginId, sourceCategory,
             externalId, contentPreview, contentText) = _processor.ConvertToInboxParameters(evt);

        externalId.Should().Be("google:work-cal:abc123");
        sourcePluginId.Should().Be("com.agentx.calendar");
        sourceCategory.Should().Be("calendar_event");
        sourceType.Should().Be("calendar-connector");
        fileType.Should().Be("CalendarEvent");
    }

    [Fact]
    public void Processor_ProducesCorrectExternalId_ForOutlookEvent()
    {
        var evt = CreateEvent("xyz789", "Meeting", "outlook-cal", "microsoft");
        var (fileName, fileType, sourceType, sourceUrl, sourcePluginId, sourceCategory,
             externalId, contentPreview, contentText) = _processor.ConvertToInboxParameters(evt);

        externalId.Should().Be("microsoft:outlook-cal:xyz789");
    }

    [Fact]
    public void Processor_ContentIncludesAttendeeStatusSymbols()
    {
        var evt = CreateEvent();
        var (_, _, _, _, _, _, _, _, contentText) = _processor.ConvertToInboxParameters(evt);

        contentText.Should().Contain("[+]");
        contentText.Should().Contain("[-]");
    }

    // -- CalendarPlugin integration tests -----------------------------------------

    [Fact]
    public async Task CalendarPlugin_SyncCycle_WithInboxService_ProcessesEvents()
    {
        // Arrange
        var plugin = new CalendarPlugin();

        var mockInbox = new Mock<IInboxService>(MockBehavior.Loose);
        mockInbox
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(Created(new InboxItemEntity
            {
                Id = 1,
                FilePath = @"C:\Temp\test.txt",
                Status = "accepted",
                AddedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow,
            }));

        var services = new ServiceCollection();
        services.AddSingleton(_oauthService.Object);
        services.AddSingleton(mockInbox.Object);

        var context = new Mock<IPluginContext>();
        context.SetupGet(c => c.Services).Returns(services.BuildServiceProvider());
        context.SetupGet(c => c.PluginDataPath).Returns(_tempDir);
        context.SetupGet(c => c.Logger).Returns(_logger);

        _oauthService
            .Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync((OAuthCredential?)null);
        _oauthService
            .Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        // Act - should not throw
        await plugin.InitializeAsync(context.Object);
        await plugin.ActivateAsync();

        // Cleanup
        await plugin.DeactivateAsync();
        plugin.Dispose();
    }

    [Fact]
    public async Task CalendarPlugin_FetchOnlyFallback_WorksWhenNoInboxService()
    {
        // Arrange
        var plugin = new CalendarPlugin();
        var context = new Mock<IPluginContext>();
        var services = new ServiceCollection();
        services.AddSingleton(_oauthService.Object);
        // No IInboxService registered - should trigger fetch-only fallback

        context.SetupGet(c => c.Services).Returns(services.BuildServiceProvider());
        context.SetupGet(c => c.PluginDataPath).Returns(_tempDir);
        context.SetupGet(c => c.Logger).Returns(_logger);

        _oauthService
            .Setup(o => o.GetCredentialAsync("google"))
            .ReturnsAsync((OAuthCredential?)null);
        _oauthService
            .Setup(o => o.GetCredentialAsync("microsoft"))
            .ReturnsAsync((OAuthCredential?)null);

        // Act - should not throw
        await plugin.InitializeAsync(context.Object);
        await plugin.ActivateAsync();

        await plugin.DeactivateAsync();
        plugin.Dispose();
    }

    // -- SyncSettings integration --------------------------------------------------

    [Fact]
    public async Task SyncAsync_SettingsDaysRange_ArePassedToProvider()
    {
        // Arrange
        var capturedStart = DateTime.MinValue;
        var capturedEnd = DateTime.MinValue;

        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new() { Id = "cal-primary", Name = "Primary" },
            });
        _googleProvider
            .Setup(p => p.GetEventsAsync("cal-primary", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTime, DateTime, string?, CancellationToken>((_, start, end, _, _) =>
            {
                capturedStart = start;
                capturedEnd = end;
            })
            .ReturnsAsync((new List<CalEvent>() as IReadOnlyList<CalEvent>, (string?)"delta-1"));

        var settings = DefaultSettings("cal-primary");
        settings.DaysPastToSync = 30;
        settings.DaysFutureToSync = 14;

        // Act
        await _syncService.SyncAsync([_googleProvider.Object], settings);

        // Assert - date range roughly matches settings (allowing for execution time skew)
        capturedStart.Should().BeCloseTo(DateTime.UtcNow.AddDays(-30), TimeSpan.FromMinutes(1));
        capturedEnd.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SyncAsync_DisabledCalendar_SkipsEvents()
    {
        // Arrange
        SetupGoogleProvider(CreateEvent());

        var settings = new CalendarSyncSettings();
        settings.EnabledCalendars["cal-primary"] = false; // disabled

        // Act
        var result = await _syncService.SyncAsync([_googleProvider.Object], settings);

        // Assert - no events processed because calendar disabled
        result.ItemsAdded.Should().Be(0);
        result.ItemsSkipped.Should().Be(0);
        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Never);
    }

    // -- Per-calendar isolation, sync positions, connector settings ----------------

    private string DeltaTokenPath => Path.Combine(_tempDir, "calendar-delta-tokens.json");

    private Task WriteStoredTokenAsync(string key, string token) =>
        File.WriteAllTextAsync(DeltaTokenPath, $"{{ \"{key}\": \"{token}\" }}");

    private async Task<Dictionary<string, string>> ReadStoredTokensAsync()
    {
        if (!File.Exists(DeltaTokenPath))
            return new Dictionary<string, string>();

        var json = await File.ReadAllTextAsync(DeltaTokenPath);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }

    private void SetupUpsertSucceeds() =>
        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(Created(CreateInboxItem()));

    private void SetupGoogleEvents(string calendarId, string? expectedToken, string? returnedToken, params CalEvent[] events) =>
        _googleProvider
            .Setup(p => p.GetEventsAsync(calendarId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                expectedToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((events.ToList() as IReadOnlyList<CalEvent>, returnedToken));

    [Fact]
    public async Task SyncAsync_OneCalendarFails_TheOtherCalendarsStillSync()
    {
        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new() { Id = "cal-shared", Name = "Revoked share" },
                new() { Id = "cal-primary", Name = "Primary" },
            });
        _googleProvider
            .Setup(p => p.GetEventsAsync("cal-shared", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden"));
        SetupGoogleEvents("cal-primary", null, "tok-primary", CreateEvent());
        SetupUpsertSucceeds();

        var result = await _syncService.SyncAsync([_googleProvider.Object], DefaultSettings("cal-shared", "cal-primary"));

        result.ItemsFailed.Should().Be(1);
        result.ItemsAdded.Should().Be(1, "the calendar after the failing one is still synced");
        (await ReadStoredTokensAsync()).Should().ContainKey("google:cal-primary");
    }

    [Fact]
    public async Task SyncAsync_ProviderRejectedTheStoredToken_TheTokenIsDropped()
    {
        // The provider was handed the stored token, fell back to a full read and returned no
        // token; resending the rejected one every cycle would repeat the fallback forever.
        await WriteStoredTokenAsync("google:cal-primary", "stale-token");
        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { new() { Id = "cal-primary", Name = "Primary" } });
        SetupGoogleEvents("cal-primary", "stale-token", null, CreateEvent());
        SetupUpsertSucceeds();

        var result = await _syncService.SyncAsync([_googleProvider.Object], DefaultSettings("cal-primary"));

        result.ItemsAdded.Should().Be(1);
        (await ReadStoredTokensAsync()).Should().NotContainKey("google:cal-primary");
    }

    [Fact]
    public async Task SyncAsync_NewToken_ReplacesTheStoredToken()
    {
        await WriteStoredTokenAsync("google:cal-primary", "tok-1");
        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { new() { Id = "cal-primary", Name = "Primary" } });
        SetupGoogleEvents("cal-primary", "tok-1", "tok-2", CreateEvent());
        SetupUpsertSucceeds();

        await _syncService.SyncAsync([_googleProvider.Object], DefaultSettings("cal-primary"));

        (await ReadStoredTokensAsync()).Should().Contain("google:cal-primary", "tok-2");
    }

    [Fact]
    public async Task SyncAsync_EventsFailed_KeepsThePreviousTokenSoTheyAreRetried()
    {
        await WriteStoredTokenAsync("google:cal-primary", "tok-1");
        _googleProvider.SetupGet(p => p.ProviderId).Returns("google");
        _googleProvider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { new() { Id = "cal-primary", Name = "Primary" } });
        SetupGoogleEvents("cal-primary", "tok-1", "tok-2", CreateEvent());
        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disk full"));

        var result = await _syncService.SyncAsync([_googleProvider.Object], DefaultSettings("cal-primary"));

        result.ItemsFailed.Should().Be(1);
        (await ReadStoredTokensAsync()).Should().Contain("google:cal-primary", "tok-1");
    }

    [Fact]
    public async Task SyncAsync_ChangedEvent_IsCountedAsUpdated()
    {
        SetupGoogleProvider(CreateEvent());
        _inboxService
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(new ExternalTriageResult(CreateInboxItem(), ExternalTriageOutcome.Updated));

        var result = await _syncService.SyncAsync([_googleProvider.Object], DefaultSettings("cal-primary"));

        result.ItemsUpdated.Should().Be(1);
        result.ItemsAdded.Should().Be(0);
        result.ItemsSkipped.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_AppliesTheConnectorSettingsToTheIndexedText()
    {
        SetupGoogleProvider(CreateEvent());
        SetupUpsertSucceeds();
        var settings = DefaultSettings("cal-primary");
        settings.IncludeDescriptions = false;
        settings.IncludeAttendeeDetails = false;

        await _syncService.SyncAsync([_googleProvider.Object], settings);

        _inboxService.Verify(i => i.UpsertExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(),
            It.Is<string>(text => text.Contains("Sprint Planning")
                && !text.Contains("Weekly sprint planning meeting")
                && !text.Contains("alice@example.com"))),
            Times.Once);
    }
}


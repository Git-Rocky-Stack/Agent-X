using System.Globalization;
using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Events that leave a calendar: the removal notice <see cref="CalendarEventProcessor"/> writes
/// into a stored copy, and how <see cref="CalendarSyncService"/> finds the events to retire
/// (deletion notices from incremental reads, and stored events a full read of the range no
/// longer lists) without touching events that merely aged out of the synced range.
/// </summary>
public sealed class CalendarEventRemovalTests : IDisposable
{
    private const string Pid = CalendarEventProcessor.PluginId;

    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
    private readonly Mock<IInboxService> _inbox = new(MockBehavior.Strict);
    private readonly Mock<ICalendarProvider> _provider = new(MockBehavior.Strict);
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "agentx-cal-removal-" + Guid.NewGuid().ToString("N"));
    private readonly CalendarSyncService _sync;

    public CalendarEventRemovalTests()
    {
        Directory.CreateDirectory(_dataDir);
        _sync = new CalendarSyncService(_inbox.Object, new CalendarEventProcessor(_logger), _logger, _dataDir);
        _provider.SetupGet(p => p.ProviderId).Returns("google");
        _provider
            .Setup(p => p.ListCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { new() { Id = "cal-1", Name = "Work" } });
    }

    public void Dispose()
    {
        (_logger as IDisposable)?.Dispose();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    // -- Removal notice ----------------------------------------------------------

    [Theory]
    [InlineData("Calendar: Standup (2026-04-15 09:30)", "Calendar: Standup (2026-04-15 09:30, removed)")]
    [InlineData("Calendar: Offsite (2026-04-20)", "Calendar: Offsite (2026-04-20, removed)")]
    [InlineData("Calendar: Review (2026-04-15 09:30, cancelled)", "Calendar: Review (2026-04-15 09:30, removed)")]
    [InlineData("Calendar: Q2 (plan) (2026-04-15 09:30)", "Calendar: Q2 (plan) (2026-04-15 09:30, removed)")]
    [InlineData("Calendar: Standup (2026-04-15 09:30, removed)", "Calendar: Standup (2026-04-15 09:30, removed)")]
    [InlineData("Imported by an older build", "Imported by an older build (removed)")]
    public void MarkRemovedName_KeepsTitleAndDate_AndTagsTheRemoval(string stored, string expected)
    {
        CalendarEventProcessor.MarkRemovedName(stored).Should().Be(expected);
        CalendarEventProcessor.IsMarkedRemoved(expected).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "Removed")]
    [InlineData("2026-04-15 09:30 - 10:00, at Room 4", "Removed, 2026-04-15 09:30 - 10:00, at Room 4")]
    [InlineData("Cancelled, 2026-04-15 09:30 - 10:00", "Removed, 2026-04-15 09:30 - 10:00")]
    [InlineData("Removed, 2026-04-15 09:30 - 10:00", "Removed, 2026-04-15 09:30 - 10:00")]
    public void MarkRemovedPreview_LeadsWithRemoved(string? stored, string expected)
    {
        CalendarEventProcessor.MarkRemovedPreview(stored).Should().Be(expected);
    }

    [Fact]
    public void MarkRemovedText_PutsTheReasonUnderTheTitle_ReplacingACancellation()
    {
        const string stored = "Title: Review\nStatus: Cancelled\nStart: 2026-04-15 09:30 UTC\n";

        var deleted = CalendarEventProcessor.MarkRemovedText(stored, CalendarRemovalReason.Deleted);
        var unlisted = CalendarEventProcessor.MarkRemovedText(stored, CalendarRemovalReason.NoLongerListed);

        deleted.Should().Be("Title: Review\nStatus: Deleted or cancelled in the calendar\nStart: 2026-04-15 09:30 UTC\n");
        unlisted.Should().StartWith("Title: Review\nStatus: No longer in the calendar's synced date range");
        unlisted.Should().EndWith("Start: 2026-04-15 09:30 UTC\n");
    }

    [Fact]
    public void MarkRemoved_IsIdempotent_SoARepeatedNoticeWritesNothing()
    {
        var stored = new ExternalItemContent(
            "Calendar: Review (2026-04-15 09:30)", "2026-04-15 09:30 - 10:00", "Title: Review\r\nStart: 2026-04-15 09:30 UTC\r\n");

        var once = CalendarEventProcessor.MarkRemoved(stored, CalendarRemovalReason.Deleted);
        var twice = CalendarEventProcessor.MarkRemoved(once, CalendarRemovalReason.Deleted);

        twice.Should().Be(once);
        once.ContentText.Should().Be("Title: Review\r\nStatus: Deleted or cancelled in the calendar\r\nStart: 2026-04-15 09:30 UTC\r\n");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TryReadStartFromStoredName_ReadsBackTheStartTheProcessorWrote(bool isAllDay, bool isCancelled)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A Buddhist-calendar culture must not change what is written or read back.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var calEvent = new CalEvent
            {
                Id = "evt-1",
                Title = "Planning (Q3)",
                Start = isAllDay ? new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 4, 15, 9, 30, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
                IsAllDay = isAllDay,
                IsCancelled = isCancelled,
                SourceProvider = "google",
                CalendarId = "cal-1",
            };
            var name = new CalendarEventProcessor(_logger).ConvertToInboxParameters(calEvent).FileName;

            CalendarEventProcessor.TryReadStartFromStoredName(name, out var start).Should().BeTrue(name);
            start.Should().Be(calEvent.Start);
            start.Kind.Should().Be(DateTimeKind.Utc);
            CalendarEventProcessor.TryReadStartFromStoredName(CalendarEventProcessor.MarkRemovedName(name), out var markedStart).Should().BeTrue();
            markedStart.Should().Be(calEvent.Start);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Email: Hello")]
    [InlineData("Calendar: Review (2026-13-45 09:30)")]
    [InlineData("")]
    public void TryReadStartFromStoredName_RejectsOtherNames(string name)
    {
        CalendarEventProcessor.TryReadStartFromStoredName(name, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("google:cal-1:abc_20260415T150000Z", true)]
    [InlineData("google:cal-1:abc_20260415", true)]
    [InlineData("google:cal-1:abc_R20260501T150000", false)]
    [InlineData("google:cal-1:abc_R20260501T150000_20260508T150000Z", false)]
    [InlineData("google:cal-1:abcd_20260415", false)]
    [InlineData("google:cal-1:abc_", false)]
    [InlineData("google:cal-1:abc", false)]
    public void IsOccurrenceOf_MatchesGoogleOccurrenceIdsOnly(string candidate, bool expected)
    {
        CalendarEventProcessor.IsOccurrenceOf(candidate, "google:cal-1:abc").Should().Be(expected);
    }

    // -- Sync: deletion notices ----------------------------------------------------

    [Fact]
    public async Task SyncAsync_DeletionNotice_RetiresTheStoredEventInsteadOfStoringIt()
    {
        ProviderReturns(changesOnly: true, Notice("gone-1"));
        NoStoredRows("google:cal-1:gone-1_");
        Func<ExternalItemContent, ExternalItemContent>? notice = null;
        _inbox
            .Setup(i => i.RemoveExternalAsync(Pid, "google:cal-1:gone-1", It.IsAny<Func<ExternalItemContent, ExternalItemContent>>()))
            .Callback((string _, string _, Func<ExternalItemContent, ExternalItemContent> mark) => notice = mark)
            .ReturnsAsync(new ExternalRemovalResult(ExternalRemovalOutcome.Marked, 7));

        var result = await _sync.SyncAsync([_provider.Object], Settings());

        result.ItemsRemoved.Should().Be(1);
        result.ItemsFailed.Should().Be(0);
        notice!(new ExternalItemContent("Calendar: X (2026-04-15 09:30)", null, "Title: X\n"))
            .ContentText.Should().Contain("Status: Deleted or cancelled in the calendar");
    }

    [Fact]
    public async Task SyncAsync_DeletedSeries_RetiresItsStoredOccurrences_ButNotASplitOffSeries()
    {
        ProviderReturns(changesOnly: true, Notice("series-1"));
        _inbox
            .Setup(i => i.GetExternalItemsAsync(Pid, "google:cal-1:series-1_"))
            .ReturnsAsync(
            [
                StoredRow("google:cal-1:series-1_20260415T150000Z", "Calendar: Standup (2026-04-15 15:00)"),
                StoredRow("google:cal-1:series-1_20260422T150000Z", "Calendar: Standup (2026-04-22 15:00)"),
                StoredRow("google:cal-1:series-1_R20260501T150000_20260501T150000Z", "Calendar: Standup (2026-05-01 15:00)"),
            ]);
        _inbox
            .Setup(i => i.RemoveExternalAsync(Pid, "google:cal-1:series-1", It.IsAny<Func<ExternalItemContent, ExternalItemContent>>()))
            .ReturnsAsync(new ExternalRemovalResult(ExternalRemovalOutcome.NotFound));
        _inbox
            .Setup(i => i.RemoveExternalAsync(Pid, It.Is<string>(id => id.Contains("_2026")), It.IsAny<Func<ExternalItemContent, ExternalItemContent>>()))
            .ReturnsAsync(new ExternalRemovalResult(ExternalRemovalOutcome.Deleted));

        var result = await _sync.SyncAsync([_provider.Object], Settings());

        result.ItemsRemoved.Should().Be(2);
        result.ItemsSkipped.Should().Be(1, "the series itself had no stored copy");
        _inbox.Verify(i => i.RemoveExternalAsync(Pid, It.Is<string>(id => id.Contains("_R")), It.IsAny<Func<ExternalItemContent, ExternalItemContent>>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_RetirementFailure_KeepsTheSyncPositionSoTheNoticeIsReadAgain()
    {
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "calendar-delta-tokens.json"), """{ "google:cal-1": "sync-1" }""");
        ProviderReturns(changesOnly: true, "sync-2", Notice("gone-2"));
        NoStoredRows("google:cal-1:gone-2_");
        _inbox
            .Setup(i => i.RemoveExternalAsync(Pid, "google:cal-1:gone-2", It.IsAny<Func<ExternalItemContent, ExternalItemContent>>()))
            .ThrowsAsync(new IOException("disk full"));

        var result = await _sync.SyncAsync([_provider.Object], Settings());

        result.ItemsFailed.Should().Be(1);
        _provider.Verify(p => p.GetEventsAsync("cal-1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), "sync-1", It.IsAny<CancellationToken>()), Times.Once);
        var tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(Path.Combine(_dataDir, "calendar-delta-tokens.json")));
        tokens!["google:cal-1"].Should().Be("sync-1");
    }

    // -- Sync: full reads ----------------------------------------------------------

    [Fact]
    public async Task SyncAsync_FullRead_RetiresStoredEventsItNoLongerLists_OnlyWellInsideTheRange()
    {
        var now = DateTime.UtcNow;
        ProviderReturns(changesOnly: false, Event("keep", now.AddDays(2)));
        _inbox
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), Pid,
                It.IsAny<string?>(), "google:cal-1:keep", It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(new ExternalTriageResult(new InboxItemEntity(), ExternalTriageOutcome.Unchanged));
        _inbox
            .Setup(i => i.GetExternalItemsAsync(Pid, "google:cal-1:"))
            .ReturnsAsync(
            [
                StoredRow("google:cal-1:keep", StoredName("Keep", now.AddDays(2))),
                StoredRow("google:cal-1:gone", StoredName("Gone", now.AddDays(3))),
                StoredRow("google:cal-1:gone-all-day", string.Create(CultureInfo.InvariantCulture, $"Calendar: Offsite ({now.AddDays(-10):yyyy-MM-dd})")),
                StoredRow("google:cal-1:aged-out", StoredName("Last quarter", now.AddDays(-120))),
                StoredRow("google:cal-1:past-edge", StoredName("Near the start", now.AddDays(-89.5))),
                StoredRow("google:cal-1:future-edge", StoredName("Near the end", now.AddDays(29.5))),
                StoredRow("google:cal-1:marked", StoredName("Already", now.AddDays(4)).Replace(")", ", removed)")),
                StoredRow("google:cal-1:older-build", "Sprint review"),
            ]);
        var retired = new List<string>();
        _inbox
            .Setup(i => i.RemoveExternalAsync(Pid, It.IsAny<string>(), It.IsAny<Func<ExternalItemContent, ExternalItemContent>>()))
            .Callback((string _, string id, Func<ExternalItemContent, ExternalItemContent> mark) =>
            {
                retired.Add(id);
                mark(new ExternalItemContent("Calendar: Gone (2026-04-15 09:30)", null, "Title: Gone\n"))
                    .ContentText.Should().Contain("No longer in the calendar's synced date range");
            })
            .ReturnsAsync(new ExternalRemovalResult(ExternalRemovalOutcome.Marked, 3));

        var result = await _sync.SyncAsync([_provider.Object], Settings());

        retired.Should().BeEquivalentTo("google:cal-1:gone", "google:cal-1:gone-all-day");
        result.ItemsRemoved.Should().Be(2);
        result.ItemsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_IncrementalRead_DoesNotTakeUnlistedEventsForGone()
    {
        // Only a full read lists the whole range; the strict inbox mock fails on any lookup.
        ProviderReturns(changesOnly: true, Event("changed", DateTime.UtcNow.AddDays(1)));
        _inbox
            .Setup(i => i.UpsertExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), Pid,
                It.IsAny<string?>(), "google:cal-1:changed", It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(new ExternalTriageResult(new InboxItemEntity(), ExternalTriageOutcome.Updated));

        var result = await _sync.SyncAsync([_provider.Object], Settings());

        result.ItemsUpdated.Should().Be(1);
        result.ItemsRemoved.Should().Be(0);
        _inbox.Verify(i => i.GetExternalItemsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // -- Helpers ---------------------------------------------------------------------

    private static CalendarSyncSettings Settings() => new()
    {
        EnabledCalendars = { ["cal-1"] = true },
        DaysPastToSync = 90,
        DaysFutureToSync = 30,
    };

    private void ProviderReturns(bool changesOnly, params CalEvent[] events) =>
        ProviderReturns(changesOnly, "token-next", events);

    private void ProviderReturns(bool changesOnly, string nextToken, params CalEvent[] events) =>
        _provider
            .Setup(p => p.GetEventsAsync("cal-1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new CalendarEventBatch(events, isCompleteWindow: !changesOnly), (string?)nextToken));

    private void NoStoredRows(string prefix) =>
        _inbox.Setup(i => i.GetExternalItemsAsync(Pid, prefix)).ReturnsAsync(Array.Empty<InboxItemEntity>());

    private static CalEvent Notice(string id) => new()
    {
        Id = id,
        IsDeleted = true,
        SourceProvider = "google",
        CalendarId = "cal-1",
    };

    private static CalEvent Event(string id, DateTime start) => new()
    {
        Id = id,
        Title = id,
        Start = start,
        End = start.AddHours(1),
        SourceProvider = "google",
        CalendarId = "cal-1",
    };

    private static string StoredName(string title, DateTime start) =>
        string.Create(CultureInfo.InvariantCulture, $"Calendar: {title} ({start:yyyy-MM-dd HH:mm})");

    private static InboxItemEntity StoredRow(string externalId, string fileName) => new()
    {
        FileName = fileName,
        FilePath = "unused.txt",
        FileType = "CalendarEvent",
        Status = "accepted",
        SourcePluginId = Pid,
        ExternalId = externalId,
    };
}

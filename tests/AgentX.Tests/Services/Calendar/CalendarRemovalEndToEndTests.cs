using System.Globalization;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Calendar;

/// <summary>
/// Events deleted at the source, end to end: the real providers answer from a stub HTTP handler,
/// <see cref="CalendarSyncService"/> runs twice, and a real <see cref="InboxService"/> on an
/// in-memory SQLite store holds the result. An event that never reached the vault leaves the
/// inbox; one that did keeps its vault document, which is marked as removed.
/// </summary>
public sealed class CalendarRemovalEndToEndTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Mock<IDocumentService> _documents = new();
    private readonly Mock<IOAuthService> _oauth = new();
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agentx-cal-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _importRefusedFor = [];
    private readonly CalendarSyncService _sync;

    public CalendarRemovalEndToEndTests()
    {
        _db = _factory.CreateContext();
        _oauth.Setup(o => o.GetAccessTokenAsync(It.IsAny<string>())).ReturnsAsync("access-token");

        _documents
            .Setup(d => d.ImportExternalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string type, string name, string? url, long? collection, CancellationToken _) =>
            {
                if (_importRefusedFor.Any(name.Contains))
                    throw new NotSupportedException("Import refused for this test event.");

                using var ctx = _factory.CreateContext();
                var document = new DocumentEntity
                {
                    FileName = name,
                    FilePath = path,
                    FileType = type,
                    ContentHash = Guid.NewGuid().ToString("N"),
                    IndexingStatus = "pending",
                    ImportedAt = DateTime.UtcNow,
                };
                ctx.Documents.Add(document);
                ctx.SaveChanges();
                return document;
            });
        _documents
            .Setup(d => d.ReindexDocumentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var inbox = new InboxService(
            _db,
            Mock.Of<ISummaryService>(),
            Mock.Of<ICollectionService>(),
            Mock.Of<IAiService>(),
            _documents.Object,
            new TestAppPaths(_root));

        var pluginData = Directory.CreateDirectory(Path.Combine(_root, "plugin")).FullName;
        _sync = new CalendarSyncService(inbox, new CalendarEventProcessor(_logger), _logger, pluginData);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
        (_logger as IDisposable)?.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task Google_EventsDeletedAtTheSource_LeaveTheInbox_OrAreMarkedInTheVault()
    {
        var now = DateTime.UtcNow;
        var fullRead = $$"""
            {
              "items": [
                { "id": "evt-filed", "status": "confirmed", "summary": "Budget review",
                  "start": { "dateTime": "{{Rfc3339(now.AddDays(2))}}" }, "end": { "dateTime": "{{Rfc3339(now.AddDays(2).AddHours(1))}}" } },
                { "id": "evt-unfiled", "status": "confirmed", "summary": "Coffee",
                  "start": { "dateTime": "{{Rfc3339(now.AddDays(3))}}" }, "end": { "dateTime": "{{Rfc3339(now.AddDays(3).AddHours(1))}}" } }
              ],
              "nextSyncToken": "sync-1"
            }
            """;
        const string changes = """
            { "items": [ { "id": "evt-filed", "status": "cancelled" }, { "id": "evt-unfiled", "status": "cancelled" } ],
              "nextSyncToken": "sync-2" }
            """;
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/calendar/v3/users/me/calendarList" => StubHttpMessageHandler.Json("""{ "items": [ { "id": "primary", "summary": "Me", "primary": true } ] }"""),
            _ => StubHttpMessageHandler.Json(request.RequestUri.Query.Contains("syncToken=") ? changes : fullRead),
        });
        var provider = new GoogleCalendarProvider(_oauth.Object, _logger, handler);
        _importRefusedFor.Add("Coffee"); // so that event never reaches the vault

        var first = await _sync.SyncAsync([provider], Settings("primary"));
        var second = await _sync.SyncAsync([provider], Settings("primary"));

        first.ItemsAdded.Should().Be(2);
        second.ItemsRemoved.Should().Be(2);
        second.ItemsFailed.Should().Be(0);

        using var db = _factory.CreateContext();
        var row = await db.InboxItems.SingleAsync();
        row.ExternalId.Should().Be("google:primary:evt-filed", "the event that never reached the vault left the inbox");
        row.FileName.Should().StartWith("Calendar: Budget review (").And.EndWith(", removed)");
        (await File.ReadAllTextAsync(row.FilePath))
            .Should().Contain("Status: Deleted or cancelled in the calendar")
            .And.Contain("Title: Budget review", "the rest of the stored text is kept");

        var document = await db.Documents.SingleAsync();
        document.Id.Should().Be(row.DocumentId!.Value, "the vault document is kept, never deleted");
        document.FileName.Should().Be(row.FileName);
        _documents.Verify(d => d.ReindexDocumentAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Outlook_OccurrenceThatLeavesTheCalendarView_IsMarkedInTheVault()
    {
        var now = DateTime.UtcNow;
        var view = new List<string>
        {
            GraphOccurrence("uid-occ-1", now.AddDays(2)),
            GraphOccurrence("uid-occ-2", now.AddDays(9)),
        };
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1.0/me/calendars" => StubHttpMessageHandler.Json("""{ "value": [ { "id": "cal-1", "name": "Calendar", "isDefaultCalendar": true } ] }"""),
            _ => StubHttpMessageHandler.Json($$"""{ "value": [ {{string.Join(",", view)}} ] }"""),
        });
        var provider = new OutlookCalendarProvider(_oauth.Object, _logger, handler);

        await _sync.SyncAsync([provider], Settings("cal-1"));
        view.RemoveAt(1); // the organizer deleted the second occurrence
        var second = await _sync.SyncAsync([provider], Settings("cal-1"));
        var third = await _sync.SyncAsync([provider], Settings("cal-1"));

        second.ItemsRemoved.Should().Be(1);
        third.ItemsRemoved.Should().Be(0, "a marked copy is not marked again");

        using var db = _factory.CreateContext();
        var rows = await db.InboxItems.OrderBy(r => r.ExternalId).ToListAsync();
        rows.Select(r => r.ExternalId).Should().Equal("microsoft:cal-1:uid-occ-1", "microsoft:cal-1:uid-occ-2");
        rows[0].FileName.Should().NotContain("removed");
        rows[1].FileName.Should().EndWith(", removed)");
        (await File.ReadAllTextAsync(rows[1].FilePath)).Should().Contain("No longer in the calendar's synced date range");
        (await db.Documents.CountAsync()).Should().Be(2, "the vault keeps the removed occurrence's document");
    }

    // -- Helpers ---------------------------------------------------------------------

    private static CalendarSyncSettings Settings(string calendarId) => new()
    {
        EnabledCalendars = { [calendarId] = true },
    };

    private static string Rfc3339(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string GraphOccurrence(string iCalUId, DateTime startUtc)
    {
        var start = startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        var end = startUtc.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        return $$"""
            { "id": "{{iCalUId}}-graph", "iCalUId": "{{iCalUId}}", "subject": "Weekly sync", "type": "occurrence",
              "seriesMasterId": "master-1", "isAllDay": false, "isCancelled": false,
              "start": { "dateTime": "{{start}}", "timeZone": "UTC" }, "end": { "dateTime": "{{end}}", "timeZone": "UTC" } }
            """;
    }

    private sealed class TestAppPaths(string root) : IAppPathService
    {
        public string GetAppDataPath() => Directory.CreateDirectory(root).FullName;
        public string GetTempPath() => Directory.CreateDirectory(Path.Combine(root, "Temp")).FullName;
    }
}

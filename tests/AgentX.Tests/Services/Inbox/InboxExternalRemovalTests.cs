using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Intelligence;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Inbox;

/// <summary>
/// <see cref="InboxService.RemoveExternalAsync"/> and <see cref="InboxService.GetExternalItemsAsync"/>:
/// how the inbox retires a connector item that is gone at its source. An item that never reached
/// the vault leaves the inbox; one that did keeps its document, which is marked, never deleted.
/// Runs on a real in-memory SQLite store with a document service that records real rows.
/// </summary>
public sealed class InboxExternalRemovalTests : IDisposable
{
    private const string PluginId = "com.agentx.calendar";

    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Mock<IDocumentService> _documents = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agentx-inbox-removal-" + Guid.NewGuid().ToString("N"));
    private readonly InboxService _service;
    private bool _importFails;

    public InboxExternalRemovalTests()
    {
        _db = _factory.CreateContext();

        _documents
            .Setup(d => d.ImportExternalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string type, string name, string? url, long? collection, CancellationToken _) =>
            {
                if (_importFails)
                    throw new NotSupportedException("No processor for this file.");

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

        _service = new InboxService(
            _db,
            Mock.Of<ISummaryService>(),
            Mock.Of<ICollectionService>(),
            Mock.Of<IAiService>(),
            _documents.Object,
            new TestAppPaths(_root));
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    private sealed class TestAppPaths(string root) : IAppPathService
    {
        public string GetAppDataPath() => Directory.CreateDirectory(root).FullName;
        public string GetTempPath() => Directory.CreateDirectory(Path.Combine(root, "Temp")).FullName;
    }

    /// <summary>An idempotent removal notice, as a connector supplies it.</summary>
    private static ExternalItemContent MarkGone(ExternalItemContent stored) =>
        stored.FileName.EndsWith(" (gone)", StringComparison.Ordinal)
            ? stored
            : new ExternalItemContent(stored.FileName + " (gone)", "Gone", "Status: gone\n" + stored.ContentText);

    private Task<ExternalTriageResult> StoreAsync(string externalId, string name = "Calendar: Review (2026-04-15 09:00)") =>
        _service.UpsertExternalAsync(
            name, "CalendarEvent", "calendar-connector", "https://calendar.example/evt",
            PluginId, "calendar_event", externalId, "2026-04-15 09:00 - 10:00", "Title: Review\nStart: 2026-04-15 09:00 UTC\n");

    [Fact]
    public async Task RemoveExternalAsync_UnknownItem_ReportsNotFoundAndChangesNothing()
    {
        await StoreAsync("google:primary:other");

        var result = await _service.RemoveExternalAsync(PluginId, "google:primary:missing", MarkGone);

        result.Outcome.Should().Be(ExternalRemovalOutcome.NotFound);
        using var fresh = _factory.CreateContext();
        (await fresh.InboxItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveExternalAsync_ItemThatNeverReachedTheVault_LeavesTheInbox()
    {
        _importFails = true;
        var stored = await StoreAsync("google:primary:evt-1");
        stored.Item.DocumentId.Should().BeNull("the import into the vault failed");
        var contentPath = stored.Item.FilePath;
        File.Exists(contentPath).Should().BeTrue();

        var result = await _service.RemoveExternalAsync(PluginId, "google:primary:evt-1", MarkGone);

        result.Outcome.Should().Be(ExternalRemovalOutcome.Deleted);
        using var fresh = _factory.CreateContext();
        (await fresh.InboxItems.AnyAsync()).Should().BeFalse();
        File.Exists(contentPath).Should().BeFalse("the inbox owns the content file and removes it with the row");
    }

    [Fact]
    public async Task RemoveExternalAsync_ItemInTheVault_KeepsTheDocumentAndMarksIt()
    {
        var stored = await StoreAsync("google:primary:evt-2");
        var documentId = stored.Item.DocumentId!.Value;

        var result = await _service.RemoveExternalAsync(PluginId, "google:primary:evt-2", MarkGone);

        result.Should().Be(new ExternalRemovalResult(ExternalRemovalOutcome.Marked, documentId));

        using var fresh = _factory.CreateContext();
        var row = await fresh.InboxItems.SingleAsync();
        row.FileName.Should().Be("Calendar: Review (2026-04-15 09:00) (gone)");
        row.Preview.Should().Be("Gone");
        row.Status.Should().Be("accepted");
        (await File.ReadAllTextAsync(row.FilePath)).Should().StartWith("Status: gone\nTitle: Review");

        var document = await fresh.Documents.SingleAsync();
        document.Id.Should().Be(documentId, "a connector never deletes a document from the vault");
        document.FileName.Should().Be(row.FileName);
        document.FilePath.Should().Be(Path.GetFullPath(row.FilePath));
        _documents.Verify(d => d.ReindexDocumentAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveExternalAsync_Twice_IsAlreadyMarkedAndWritesNothing()
    {
        await StoreAsync("google:primary:evt-3");
        await _service.RemoveExternalAsync(PluginId, "google:primary:evt-3", MarkGone);

        var again = await _service.RemoveExternalAsync(PluginId, "google:primary:evt-3", MarkGone);

        again.Outcome.Should().Be(ExternalRemovalOutcome.AlreadyMarked);
        _documents.Verify(d => d.ReindexDocumentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveExternalAsync_DocumentTheUserDeleted_LeavesTheInbox()
    {
        var stored = await StoreAsync("google:primary:evt-4");
        using (var ctx = _factory.CreateContext())
        {
            ctx.Documents.Remove(await ctx.Documents.SingleAsync(d => d.Id == stored.Item.DocumentId));
            await ctx.SaveChangesAsync();
        }

        var result = await _service.RemoveExternalAsync(PluginId, "google:primary:evt-4", MarkGone);

        result.Outcome.Should().Be(ExternalRemovalOutcome.Deleted);
        using var fresh = _factory.CreateContext();
        (await fresh.InboxItems.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveExternalAsync_StoredTextMissing_MarksOnlyTheNameAndKeepsTheIndexedText()
    {
        var stored = await StoreAsync("google:primary:evt-5");
        File.Delete(stored.Item.FilePath);

        var result = await _service.RemoveExternalAsync(PluginId, "google:primary:evt-5", MarkGone);

        result.Outcome.Should().Be(ExternalRemovalOutcome.Marked);
        using var fresh = _factory.CreateContext();
        (await fresh.InboxItems.SingleAsync()).FileName.Should().EndWith("(gone)");
        (await fresh.Documents.SingleAsync()).FileName.Should().EndWith("(gone)");
        File.Exists(stored.Item.FilePath).Should().BeFalse("the notice alone must not replace the text the vault indexed");
        _documents.Verify(d => d.ReindexDocumentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertExternalAsync_AfterRemoval_RestoresTheCurrentCopy()
    {
        // The event comes back (restored, or moved into the synced range again).
        await StoreAsync("google:primary:evt-6");
        await _service.RemoveExternalAsync(PluginId, "google:primary:evt-6", MarkGone);

        var restored = await StoreAsync("google:primary:evt-6");

        restored.Outcome.Should().Be(ExternalTriageOutcome.Updated);
        restored.Item.FileName.Should().Be("Calendar: Review (2026-04-15 09:00)");
        (await File.ReadAllTextAsync(restored.Item.FilePath)).Should().NotContain("gone");
    }

    [Fact]
    public async Task GetExternalItemsAsync_MatchesThePrefixExactlyForOnePlugin()
    {
        using (var ctx = _factory.CreateContext())
        {
            ctx.InboxItems.AddRange(
                Row("google:cal_1:a"),
                Row("google:calX1:b"),              // LIKE would read '_' as any character
                Row("Google:cal_1:c"),              // LIKE ignores ASCII case
                Row("google:cal_1:d", "com.other"));
            await ctx.SaveChangesAsync();
        }

        var rows = await _service.GetExternalItemsAsync(PluginId, "google:cal_1:");

        rows.Select(r => r.ExternalId).Should().Equal("google:cal_1:a");
    }

    private static InboxItemEntity Row(string externalId, string pluginId = PluginId) => new()
    {
        FileName = externalId,
        FilePath = Path.Combine(Path.GetTempPath(), "missing.txt"),
        FileType = "CalendarEvent",
        Status = "accepted",
        AddedAt = DateTime.UtcNow,
        SourcePluginId = pluginId,
        ExternalId = externalId,
    };
}

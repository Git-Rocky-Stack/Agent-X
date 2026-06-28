using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
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
/// Behavioural coverage for <see cref="InboxService"/> — the Smart-Inbox triage queue:
/// ingestion + dedup, pending/paged queries, single + batch accept/reject/defer, AI preview
/// generation (with collection/tag suggestion parsing), processed-item purge, and the
/// plugin-sourced <c>TriageExternal</c> bridge into the document library.
///
/// <para><b>Harness design.</b> The service is an EF-Core orchestrator over a real
/// <see cref="AgentXDbContext"/> (in-memory SQLite via <see cref="TestDbContextFactory"/>) plus three
/// mockable collaborators: <see cref="ICollectionService"/> (collection lookup for suggestions),
/// <see cref="IAiService"/> (token-streamed triage completion), and an optional
/// <see cref="IDocumentService"/> (external-content bridge). <see cref="ISummaryService"/> is
/// constructor-injected but unused, so a bare mock satisfies it. Logging is Serilog's <b>static</b>
/// <c>Log</c> (silent by default — no logger seam). Ingestion + preview read real files, so fixtures
/// write real temp files into a per-test temp directory torn down on dispose; the
/// <c>TriageExternal</c> temp-file tree (<c>%TEMP%/AgentX/ExternalItems/{pluginId}</c>) is likewise
/// tracked and cleaned.</para>
/// </summary>
public sealed class InboxServiceTests : IDisposable
{
    private readonly List<InboxHarness> _harnesses = new();

    private InboxHarness NewHarness(bool withDocumentService = true)
    {
        var h = new InboxHarness(withDocumentService);
        _harnesses.Add(h);
        return h;
    }

    public void Dispose()
    {
        foreach (var h in _harnesses)
        {
            h.Dispose();
        }
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private sealed class InboxHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public AgentXDbContext Db { get; }
        public Mock<ISummaryService> Summary { get; } = new();
        public Mock<ICollectionService> Collections { get; } = new();
        public Mock<IAiService> Ai { get; } = new();
        public Mock<IDocumentService> Documents { get; } = new();
        public InboxService Service { get; }
        public string TempDir { get; }

        private readonly List<string> _externalDirs = new();

        public InboxHarness(bool withDocumentService)
        {
            Db = Factory.CreateContext();
            TempDir = Path.Combine(Path.GetTempPath(), "agentx-inbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);

            Service = new InboxService(
                Db,
                Summary.Object,
                Collections.Object,
                Ai.Object,
                withDocumentService ? Documents.Object : null);
        }

        public string WriteFile(string name, string content = "Quarterly revenue rose 12% on strong enterprise demand.")
        {
            var path = Path.Combine(TempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Allocates a unique plugin id and registers its external temp dir for cleanup.</summary>
        public string NewPluginId()
        {
            var pid = "plugin." + Guid.NewGuid().ToString("N");
            _externalDirs.Add(Path.Combine(Path.GetTempPath(), "AgentX", "ExternalItems", pid));
            return pid;
        }

        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        public AgentXDbContext Fresh() => Factory.CreateContext();

        public void Dispose()
        {
            Db.Dispose();
            Factory.Dispose();
            TryDelete(TempDir);
            foreach (var dir in _externalDirs)
            {
                TryDelete(dir);
            }
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                /* best-effort temp cleanup */
            }
        }
    }

    // ─── Async-stream + seed helpers ──────────────────────────────────────────

    /// <summary>Async enumerable that yields the given tokens (simulates AI streaming).</summary>
    private static async IAsyncEnumerable<string> TokenStream(params string[] tokens)
    {
        foreach (var t in tokens)
        {
            await Task.Yield();
            yield return t;
        }
    }

    /// <summary>Async enumerable that faults with the given exception on first iteration.</summary>
    private static async IAsyncEnumerable<string> FaultedStream(Exception ex)
    {
        // Guarded so the `yield break` stays reachable (no unreachable-code warning) while the
        // throw still fires on the first MoveNextAsync.
        if (ex is not null)
        {
            throw ex;
        }

        await Task.Yield();
        yield break;
    }

    private static void SetupAi(InboxHarness h, params string[] tokens) =>
        h.Ai.Setup(a => a.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => TokenStream(tokens));

    private static void SetupAiFault(InboxHarness h, Exception ex) =>
        h.Ai.Setup(a => a.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => FaultedStream(ex));

    private static void SetupCollections(InboxHarness h, params CollectionEntity[] collections) =>
        h.Collections.Setup(c => c.GetAllCollectionsAsync())
            .ReturnsAsync(collections.ToList());

    private static string TriageResponse(string? preview, string? collection, string? tags)
    {
        var sb = new StringBuilder();
        if (preview is not null) sb.Append("PREVIEW: ").Append(preview).Append('\n');
        if (collection is not null) sb.Append("COLLECTION: ").Append(collection).Append('\n');
        if (tags is not null) sb.Append("TAGS: ").Append(tags).Append('\n');
        return sb.ToString();
    }

    private static CollectionEntity Coll(long id, string name) =>
        new() { Id = id, Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static InboxItemEntity NewItem(
        string status = "pending",
        string fileName = "f.txt",
        string? filePath = null,
        DateTime? addedAt = null,
        string? preview = null,
        string? externalId = null,
        string? sourcePluginId = null)
    {
        return new InboxItemEntity
        {
            FileName = fileName,
            FilePath = filePath ?? $"C:\\inbox\\{fileName}",
            FileType = "Text",
            FileSizeBytes = 10,
            Status = status,
            AddedAt = addedAt ?? DateTime.UtcNow,
            Preview = preview,
            ExternalId = externalId,
            SourcePluginId = sourcePluginId,
        };
    }

    /// <summary>Seeds a single inbox item and returns its generated id.</summary>
    private static long SeedItem(InboxHarness h, InboxItemEntity item)
    {
        h.Seed(ctx => ctx.InboxItems.Add(item));
        return item.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Constructor guards
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Ctor_NullDb_Throws()
    {
        var act = () => new InboxService(
            null!, new Mock<ISummaryService>().Object, new Mock<ICollectionService>().Object, new Mock<IAiService>().Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Ctor_NullSummary_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var act = () => new InboxService(
            db, null!, new Mock<ICollectionService>().Object, new Mock<IAiService>().Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("summaryService");
    }

    [Fact]
    public void Ctor_NullCollection_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var act = () => new InboxService(
            db, new Mock<ISummaryService>().Object, null!, new Mock<IAiService>().Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("collectionService");
    }

    [Fact]
    public void Ctor_NullAi_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var act = () => new InboxService(
            db, new Mock<ISummaryService>().Object, new Mock<ICollectionService>().Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("aiService");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  AddToInboxAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddToInboxAsync_BlankPath_Throws(string path)
    {
        var h = NewHarness();
        var act = () => h.Service.AddToInboxAsync(path);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddToInboxAsync_ExistingPending_ReturnsExistingWithoutDuplicate()
    {
        var h = NewHarness();
        var path = h.WriteFile("dup.txt");
        var normalized = Path.GetFullPath(path);
        SeedItem(h, NewItem(status: "pending", fileName: "dup.txt", filePath: normalized));

        var result = await h.Service.AddToInboxAsync(path);

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync()).Should().Be(1); // no second row
        result.FilePath.Should().Be(normalized);
    }

    [Fact]
    public async Task AddToInboxAsync_FileMissing_ThrowsFileNotFound()
    {
        var h = NewHarness();
        var missing = Path.Combine(h.TempDir, "ghost.txt");
        var act = () => h.Service.AddToInboxAsync(missing);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task AddToInboxAsync_Valid_CreatesPendingItem()
    {
        var h = NewHarness();
        var path = h.WriteFile("report.pdf", "binary-ish content");

        var item = await h.Service.AddToInboxAsync(path, watchFolderId: 7, sourceType: "file-watcher", sourceUrl: null);

        item.Id.Should().BeGreaterThan(0);
        item.FileName.Should().Be("report.pdf");
        item.FileType.Should().Be("PDF"); // FileTypeHelper category
        item.Status.Should().Be("pending");
        item.WatchFolderId.Should().Be(7);
        item.SourceType.Should().Be("file-watcher");
        item.FileSizeBytes.Should().BeGreaterThan(0);

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync()).Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Queries
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsPendingOldestFirst()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "new.txt", addedAt: t0.AddHours(2)));
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "old.txt", addedAt: t0));
            ctx.InboxItems.Add(NewItem(status: "accepted", fileName: "done.txt", addedAt: t0.AddHours(1)));
        });

        var pending = await h.Service.GetPendingItemsAsync();

        pending.Should().HaveCount(2);
        pending[0].FileName.Should().Be("old.txt"); // oldest first
    }

    [Fact]
    public async Task GetPendingItemsAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.GetPendingItemsAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetAllItemsAsync_NoFilter_ReturnsNewestFirst()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "a.txt", addedAt: t0));
            ctx.InboxItems.Add(NewItem(status: "rejected", fileName: "b.txt", addedAt: t0.AddHours(1)));
        });

        var items = await h.Service.GetAllItemsAsync();

        items.Should().HaveCount(2);
        items[0].FileName.Should().Be("b.txt"); // newest first
    }

    [Fact]
    public async Task GetAllItemsAsync_StatusFilter_Filters()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "p.txt"));
            ctx.InboxItems.Add(NewItem(status: "accepted", fileName: "a.txt"));
        });

        var items = await h.Service.GetAllItemsAsync(statusFilter: "accepted");

        items.Should().ContainSingle();
        items[0].FileName.Should().Be("a.txt");
    }

    [Fact]
    public async Task GetAllItemsAsync_Paging_SkipsAndTakes()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            for (int i = 0; i < 5; i++)
            {
                ctx.InboxItems.Add(NewItem(status: "pending", fileName: $"f{i}.txt", addedAt: t0.AddMinutes(i)));
            }
        });

        // Newest first: f4, f3, f2, f1, f0. Skip 1 → start at f3; take 2 → f3, f2.
        var page = await h.Service.GetAllItemsAsync(statusFilter: null, skip: 1, take: 2);

        page.Should().HaveCount(2);
        page[0].FileName.Should().Be("f3.txt");
        page[1].FileName.Should().Be("f2.txt");
    }

    [Fact]
    public async Task GetAllItemsAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.GetAllItemsAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsPendingCount()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "p1.txt"));
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "p2.txt"));
            ctx.InboxItems.Add(NewItem(status: "accepted", fileName: "a.txt"));
        });

        (await h.Service.GetPendingCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetPendingCountAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.GetPendingCountAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Single-item triage
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AcceptItemAsync_NoCollection_MarksAccepted()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));

        await h.Service.AcceptItemAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Status.Should().Be("accepted");
        item.ProcessedAt.Should().NotBeNull();
        item.SuggestedCollectionId.Should().BeNull();
        h.Collections.Verify(c => c.GetCollectionAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task AcceptItemAsync_WithCollectionOverride_SetsCollection()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));
        h.Collections.Setup(c => c.GetCollectionAsync(42)).ReturnsAsync(Coll(42, "Taxes"));

        await h.Service.AcceptItemAsync(id, collectionId: 42);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.SuggestedCollectionId.Should().Be(42);
        item.SuggestedCollectionName.Should().Be("Taxes");
    }

    [Fact]
    public async Task AcceptItemAsync_CollectionOverrideNotFound_LeavesNameNull()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));
        h.Collections.Setup(c => c.GetCollectionAsync(99)).ReturnsAsync((CollectionEntity?)null);

        await h.Service.AcceptItemAsync(id, collectionId: 99);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.SuggestedCollectionId.Should().Be(99);
        item.SuggestedCollectionName.Should().BeNull();
    }

    [Fact]
    public async Task AcceptItemAsync_Missing_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.AcceptItemAsync(404);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AcceptAllPendingAsync_AcceptsEveryPending()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "p1.txt"));
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "p2.txt"));
            ctx.InboxItems.Add(NewItem(status: "rejected", fileName: "r.txt"));
        });

        await h.Service.AcceptAllPendingAsync();

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync(i => i.Status == "pending")).Should().Be(0);
        (await fresh.InboxItems.CountAsync(i => i.Status == "accepted")).Should().Be(2);
    }

    [Fact]
    public async Task AcceptAllPendingAsync_NoPending_NoOp()
    {
        var h = NewHarness();
        SeedItem(h, NewItem(status: "accepted"));

        await h.Service.AcceptAllPendingAsync();

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync(i => i.Status == "accepted")).Should().Be(1);
    }

    [Fact]
    public async Task AcceptAllPendingAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.AcceptAllPendingAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task RejectItemAsync_MarksRejected()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));

        await h.Service.RejectItemAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Status.Should().Be("rejected");
        item.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectItemAsync_Missing_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.RejectItemAsync(404);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeferItemAsync_MarksDeferred()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));

        await h.Service.DeferItemAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Status.Should().Be("deferred");
        item.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeferItemAsync_Missing_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.DeferItemAsync(404);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Batch triage
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AcceptSelectedAsync_NullIds_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.AcceptSelectedAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AcceptSelectedAsync_Empty_NoOp()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));

        await h.Service.AcceptSelectedAsync(Array.Empty<long>());

        using var fresh = h.Fresh();
        (await fresh.InboxItems.FindAsync(id))!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task AcceptSelectedAsync_WithCollectionOverride_AcceptsAndSkipsMissing()
    {
        var h = NewHarness();
        long id1 = 0, id2 = 0;
        h.Seed(ctx =>
        {
            var a = NewItem(status: "pending", fileName: "a.txt");
            var b = NewItem(status: "pending", fileName: "b.txt");
            ctx.InboxItems.AddRange(a, b);
            ctx.SaveChanges();
            id1 = a.Id;
            id2 = b.Id;
        });
        h.Collections.Setup(c => c.GetCollectionAsync(5)).ReturnsAsync(Coll(5, "Work"));

        // Duplicates + a non-existent id (88888) exercise Distinct() and the silent-skip path.
        await h.Service.AcceptSelectedAsync(new[] { id1, id1, id2, 88888L }, collectionId: 5);

        using var fresh = h.Fresh();
        var a2 = await fresh.InboxItems.FindAsync(id1);
        a2!.Status.Should().Be("accepted");
        a2.SuggestedCollectionId.Should().Be(5);
        a2.SuggestedCollectionName.Should().Be("Work");
        (await fresh.InboxItems.FindAsync(id2))!.Status.Should().Be("accepted");
    }

    [Fact]
    public async Task AcceptSelectedAsync_NoCollection_RetainsOwnSuggestion()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var a = NewItem(status: "pending");
            a.SuggestedCollectionId = 3;
            a.SuggestedCollectionName = "Existing";
            ctx.InboxItems.Add(a);
            ctx.SaveChanges();
            id = a.Id;
        });

        await h.Service.AcceptSelectedAsync(new[] { id });

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Status.Should().Be("accepted");
        item.SuggestedCollectionId.Should().Be(3); // retained
        h.Collections.Verify(c => c.GetCollectionAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task AcceptSelectedAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.AcceptSelectedAsync(new[] { 1L });
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task RejectSelectedAsync_NullIds_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.RejectSelectedAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RejectSelectedAsync_Empty_NoOp()
    {
        var h = NewHarness();
        var id = SeedItem(h, NewItem(status: "pending"));

        await h.Service.RejectSelectedAsync(Array.Empty<long>());

        using var fresh = h.Fresh();
        (await fresh.InboxItems.FindAsync(id))!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task RejectSelectedAsync_RejectsMatching()
    {
        var h = NewHarness();
        long id1 = 0, id2 = 0;
        h.Seed(ctx =>
        {
            var a = NewItem(status: "pending", fileName: "a.txt");
            var b = NewItem(status: "pending", fileName: "b.txt");
            ctx.InboxItems.AddRange(a, b);
            ctx.SaveChanges();
            id1 = a.Id;
            id2 = b.Id;
        });

        await h.Service.RejectSelectedAsync(new[] { id1, id2 });

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync(i => i.Status == "rejected")).Should().Be(2);
    }

    [Fact]
    public async Task RejectSelectedAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.RejectSelectedAsync(new[] { 1L });
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  AI preview generation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratePreviewAsync_Missing_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.GeneratePreviewAsync(404);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GeneratePreviewAsync_FileUnreadable_SkipsWithoutAiCall()
    {
        var h = NewHarness();
        // FilePath points at a non-existent file → snippet empty → early return.
        var id = SeedItem(h, NewItem(status: "pending", filePath: Path.Combine(h.TempDir, "gone.txt")));

        await h.Service.GeneratePreviewAsync(id);

        h.Ai.Verify(a => a.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        using var fresh = h.Fresh();
        (await fresh.InboxItems.FindAsync(id))!.Preview.Should().BeNull();
    }

    [Fact]
    public async Task GeneratePreviewAsync_MatchingCollection_SetsPreviewTagsAndCollection()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h, Coll(1, "Finance"), Coll(2, "Legal"));
        SetupAi(h, TriageResponse("A revenue report.", "Finance", "finance,quarterly"));

        await h.Service.GeneratePreviewAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Preview.Should().Be("A revenue report.");
        item.SuggestedTags.Should().Be("finance,quarterly");
        item.SuggestedCollectionId.Should().Be(1);
        item.SuggestedCollectionName.Should().Be("Finance");
    }

    [Fact]
    public async Task GeneratePreviewAsync_UnmatchedCollection_StoresNameOnly()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h, Coll(1, "Finance"));
        SetupAi(h, TriageResponse("Summary.", "Marketing", "ads"));

        await h.Service.GeneratePreviewAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.SuggestedCollectionName.Should().Be("Marketing");
        item.SuggestedCollectionId.Should().BeNull(); // no matching collection id
    }

    [Fact]
    public async Task GeneratePreviewAsync_NoCollectionsAvailable_StillGeneratesPreview()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h); // empty → "none available" prompt branch
        SetupAi(h, TriageResponse("Just a preview.", "none", "none"));

        await h.Service.GeneratePreviewAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Preview.Should().Be("Just a preview.");
        item.SuggestedCollectionName.Should().BeNull(); // "none" → not stored
        item.SuggestedTags.Should().BeNull();           // "none" → not stored
    }

    [Fact]
    public async Task GeneratePreviewAsync_NormalizesAndCapsTags()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h);
        // Mixed case, blanks, and more than five tags → lowercased, trimmed, empties removed, first 5.
        SetupAi(h, TriageResponse("P.", null, "Finance, , Quarterly,REPORT,Audit,Tax,Extra"));

        await h.Service.GeneratePreviewAsync(id);

        using var fresh = h.Fresh();
        (await fresh.InboxItems.FindAsync(id))!.SuggestedTags
            .Should().Be("finance,quarterly,report,audit,tax");
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhitespaceResponse_LeavesFieldsNull()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h);
        SetupAi(h, "   ", "\n"); // trims to empty → ParseTriageResponse early-return

        await h.Service.GeneratePreviewAsync(id);

        using var fresh = h.Fresh();
        var item = await fresh.InboxItems.FindAsync(id);
        item!.Preview.Should().BeNull();
        item.SuggestedTags.Should().BeNull();
    }

    [Fact]
    public async Task GeneratePreviewAsync_AiCancelled_Rethrows()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h);
        SetupAiFault(h, new OperationCanceledException());

        var act = () => h.Service.GeneratePreviewAsync(id);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GeneratePreviewAsync_AiFails_Rethrows()
    {
        var h = NewHarness();
        var path = h.WriteFile("doc.txt");
        var id = SeedItem(h, NewItem(status: "pending", filePath: path));
        SetupCollections(h);
        SetupAiFault(h, new InvalidOperationException("ai down"));

        var act = () => h.Service.GeneratePreviewAsync(id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GenerateAllPreviewsAsync_NoEligibleItems_Completes()
    {
        var h = NewHarness();
        // One pending item that ALREADY has a preview → excluded by the Preview == null filter.
        SeedItem(h, NewItem(status: "pending", preview: "already done"));

        await h.Service.GenerateAllPreviewsAsync();

        h.Ai.Verify(a => a.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateAllPreviewsAsync_AllSucceed_SetsPreviews()
    {
        var h = NewHarness();
        var p1 = h.WriteFile("a.txt");
        var p2 = h.WriteFile("b.txt");
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "a.txt", filePath: p1));
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "b.txt", filePath: p2));
        });
        SetupCollections(h);
        SetupAi(h, TriageResponse("Preview here.", "none", "tag"));

        await h.Service.GenerateAllPreviewsAsync();

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync(i => i.Preview == "Preview here.")).Should().Be(2);
    }

    [Fact]
    public async Task GenerateAllPreviewsAsync_OneFails_ContinuesBatch()
    {
        var h = NewHarness();
        var p1 = h.WriteFile("a.txt");
        h.Seed(ctx => ctx.InboxItems.Add(NewItem(status: "pending", fileName: "a.txt", filePath: p1)));
        SetupCollections(h);
        // AI throws (non-cancellation) → GeneratePreview rethrows → batch catches, logs, continues.
        SetupAiFault(h, new InvalidOperationException("ai down"));

        var act = () => h.Service.GenerateAllPreviewsAsync();

        await act.Should().NotThrowAsync(); // failure is swallowed per-item
        using var fresh = h.Fresh();
        (await fresh.InboxItems.FindAsync(await FirstIdAsync(h)))!.Preview.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAllPreviewsAsync_Cancelled_Rethrows()
    {
        var h = NewHarness();
        var p1 = h.WriteFile("a.txt");
        h.Seed(ctx => ctx.InboxItems.Add(NewItem(status: "pending", fileName: "a.txt", filePath: p1)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.Service.GenerateAllPreviewsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateAllPreviewsAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.GenerateAllPreviewsAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static async Task<long> FirstIdAsync(InboxHarness h)
    {
        using var fresh = h.Fresh();
        return (await fresh.InboxItems.OrderBy(i => i.Id).FirstAsync()).Id;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Maintenance
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteProcessedItemsAsync_RemovesProcessedKeepsPending()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.InboxItems.Add(NewItem(status: "accepted", fileName: "a.txt"));
            ctx.InboxItems.Add(NewItem(status: "rejected", fileName: "r.txt"));
            ctx.InboxItems.Add(NewItem(status: "deferred", fileName: "d.txt"));
            ctx.InboxItems.Add(NewItem(status: "pending", fileName: "p.txt"));
        });

        await h.Service.DeleteProcessedItemsAsync();

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync()).Should().Be(1);
        (await fresh.InboxItems.SingleAsync()).Status.Should().Be("pending");
    }

    [Fact]
    public async Task DeleteProcessedItemsAsync_NoneProcessed_NoOp()
    {
        var h = NewHarness();
        SeedItem(h, NewItem(status: "pending"));

        await h.Service.DeleteProcessedItemsAsync();

        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteProcessedItemsAsync_DbDisposed_RethrowsFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose();
        var act = () => h.Service.DeleteProcessedItemsAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  TriageExternalAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TriageExternalAsync_BlankFileName_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.TriageExternalAsync(
            "  ", "CalendarEvent", "calendar", null, "com.x", null, "ext-1", null, "body");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TriageExternalAsync_BlankPluginId_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.TriageExternalAsync(
            "Event", "CalendarEvent", "calendar", null, "  ", null, "ext-1", null, "body");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TriageExternalAsync_BlankExternalId_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.TriageExternalAsync(
            "Event", "CalendarEvent", "calendar", null, "com.x", null, "  ", null, "body");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TriageExternalAsync_Duplicate_ReturnsExisting()
    {
        var h = NewHarness();
        var pid = h.NewPluginId();
        SeedItem(h, NewItem(status: "accepted", fileName: "old", externalId: "ext-7", sourcePluginId: pid));

        var result = await h.Service.TriageExternalAsync(
            "New Name", "CalendarEvent", "calendar", null, pid, null, "ext-7", null, "body");

        result.FileName.Should().Be("old"); // returned the pre-existing row
        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync()).Should().Be(1); // no duplicate
    }

    [Fact]
    public async Task TriageExternalAsync_NoDocumentService_CreatesAcceptedItem()
    {
        var h = NewHarness(withDocumentService: false);
        var pid = h.NewPluginId();

        var item = await h.Service.TriageExternalAsync(
            "Sprint Planning", "CalendarEvent", "calendar-connector", "https://cal/evt/1",
            pid, "calendar_event", "evt-1", "preview text", "full content body");

        item.Id.Should().BeGreaterThan(0);
        item.Status.Should().Be("accepted");
        item.Preview.Should().Be("preview text");
        item.ExternalId.Should().Be("evt-1");
        item.SourcePluginId.Should().Be(pid);
        item.DocumentId.Should().BeNull(); // no bridge without IDocumentService
        File.Exists(item.FilePath).Should().BeTrue(); // temp content file written
    }

    [Fact]
    public async Task TriageExternalAsync_WithDocumentService_LinksDocument()
    {
        var h = NewHarness();
        var pid = h.NewPluginId();
        h.Documents
            .Setup(d => d.ImportExternalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity { Id = 909 });

        var item = await h.Service.TriageExternalAsync(
            "Email Subject", "EmailMessage", "email-connector", "https://mail/1",
            pid, "ActionRequired", "msg-1", "snippet", "email body");

        item.DocumentId.Should().Be(909);
        h.Documents.Verify(d => d.ImportExternalContentAsync(
            It.IsAny<string>(), "EmailMessage", "Email Subject", "https://mail/1", It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TriageExternalAsync_DocumentServiceThrows_NonFatal()
    {
        var h = NewHarness();
        var pid = h.NewPluginId();
        h.Documents
            .Setup(d => d.ImportExternalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("import boom"));

        var item = await h.Service.TriageExternalAsync(
            "Doc", "CalendarEvent", "calendar", null, pid, null, "evt-2", null, "body");

        item.Status.Should().Be("accepted"); // item still valid
        item.DocumentId.Should().BeNull();    // bridge failed but swallowed
        using var fresh = h.Fresh();
        (await fresh.InboxItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TriageExternalAsync_SanitizesInvalidFileNameChars()
    {
        var h = NewHarness(withDocumentService: false);
        var pid = h.NewPluginId();

        // Invalid path chars in the display name must not break temp-file creation.
        var item = await h.Service.TriageExternalAsync(
            "Re: Q1/Q2 <Report>", "EmailMessage", "email", null, pid, null, "evt-3", null, "body");

        item.FileName.Should().Be("Re: Q1/Q2 <Report>"); // original display name preserved
        File.Exists(item.FilePath).Should().BeTrue();      // sanitized temp path created
    }
}

using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Documents;

/// <summary>
/// Behavioural coverage for <see cref="DocumentService"/> — the full import / query / delete /
/// reindex / duplicate-detection / bulk-operation surface of the knowledge-vault ingestion pipeline.
///
/// <para><b>Harness design.</b> The service is a straight EF-Core orchestrator over the shared
/// <see cref="AgentXDbContext"/>, fronted by an in-memory SQLite database (<see cref="TestDbContextFactory"/>).
/// Text extraction is delegated to <see cref="IDocumentProcessor"/>; the harness supplies a fully
/// deterministic <see cref="StubProcessor"/> so a real <see cref="ProcessedDocument"/> flows through
/// without touching any PDF/DOCX engine. Import paths read the file from disk (hash + <see cref="FileInfo"/>),
/// so every fixture writes a real temp file into a per-test temp directory that is torn down on dispose.
/// The optional <see cref="IVectorStore"/> is a loose mock used to assert (or refute) embedding cleanup.</para>
/// </summary>
public sealed class DocumentServiceTests : IDisposable
{
    private readonly List<DocHarness> _harnesses = new();

    private DocHarness NewHarness(
        StubProcessor? processor = null,
        bool withVectorStore = true,
        IEnumerable<IDocumentProcessor>? processors = null)
    {
        var h = new DocHarness(processor, withVectorStore, processors);
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

    private sealed class DocHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public AgentXDbContext Db { get; }
        public StubProcessor Processor { get; }
        public Mock<IVectorStore> VectorStore { get; } = new();
        public Mock<ISettingsService> Settings { get; } = new();
        public Mock<ILogger> Logger { get; } = new();
        public DocumentService Service { get; }
        public string TempDir { get; }

        public DocHarness(StubProcessor? processor, bool withVectorStore, IEnumerable<IDocumentProcessor>? processors)
        {
            Db = Factory.CreateContext();
            Processor = processor ?? new StubProcessor(new[] { ".txt", ".md", ".pdf", ".xyz" });
            var procList = processors ?? new IDocumentProcessor[] { Processor };

            TempDir = Path.Combine(Path.GetTempPath(), "agentx-doc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);

            Service = new DocumentService(
                Db,
                procList,
                Settings.Object,
                Logger.Object,
                withVectorStore ? VectorStore.Object : null);
        }

        /// <summary>Writes a real file into the per-test temp directory and returns its full path.</summary>
        public string WriteFile(string name, string content = "the quick brown fox jumps over the lazy dog")
        {
            var path = Path.Combine(TempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Applies a seed action against a fresh context that shares the in-memory database.</summary>
        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        /// <summary>A fresh context over the same in-memory database for assertions.</summary>
        public AgentXDbContext Fresh() => Factory.CreateContext();

        public void Dispose()
        {
            Db.Dispose();
            Factory.Dispose();
            try
            {
                if (Directory.Exists(TempDir))
                {
                    Directory.Delete(TempDir, recursive: true);
                }
            }
            catch
            {
                /* best-effort temp cleanup */
            }
        }
    }

    /// <summary>
    /// Deterministic <see cref="IDocumentProcessor"/> double: routes by extension membership and
    /// returns a configurable <see cref="ProcessedDocument"/> (freshly built per call so callers may
    /// safely mutate its metadata).
    /// </summary>
    private sealed class StubProcessor : IDocumentProcessor
    {
        private readonly Func<string, ProcessedDocument> _factory;

        public IReadOnlySet<string> SupportedExtensions { get; }
        public List<string> ProcessedPaths { get; } = new();
        public Exception? ThrowOnProcess { get; set; }

        public StubProcessor(IEnumerable<string> extensions, Func<string, ProcessedDocument>? factory = null)
        {
            SupportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            _factory = factory ?? (path => new ProcessedDocument
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                ExtractedText = "stub extracted text",
                ExtractedTitle = "Stub Title",
                PageCount = 3,
                WordCount = 42,
                Language = "en",
                Metadata = new DocumentMetadata()
            });
        }

        public bool CanProcess(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
        }

        public Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (ThrowOnProcess is not null)
            {
                throw ThrowOnProcess;
            }

            ProcessedPaths.Add(filePath);
            return Task.FromResult(_factory(filePath));
        }
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    private static DocumentEntity NewDoc(
        string fileName = "doc.txt",
        string fileType = "txt",
        string? hash = null,
        long size = 100,
        string status = "completed",
        DateTime? importedAt = null,
        string? filePath = null)
    {
        return new DocumentEntity
        {
            FileName = fileName,
            FilePath = filePath ?? $"C:\\vault\\{fileName}",
            FileType = fileType,
            ContentHash = hash ?? Guid.NewGuid().ToString("N"),
            FileSizeBytes = size,
            ImportedAt = importedAt ?? DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            IndexingStatus = status
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Constructor guards
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Ctor_NullDb_Throws()
    {
        var act = () => new DocumentService(
            null!, Array.Empty<IDocumentProcessor>(), new Mock<ISettingsService>().Object, new Mock<ILogger>().Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Ctor_NullProcessors_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();

        var act = () => new DocumentService(
            db, null!, new Mock<ISettingsService>().Object, new Mock<ILogger>().Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("processors");
    }

    [Fact]
    public void Ctor_NullSettings_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();

        var act = () => new DocumentService(
            db, Array.Empty<IDocumentProcessor>(), null!, new Mock<ILogger>().Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settingsService");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();

        var act = () => new DocumentService(
            db, Array.Empty<IDocumentProcessor>(), new Mock<ISettingsService>().Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ImportFileAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportFileAsync_FileMissing_ThrowsFileNotFound()
    {
        var h = NewHarness();
        var missing = Path.Combine(h.TempDir, "nope.txt");

        var act = () => h.Service.ImportFileAsync(missing);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ImportFileAsync_NoExtension_ThrowsInvalidOperation()
    {
        var h = NewHarness();
        var path = h.WriteFile("extensionless");

        var act = () => h.Service.ImportFileAsync(path);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Cannot determine file type*");
    }

    [Fact]
    public async Task ImportFileAsync_UnsupportedType_ThrowsNotSupported()
    {
        // Processor only handles .txt; a .zzz file has a valid extension but no processor.
        var h = NewHarness(new StubProcessor(new[] { ".txt" }));
        var path = h.WriteFile("data.zzz");

        var act = () => h.Service.ImportFileAsync(path);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ImportFileAsync_Valid_PersistsPendingDocument()
    {
        var h = NewHarness();
        var path = h.WriteFile("notes.txt", "alpha beta gamma");

        var entity = await h.Service.ImportFileAsync(path);

        entity.Id.Should().BeGreaterThan(0);
        entity.FileName.Should().Be("notes.txt");
        entity.FileType.Should().Be("txt");
        entity.MimeType.Should().Be("text/plain");
        entity.IndexingStatus.Should().Be("pending");
        entity.WordCount.Should().Be(42);
        entity.PageCount.Should().Be(3);
        entity.ExtractedTitle.Should().Be("Stub Title");
        entity.Language.Should().Be("en");
        entity.ContentHash.Should().NotBeNullOrWhiteSpace();
        entity.FilePath.Should().Be(Path.GetFullPath(path));
        h.Processor.ProcessedPaths.Should().ContainSingle();

        using var fresh = h.Fresh();
        (await fresh.Documents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportFileAsync_UnknownExtension_HasNullMimeType()
    {
        var h = NewHarness(new StubProcessor(new[] { ".xyz" }));
        var path = h.WriteFile("blob.xyz");

        var entity = await h.Service.ImportFileAsync(path);

        entity.FileType.Should().Be("xyz");
        entity.MimeType.Should().BeNull();
    }

    [Fact]
    public async Task ImportFileAsync_WithMetadata_SerializesMetadataJson()
    {
        var processor = new StubProcessor(new[] { ".txt" }, path => new ProcessedDocument
        {
            FilePath = path,
            WordCount = 10,
            Metadata = new DocumentMetadata
            {
                Author = "Ada Lovelace",
                Subject = "Analytical Engine",
                Custom = { ["pages"] = "5" }
            }
        });
        var h = NewHarness(processor);
        var path = h.WriteFile("paper.txt");

        var entity = await h.Service.ImportFileAsync(path);

        entity.MetadataJson.Should().NotBeNull();
        entity.MetadataJson.Should().Contain("Ada Lovelace");
        using var doc = JsonDocument.Parse(entity.MetadataJson!);
        doc.RootElement.GetProperty("author").GetString().Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task ImportFileAsync_EmptyMetadata_LeavesMetadataJsonNull()
    {
        var h = NewHarness(); // default processor → empty DocumentMetadata
        var path = h.WriteFile("plain.txt");

        var entity = await h.Service.ImportFileAsync(path);

        entity.MetadataJson.Should().BeNull();
    }

    [Fact]
    public async Task ImportFileAsync_DuplicateContent_ThrowsInvalidOperation()
    {
        var h = NewHarness();
        var path = h.WriteFile("dup.txt", "identical bytes");

        await h.Service.ImportFileAsync(path); // first import succeeds

        var act = () => h.Service.ImportFileAsync(path); // same content hash

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*identical content already exists*");
    }

    [Fact]
    public async Task ImportFileAsync_WithExistingCollection_CreatesAssociation()
    {
        var h = NewHarness();
        long collectionId = 0;
        h.Seed(ctx =>
        {
            var c = new CollectionEntity { Name = "Research", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            ctx.Collections.Add(c);
            ctx.SaveChanges();
            collectionId = c.Id;
        });
        var path = h.WriteFile("inbox.txt");

        var entity = await h.Service.ImportFileAsync(path, collectionId);

        using var fresh = h.Fresh();
        var link = await fresh.DocumentCollections.FindAsync(entity.Id, collectionId);
        link.Should().NotBeNull();
    }

    [Fact]
    public async Task ImportFileAsync_WithMissingCollection_SkipsAssociation()
    {
        var h = NewHarness();
        var path = h.WriteFile("orphan.txt");

        var entity = await h.Service.ImportFileAsync(path, collectionId: 9999);

        using var fresh = h.Fresh();
        (await fresh.DocumentCollections.CountAsync()).Should().Be(0);
        entity.Id.Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ImportExternalContentAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("", "CalendarEvent", "My Event")]
    [InlineData("   ", "CalendarEvent", "My Event")]
    public async Task ImportExternalContentAsync_BlankFilePath_Throws(string filePath, string type, string name)
    {
        var h = NewHarness();
        var act = () => h.Service.ImportExternalContentAsync(filePath, type, name);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ImportExternalContentAsync_BlankFileType_Throws()
    {
        var h = NewHarness();
        var path = h.WriteFile("event.txt");
        var act = () => h.Service.ImportExternalContentAsync(path, "  ", "My Event");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ImportExternalContentAsync_BlankDisplayName_Throws()
    {
        var h = NewHarness();
        var path = h.WriteFile("event.txt");
        var act = () => h.Service.ImportExternalContentAsync(path, "CalendarEvent", "");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ImportExternalContentAsync_FileMissing_ThrowsFileNotFound()
    {
        var h = NewHarness();
        var missing = Path.Combine(h.TempDir, "ghost.txt");
        var act = () => h.Service.ImportExternalContentAsync(missing, "CalendarEvent", "Ghost");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ImportExternalContentAsync_NoProcessor_ThrowsNotSupported()
    {
        var h = NewHarness(new StubProcessor(new[] { ".txt" }));
        var path = h.WriteFile("payload.zzz");
        var act = () => h.Service.ImportExternalContentAsync(path, "CalendarEvent", "Z");
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ImportExternalContentAsync_PreservesSemanticTypeAndDisplayName()
    {
        var h = NewHarness();
        var path = h.WriteFile("temp-event.txt");

        var entity = await h.Service.ImportExternalContentAsync(path, "CalendarEvent", "Quarterly Review");

        entity.FileType.Should().Be("CalendarEvent");
        entity.FileName.Should().Be("Quarterly Review");
        entity.ExtractedTitle.Should().Be("Quarterly Review");
        entity.MimeType.Should().Be("text/plain");
        entity.IndexingStatus.Should().Be("pending");
    }

    [Fact]
    public async Task ImportExternalContentAsync_WithSourceUrl_StoresUrlInMetadata()
    {
        var h = NewHarness();
        var path = h.WriteFile("temp-mail.txt");

        var entity = await h.Service.ImportExternalContentAsync(
            path, "EmailMessage", "Inbox Item", sourceUrl: "https://mail.example.com/123");

        entity.MetadataJson.Should().NotBeNull();
        entity.MetadataJson.Should().Contain("sourceUrl");
        entity.MetadataJson.Should().Contain("https://mail.example.com/123");
    }

    [Fact]
    public async Task ImportExternalContentAsync_WithExistingCollection_CreatesAssociation()
    {
        var h = NewHarness();
        long collectionId = 0;
        h.Seed(ctx =>
        {
            var c = new CollectionEntity { Name = "Mail", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            ctx.Collections.Add(c);
            ctx.SaveChanges();
            collectionId = c.Id;
        });
        var path = h.WriteFile("temp.txt");

        var entity = await h.Service.ImportExternalContentAsync(
            path, "EmailMessage", "Item", collectionId: collectionId);

        using var fresh = h.Fresh();
        (await fresh.DocumentCollections.FindAsync(entity.Id, collectionId)).Should().NotBeNull();
    }

    [Fact]
    public async Task ImportExternalContentAsync_WithMissingCollection_SkipsAssociation()
    {
        var h = NewHarness();
        var path = h.WriteFile("temp.txt");

        await h.Service.ImportExternalContentAsync(path, "EmailMessage", "Item", collectionId: 4242);

        using var fresh = h.Fresh();
        (await fresh.DocumentCollections.CountAsync()).Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ImportFilesAsync (batch)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportFilesAsync_NullList_ReturnsEmpty()
    {
        var h = NewHarness();
        var result = await h.Service.ImportFilesAsync(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportFilesAsync_EmptyList_ReturnsEmpty()
    {
        var h = NewHarness();
        var result = await h.Service.ImportFilesAsync(Array.Empty<string>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportFilesAsync_AllValid_ImportsAllAndReportsProgress()
    {
        var h = NewHarness();
        var paths = new[]
        {
            h.WriteFile("a.txt", "content a"),
            h.WriteFile("b.txt", "content b"),
            h.WriteFile("c.txt", "content c")
        };
        var progress = new List<int>();

        var result = await h.Service.ImportFilesAsync(paths, progress: new SyncProgress<int>(progress.Add));

        result.Should().HaveCount(3);
        progress.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ImportFilesAsync_OneFails_SkipsAndContinues()
    {
        var h = NewHarness(new StubProcessor(new[] { ".txt" }));
        var paths = new[]
        {
            h.WriteFile("ok1.txt", "one"),
            h.WriteFile("bad.zzz", "unsupported"),
            h.WriteFile("ok2.txt", "two")
        };
        var progress = new List<int>();

        var result = await h.Service.ImportFilesAsync(paths, progress: new SyncProgress<int>(progress.Add));

        result.Should().HaveCount(2); // bad.zzz skipped
        progress.Should().Equal(1, 2, 3); // progress still reported for every attempt
    }

    [Fact]
    public async Task ImportFilesAsync_CancelledToken_Throws()
    {
        var h = NewHarness();
        var paths = new[] { h.WriteFile("x.txt") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.Service.ImportFilesAsync(paths, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GetDocumentAsync / GetDocumentByHashAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDocumentAsync_Existing_ReturnsWithIncludes()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        var doc = await h.Service.GetDocumentAsync(id);

        doc.Should().NotBeNull();
        doc!.Id.Should().Be(id);
        doc.DocumentCollections.Should().NotBeNull();
        doc.DocumentTags.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDocumentAsync_Missing_ReturnsNull()
    {
        var h = NewHarness();
        (await h.Service.GetDocumentAsync(123)).Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentByHashAsync_BlankHash_ReturnsNull()
    {
        var h = NewHarness();
        (await h.Service.GetDocumentByHashAsync("   ")).Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentByHashAsync_Found_ReturnsDocument()
    {
        var h = NewHarness();
        h.Seed(ctx => { ctx.Documents.Add(NewDoc(hash: "deadbeef")); ctx.SaveChanges(); });

        var doc = await h.Service.GetDocumentByHashAsync("deadbeef");

        doc.Should().NotBeNull();
        doc!.ContentHash.Should().Be("deadbeef");
    }

    [Fact]
    public async Task GetDocumentByHashAsync_NotFound_ReturnsNull()
    {
        var h = NewHarness();
        (await h.Service.GetDocumentByHashAsync("nomatch")).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GetDocumentPreviewTextAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDocumentPreviewTextAsync_ShortSummary_ReturnsTrimmed()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            d.Summary = "  A concise summary.  ";
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        var preview = await h.Service.GetDocumentPreviewTextAsync(id);

        preview.Should().Be("A concise summary.");
    }

    [Fact]
    public async Task GetDocumentPreviewTextAsync_LongSummary_TruncatesWithEllipsis()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            d.Summary = new string('x', 500);
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        var preview = await h.Service.GetDocumentPreviewTextAsync(id, maxChars: 200);

        preview.Should().EndWith("...");
        preview!.Length.Should().Be(203); // 200 chars + "..."
    }

    [Fact]
    public async Task GetDocumentPreviewTextAsync_BelowMinClampsTo200()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            d.Summary = new string('y', 400);
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        // maxChars below the 200 floor → clamped up to 200.
        var preview = await h.Service.GetDocumentPreviewTextAsync(id, maxChars: 5);

        preview!.Length.Should().Be(203);
    }

    [Fact]
    public async Task GetDocumentPreviewTextAsync_NoSummary_FallsBackToFirstChunk()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            // Out-of-order chunk indices to exercise the OrderBy(ChunkIndex) projection.
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 1, Content = "second chunk" });
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "  first chunk  " });
            ctx.SaveChanges();
        });

        var preview = await h.Service.GetDocumentPreviewTextAsync(id);

        preview.Should().Be("first chunk");
    }

    [Fact]
    public async Task GetDocumentPreviewTextAsync_LongChunk_Truncates()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = new string('z', 600) });
            ctx.SaveChanges();
        });

        var preview = await h.Service.GetDocumentPreviewTextAsync(id, maxChars: 300);

        preview.Should().EndWith("...");
        preview!.Length.Should().Be(303);
    }

    [Fact]
    public async Task GetDocumentPreviewTextAsync_NoSummaryNoChunk_ReturnsNull()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        (await h.Service.GetDocumentPreviewTextAsync(id)).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GetAllDocumentsAsync (filters + sorting)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAllDocumentsAsync_NoFilters_ReturnsAllNewestFirst()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "old.txt", importedAt: t0));
            ctx.Documents.Add(NewDoc(fileName: "new.txt", importedAt: t0.AddDays(5)));
            ctx.SaveChanges();
        });

        // sortBy: null pins resolution to the extended overload (no shared-signature ambiguity).
        var result = await h.Service.GetAllDocumentsAsync(sortBy: null);

        result.Should().HaveCount(2);
        result[0].FileName.Should().Be("new.txt"); // newest first
    }

    [Fact]
    public async Task GetAllDocumentsAsync_SimpleOverload_DelegatesWithFilters()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "a.pdf", fileType: "pdf", status: "completed"));
            ctx.Documents.Add(NewDoc(fileName: "b.txt", fileType: "txt", status: "pending"));
            ctx.SaveChanges();
        });

        // The 2-arg overload delegates into the extended overload.
        var result = await h.Service.GetAllDocumentsAsync(".PDF", "COMPLETED");

        result.Should().ContainSingle();
        result[0].FileType.Should().Be("pdf");
    }

    [Fact]
    public async Task GetAllDocumentsAsync_StatusFilter_Filters()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "p.txt", status: "pending"));
            ctx.Documents.Add(NewDoc(fileName: "c.txt", status: "completed"));
            ctx.SaveChanges();
        });

        var result = await h.Service.GetAllDocumentsAsync(statusFilter: "pending", sortBy: null);

        result.Should().ContainSingle();
        result[0].FileName.Should().Be("p.txt");
    }

    [Fact]
    public async Task GetAllDocumentsAsync_TagFilter_FiltersByTag()
    {
        var h = NewHarness();
        long taggedId = 0;
        h.Seed(ctx =>
        {
            var tagged = NewDoc(fileName: "tagged.txt");
            var untagged = NewDoc(fileName: "untagged.txt");
            ctx.Documents.AddRange(tagged, untagged);
            var tag = new TagEntity { Name = "Important", CreatedAt = DateTime.UtcNow };
            ctx.Tags.Add(tag);
            ctx.SaveChanges();
            taggedId = tagged.Id;
            ctx.DocumentTags.Add(new DocumentTagEntity { DocumentId = tagged.Id, TagId = tag.Id, AssignedAt = DateTime.UtcNow, Confidence = 1.0 });
            ctx.SaveChanges();
        });

        var result = await h.Service.GetAllDocumentsAsync(tagFilter: "important");

        result.Should().ContainSingle();
        result[0].Id.Should().Be(taggedId);
    }

    [Fact]
    public async Task GetAllDocumentsAsync_CollectionFilter_Filters()
    {
        var h = NewHarness();
        long collId = 0;
        long inCollId = 0;
        h.Seed(ctx =>
        {
            var inColl = NewDoc(fileName: "in.txt");
            var outColl = NewDoc(fileName: "out.txt");
            ctx.Documents.AddRange(inColl, outColl);
            var c = new CollectionEntity { Name = "Set", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            ctx.Collections.Add(c);
            ctx.SaveChanges();
            collId = c.Id;
            inCollId = inColl.Id;
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = inColl.Id, CollectionId = c.Id, AddedAt = DateTime.UtcNow });
            ctx.SaveChanges();
        });

        var result = await h.Service.GetAllDocumentsAsync(collectionId: collId);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(inCollId);
    }

    [Fact]
    public async Task GetAllDocumentsAsync_DateRange_Filters()
    {
        var h = NewHarness();
        var anchor = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "before.txt", importedAt: anchor.AddDays(-10)));
            ctx.Documents.Add(NewDoc(fileName: "within.txt", importedAt: anchor));
            ctx.Documents.Add(NewDoc(fileName: "after.txt", importedAt: anchor.AddDays(10)));
            ctx.SaveChanges();
        });

        var result = await h.Service.GetAllDocumentsAsync(
            importedAfter: anchor.AddDays(-1), importedBefore: anchor.AddDays(1));

        result.Should().ContainSingle();
        result[0].FileName.Should().Be("within.txt");
    }

    // Fixture (BINARY-collation, all-lowercase names so ASCII order == alphabetical):
    //   alpha.txt  type aaa  size   10  imported t0+1
    //   delta.txt  type aaa  size    5  imported t0+3
    //   mid.txt    type mmm  size   50  imported t0+2
    //   zeta.txt   type zzz  size 9999  imported t0+4 (newest & biggest)
    // name → alpha (first alphabetically); size → zeta (largest); type → delta
    //   (aaa group, then ImportedAt desc → delta before alpha); date/unknown → zeta (newest).
    [Theory]
    [InlineData("name", "alpha.txt")]
    [InlineData("size", "zeta.txt")]
    [InlineData("type", "delta.txt")]
    [InlineData("date", "zeta.txt")]
    [InlineData("unrecognized", "zeta.txt")]
    public async Task GetAllDocumentsAsync_Sorting_OrdersByRequestedField(string sortBy, string expectedFirst)
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "alpha.txt", fileType: "aaa", size: 10, importedAt: t0.AddDays(1)));
            ctx.Documents.Add(NewDoc(fileName: "delta.txt", fileType: "aaa", size: 5, importedAt: t0.AddDays(3)));
            ctx.Documents.Add(NewDoc(fileName: "mid.txt", fileType: "mmm", size: 50, importedAt: t0.AddDays(2)));
            ctx.Documents.Add(NewDoc(fileName: "zeta.txt", fileType: "zzz", size: 9999, importedAt: t0.AddDays(4)));
            ctx.SaveChanges();
        });

        var result = await h.Service.GetAllDocumentsAsync(sortBy: sortBy);

        result[0].FileName.Should().Be(expectedFirst);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GetDocumentsByCollectionAsync / GetRecentDocumentsAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDocumentsByCollectionAsync_ReturnsMembersNewestFirst()
    {
        var h = NewHarness();
        long collId = 0;
        var t0 = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            var d1 = NewDoc(fileName: "first.txt", importedAt: t0);
            var d2 = NewDoc(fileName: "second.txt", importedAt: t0.AddHours(1));
            ctx.Documents.AddRange(d1, d2);
            var c = new CollectionEntity { Name = "C", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            ctx.Collections.Add(c);
            ctx.SaveChanges();
            collId = c.Id;
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = d1.Id, CollectionId = c.Id, AddedAt = DateTime.UtcNow });
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = d2.Id, CollectionId = c.Id, AddedAt = DateTime.UtcNow });
            ctx.SaveChanges();
        });

        var result = await h.Service.GetDocumentsByCollectionAsync(collId);

        result.Should().HaveCount(2);
        result[0].FileName.Should().Be("second.txt");
    }

    [Fact]
    public async Task GetDocumentsByCollectionAsync_EmptyCollection_ReturnsEmpty()
    {
        var h = NewHarness();
        (await h.Service.GetDocumentsByCollectionAsync(777)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentDocumentsAsync_RespectsLimitAndOrder()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            for (int i = 0; i < 5; i++)
            {
                ctx.Documents.Add(NewDoc(fileName: $"d{i}.txt", importedAt: t0.AddDays(i)));
            }
            ctx.SaveChanges();
        });

        var result = await h.Service.GetRecentDocumentsAsync(limit: 2);

        result.Should().HaveCount(2);
        result[0].FileName.Should().Be("d4.txt");
        result[1].FileName.Should().Be("d3.txt");
    }

    [Fact]
    public async Task GetRecentDocumentsAsync_NonPositiveLimit_NormalizesToOne()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "a.txt"));
            ctx.Documents.Add(NewDoc(fileName: "b.txt"));
            ctx.SaveChanges();
        });

        var result = await h.Service.GetRecentDocumentsAsync(limit: 0);

        result.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DeleteDocumentAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteDocumentAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        var act = () => h.Service.DeleteDocumentAsync(404);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteDocumentAsync_WithEmbeddedChunks_DeletesEmbeddingsAndDocument()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "c0", IsEmbedded = true, VectorRowId = 10 });
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 1, Content = "c1", IsEmbedded = true, VectorRowId = 11 });
            ctx.SaveChanges();
        });

        await h.Service.DeleteDocumentAsync(id);

        h.VectorStore.Verify(
            v => v.DeleteEmbeddingsForDocumentAsync(id, It.Is<IReadOnlyList<long>>(l => l.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);

        using var fresh = h.Fresh();
        (await fresh.Documents.CountAsync()).Should().Be(0);
        (await fresh.DocumentChunks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteDocumentAsync_VectorStoreThrows_StillDeletes()
    {
        var h = NewHarness();
        h.VectorStore
            .Setup(v => v.DeleteEmbeddingsForDocumentAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vec down"));
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "c0", IsEmbedded = true, VectorRowId = 1 });
            ctx.SaveChanges();
        });

        await h.Service.DeleteDocumentAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Documents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteDocumentAsync_NoVectorStore_DeletesWithoutVectorCall()
    {
        var h = NewHarness(withVectorStore: false);
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "c0", IsEmbedded = true, VectorRowId = 1 });
            ctx.SaveChanges();
        });

        await h.Service.DeleteDocumentAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Documents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteDocumentAsync_ChunksNotEmbedded_NoVectorCall()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "c0", IsEmbedded = false });
            ctx.SaveChanges();
        });

        await h.Service.DeleteDocumentAsync(id);

        h.VectorStore.Verify(
            v => v.DeleteEmbeddingsForDocumentAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ReindexDocumentAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReindexDocumentAsync_Missing_ThrowsInvalidOperation()
    {
        var h = NewHarness();
        var act = () => h.Service.ReindexDocumentAsync(999);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReindexDocumentAsync_SourceFileGone_MarksFailedAndThrows()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc(filePath: Path.Combine(h.TempDir, "vanished.txt"));
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        var act = () => h.Service.ReindexDocumentAsync(id);

        await act.Should().ThrowAsync<FileNotFoundException>();
        using var fresh = h.Fresh();
        var doc = await fresh.Documents.FindAsync(id);
        doc!.IndexingStatus.Should().Be("failed");
        doc.IndexingError.Should().Contain("no longer exists");
    }

    [Fact]
    public async Task ReindexDocumentAsync_NoProcessor_MarksFailedAndThrows()
    {
        var h = NewHarness(new StubProcessor(new[] { ".txt" }));
        var orphan = h.WriteFile("orphan.zzz", "unsupported but present");
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc(fileName: "orphan.zzz", fileType: "zzz", filePath: orphan);
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
        });

        var act = () => h.Service.ReindexDocumentAsync(id);

        await act.Should().ThrowAsync<NotSupportedException>();
        using var fresh = h.Fresh();
        (await fresh.Documents.FindAsync(id))!.IndexingStatus.Should().Be("failed");
    }

    [Fact]
    public async Task ReindexDocumentAsync_Valid_ResetsToPendingAndClearsChunks()
    {
        var h = NewHarness();
        var path = h.WriteFile("live.txt", "current content");
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc(fileName: "live.txt", filePath: path, status: "completed");
            d.ChunkCount = 3;
            d.LastIndexedAt = DateTime.UtcNow;
            d.IndexingError = "stale error";
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "old", IsEmbedded = true, VectorRowId = 7 });
            ctx.SaveChanges();
        });

        await h.Service.ReindexDocumentAsync(id);

        h.VectorStore.Verify(
            v => v.DeleteEmbeddingsForDocumentAsync(id, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        using var fresh = h.Fresh();
        var doc = await fresh.Documents.FindAsync(id);
        doc!.IndexingStatus.Should().Be("pending");
        doc.IndexingError.Should().BeNull();
        doc.LastIndexedAt.Should().BeNull();
        doc.ChunkCount.Should().Be(0);
        (await fresh.DocumentChunks.CountAsync()).Should().Be(0);
        h.Processor.ProcessedPaths.Should().Contain(path);
    }

    [Fact]
    public async Task ReindexDocumentAsync_VectorStoreThrows_StillReindexes()
    {
        var h = NewHarness();
        h.VectorStore
            .Setup(v => v.DeleteEmbeddingsForDocumentAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vec down"));
        var path = h.WriteFile("live2.txt", "content");
        long id = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc(fileName: "live2.txt", filePath: path);
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            id = d.Id;
            ctx.DocumentChunks.Add(new DocumentChunkEntity { DocumentId = id, ChunkIndex = 0, Content = "old", IsEmbedded = true, VectorRowId = 9 });
            ctx.SaveChanges();
        });

        await h.Service.ReindexDocumentAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Documents.FindAsync(id))!.IndexingStatus.Should().Be("pending");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Statistics
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTotalDocumentCountAsync_ReturnsCount()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "1.txt"));
            ctx.Documents.Add(NewDoc(fileName: "2.txt"));
            ctx.SaveChanges();
        });

        (await h.Service.GetTotalDocumentCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetTotalStorageBytesAsync_SumsSizes()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "1.txt", size: 100));
            ctx.Documents.Add(NewDoc(fileName: "2.txt", size: 250));
            ctx.SaveChanges();
        });

        (await h.Service.GetTotalStorageBytesAsync()).Should().Be(350);
    }

    [Fact]
    public async Task GetTotalStorageBytesAsync_Empty_ReturnsZero()
    {
        var h = NewHarness();
        (await h.Service.GetTotalStorageBytesAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetFileTypeDistributionAsync_GroupsByType()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Documents.Add(NewDoc(fileName: "a.pdf", fileType: "pdf"));
            ctx.Documents.Add(NewDoc(fileName: "b.pdf", fileType: "pdf"));
            ctx.Documents.Add(NewDoc(fileName: "c.txt", fileType: "txt"));
            ctx.SaveChanges();
        });

        var dist = await h.Service.GetFileTypeDistributionAsync();

        dist["pdf"].Should().Be(2);
        dist["txt"].Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CanProcess / GetSupportedExtensions
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanProcess_Blank_ReturnsFalse(string? filePath)
    {
        var h = NewHarness();
        h.Service.CanProcess(filePath!).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_Supported_ReturnsTrue()
    {
        var h = NewHarness(new StubProcessor(new[] { ".txt" }));
        h.Service.CanProcess("file.txt").Should().BeTrue();
    }

    [Fact]
    public void CanProcess_Unsupported_ReturnsFalse()
    {
        var h = NewHarness(new StubProcessor(new[] { ".txt" }));
        h.Service.CanProcess("file.zzz").Should().BeFalse();
    }

    [Fact]
    public void GetSupportedExtensions_UnionsAcrossProcessors()
    {
        var p1 = new StubProcessor(new[] { ".txt" });
        var p2 = new StubProcessor(new[] { ".md", ".pdf" });
        var h = NewHarness(processors: new IDocumentProcessor[] { p1, p2 });

        var exts = h.Service.GetSupportedExtensions();

        exts.Should().Contain(new[] { ".txt", ".md", ".pdf" });
        // Lazy union is memoized — second call returns the same instance.
        h.Service.GetSupportedExtensions().Should().BeSameAs(exts);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CheckForDuplicateAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckForDuplicateAsync_FileMissing_NotDuplicate()
    {
        var h = NewHarness();
        var result = await h.Service.CheckForDuplicateAsync(Path.Combine(h.TempDir, "absent.txt"));
        result.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForDuplicateAsync_ExactMatch_ReturnsExactResult()
    {
        var h = NewHarness();
        var path = h.WriteFile("candidate.txt", "duplicate me");
        var hash = await HashHelper.ComputeFileHashAsync(path);
        long existingId = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc(fileName: "original.txt", hash: hash);
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            existingId = d.Id;
        });

        var result = await h.Service.CheckForDuplicateAsync(path);

        result.IsDuplicate.Should().BeTrue();
        result.IsExactMatch.Should().BeTrue();
        result.ExistingDocumentId.Should().Be(existingId);
        result.ExistingFileName.Should().Be("original.txt");
        result.MatchScore.Should().Be(1.0f);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_NoMatch_NotDuplicate()
    {
        var h = NewHarness();
        var path = h.WriteFile("unique.txt", "one of a kind");

        var result = await h.Service.CheckForDuplicateAsync(path);

        result.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForDuplicateAsync_QueryThrows_SwallowedAsNotDuplicate()
    {
        var h = NewHarness();
        var path = h.WriteFile("candidate.txt", "content");
        // Dispose the service's context so the EF query inside the try/catch throws.
        h.Db.Dispose();

        var result = await h.Service.CheckForDuplicateAsync(path);

        result.IsDuplicate.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Bulk operations
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BulkDeleteAsync_NullOrEmpty_NoOp()
    {
        var h = NewHarness();
        await h.Service.BulkDeleteAsync(null!);
        await h.Service.BulkDeleteAsync(Array.Empty<long>());
        // No exception == pass.
    }

    [Fact]
    public async Task BulkDeleteAsync_DeletesEach()
    {
        var h = NewHarness();
        var ids = new List<long>();
        h.Seed(ctx =>
        {
            for (int i = 0; i < 3; i++)
            {
                var d = NewDoc(fileName: $"d{i}.txt");
                ctx.Documents.Add(d);
                ctx.SaveChanges();
                ids.Add(d.Id);
            }
        });

        await h.Service.BulkDeleteAsync(ids);

        using var fresh = h.Fresh();
        (await fresh.Documents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BulkDeleteAsync_CancelledToken_Throws()
    {
        var h = NewHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.Service.BulkDeleteAsync(new long[] { 1 }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BulkReindexAsync_NullOrEmpty_NoOp()
    {
        var h = NewHarness();
        await h.Service.BulkReindexAsync(null!);
        await h.Service.BulkReindexAsync(Array.Empty<long>());
    }

    [Fact]
    public async Task BulkReindexAsync_FailuresAreIsolated()
    {
        var h = NewHarness();
        var path = h.WriteFile("good.txt", "indexable");
        long goodId = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc(fileName: "good.txt", filePath: path);
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            goodId = d.Id;
        });

        // 999 does not exist → ReindexDocumentAsync throws, caught by the bulk loop;
        // goodId still gets reset to pending.
        await h.Service.BulkReindexAsync(new[] { 999L, goodId });

        using var fresh = h.Fresh();
        (await fresh.Documents.FindAsync(goodId))!.IndexingStatus.Should().Be("pending");
    }

    [Fact]
    public async Task BulkReindexAsync_CancelledToken_Throws()
    {
        var h = NewHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.Service.BulkReindexAsync(new long[] { 1 }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BulkAssignToCollectionAsync_NullOrEmpty_NoOp()
    {
        var h = NewHarness();
        await h.Service.BulkAssignToCollectionAsync(null!, 1);
        await h.Service.BulkAssignToCollectionAsync(Array.Empty<long>(), 1);
    }

    [Fact]
    public async Task BulkAssignToCollectionAsync_MissingCollection_Aborts()
    {
        var h = NewHarness();
        long docId = 0;
        h.Seed(ctx =>
        {
            var d = NewDoc();
            ctx.Documents.Add(d);
            ctx.SaveChanges();
            docId = d.Id;
        });

        await h.Service.BulkAssignToCollectionAsync(new[] { docId }, collectionId: 12345);

        using var fresh = h.Fresh();
        (await fresh.DocumentCollections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BulkAssignToCollectionAsync_AssignsSkippingExistingAndMissing()
    {
        var h = NewHarness();
        long collId = 0;
        long alreadyId = 0;
        long freshId = 0;
        h.Seed(ctx =>
        {
            var c = new CollectionEntity { Name = "Target", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            ctx.Collections.Add(c);
            var already = NewDoc(fileName: "already.txt");
            var notYet = NewDoc(fileName: "fresh.txt");
            ctx.Documents.AddRange(already, notYet);
            ctx.SaveChanges();
            collId = c.Id;
            alreadyId = already.Id;
            freshId = notYet.Id;
            // Pre-existing association for "already".
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = already.Id, CollectionId = c.Id, AddedAt = DateTime.UtcNow });
            ctx.SaveChanges();
        });

        // Mix: already-assigned (skip), fresh (assign), non-existent doc 88888 (skip).
        await h.Service.BulkAssignToCollectionAsync(new[] { alreadyId, freshId, 88888L }, collId);

        using var fresh = h.Fresh();
        (await fresh.DocumentCollections.CountAsync()).Should().Be(2); // already + fresh, no duplicate
        (await fresh.DocumentCollections.FindAsync(freshId, collId)).Should().NotBeNull();
    }

    [Fact]
    public async Task BulkAssignToCollectionAsync_CancelledToken_Throws()
    {
        var h = NewHarness();
        long collId = 0;
        h.Seed(ctx =>
        {
            var c = new CollectionEntity { Name = "C", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            ctx.Collections.Add(c);
            ctx.SaveChanges();
            collId = c.Id;
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.Service.BulkAssignToCollectionAsync(new long[] { 1 }, collId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

/// <summary>
/// Synchronous <see cref="IProgress{T}"/> that invokes its callback inline on the reporting thread,
/// so assertions on reported values are deterministic (unlike <see cref="System.Progress{T}"/>, which
/// posts callbacks to the captured synchronization context / thread pool).
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;
    public SyncProgress(Action<T> onReport) => _onReport = onReport;
    public void Report(T value) => _onReport(value);
}

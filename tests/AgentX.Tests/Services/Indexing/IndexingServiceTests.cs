using System.Diagnostics;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using AgentX.Core.Search;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Tagging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Indexing;

/// <summary>
/// Covers the background indexing pipeline end to end: how work reaches the queue (the
/// DocumentService event, explicit requests, the pending sweep, startup recovery), what a
/// processed document looks like afterwards, and how failures and shutdowns are recorded.
///
/// <para><b>Harness design.</b> The service processes documents on a background loop, so the
/// database is a temporary SQLite FILE and every participant (the service, the document
/// service, the assertions) gets its own context and therefore its own connection. Sharing
/// one in-memory connection across threads is not safe. Assertions read the database only
/// after the service has been disposed, which stops the loop. The embedding service, vector
/// store, keyword index and auto-tagger are mocks; chunking is the real splitter.</para>
/// </summary>
public sealed class IndexingServiceTests : IDisposable
{
    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly string _tempDir;
    private readonly DbContextOptions<AgentXDbContext> _options;
    private readonly List<IDisposable> _disposables = new();

    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<IKeywordSearchService> _keywordSearch = new();
    private readonly Mock<IAutoTagService> _autoTag = new();
    private readonly Mock<ISearchCacheService> _searchCache = new();
    private readonly CountingTextProcessor _processor = new();
    private long _nextVectorRowId;

    public IndexingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentx-indexing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_tempDir, "index.db")};Pooling=False")
            .Options;

        using (var context = new AgentXDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _embedding.SetupGet(e => e.ModelVersion).Returns("test-embed:1.0");
        _embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                (IReadOnlyList<float[]>)texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList());

        _vectorStore
            .Setup(v => v.InsertEmbeddingAsync(It.IsAny<long>(), It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref _nextVectorRowId));

        _settings.Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { ChunkSize = 512, ChunkOverlap = 50 });
    }

    public void Dispose()
    {
        // Services first (stops the loops), then contexts, then the database file.
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); } catch { /* best effort */ }
        }

        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // Work reaching the queue

    [Fact]
    public async Task ImportedDocument_IsIndexedDuringTheSession()
    {
        // The defect: imports only created "pending" rows and nothing queued them until the
        // next startup. The DocumentService event must hand them to the running indexer.
        var documentService = NewDocumentService();
        var service = NewService(documentService: documentService);
        await service.InitializeAsync();

        var indexed = WhenIndexed(service);
        var path = WriteFile("note.txt", "quarterly revenue grew across every region this year");

        var imported = await documentService.ImportFileAsync(path);

        (await indexed.WaitAsync(WaitLimit)).Should().Be(imported.Id);
        await StopAsync(service);

        using var db = NewContext();
        var document = await db.Documents.SingleAsync(d => d.Id == imported.Id);
        document.IndexingStatus.Should().Be("completed");
        document.ChunkCount.Should().BeGreaterThan(0);
        _keywordSearch.Verify(k => k.IndexDocumentChunksAsync(imported.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportedDocument_ReusesTheImportExtractionInsteadOfExtractingTwice()
    {
        var documentService = NewDocumentService();
        var service = NewService(documentService: documentService);
        await service.InitializeAsync();

        var indexed = WhenIndexed(service);
        await documentService.ImportFileAsync(WriteFile("once.txt", "extract me exactly one time"));

        await indexed.WaitAsync(WaitLimit);
        await StopAsync(service);

        _processor.Calls.Should().Be(1, "the indexer reuses the extraction the import already did");
    }

    [Fact]
    public async Task ImportedFileChangedBeforeIndexing_IsExtractedAgain()
    {
        var documentService = NewDocumentService();
        var service = NewService(documentService: documentService);

        // The loop is not running yet, so the change lands before the document is processed.
        var path = WriteFile("draft.txt", "first draft wording");
        await documentService.ImportFileAsync(path);
        await File.WriteAllTextAsync(path, "second draft wording that is noticeably longer");

        var indexed = WhenIndexed(service);
        await service.InitializeAsync();
        var id = await indexed.WaitAsync(WaitLimit);
        await StopAsync(service);

        _processor.Calls.Should().Be(2);
        using var db = NewContext();
        (await db.DocumentChunks.Where(c => c.DocumentId == id).Select(c => c.Content).ToListAsync())
            .Should().ContainSingle().Which.Should().Contain("second draft");
    }

    [Fact]
    public async Task ReindexRequestedThroughDocumentService_IsQueuedAndProcessed()
    {
        var path = WriteFile("policy.txt", "updated travel policy text");
        var id = SeedDocument("policy.txt", path, status: "completed", chunkContents: "stale travel policy text");

        var documentService = NewDocumentService();
        var service = NewService(documentService: documentService);
        await service.InitializeAsync();

        var indexed = WhenIndexed(service);
        await documentService.ReindexDocumentAsync(id);

        (await indexed.WaitAsync(WaitLimit)).Should().Be(id);
        await StopAsync(service);

        using var db = NewContext();
        (await db.Documents.SingleAsync(d => d.Id == id)).IndexingStatus.Should().Be("completed");
        (await db.DocumentChunks.Where(c => c.DocumentId == id).Select(c => c.Content).ToListAsync())
            .Should().ContainSingle().Which.Should().Be("updated travel policy text");
    }

    [Fact]
    public async Task PendingDocumentWrittenWithoutASignal_IsPickedUpByTheSweep()
    {
        // Web import, sync and the local API write "pending" rows directly. The idle loop
        // sweeps for them instead of leaving them for the next startup.
        var service = NewService();
        service.PendingSweepInterval = TimeSpan.FromMilliseconds(100);
        await service.InitializeAsync();

        var indexed = WhenIndexed(service);
        var id = SeedDocument("web.txt", WriteFile("web.txt", "article saved from the web"), status: "pending");

        (await indexed.WaitAsync(WaitLimit)).Should().Be(id);
        await StopAsync(service);
    }

    [Fact]
    public async Task InitializeAsync_QueuesDocumentsThatWereLeftPending()
    {
        var id = SeedDocument("left.txt", WriteFile("left.txt", "imported while the app was closing"), status: "pending");
        var service = NewService();
        var indexed = WhenIndexed(service);

        await service.InitializeAsync();

        (await indexed.WaitAsync(WaitLimit)).Should().Be(id);
        await StopAsync(service);
    }

    // Recovery and queue state

    [Fact]
    public async Task InitializeAsync_RecoversADocumentInterruptedMidPipeline()
    {
        // A shutdown mid-document left it (and its job) in "processing". Previously the job
        // was reset to "queued" but never enqueued, and only "pending" documents were picked
        // up, so the document was stuck forever.
        var id = SeedDocument("stuck.txt", WriteFile("stuck.txt", "interrupted content"), status: "processing");
        using (var db = NewContext())
        {
            db.IndexingJobs.Add(new IndexingJobEntity
            {
                DocumentId = id,
                Status = "processing",
                QueuedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = NewService();
        var indexed = WhenIndexed(service);
        await service.InitializeAsync();

        (await indexed.WaitAsync(WaitLimit)).Should().Be(id);
        await StopAsync(service);

        using var check = NewContext();
        (await check.Documents.SingleAsync(d => d.Id == id)).IndexingStatus.Should().Be("completed");
        (await check.IndexingJobs.Where(j => j.DocumentId == id).Select(j => j.Status).ToListAsync())
            .Should().Equal("completed");
    }

    [Fact]
    public async Task ShutdownMidDocument_ReturnsTheDocumentToPending()
    {
        var embeddingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<string> _, CancellationToken ct) =>
            {
                embeddingStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return (IReadOnlyList<float[]>)Array.Empty<float[]>();
            });

        var id = SeedDocument("slow.txt", WriteFile("slow.txt", "content that never finishes embedding"), status: "pending");
        var service = NewService();
        await service.InitializeAsync();
        await embeddingStarted.Task.WaitAsync(WaitLimit);

        service.Dispose();

        using var db = NewContext();
        (await db.Documents.SingleAsync(d => d.Id == id)).IndexingStatus.Should().Be("pending");
        (await db.IndexingJobs.SingleAsync(j => j.DocumentId == id)).Status.Should().Be("queued");
    }

    [Fact]
    public async Task GetQueueLengthAsync_CountsTheInMemoryBacklog()
    {
        // It used to count indexing_jobs rows, which reads 0 or 1 whatever the backlog.
        var ids = new[]
        {
            SeedDocument("a.txt", WriteFile("a.txt", "a"), status: "completed"),
            SeedDocument("b.txt", WriteFile("b.txt", "b"), status: "completed"),
            SeedDocument("c.txt", WriteFile("c.txt", "c"), status: "completed"),
        };

        var service = NewService(); // not initialized: nothing drains the queue
        foreach (var id in ids)
        {
            await service.IndexDocumentAsync(id);
        }

        await service.IndexDocumentAsync(ids[0]); // already queued: not counted twice

        (await service.GetQueueLengthAsync()).Should().Be(3);
    }

    // What an indexed document looks like

    [Fact]
    public async Task IndexedChunks_AreStampedWithTheEmbeddingModelVersionAndDimensions()
    {
        var id = SeedDocument("stamp.txt", WriteFile("stamp.txt", "versioned embedding content"), status: "pending");
        var service = NewService();
        var indexed = WhenIndexed(service);
        await service.InitializeAsync();
        await indexed.WaitAsync(WaitLimit);
        await StopAsync(service);

        using var db = NewContext();
        var chunks = await db.DocumentChunks.Where(c => c.DocumentId == id).ToListAsync();
        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(c =>
            c.IsEmbedded
            && c.EmbeddingModelVersion == "test-embed:1.0"
            && c.EmbeddingDimensions == 3
            && c.EmbeddedAt != null);
    }

    [Fact]
    public async Task IndexedDocument_InvalidatesEveryCachedSearch()
    {
        // Cached result sets computed before the document existed are stale too, not only
        // the ones that referenced it.
        SeedDocument("fresh.txt", WriteFile("fresh.txt", "brand new searchable text"), status: "pending");
        var service = NewService();
        var indexed = WhenIndexed(service);
        await service.InitializeAsync();
        await indexed.WaitAsync(WaitLimit);
        await StopAsync(service);

        _searchCache.Verify(c => c.InvalidateAll(), Times.AtLeastOnce);
    }

    // Failures

    [Fact]
    public async Task VectorStoreThatCannotInitialize_FailsDocumentsWithTheReason()
    {
        _vectorStore
            .Setup(v => v.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var id = SeedDocument("blocked.txt", WriteFile("blocked.txt", "cannot be embedded"), status: "pending");
        var service = NewService();

        await service.InitializeAsync(); // logs, does not throw
        await WaitUntilIdleAsync(service);
        await StopAsync(service);

        using var db = NewContext();
        var document = await db.Documents.SingleAsync(d => d.Id == id);
        document.IndexingStatus.Should().Be("failed");
        document.IndexingError.Should().Contain("vector store").And.Contain("disk full");
    }

    [Fact]
    public async Task FailedDocument_RaisesDocumentIndexingFailedWithTheSavedReason()
    {
        // Only success was announced, so a page showing the document could not tell that
        // indexing had failed and kept showing it as pending.
        var missing = Path.Combine(_tempDir, "gone.txt");
        var id = SeedDocument("gone.txt", WriteFile("gone.txt", "soon deleted"), status: "pending");
        File.Delete(missing);

        var service = NewService();
        var failed = new TaskCompletionSource<DocumentIndexingFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DocumentIndexingFailed += (_, e) => failed.TrySetResult(e);
        await service.InitializeAsync();

        var args = await failed.Task.WaitAsync(WaitLimit);
        await StopAsync(service);

        args.DocumentId.Should().Be(id);
        using var db = NewContext();
        var document = await db.Documents.SingleAsync(d => d.Id == id);
        document.IndexingStatus.Should().Be("failed");
        args.Error.Should().Be(document.IndexingError).And.Contain("no longer exists");
    }

    [Fact]
    public async Task ThrowingDocumentIndexedSubscriber_NeitherFailsTheDocumentNorSilencesOtherSubscribers()
    {
        // The event is raised inside the pipeline's try block: a subscriber's exception used
        // to land in the failure handler and mark a correctly indexed document as failed.
        var id = SeedDocument("fine.txt", WriteFile("fine.txt", "perfectly indexable text"), status: "pending");
        var service = NewService();
        service.DocumentIndexed += (_, _) => throw new InvalidOperationException("subscriber bug");
        var indexed = WhenIndexed(service);
        await service.InitializeAsync();

        (await indexed.WaitAsync(WaitLimit)).Should().Be(id);
        await StopAsync(service);

        using var db = NewContext();
        var document = await db.Documents.SingleAsync(d => d.Id == id);
        document.IndexingStatus.Should().Be("completed");
        document.IndexingError.Should().BeNull();
    }

    // Helpers

    private AgentXDbContext NewContext()
    {
        var context = new AgentXDbContext(_options);
        return context;
    }

    private IndexingService NewService(IDocumentService? documentService = null)
    {
        var context = NewContext();
        var service = new IndexingService(
            context,
            new IDocumentProcessor[] { _processor },
            new ChunkingService(Silent),
            _embedding.Object,
            _vectorStore.Object,
            _settings.Object,
            _keywordSearch.Object,
            _autoTag.Object,
            ragConfiguration: null,
            Silent,
            _searchCache.Object,
            documentService);

        // Disposed before the context it uses.
        _disposables.Insert(0, context);
        _disposables.Insert(0, service);
        return service;
    }

    private DocumentService NewDocumentService()
    {
        var context = NewContext();
        _disposables.Add(context);
        return new DocumentService(context, new IDocumentProcessor[] { _processor }, _settings.Object, Silent);
    }

    private static Task<long> WhenIndexed(IndexingService service)
    {
        var indexed = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DocumentIndexed += (_, id) => indexed.TrySetResult(id);
        return indexed.Task;
    }

    /// <summary>Waits for the loop to go idle, then disposes the service so the database can be read.</summary>
    private static async Task StopAsync(IndexingService service)
    {
        await WaitUntilIdleAsync(service);
        service.Dispose();
    }

    private static async Task WaitUntilIdleAsync(IndexingService service)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < WaitLimit)
        {
            if (!service.IsProcessing && await service.GetQueueLengthAsync() == 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The indexing queue did not drain in time.");
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private long SeedDocument(string fileName, string filePath, string status, params string[] chunkContents)
    {
        using var db = NewContext();
        var fileInfo = new FileInfo(filePath);
        var document = new DocumentEntity
        {
            FileName = fileName,
            FilePath = filePath,
            FileType = "txt",
            ContentHash = Guid.NewGuid().ToString("N"),
            FileSizeBytes = fileInfo.Length,
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = fileInfo.LastWriteTimeUtc,
            IndexingStatus = status,
        };

        for (var i = 0; i < chunkContents.Length; i++)
        {
            document.Chunks.Add(new DocumentChunkEntity { ChunkIndex = i, Content = chunkContents[i] });
        }

        db.Documents.Add(document);
        db.SaveChanges();
        return document.Id;
    }

    /// <summary>Reads .txt files verbatim and counts how often it was asked to.</summary>
    private sealed class CountingTextProcessor : IDocumentProcessor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlySet<string> SupportedExtensions { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };

        public bool CanProcess(string filePath) => SupportedExtensions.Contains(Path.GetExtension(filePath));

        public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            var text = await File.ReadAllTextAsync(filePath, ct);
            return new ProcessedDocument
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                ExtractedText = text,
                PageCount = 1,
                WordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            };
        }
    }
}

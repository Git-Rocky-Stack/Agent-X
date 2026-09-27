using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using AgentX.Core.AI;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using AgentX.Core.Search;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Tagging;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Indexing;

/// <summary>
/// Background indexing pipeline that processes documents through:
///   1. Text re-extraction via <see cref="IDocumentProcessor"/>
///   2. Text chunking via <see cref="IChunkingService"/>
///   3. Embedding generation via <see cref="IEmbeddingService"/>
///   4. Vector storage via <see cref="IVectorStore"/>
///
/// Uses a <see cref="Channel{T}"/> for queue-based sequential processing to avoid
/// overwhelming local model inference. Work arrives three ways: documents that
/// <see cref="IDocumentService"/> imports or re-indexes are queued the moment its
/// <see cref="IDocumentService.DocumentPendingIndexing"/> event fires; explicit
/// <see cref="IndexDocumentAsync"/> calls; and a periodic sweep for "pending" documents
/// written by paths that do not signal the indexer (sync, the local API).
/// When that sweep finds nothing to index either, chunks embedded before embedding model
/// versions were recorded are embedded again with the current model (see
/// <see cref="ReembedLegacyChunksAsync"/>).
/// </summary>
public sealed class IndexingService : IIndexingService
{
    /// <summary>
    /// The version every chunk was stamped with before versions named the provider, model and
    /// vector size (<c>provider:model:dims</c>): the embeddings came from whichever provider was
    /// active, whatever this said. Chunks carrying it, or no version at all, are legacy.
    /// </summary>
    internal const string LegacyEmbeddingModelVersion = "all-minilm:1.0";

    /// <summary>Attempts per document and session before legacy re-embedding gives up on it.</summary>
    private const int MaxLegacyReembedAttempts = 3;

    /// <summary>How long legacy re-embedding pauses after a failure (a provider that is down).</summary>
    private static readonly TimeSpan LegacyReembedBackoff = TimeSpan.FromMinutes(10);

    private readonly AgentXDbContext _db;
    private readonly IEnumerable<IDocumentProcessor> _processors;
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ISettingsService _settingsService;
    private readonly IKeywordSearchService _keywordSearchService;
    private readonly IAutoTagService _autoTagService;
    private readonly ISearchCacheService? _searchCacheService;
    private readonly IDocumentService? _documentService;
    private readonly ILogger _logger;

    // Background processing infrastructure
    private readonly Channel<long> _documentQueue;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _startLock = new();
    private Task? _backgroundTask;

    /// <summary>
    /// Ids currently waiting in the channel. Keeps the same document from being queued twice
    /// by the event, the sweep and explicit requests. An id is released when the loop starts
    /// on it, so a re-index requested while the document is being processed queues a second
    /// pass instead of being dropped.
    /// </summary>
    private readonly ConcurrentDictionary<long, byte> _queuedDocumentIds = new();

    /// <summary>
    /// Extractions handed over by <see cref="IDocumentService"/>, keyed by document id, so a
    /// freshly imported file is not parsed, OCR'd or fetched a second time. Bounded by
    /// <see cref="MaxHandoffCharacters"/>; anything over the budget is simply re-extracted.
    /// </summary>
    private readonly Dictionary<long, ProcessedDocument> _extractionHandoff = new();
    private readonly object _handoffLock = new();
    private long _handoffCharacters;
    private const long MaxHandoffCharacters = 8_000_000;

    /// <summary>How long the idle loop waits for new work before sweeping for pending documents.</summary>
    private static readonly TimeSpan DefaultPendingSweepInterval = TimeSpan.FromSeconds(30);

    // Vector store readiness: initialized once, and a failure is reported on every document
    // instead of leaving the queue to stall silently.
    private readonly SemaphoreSlim _vectorStoreGate = new(1, 1);
    private volatile bool _vectorStoreReady;
    private string? _vectorStoreError;

    // State tracking
    private int _processedCount;
    private int _notStartedWarned;
    private volatile bool _isProcessing;
    private bool _disposed;

    // Legacy re-embedding: failed attempts per document, and the pause after a failure. Only
    // the background loop (or a test driving it) touches these.
    private readonly Dictionary<long, int> _legacyReembedFailures = new();
    private DateTime _legacyReembedPausedUntilUtc = DateTime.MinValue;

    /// <inheritdoc />
    public bool IsProcessing => _isProcessing;

    /// <inheritdoc />
    public event EventHandler<IndexingProgressEventArgs>? ProgressChanged;

    /// <inheritdoc />
    public event EventHandler<long>? DocumentIndexed;

    /// <inheritdoc />
    public event EventHandler<DocumentIndexingFailedEventArgs>? DocumentIndexingFailed;

    /// <summary>
    /// Idle time between sweeps for pending documents. Settable by tests so the sweep can be
    /// exercised without waiting for the production interval.
    /// </summary>
    internal TimeSpan PendingSweepInterval { get; set; } = DefaultPendingSweepInterval;

    /// <summary>
    /// Default batch size for embedding generation when no IRagConfiguration is
    /// supplied. Keeps memory usage bounded while still benefiting from batch
    /// inference when supported by the model. P2-9: prefer the config value
    /// (<c>IRagConfiguration.EmbeddingBatchSize</c>) — that way the outer batch
    /// here matches the inner batch in <c>EmbeddingService.EmbedBatchAsync</c>
    /// instead of the two fighting at different sizes.
    /// </summary>
    private const int FallbackEmbeddingBatchSize = 16;

    private readonly IRagConfiguration? _ragConfiguration;

    public IndexingService(
        AgentXDbContext db,
        IEnumerable<IDocumentProcessor> processors,
        IChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ISettingsService settingsService,
        IKeywordSearchService keywordSearchService,
        IAutoTagService autoTagService,
        ILogger logger,
        ISearchCacheService? searchCacheService = null)
        : this(db, processors, chunkingService, embeddingService, vectorStore,
               settingsService, keywordSearchService, autoTagService,
               null, logger, searchCacheService)
    {
    }

    public IndexingService(
        AgentXDbContext db,
        IEnumerable<IDocumentProcessor> processors,
        IChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ISettingsService settingsService,
        IKeywordSearchService keywordSearchService,
        IAutoTagService autoTagService,
        IRagConfiguration? ragConfiguration,
        ILogger logger,
        ISearchCacheService? searchCacheService = null,
        IDocumentService? documentService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processors = processors ?? throw new ArgumentNullException(nameof(processors));
        _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _keywordSearchService = keywordSearchService ?? throw new ArgumentNullException(nameof(keywordSearchService));
        _autoTagService = autoTagService ?? throw new ArgumentNullException(nameof(autoTagService));
        _ragConfiguration = ragConfiguration;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchCacheService = searchCacheService;
        _documentService = documentService;

        // Unbounded channel: items are cheap (just a long ID) and we want to accept
        // enqueue requests without blocking the caller. Processing is serialized.
        _documentQueue = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Imports and re-indexes queue themselves as they happen. The handler only touches
        // in-memory state, so it is safe to run on whichever thread raised the event.
        if (_documentService is not null)
        {
            _documentService.DocumentPendingIndexing += OnDocumentPendingIndexing;
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.Information("Initializing IndexingService");

        // Initialize the vector store (creates tables, loads indexes, etc.). A failure is
        // logged and remembered rather than thrown: the loop still starts, and every document
        // it takes is marked failed with the reason instead of waiting silently forever.
        await EnsureVectorStoreReadyAsync(ct);

        try
        {
            await RecoverInterruptedWorkAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to recover indexing work interrupted by the previous shutdown");
        }

        // Queue everything left in "pending": documents imported while the service was not
        // running, and documents handed back by the recovery step above.
        var queued = await EnqueuePendingDocumentsAsync(ct);

        // Start the background processing loop (once)
        lock (_startLock)
        {
            _backgroundTask ??= Task.Run(() => ProcessQueueAsync(_shutdownCts.Token), CancellationToken.None);
        }

        _logger.Information("IndexingService initialized. {PendingCount} documents queued for processing", queued);
    }

    /// <inheritdoc />
    public async Task IndexDocumentAsync(long documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents.FindAsync(new object[] { documentId }, ct);
        if (document is null)
        {
            throw new InvalidOperationException($"Document with ID {documentId} not found.");
        }

        // Enqueue for background processing
        Enqueue(documentId);
        WarnIfLoopNotRunning();

        RaiseProgressChanged(QueueLength, _processedCount, document.FileName);

        _logger.Information("Document {DocumentId} ({FileName}) enqueued for indexing", documentId, document.FileName);
    }

    /// <inheritdoc />
    public async Task ReindexAllAsync(
        IProgress<(int Processed, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        // Invalidate the entire search cache since all documents are being re-indexed
        _searchCacheService?.InvalidateAll();

        var completedDocs = await _db.Documents
            .Where(d => d.IndexingStatus == "completed" || d.IndexingStatus == "failed")
            .Select(d => d.Id)
            .ToListAsync(ct);

        var total = completedDocs.Count;
        var processed = 0;

        _logger.Information("Starting full re-index of {Total} documents", total);

        foreach (var docId in completedDocs)
        {
            ct.ThrowIfCancellationRequested();

            // Reset document status to pending so the pipeline processes it fresh
            var doc = await _db.Documents
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.Id == docId, ct);

            if (doc is null) continue;

            // Remove document from FTS5 index before re-indexing
            try
            {
                await _keywordSearchService.RemoveDocumentFromFtsAsync(docId, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to remove document {DocumentId} from FTS5 during re-index", docId);
            }

            // Delete existing chunks and embeddings
            if (doc.Chunks.Count > 0)
            {
                var embeddedChunkIds = doc.Chunks
                    .Where(c => c.IsEmbedded && c.VectorRowId.HasValue)
                    .Select(c => c.Id)
                    .ToList();

                if (embeddedChunkIds.Count > 0)
                {
                    try
                    {
                        await _vectorStore.DeleteEmbeddingsForDocumentAsync(docId, embeddedChunkIds, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to delete embeddings during re-index for document {DocumentId}", docId);
                    }
                }

                _db.DocumentChunks.RemoveRange(doc.Chunks);
            }

            doc.IndexingStatus = "pending";
            doc.IndexingError = null;
            doc.ChunkCount = 0;
            doc.LastIndexedAt = null;
            await _db.SaveChangesAsync(ct);

            // Enqueue for indexing
            Enqueue(docId);

            processed++;
            progress?.Report((processed, total));
        }

        WarnIfLoopNotRunning();

        _logger.Information("Re-index enqueued {Processed}/{Total} documents", processed, total);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Counts the in-memory backlog (documents waiting in the channel plus the one being
    /// processed). The indexing_jobs table is a history log, not the queue.
    /// </remarks>
    public Task<int> GetQueueLengthAsync() => Task.FromResult(QueueLength);

    /// <inheritdoc />
    public async Task<int> GetProcessedCountAsync()
    {
        return await _db.IndexingJobs
            .CountAsync(j => j.Status == "completed");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.Debug("Disposing IndexingService");

        if (_documentService is not null)
        {
            _documentService.DocumentPendingIndexing -= OnDocumentPendingIndexing;
        }

        // Signal the background loop to stop
        _shutdownCts.Cancel();
        _documentQueue.Writer.TryComplete();

        // Wait briefly for graceful shutdown
        try
        {
            _backgroundTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected when cancellation fires
        }

        _shutdownCts.Dispose();
    }

    // ─── Background Processing Loop ─────────────────────────────────

    /// <summary>
    /// Continuously reads document IDs from the channel and processes them one at a time.
    /// When the channel stays empty for <see cref="PendingSweepInterval"/>, sweeps the
    /// database for pending documents that were created without signalling the indexer.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        _logger.Debug("Background indexing loop started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                while (_documentQueue.Reader.TryRead(out var documentId))
                {
                    // Flag processing before releasing the id so the queue length never
                    // reads zero while a document is changing hands.
                    _isProcessing = true;
                    _queuedDocumentIds.TryRemove(documentId, out _);

                    try
                    {
                        await ProcessSingleDocumentAsync(documentId, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        _logger.Information("Indexing loop cancelled during document {DocumentId}", documentId);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unexpected error processing document {DocumentId} in background loop", documentId);
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                }

                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idleCts.CancelAfter(PendingSweepInterval);

                try
                {
                    if (!await _documentQueue.Reader.WaitToReadAsync(idleCts.Token))
                    {
                        break; // The channel was completed by Dispose.
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Idle long enough: pick up documents left "pending" by paths that write
                    // documents directly instead of going through IDocumentService.
                    await EnqueuePendingDocumentsAsync(ct);

                    // Still nothing to index: bring chunks embedded before embedding model
                    // versions were recorded into the current embedding space.
                    await ReembedLegacyChunksAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        _logger.Debug("Background indexing loop stopped");
    }

    /// <summary>
    /// Processes a single document through the full indexing pipeline:
    /// text extraction, chunking, embedding generation, and vector storage.
    /// </summary>
    private async Task ProcessSingleDocumentAsync(long documentId, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // Load the document
        var document = await _db.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            _logger.Warning("Document {DocumentId} not found; skipping indexing", documentId);
            TakeHandoff(documentId);
            return;
        }

        _logger.Information("Starting indexing pipeline for document {DocumentId} ({FileName})", documentId, document.FileName);

        // Claim the import's extraction now so it is released even if this attempt fails early.
        var handedOver = TakeUsableHandoff(document);

        // Update document status to "processing"
        document.IndexingStatus = "processing";
        document.IndexingError = null;
        await _db.SaveChangesAsync(ct);

        // Create or find an indexing job
        var job = await _db.IndexingJobs
            .FirstOrDefaultAsync(j => j.DocumentId == documentId && j.Status == "queued", ct);

        if (job is null)
        {
            job = new IndexingJobEntity
            {
                DocumentId = documentId,
                Status = "processing",
                QueuedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow
            };
            _db.IndexingJobs.Add(job);
        }
        else
        {
            job.Status = "processing";
            job.StartedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        RaiseProgressChanged(QueueLength, _processedCount, document.FileName);

        var indexed = false;

        try
        {
            // 1. Embeddings have nowhere to go without the vector store. Fail with the
            //    store's own error so the document shows why it was not indexed.
            await EnsureVectorStoreReadyAsync(ct);
            if (!_vectorStoreReady)
            {
                throw new InvalidOperationException(
                    $"The vector store is not available ({_vectorStoreError ?? "not initialized"}). " +
                    "Re-index the document once the store is available.");
            }

            // 2. Extract text, reusing the import's extraction when the file is unchanged
            var processed = handedOver ?? await ExtractAsync(document, ct);

            // 3. Get chunking settings
            var settings = await _settingsService.GetSettingsAsync();
            var chunkSize = settings.ChunkSize;
            var chunkOverlap = settings.ChunkOverlap;

            // 4. Chunk the document
            var chunks = _chunkingService.ChunkDocument(processed, chunkSize, chunkOverlap);
            _logger.Debug("Generated {ChunkCount} chunks for document {DocumentId}", chunks.Count, documentId);

            // 5. Delete any existing index data (in case of re-index). FTS rows are cleared
            //    even when the document has no chunks: a re-index requested through
            //    DocumentService removes the chunks first, and the rows of the previous
            //    version must not survive to be served next to the new ones.
            try
            {
                await _keywordSearchService.RemoveDocumentFromFtsAsync(documentId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning(ex, "Failed to remove document {DocumentId} from FTS5 during re-index", documentId);
            }

            if (document.Chunks.Count > 0)
            {
                var existingEmbeddedIds = document.Chunks
                    .Where(c => c.IsEmbedded && c.VectorRowId.HasValue)
                    .Select(c => c.Id)
                    .ToList();

                if (existingEmbeddedIds.Count > 0)
                {
                    await _vectorStore.DeleteEmbeddingsForDocumentAsync(documentId, existingEmbeddedIds, ct);
                }

                _db.DocumentChunks.RemoveRange(document.Chunks);
                await _db.SaveChangesAsync(ct);
            }

            // 6. Create DocumentChunkEntity records
            var chunkEntities = new List<DocumentChunkEntity>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var chunkEntity = new DocumentChunkEntity
                {
                    DocumentId = documentId,
                    ChunkIndex = i,
                    Content = chunk.Content,
                    StartCharOffset = chunk.StartCharOffset,
                    EndCharOffset = chunk.EndCharOffset,
                    PageNumber = chunk.PageNumber,
                    SectionTitle = chunk.SectionTitle,
                    TokenCount = chunk.TokenCount,
                    IsEmbedded = false
                };

                _db.DocumentChunks.Add(chunkEntity);
                chunkEntities.Add(chunkEntity);
            }

            await _db.SaveChangesAsync(ct);

            // 7. Generate embeddings in batches
            var embeddingsGenerated = 0;
            var embeddingBatchSize = Math.Max(1,
                _ragConfiguration?.EmbeddingBatchSize ?? FallbackEmbeddingBatchSize);

            // Stamped on every chunk so retrieval can tell vectors from different models
            // apart instead of treating every chunk as an unversioned legacy row.
            var embeddingModelVersion = _embeddingService.ModelVersion;

            for (var batchStart = 0; batchStart < chunkEntities.Count; batchStart += embeddingBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batchEnd = Math.Min(batchStart + embeddingBatchSize, chunkEntities.Count);
                var batchChunks = chunkEntities.GetRange(batchStart, batchEnd - batchStart);
                var batchTexts = batchChunks.Select(c => c.Content).ToList();

                _logger.Debug(
                    "Generating embeddings for batch {Start}-{End} of {Total} chunks (document {DocumentId})",
                    batchStart, batchEnd - 1, chunkEntities.Count, documentId);

                var embeddings = await _embeddingService.EmbedBatchAsync(batchTexts, ct);

                // 8. Store each embedding in the vector database
                for (var j = 0; j < batchChunks.Count; j++)
                {
                    var chunkEntity = batchChunks[j];
                    var embedding = embeddings[j];

                    var vectorRowId = await _vectorStore.InsertEmbeddingAsync(chunkEntity.Id, embedding, ct);

                    chunkEntity.VectorRowId = vectorRowId;
                    chunkEntity.IsEmbedded = true;
                    chunkEntity.EmbeddingModelVersion = embeddingModelVersion;
                    chunkEntity.EmbeddingDimensions = embedding.Length;
                    chunkEntity.EmbeddedAt = DateTime.UtcNow;
                    embeddingsGenerated++;
                }

                await _db.SaveChangesAsync(ct);
            }

            // 9. Index document chunks into FTS5 for keyword search (non-fatal). Done before
            //    the document is reported complete so a search that follows the completion
            //    sees both the vectors and the keyword rows.
            try
            {
                await _keywordSearchService.IndexDocumentChunksAsync(documentId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning(ex, "FTS5 keyword indexing failed for document {DocumentId}", documentId);
            }

            stopwatch.Stop();

            // 10. Update document with indexing results. When this pass extracted the text
            //     itself (startup recovery, audio queued again once the speech model is
            //     installed), the counts still describe the previous extraction, so a
            //     transcribed recording kept showing 0 words. Only the text-derived fields
            //     are refreshed: the title and metadata can hold what an import recorded
            //     (a web page's title and source URL) and stay as they are.
            if (handedOver is null)
            {
                document.WordCount = processed.WordCount;
                if (processed.PageCount > 0)
                {
                    document.PageCount = processed.PageCount;
                }

                if (!string.IsNullOrWhiteSpace(processed.Language))
                {
                    document.Language = processed.Language;
                }
            }

            document.ChunkCount = chunkEntities.Count;
            document.IndexingStatus = "completed";
            document.IndexingError = null;
            document.LastIndexedAt = DateTime.UtcNow;

            // 11. Update the indexing job
            job.Status = "completed";
            job.CompletedAt = DateTime.UtcNow;
            job.ChunksProcessed = chunkEntities.Count;
            job.EmbeddingsGenerated = embeddingsGenerated;
            job.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;

            await _db.SaveChangesAsync(ct);

            Interlocked.Increment(ref _processedCount);

            _logger.Information(
                "Indexed document {DocumentId} ({FileName}): {ChunkCount} chunks, {EmbeddingCount} embeddings in {ElapsedMs:F0}ms",
                documentId, document.FileName, chunkEntities.Count, embeddingsGenerated, stopwatch.Elapsed.TotalMilliseconds);

            // Any cached result set may now be stale: entries that referenced this document
            // hold its old chunks, and every other entry was computed without the new text.
            _searchCacheService?.InvalidateAll();

            // Raise events
            RaiseProgressChanged(QueueLength, _processedCount, null);
            indexed = true;
            RaiseDocumentIndexed(documentId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown mid-document: hand it back to the queue for the next start instead of
            // leaving it in "processing", where nothing would ever pick it up again.
            await RequeueInterruptedAsync(document, job);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.Error(ex, "Failed to index document {DocumentId} ({FileName})", documentId, document.FileName);

            // Mark document as failed
            document.IndexingStatus = "failed";
            document.IndexingError = ex.Message;

            // Mark job as failed
            job.Status = "failed";
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = ex.Message;
            job.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;

            await _db.SaveChangesAsync(CancellationToken.None);

            RaiseProgressChanged(QueueLength, _processedCount, null);
            RaiseDocumentIndexingFailed(documentId, ex.Message);
        }

        if (!indexed)
        {
            return;
        }

        // Auto-tag the document (non-fatal: it must not block the indexing pipeline, and the
        // document is already indexed, so a shutdown here must not send it back to the queue)
        try
        {
            await _autoTagService.ApplyAutoTagsAsync(documentId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Debug("Auto-tagging of document {DocumentId} stopped by shutdown", documentId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Auto-tagging failed for document {DocumentId}", documentId);
        }
    }

    // ─── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Documents waiting in the channel plus the one currently being processed.
    /// </summary>
    private int QueueLength => _queuedDocumentIds.Count + (_isProcessing ? 1 : 0);

    /// <summary>
    /// Queues a document unless it is already waiting. Returns true when it was added.
    /// </summary>
    private bool Enqueue(long documentId)
    {
        if (!_queuedDocumentIds.TryAdd(documentId, 0))
        {
            return false;
        }

        if (_documentQueue.Writer.TryWrite(documentId))
        {
            return true;
        }

        // The channel is completed (the service is shutting down).
        _queuedDocumentIds.TryRemove(documentId, out _);
        return false;
    }

    private void OnDocumentPendingIndexing(object? sender, DocumentPendingIndexingEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.Extracted is not null)
        {
            StoreHandoff(e.DocumentId, e.Extracted);
        }

        if (Enqueue(e.DocumentId))
        {
            _logger.Debug("Document {DocumentId} queued for indexing", e.DocumentId);
            RaiseProgressChanged(QueueLength, _processedCount, null);
        }

        WarnIfLoopNotRunning();
    }

    private void WarnIfLoopNotRunning()
    {
        if (_backgroundTask is null && Interlocked.Exchange(ref _notStartedWarned, 1) == 0)
        {
            _logger.Warning(
                "Documents are being queued for indexing before IndexingService.InitializeAsync ran; they will be processed once the service starts");
        }
    }

    /// <summary>
    /// Queues every document in "pending" status. Returns the number newly queued.
    /// </summary>
    private async Task<int> EnqueuePendingDocumentsAsync(CancellationToken ct)
    {
        try
        {
            var pendingDocIds = await _db.Documents
                .AsNoTracking()
                .Where(d => d.IndexingStatus == "pending")
                .OrderBy(d => d.Id)
                .Select(d => d.Id)
                .ToListAsync(ct);

            var added = 0;
            foreach (var docId in pendingDocIds)
            {
                if (Enqueue(docId))
                {
                    added++;
                    _logger.Debug("Queued pending document {DocumentId} for indexing", docId);
                }
            }

            if (added > 0)
            {
                RaiseProgressChanged(QueueLength, _processedCount, null);
            }

            return added;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to look up pending documents for indexing");
            return 0;
        }
    }

    /// <summary>
    /// Hands work interrupted by a previous shutdown back to the queue. Nothing is being
    /// processed while the service starts, so any document or job still marked
    /// "processing" was cut off mid-pipeline.
    /// </summary>
    private async Task RecoverInterruptedWorkAsync(CancellationToken ct)
    {
        var interruptedDocs = await _db.Documents
            .Where(d => d.IndexingStatus == "processing")
            .ToListAsync(ct);

        foreach (var doc in interruptedDocs)
        {
            doc.IndexingStatus = "pending";
            _logger.Warning("Document {DocumentId} was interrupted while indexing; queued again", doc.Id);
        }

        var staleJobs = await _db.IndexingJobs
            .Where(j => j.Status == "processing")
            .ToListAsync(ct);

        foreach (var staleJob in staleJobs)
        {
            staleJob.Status = "queued";
            staleJob.StartedAt = null;
            _logger.Warning("Reset stale indexing job {JobId} for document {DocumentId} back to queued",
                staleJob.Id, staleJob.DocumentId);
        }

        if (interruptedDocs.Count > 0 || staleJobs.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Best-effort reset of a document interrupted by shutdown. When this cannot be saved
    /// (the database may already be closing), <see cref="RecoverInterruptedWorkAsync"/>
    /// performs the same reset on the next start.
    /// </summary>
    private async Task RequeueInterruptedAsync(DocumentEntity document, IndexingJobEntity job)
    {
        try
        {
            document.IndexingStatus = "pending";
            job.Status = "queued";
            job.StartedAt = null;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex,
                "Could not return interrupted document {DocumentId} to the queue; it will be recovered at the next start",
                document.Id);
        }
    }

    /// <summary>
    /// Embeds the chunks of completed documents again when they were embedded before embedding
    /// model versions were recorded (version <see cref="LegacyEmbeddingModelVersion"/> or none).
    /// Retrieval excludes chunks whose version differs from the current one, and the vector
    /// index only serves vectors of the current size, so such documents had dropped out of
    /// semantic search. Runs one document at a time and only while nothing is queued for
    /// indexing. The stored chunk text is embedded again: nothing is extracted or chunked, and
    /// chunk ids and keyword rows stay as they are. A failure pauses re-embedding for
    /// <see cref="LegacyReembedBackoff"/>; a document that keeps failing is left for the next
    /// session.
    /// </summary>
    /// <returns>The number of documents re-embedded.</returns>
    internal async Task<int> ReembedLegacyChunksAsync(CancellationToken ct)
    {
        var reembedded = 0;
        long? previousDocumentId = null;

        while (!ct.IsCancellationRequested
               && _queuedDocumentIds.IsEmpty
               && DateTime.UtcNow >= _legacyReembedPausedUntilUtc)
        {
            var currentVersion = _embeddingService.ModelVersion;
            if (string.IsNullOrEmpty(currentVersion) || currentVersion == LegacyEmbeddingModelVersion)
            {
                return reembedded; // nothing better to stamp them with
            }

            long? documentId;
            try
            {
                documentId = await FindDocumentWithLegacyChunksAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to look up chunks embedded before embedding model versions were recorded");
                return reembedded;
            }

            if (documentId is null)
            {
                return reembedded;
            }

            await EnsureVectorStoreReadyAsync(ct);
            if (!_vectorStoreReady)
            {
                return reembedded;
            }

            // The same document again right after it was re-embedded means its chunks did not
            // change: count it as a failure instead of spinning on it.
            if (documentId != previousDocumentId && await ReembedDocumentAsync(documentId.Value, ct))
            {
                reembedded++;
                previousDocumentId = documentId;
                continue;
            }

            _legacyReembedFailures[documentId.Value] = _legacyReembedFailures.GetValueOrDefault(documentId.Value) + 1;
            _legacyReembedPausedUntilUtc = DateTime.UtcNow + LegacyReembedBackoff;
            return reembedded;
        }

        return reembedded;
    }

    private async Task<long?> FindDocumentWithLegacyChunksAsync(CancellationToken ct)
    {
        var givenUp = _legacyReembedFailures
            .Where(failure => failure.Value >= MaxLegacyReembedAttempts)
            .Select(failure => failure.Key)
            .ToList();

        return await _db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.IsEmbedded
                        && (c.EmbeddingModelVersion == null
                            || c.EmbeddingModelVersion == string.Empty
                            || c.EmbeddingModelVersion == LegacyEmbeddingModelVersion)
                        && c.Document.IndexingStatus == "completed"
                        && !givenUp.Contains(c.DocumentId))
            .OrderBy(c => c.DocumentId)
            .Select(c => (long?)c.DocumentId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Replaces the vectors of one document's legacy chunks. Every vector is computed before
    /// anything is changed, so a provider failure leaves the document exactly as it was. The
    /// chunks are read untracked and stamped with set-based updates, so this background pass
    /// adds nothing to the shared context. Returns false when the document could not be
    /// re-embedded.
    /// </summary>
    private async Task<bool> ReembedDocumentAsync(long documentId, CancellationToken ct)
    {
        try
        {
            var chunks = await _db.DocumentChunks
                .AsNoTracking()
                .Where(c => c.DocumentId == documentId
                            && c.IsEmbedded
                            && (c.EmbeddingModelVersion == null
                                || c.EmbeddingModelVersion == string.Empty
                                || c.EmbeddingModelVersion == LegacyEmbeddingModelVersion))
                .OrderBy(c => c.ChunkIndex)
                .Select(c => new { c.Id, c.Content })
                .ToListAsync(ct);

            if (chunks.Count == 0)
            {
                return true;
            }

            // A chunk without text has nothing to embed: it is marked as not embedded instead,
            // so it is not picked up again.
            var withText = chunks.Where(c => !string.IsNullOrWhiteSpace(c.Content)).ToList();
            var withoutText = chunks.Where(c => string.IsNullOrWhiteSpace(c.Content)).Select(c => c.Id).ToList();
            var batchSize = Math.Max(1, _ragConfiguration?.EmbeddingBatchSize ?? FallbackEmbeddingBatchSize);
            var vectors = new List<float[]>(withText.Count);

            for (var start = 0; start < withText.Count; start += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var texts = withText.Skip(start).Take(batchSize).Select(c => c.Content).ToList();
                var embeddings = await _embeddingService.EmbedBatchAsync(texts, ct);
                if (embeddings.Count != texts.Count)
                {
                    throw new InvalidOperationException(
                        $"The embedding service returned {embeddings.Count} embeddings for {texts.Count} chunks.");
                }

                vectors.AddRange(embeddings);
            }

            // Read after embedding: the version carries the vector size the provider returned.
            var modelVersion = _embeddingService.ModelVersion;
            if (string.IsNullOrEmpty(modelVersion) || modelVersion == LegacyEmbeddingModelVersion)
            {
                throw new InvalidOperationException("The embedding service reported no usable model version.");
            }

            await _vectorStore.DeleteEmbeddingsForDocumentAsync(documentId, chunks.Select(c => c.Id).ToList(), ct);

            var embeddedAt = DateTime.UtcNow;
            for (var i = 0; i < withText.Count; i++)
            {
                var chunkId = withText[i].Id;
                var dimensions = vectors[i].Length;
                var rowId = await _vectorStore.InsertEmbeddingAsync(chunkId, vectors[i], ct);

                var stamped = await _db.DocumentChunks
                    .Where(c => c.Id == chunkId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.VectorRowId, rowId)
                        .SetProperty(c => c.EmbeddingModelVersion, modelVersion)
                        .SetProperty(c => c.EmbeddingDimensions, dimensions)
                        .SetProperty(c => c.EmbeddedAt, embeddedAt), ct);

                if (stamped == 0)
                {
                    // The chunk was removed meanwhile (a re-index or delete): drop its vector.
                    await _vectorStore.DeleteEmbeddingAsync(chunkId, ct);
                }
            }

            if (withoutText.Count > 0)
            {
                await _db.DocumentChunks
                    .Where(c => withoutText.Contains(c.Id))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.IsEmbedded, false)
                        .SetProperty(c => c.VectorRowId, (long?)null)
                        .SetProperty(c => c.EmbeddingModelVersion, (string?)null)
                        .SetProperty(c => c.EmbeddingDimensions, (int?)null)
                        .SetProperty(c => c.EmbeddedAt, (DateTime?)null), ct);
            }

            await RefreshTrackedChunksAsync(chunks.Select(c => c.Id).ToHashSet(), ct);

            _logger.Information(
                "Re-embedded {Count} chunks of document {DocumentId} that predate embedding model versions, as {ModelVersion}",
                withText.Count, documentId, modelVersion);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex,
                "Could not re-embed the chunks of document {DocumentId}; retrying in {Minutes} minutes",
                documentId, LegacyReembedBackoff.TotalMinutes);
            return false;
        }

        // Result sets cached while these chunks were excluded are stale.
        _searchCacheService?.InvalidateAll();
        return true;
    }

    /// <summary>
    /// Set-based updates bypass the change tracker, so a copy of one of these chunks that the
    /// shared context already tracks is reloaded; otherwise tracked queries would keep serving
    /// the old values. Copies with unsaved changes are left alone.
    /// </summary>
    private async Task RefreshTrackedChunksAsync(IReadOnlySet<long> chunkIds, CancellationToken ct)
    {
        var tracked = _db.ChangeTracker.Entries<DocumentChunkEntity>()
            .Where(entry => entry.State == EntityState.Unchanged && chunkIds.Contains(entry.Entity.Id))
            .ToList();

        foreach (var entry in tracked)
        {
            await entry.ReloadAsync(ct);
        }
    }

    /// <summary>
    /// Initializes the vector store once. Failures are logged and remembered, never thrown,
    /// so the next document retries and, if the store is still unavailable, fails loudly
    /// with the reason.
    /// </summary>
    private async Task EnsureVectorStoreReadyAsync(CancellationToken ct)
    {
        if (_vectorStoreReady)
        {
            return;
        }

        await _vectorStoreGate.WaitAsync(ct);
        try
        {
            if (_vectorStoreReady)
            {
                return;
            }

            await _vectorStore.InitializeAsync(ct);
            _vectorStoreReady = true;
            _vectorStoreError = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _vectorStoreError = ex.Message;
            _logger.Error(ex, "Vector store failed to initialize; documents cannot be indexed until it is available");
        }
        finally
        {
            _vectorStoreGate.Release();
        }
    }

    /// <summary>
    /// Runs the document's processor over its source file.
    /// </summary>
    private async Task<ProcessedDocument> ExtractAsync(DocumentEntity document, CancellationToken ct)
    {
        // Validate source file still exists
        if (!File.Exists(document.FilePath))
        {
            throw new FileNotFoundException($"Source file no longer exists: {document.FilePath}", document.FilePath);
        }

        // Find the appropriate processor and re-extract text
        var processor = FindProcessorFor(document.FilePath);
        if (processor is null)
        {
            throw new NotSupportedException(
                $"No processor found for file type: {Path.GetExtension(document.FilePath)}");
        }

        return await processor.ProcessAsync(document.FilePath, ct);
    }

    private void StoreHandoff(long documentId, ProcessedDocument extracted)
    {
        var length = extracted.ExtractedText?.Length ?? 0;

        lock (_handoffLock)
        {
            if (_extractionHandoff.Remove(documentId, out var previous))
            {
                _handoffCharacters -= previous.ExtractedText?.Length ?? 0;
            }

            if (_handoffCharacters + length > MaxHandoffCharacters)
            {
                return; // Over budget: this document is re-extracted when it is processed.
            }

            _extractionHandoff[documentId] = extracted;
            _handoffCharacters += length;
        }
    }

    private ProcessedDocument? TakeHandoff(long documentId)
    {
        lock (_handoffLock)
        {
            if (!_extractionHandoff.Remove(documentId, out var extracted))
            {
                return null;
            }

            _handoffCharacters -= extracted.ExtractedText?.Length ?? 0;
            return extracted;
        }
    }

    /// <summary>
    /// Returns the handed-over extraction when it still describes the file on disk: same
    /// size and last-write time as recorded on the document when it was extracted. A file
    /// that has disappeared since keeps the extraction, which was taken from it at import.
    /// </summary>
    private ProcessedDocument? TakeUsableHandoff(DocumentEntity document)
    {
        var extracted = TakeHandoff(document.Id);
        if (extracted is null)
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(document.FilePath);
            if (!fileInfo.Exists)
            {
                return extracted;
            }

            if (fileInfo.Length == document.FileSizeBytes
                && fileInfo.LastWriteTimeUtc.Ticks == document.FileModifiedAt.Ticks)
            {
                return extracted;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not compare document {DocumentId} with its source file", document.Id);
        }

        _logger.Debug("Source of document {DocumentId} changed since extraction; extracting again", document.Id);
        return null;
    }

    /// <summary>
    /// Finds the first registered processor that can handle the given file path.
    /// </summary>
    private IDocumentProcessor? FindProcessorFor(string filePath)
    {
        foreach (var processor in _processors)
        {
            if (processor.CanProcess(filePath))
            {
                return processor;
            }
        }

        return null;
    }

    /// <summary>
    /// Raises the <see cref="ProgressChanged"/> event with the given state.
    /// </summary>
    private void RaiseProgressChanged(int queueLength, int processed, string? currentDocument, double? percentComplete = null)
    {
        var args = new IndexingProgressEventArgs
        {
            QueueLength = queueLength,
            Processed = processed,
            CurrentDocument = currentDocument,
            PercentComplete = percentComplete
        };

        InvokeHandlers(ProgressChanged, args, nameof(ProgressChanged));
    }

    private void RaiseDocumentIndexed(long documentId) =>
        InvokeHandlers(DocumentIndexed, documentId, nameof(DocumentIndexed));

    private void RaiseDocumentIndexingFailed(long documentId, string error) =>
        InvokeHandlers(
            DocumentIndexingFailed,
            new DocumentIndexingFailedEventArgs(documentId, error),
            nameof(DocumentIndexingFailed));

    /// <summary>
    /// Calls every subscriber, logging instead of propagating a subscriber's exception. The
    /// events are raised from inside the pipeline, where an escaping exception would mark a
    /// document that indexed correctly as failed, or stop the failure from being recorded.
    /// </summary>
    private void InvokeHandlers<TArgs>(EventHandler<TArgs>? handlers, TArgs args, string eventName)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<TArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "{EventName} handler failed", eventName);
            }
        }
    }
}

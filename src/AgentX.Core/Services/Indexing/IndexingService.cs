using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using AgentX.Core.Search;
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
/// overwhelming local model inference.
/// </summary>
public sealed class IndexingService : IIndexingService
{
    private readonly AgentXDbContext _db;
    private readonly IEnumerable<IDocumentProcessor> _processors;
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ISettingsService _settingsService;
    private readonly IKeywordSearchService _keywordSearchService;
    private readonly IAutoTagService _autoTagService;
    private readonly ILogger _logger;

    // Background processing infrastructure
    private readonly Channel<long> _documentQueue;
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _backgroundTask;

    // State tracking
    private int _processedCount;
    private volatile bool _isProcessing;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsProcessing => _isProcessing;

    /// <inheritdoc />
    public event EventHandler<IndexingProgressEventArgs>? ProgressChanged;

    /// <inheritdoc />
    public event EventHandler<long>? DocumentIndexed;

    /// <summary>
    /// Batch size for embedding generation. Keeps memory usage bounded while
    /// still benefiting from batch inference when supported by the model.
    /// </summary>
    private const int EmbeddingBatchSize = 16;

    public IndexingService(
        AgentXDbContext db,
        IEnumerable<IDocumentProcessor> processors,
        IChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ISettingsService settingsService,
        IKeywordSearchService keywordSearchService,
        IAutoTagService autoTagService,
        ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processors = processors ?? throw new ArgumentNullException(nameof(processors));
        _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _keywordSearchService = keywordSearchService ?? throw new ArgumentNullException(nameof(keywordSearchService));
        _autoTagService = autoTagService ?? throw new ArgumentNullException(nameof(autoTagService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Unbounded channel: items are cheap (just a long ID) and we want to accept
        // enqueue requests without blocking the caller. Processing is serialized.
        _documentQueue = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.Information("Initializing IndexingService");

        // Initialize the vector store (creates tables, loads indexes, etc.)
        await _vectorStore.InitializeAsync(ct);

        // Re-enqueue any documents that were left in "processing" state (from a previous crash)
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

        if (staleJobs.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        // Enqueue any pending documents that have no indexing job yet
        var pendingDocIds = await _db.Documents
            .Where(d => d.IndexingStatus == "pending")
            .Select(d => d.Id)
            .ToListAsync(ct);

        foreach (var docId in pendingDocIds)
        {
            // Check if there is already a queued job for this document
            var hasQueuedJob = await _db.IndexingJobs
                .AnyAsync(j => j.DocumentId == docId && (j.Status == "queued" || j.Status == "processing"), ct);

            if (!hasQueuedJob)
            {
                _documentQueue.Writer.TryWrite(docId);
                _logger.Debug("Re-enqueued pending document {DocumentId} for indexing", docId);
            }
        }

        // Start the background processing loop
        _backgroundTask = Task.Run(() => ProcessQueueAsync(_shutdownCts.Token), ct);

        _logger.Information("IndexingService initialized. {PendingCount} documents queued for processing", pendingDocIds.Count);
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
        _documentQueue.Writer.TryWrite(documentId);

        var queueLength = await GetQueueLengthAsync();
        RaiseProgressChanged(queueLength, _processedCount, document.FileName);

        _logger.Information("Document {DocumentId} ({FileName}) enqueued for indexing", documentId, document.FileName);
    }

    /// <inheritdoc />
    public async Task ReindexAllAsync(
        IProgress<(int Processed, int Total)>? progress = null,
        CancellationToken ct = default)
    {
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
            _documentQueue.Writer.TryWrite(docId);

            processed++;
            progress?.Report((processed, total));
        }

        _logger.Information("Re-index enqueued {Processed}/{Total} documents", processed, total);
    }

    /// <inheritdoc />
    public async Task<int> GetQueueLengthAsync()
    {
        return await _db.IndexingJobs
            .CountAsync(j => j.Status == "queued" || j.Status == "processing");
    }

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
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        _logger.Debug("Background indexing loop started");

        try
        {
            await foreach (var documentId in _documentQueue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    _isProcessing = true;
                    await ProcessSingleDocumentAsync(documentId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.Information("Indexing loop cancelled during document {DocumentId}", documentId);
                    break;
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
            return;
        }

        _logger.Information("Starting indexing pipeline for document {DocumentId} ({FileName})", documentId, document.FileName);

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

        RaiseProgressChanged(await GetQueueLengthAsync(), _processedCount, document.FileName);

        try
        {
            // 1. Validate source file still exists
            if (!File.Exists(document.FilePath))
            {
                throw new FileNotFoundException($"Source file no longer exists: {document.FilePath}", document.FilePath);
            }

            // 2. Find the appropriate processor and re-extract text
            var processor = FindProcessorFor(document.FilePath);
            if (processor is null)
            {
                throw new NotSupportedException(
                    $"No processor found for file type: {Path.GetExtension(document.FilePath)}");
            }

            var processed = await processor.ProcessAsync(document.FilePath, ct);

            // 3. Get chunking settings
            var settings = await _settingsService.GetSettingsAsync();
            var chunkSize = settings.ChunkSize;
            var chunkOverlap = settings.ChunkOverlap;

            // 4. Chunk the document
            var chunks = _chunkingService.ChunkDocument(processed, chunkSize, chunkOverlap);
            _logger.Debug("Generated {ChunkCount} chunks for document {DocumentId}", chunks.Count, documentId);

            // 5. Delete any existing chunks (in case of re-index)
            if (document.Chunks.Count > 0)
            {
                // Remove from FTS5 first (non-fatal)
                try
                {
                    await _keywordSearchService.RemoveDocumentFromFtsAsync(documentId, ct);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to remove document {DocumentId} from FTS5 during re-index", documentId);
                }

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

            for (var batchStart = 0; batchStart < chunkEntities.Count; batchStart += EmbeddingBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batchEnd = Math.Min(batchStart + EmbeddingBatchSize, chunkEntities.Count);
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
                    embeddingsGenerated++;
                }

                await _db.SaveChangesAsync(ct);
            }

            stopwatch.Stop();

            // 9. Update document with indexing results
            document.ChunkCount = chunkEntities.Count;
            document.IndexingStatus = "completed";
            document.IndexingError = null;
            document.LastIndexedAt = DateTime.UtcNow;

            // 10. Update the indexing job
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

            // Raise events
            var queueLength = await GetQueueLengthAsync();
            RaiseProgressChanged(queueLength, _processedCount, null);
            DocumentIndexed?.Invoke(this, documentId);

            // Auto-tag the document (non-fatal — must not block the indexing pipeline)
            try
            {
                await _autoTagService.ApplyAutoTagsAsync(documentId, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Auto-tagging failed for document {DocumentId}", documentId);
            }

            // Index document chunks into FTS5 for keyword search (non-fatal)
            try
            {
                await _keywordSearchService.IndexDocumentChunksAsync(documentId, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "FTS5 keyword indexing failed for document {DocumentId}", documentId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

            RaiseProgressChanged(await GetQueueLengthAsync(), _processedCount, null);
        }
    }

    // ─── Private Helpers ─────────────────────────────────────────────

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
        ProgressChanged?.Invoke(this, new IndexingProgressEventArgs
        {
            QueueLength = queueLength,
            Processed = processed,
            CurrentDocument = currentDocument,
            PercentComplete = percentComplete
        });
    }
}

using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Indexing;

/// <summary>
/// EF Core-backed implementation of the indexing job queue.
/// Persists <see cref="IndexingJobEntity"/> records to SQLite, providing
/// durable queue semantics that survive application restarts.
/// </summary>
public sealed class IndexingQueueService : IIndexingQueueService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _logger;

    /// <summary>
    /// Synchronizes dequeue operations to ensure no two threads pick up the same job.
    /// </summary>
    private readonly SemaphoreSlim _dequeueLock = new(1, 1);

    public IndexingQueueService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(long documentId)
    {
        // Avoid creating duplicate queued jobs for the same document
        var existingJob = await _db.IndexingJobs
            .AnyAsync(j => j.DocumentId == documentId && (j.Status == "queued" || j.Status == "processing"));

        if (existingJob)
        {
            _logger.Debug("Document {DocumentId} already has a pending indexing job; skipping enqueue", documentId);
            return;
        }

        var job = new IndexingJobEntity
        {
            DocumentId = documentId,
            Status = "queued",
            QueuedAt = DateTime.UtcNow
        };

        _db.IndexingJobs.Add(job);
        await _db.SaveChangesAsync();

        _logger.Debug("Enqueued indexing job {JobId} for document {DocumentId}", job.Id, documentId);
    }

    /// <inheritdoc />
    public async Task EnqueueBatchAsync(IReadOnlyList<long> documentIds)
    {
        if (documentIds is null || documentIds.Count == 0)
        {
            return;
        }

        // Find documents that already have pending jobs
        var existingDocIds = await _db.IndexingJobs
            .Where(j => documentIds.Contains(j.DocumentId) && (j.Status == "queued" || j.Status == "processing"))
            .Select(j => j.DocumentId)
            .Distinct()
            .ToListAsync();

        var existingDocIdSet = new HashSet<long>(existingDocIds);
        var newJobs = new List<IndexingJobEntity>();
        var now = DateTime.UtcNow;

        foreach (var docId in documentIds)
        {
            if (existingDocIdSet.Contains(docId))
            {
                _logger.Debug("Document {DocumentId} already has a pending indexing job; skipping", docId);
                continue;
            }

            newJobs.Add(new IndexingJobEntity
            {
                DocumentId = docId,
                Status = "queued",
                QueuedAt = now
            });
        }

        if (newJobs.Count > 0)
        {
            _db.IndexingJobs.AddRange(newJobs);
            await _db.SaveChangesAsync();
            _logger.Information("Enqueued {Count} indexing jobs in batch", newJobs.Count);
        }
    }

    /// <inheritdoc />
    public async Task<IndexingJobEntity?> DequeueAsync(CancellationToken ct = default)
    {
        await _dequeueLock.WaitAsync(ct);
        try
        {
            // Find the oldest queued job
            var job = await _db.IndexingJobs
                .Include(j => j.Document)
                .Where(j => j.Status == "queued")
                .OrderBy(j => j.QueuedAt)
                .FirstOrDefaultAsync(ct);

            if (job is null)
            {
                return null;
            }

            // Atomically transition to "processing"
            job.Status = "processing";
            job.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.Debug(
                "Dequeued indexing job {JobId} for document {DocumentId} ({FileName})",
                job.Id, job.DocumentId, job.Document?.FileName ?? "unknown");

            return job;
        }
        finally
        {
            _dequeueLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(long jobId, int chunksProcessed, int embeddingsGenerated, double processingTimeMs)
    {
        var job = await _db.IndexingJobs.FindAsync(jobId);
        if (job is null)
        {
            _logger.Warning("Attempted to mark non-existent indexing job {JobId} as completed", jobId);
            return;
        }

        job.Status = "completed";
        job.CompletedAt = DateTime.UtcNow;
        job.ChunksProcessed = chunksProcessed;
        job.EmbeddingsGenerated = embeddingsGenerated;
        job.ProcessingTimeMs = processingTimeMs;

        await _db.SaveChangesAsync();

        _logger.Debug(
            "Indexing job {JobId} completed: {Chunks} chunks, {Embeddings} embeddings in {TimeMs:F0}ms",
            jobId, chunksProcessed, embeddingsGenerated, processingTimeMs);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(long jobId, string errorMessage)
    {
        var job = await _db.IndexingJobs.FindAsync(jobId);
        if (job is null)
        {
            _logger.Warning("Attempted to mark non-existent indexing job {JobId} as failed", jobId);
            return;
        }

        job.Status = "failed";
        job.CompletedAt = DateTime.UtcNow;
        job.ErrorMessage = errorMessage;

        await _db.SaveChangesAsync();

        _logger.Warning("Indexing job {JobId} failed: {Error}", jobId, errorMessage);
    }

    /// <inheritdoc />
    public async Task<int> GetPendingCountAsync()
    {
        return await _db.IndexingJobs
            .CountAsync(j => j.Status == "queued" || j.Status == "processing");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexingJobEntity>> GetRecentJobsAsync(int limit = 50)
    {
        return await _db.IndexingJobs
            .Include(j => j.Document)
            .OrderByDescending(j => j.QueuedAt)
            .Take(limit)
            .ToListAsync();
    }
}

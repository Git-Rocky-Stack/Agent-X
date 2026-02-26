using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Indexing;

/// <summary>
/// Manages the persistent indexing job queue backed by the database.
/// Provides enqueue, dequeue, and status update operations for
/// <see cref="IndexingJobEntity"/> records.
/// </summary>
public interface IIndexingQueueService
{
    /// <summary>
    /// Creates a new indexing job for the specified document with status "queued".
    /// </summary>
    /// <param name="documentId">The document to queue for indexing.</param>
    Task EnqueueAsync(long documentId);

    /// <summary>
    /// Creates indexing jobs for multiple documents at once.
    /// </summary>
    /// <param name="documentIds">The documents to queue for indexing.</param>
    Task EnqueueBatchAsync(IReadOnlyList<long> documentIds);

    /// <summary>
    /// Atomically dequeues the oldest queued job by setting its status to "processing"
    /// and recording the start time.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dequeued job, or null if the queue is empty.</returns>
    Task<IndexingJobEntity?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks a job as successfully completed with processing metrics.
    /// </summary>
    /// <param name="jobId">The ID of the indexing job.</param>
    /// <param name="chunksProcessed">Number of chunks created.</param>
    /// <param name="embeddingsGenerated">Number of embeddings generated.</param>
    /// <param name="processingTimeMs">Total processing time in milliseconds.</param>
    Task MarkCompletedAsync(long jobId, int chunksProcessed, int embeddingsGenerated, double processingTimeMs);

    /// <summary>
    /// Marks a job as failed with an error message.
    /// </summary>
    /// <param name="jobId">The ID of the indexing job.</param>
    /// <param name="errorMessage">A description of the error that caused the failure.</param>
    Task MarkFailedAsync(long jobId, string errorMessage);

    /// <summary>
    /// Returns the count of jobs that are either queued or currently processing.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Returns the most recent indexing jobs, ordered by queued time descending.
    /// </summary>
    /// <param name="limit">Maximum number of jobs to return (default 50).</param>
    Task<IReadOnlyList<IndexingJobEntity>> GetRecentJobsAsync(int limit = 50);
}

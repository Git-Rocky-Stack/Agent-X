namespace AgentX.Core.Services.Indexing;

/// <summary>
/// Manages the background indexing pipeline: processes pending documents by chunking their
/// extracted text, generating embeddings, and storing vectors for semantic search.
/// </summary>
public interface IIndexingService : IDisposable
{
    /// <summary>
    /// Initializes the indexing service: sets up the vector store and starts
    /// the background processing loop for queued indexing jobs.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Indexes a single document: re-processes the file, chunks the text,
    /// generates embeddings, and stores them in the vector database.
    /// </summary>
    /// <param name="documentId">The ID of the document to index.</param>
    /// <param name="ct">Cancellation token.</param>
    Task IndexDocumentAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Re-indexes all completed documents. Useful after changing chunking or embedding settings.
    /// </summary>
    /// <param name="progress">Optional progress reporter with (processed, total) counts.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReindexAllAsync(IProgress<(int Processed, int Total)>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of documents currently waiting in the indexing queue.
    /// </summary>
    Task<int> GetQueueLengthAsync();

    /// <summary>
    /// Returns the total number of documents that have been successfully indexed.
    /// </summary>
    Task<int> GetProcessedCountAsync();

    /// <summary>
    /// Indicates whether the indexing service is currently processing a document.
    /// </summary>
    bool IsProcessing { get; }

    /// <summary>
    /// Raised when the indexing queue state changes (item queued, processing, completed, etc.).
    /// </summary>
    event EventHandler<IndexingProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Raised when a document has been successfully indexed.
    /// The event argument is the document ID. Raised on the indexing thread.
    /// </summary>
    event EventHandler<long>? DocumentIndexed;

    /// <summary>
    /// Raised when indexing a document failed, after the document has been saved as "failed"
    /// with the reason. Raised on the indexing thread.
    /// </summary>
    event EventHandler<DocumentIndexingFailedEventArgs>? DocumentIndexingFailed;
}

/// <summary>
/// Event data for a document whose indexing failed.
/// </summary>
public sealed class DocumentIndexingFailedEventArgs : EventArgs
{
    public DocumentIndexingFailedEventArgs(long documentId, string error)
    {
        DocumentId = documentId;
        Error = error;
    }

    /// <summary>The document that could not be indexed.</summary>
    public long DocumentId { get; }

    /// <summary>The reason, as saved in the document's indexing error.</summary>
    public string Error { get; }
}

/// <summary>
/// Event data for indexing progress updates.
/// </summary>
public class IndexingProgressEventArgs : EventArgs
{
    /// <summary>Number of items remaining in the indexing queue.</summary>
    public int QueueLength { get; init; }

    /// <summary>Number of items processed so far in the current batch or since initialization.</summary>
    public int Processed { get; init; }

    /// <summary>The file name of the document currently being processed (null if idle).</summary>
    public string? CurrentDocument { get; init; }

    /// <summary>Overall completion percentage (0-100), or null if indeterminate.</summary>
    public double? PercentComplete { get; init; }
}

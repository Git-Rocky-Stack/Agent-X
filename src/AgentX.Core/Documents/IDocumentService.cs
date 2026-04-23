using AgentX.Core.Data.Entities;

namespace AgentX.Core.Documents;

/// <summary>
/// Orchestrates the document import pipeline: file validation, text extraction,
/// metadata capture, and DB record creation. The document is left in "pending"
/// status for the indexing pipeline to pick up for chunking and embedding.
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Imports a single file: validates, hashes, extracts text, creates DocumentEntity.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to import.</param>
    /// <param name="collectionId">Optional collection to associate the document with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created DocumentEntity with status "pending".</returns>
    Task<DocumentEntity> ImportFileAsync(string filePath, long? collectionId = null, CancellationToken ct = default);

    /// <summary>
    /// Imports a file produced by a DataConnector plugin (calendar, email, etc.).
    /// Unlike <see cref="ImportFileAsync"/>, this preserves the semantic file type
    /// (e.g. "CalendarEvent", "EmailMessage") rather than deriving it from the extension,
    /// so that search filtering by plugin type works correctly.
    /// </summary>
    /// <param name="filePath">Absolute path to the temp file on disk.</param>
    /// <param name="fileTypeOverride">Semantic file type to store (e.g. "CalendarEvent").</param>
    /// <param name="displayName">Display name for the document (overrides filename).</param>
    /// <param name="sourceUrl">Optional URL to the original item (e.g. web link to calendar event).</param>
    /// <param name="collectionId">Optional collection to associate the document with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created DocumentEntity with status "pending".</returns>
    Task<DocumentEntity> ImportExternalContentAsync(
        string filePath,
        string fileTypeOverride,
        string displayName,
        string? sourceUrl = null,
        long? collectionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Imports multiple files, reporting progress as each file completes.
    /// </summary>
    /// <param name="filePaths">Absolute paths to the files to import.</param>
    /// <param name="collectionId">Optional collection to associate all documents with.</param>
    /// <param name="progress">Optional progress reporter (number of files completed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of created DocumentEntity records.</returns>
    Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(
        IReadOnlyList<string> filePaths,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single document by its primary key.
    /// </summary>
    Task<DocumentEntity?> GetDocumentAsync(long documentId);

    /// <summary>
    /// Returns a short text preview suitable for launching a workflow from a document.
    /// Prefers the stored summary, then falls back to the first chunk of indexed content.
    /// </summary>
    Task<string?> GetDocumentPreviewTextAsync(long documentId, int maxChars = 1800, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all documents with optional filtering by file type and indexing status.
    /// Results are ordered by ImportedAt descending (newest first).
    /// </summary>
    /// <param name="fileTypeFilter">Optional file type to filter by (e.g., "pdf").</param>
    /// <param name="statusFilter">Optional indexing status to filter by (e.g., "completed").</param>
    /// <param name="tagFilter">Optional tag name to filter documents that have this tag assigned.</param>
    /// <param name="collectionId">Optional collection ID to filter documents belonging to a specific collection.</param>
    /// <param name="importedAfter">Optional lower bound (inclusive) for the ImportedAt date.</param>
    /// <param name="importedBefore">Optional upper bound (inclusive) for the ImportedAt date.</param>
    /// <param name="sortBy">Sort field: "name", "date" (default), "size", or "type".</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(
        string? fileTypeFilter = null,
        string? statusFilter = null,
        string? tagFilter = null,
        long? collectionId = null,
        DateTime? importedAfter = null,
        DateTime? importedBefore = null,
        string? sortBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all documents belonging to a specific collection.
    /// </summary>
    Task<IReadOnlyList<DocumentEntity>> GetDocumentsByCollectionAsync(long collectionId);

    /// <summary>
    /// Retrieves the most recently imported documents, ordered newest first.
    /// Used by overview surfaces that only need a small recent slice.
    /// </summary>
    Task<IReadOnlyList<DocumentEntity>> GetRecentDocumentsAsync(int limit = 5, CancellationToken ct = default);

    /// <summary>
    /// Deletes a document, its chunks, and any associated vector embeddings.
    /// </summary>
    Task DeleteDocumentAsync(long documentId);

    /// <summary>
    /// Re-processes a document by deleting existing chunks and re-extracting text.
    /// Resets the document status to "pending" for re-indexing.
    /// </summary>
    Task ReindexDocumentAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Looks up a document by its SHA-256 content hash (for duplicate detection).
    /// </summary>
    Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash);

    /// <summary>
    /// Returns the total number of documents in the knowledge vault.
    /// </summary>
    Task<long> GetTotalDocumentCountAsync();

    /// <summary>
    /// Returns the total storage consumed by all imported documents in bytes.
    /// </summary>
    Task<long> GetTotalStorageBytesAsync();

    /// <summary>
    /// Returns a distribution of file types and their counts (e.g., {"pdf": 12, "docx": 5}).
    /// </summary>
    Task<Dictionary<string, int>> GetFileTypeDistributionAsync();

    /// <summary>
    /// Checks whether the given file can be processed by any registered document processor.
    /// </summary>
    bool CanProcess(string filePath);

    /// <summary>
    /// Returns the union of all supported file extensions across all registered processors.
    /// </summary>
    IReadOnlySet<string> GetSupportedExtensions();

    // ── Duplicate Detection ──────────────────────────────────────

    /// <summary>
    /// Checks an incoming file against the knowledge vault for duplicate content
    /// using SHA-256 hash comparison. Returns a result indicating whether the file
    /// is an exact duplicate of an existing document.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DuplicateCheckResult"/> indicating whether a duplicate exists.</returns>
    Task<DuplicateCheckResult> CheckForDuplicateAsync(string filePath, CancellationToken ct = default);

    // ── Bulk Operations ──────────────────────────────────────────

    /// <summary>
    /// Deletes multiple documents by their IDs. Failures for individual documents
    /// are logged but do not abort the batch.
    /// </summary>
    Task BulkDeleteAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default);

    /// <summary>
    /// Re-indexes multiple documents by their IDs. Each document is reset to
    /// "pending" status. Failures for individual documents are logged but do not
    /// abort the batch.
    /// </summary>
    Task BulkReindexAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default);

    /// <summary>
    /// Associates multiple documents with the specified collection. Failures for
    /// individual documents are logged but do not abort the batch.
    /// </summary>
    Task BulkAssignToCollectionAsync(IReadOnlyList<long> documentIds, long collectionId, CancellationToken ct = default);
}

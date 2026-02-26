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
    /// Retrieves all documents with optional filtering by file type and indexing status.
    /// Results are ordered by ImportedAt descending (newest first).
    /// </summary>
    /// <param name="fileTypeFilter">Optional file type to filter by (e.g., "pdf").</param>
    /// <param name="statusFilter">Optional indexing status to filter by (e.g., "completed").</param>
    Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(string? fileTypeFilter = null, string? statusFilter = null);

    /// <summary>
    /// Retrieves all documents belonging to a specific collection.
    /// </summary>
    Task<IReadOnlyList<DocumentEntity>> GetDocumentsByCollectionAsync(long collectionId);

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
}

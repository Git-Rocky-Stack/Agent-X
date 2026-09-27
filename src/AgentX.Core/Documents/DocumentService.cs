using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Documents;

/// <summary>
/// Orchestrates document import: validates files, extracts text via the appropriate
/// <see cref="IDocumentProcessor"/>, and persists <see cref="DocumentEntity"/> records
/// with status "pending". Chunking and embedding are handled downstream by the indexing pipeline.
/// </summary>
public sealed class DocumentService : IDocumentService
{
    private readonly AgentXDbContext _db;
    private readonly IReadOnlyList<IDocumentProcessor> _processors;
    private readonly IPluginDocumentProcessorSource? _pluginProcessors;
    private readonly ISettingsService _settingsService;
    private readonly IVectorStore? _vectorStore;
    private readonly IKeywordSearchService? _keywordSearchService;
    private readonly ISearchCacheService? _searchCacheService;
    private readonly ILogger _logger;

    /// <summary>
    /// Lazily computed union of all supported extensions across every registered processor.
    /// </summary>
    private readonly Lazy<IReadOnlySet<string>> _allSupportedExtensions;

    /// <inheritdoc />
    public event EventHandler<DocumentPendingIndexingEventArgs>? DocumentPendingIndexing;

    public DocumentService(
        AgentXDbContext db,
        IEnumerable<IDocumentProcessor> processors,
        ISettingsService settingsService,
        ILogger logger,
        IVectorStore? vectorStore = null,
        IKeywordSearchService? keywordSearchService = null,
        ISearchCacheService? searchCacheService = null,
        IPluginDocumentProcessorSource? pluginProcessors = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processors = (processors ?? throw new ArgumentNullException(nameof(processors))).ToList().AsReadOnly();
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vectorStore = vectorStore;
        _keywordSearchService = keywordSearchService;
        _searchCacheService = searchCacheService;
        _pluginProcessors = pluginProcessors;

        _allSupportedExtensions = new Lazy<IReadOnlySet<string>>(() =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var processor in _processors)
            {
                foreach (var ext in processor.SupportedExtensions)
                {
                    set.Add(ext);
                }
            }
            return set;
        });
    }

    /// <inheritdoc />
    public Task<DocumentEntity> ImportFileAsync(
        string filePath,
        long? collectionId = null,
        CancellationToken ct = default)
        => ImportFileCoreAsync(filePath, collectionId, allowDuplicate: false, ct);

    /// <summary>
    /// Imports one file. When <paramref name="allowDuplicate"/> is false, a file whose content
    /// matches an existing document throws <see cref="DuplicateDocumentException"/>; when true
    /// (the user explicitly chose to import duplicates) it is imported as a separate document.
    /// </summary>
    private async Task<DocumentEntity> ImportFileCoreAsync(
        string filePath,
        long? collectionId,
        bool allowDuplicate,
        CancellationToken ct)
    {
        // 1. Validate file exists
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The file does not exist: {filePath}", filePath);
        }

        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            throw new InvalidOperationException($"Cannot determine file type for: {filePath}");
        }

        // 2. Compute file hash for duplicate detection
        _logger.Debug("Computing hash for file: {FilePath}", filePath);
        var contentHash = await HashHelper.ComputeFileHashAsync(filePath, ct);

        // 3. Check for duplicate by content hash
        var existingDoc = await GetDocumentByHashAsync(contentHash);
        if (existingDoc is not null)
        {
            if (!allowDuplicate)
            {
                _logger.Information(
                    "Duplicate detected: {FilePath} matches existing document {DocumentId} ({FileName})",
                    filePath, existingDoc.Id, existingDoc.FileName);
                throw new DuplicateDocumentException(existingDoc.Id, existingDoc.FileName);
            }

            _logger.Information(
                "Importing {FilePath} although it matches existing document {DocumentId} ({FileName}); duplicates were allowed",
                filePath, existingDoc.Id, existingDoc.FileName);
        }

        // 4. Find the appropriate processor
        var processor = FindProcessorFor(filePath);
        if (processor is null)
        {
            throw new NotSupportedException(
                $"No processor found for file type '{extension}'. Supported types: {string.Join(", ", GetSupportedExtensions())}");
        }

        // 5. Extract text and metadata. A file the processor cannot read (encrypted, corrupt,
        //    no text layer) is still recorded, as a failed document carrying the reason, so
        //    the problem is visible in the vault instead of importing as a zero-word success.
        _logger.Debug("Processing file with {Processor}: {FilePath}", processor.GetType().Name, filePath);
        var (processed, extractionError) = await TryExtractAsync(processor, filePath, ct);

        // 6. Gather file system metadata
        var fileInfo = new FileInfo(filePath);
        var metadataJson = processed is null ? null : SerializeMetadata(processed.Metadata);

        // 7. Create DocumentEntity
        var entity = new DocumentEntity
        {
            FileName = Path.GetFileName(filePath),
            FilePath = Path.GetFullPath(filePath),
            FileType = extension.TrimStart('.').ToLowerInvariant(),
            MimeType = GetMimeType(extension),
            FileSizeBytes = fileInfo.Length,
            ContentHash = contentHash,
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = fileInfo.LastWriteTimeUtc,
            IndexingStatus = extractionError is null ? "pending" : "failed",
            IndexingError = extractionError,
            PageCount = processed?.PageCount ?? 0,
            WordCount = processed?.WordCount ?? 0,
            ExtractedTitle = processed?.ExtractedTitle,
            Language = processed?.Language,
            MetadataJson = metadataJson
        };

        _db.Documents.Add(entity);
        await _db.SaveChangesAsync(ct);

        if (extractionError is null)
        {
            _logger.Information(
                "Imported document: {FileName} (ID {DocumentId}, {FileType}, {WordCount} words, {PageCount} pages)",
                entity.FileName, entity.Id, entity.FileType, entity.WordCount, entity.PageCount);
        }
        else
        {
            _logger.Warning(
                "Imported document {FileName} (ID {DocumentId}) as failed: {Error}",
                entity.FileName, entity.Id, extractionError);
        }

        // 8. Associate with collection if specified
        if (collectionId.HasValue)
        {
            if (!await AddToCollectionAsync(entity.Id, collectionId.Value, ct))
            {
                _logger.Warning("Collection {CollectionId} not found; skipping collection association", collectionId.Value);
            }
            else
            {
                _logger.Debug(
                    "Associated document {DocumentId} with collection {CollectionId}",
                    entity.Id, collectionId.Value);
            }
        }

        if (processed is not null)
        {
            RaisePendingIndexing(entity.Id, processed);
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<DocumentEntity> ImportExternalContentAsync(
        string filePath,
        string fileTypeOverride,
        string displayName,
        string? sourceUrl = null,
        long? collectionId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileTypeOverride);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The file does not exist: {filePath}", filePath);
        }

        // Find a processor for the file (typically TextProcessor for .txt temp files)
        var processor = FindProcessorFor(filePath);
        if (processor is null)
        {
            throw new NotSupportedException(
                $"No processor found for file '{filePath}'. Supported types: {string.Join(", ", GetSupportedExtensions())}");
        }

        var (processed, extractionError) = await TryExtractAsync(processor, filePath, ct);

        var fileInfo = new FileInfo(filePath);
        var contentHash = await HashHelper.ComputeFileHashAsync(filePath, ct);

        // Store the source URL in metadata if provided
        var metadata = processed?.Metadata ?? new DocumentMetadata();
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            metadata.Custom["sourceUrl"] = sourceUrl;
        }

        var metadataJson = SerializeMetadata(metadata);

        var entity = new DocumentEntity
        {
            FileName = displayName,
            FilePath = Path.GetFullPath(filePath),
            FileType = fileTypeOverride, // Preserve semantic type (CalendarEvent, EmailMessage, etc.)
            MimeType = "text/plain",
            FileSizeBytes = fileInfo.Length,
            ContentHash = contentHash,
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = fileInfo.LastWriteTimeUtc,
            IndexingStatus = extractionError is null ? "pending" : "failed",
            IndexingError = extractionError,
            PageCount = processed?.PageCount ?? 0,
            WordCount = processed?.WordCount ?? 0,
            ExtractedTitle = displayName,
            Language = processed?.Language,
            MetadataJson = metadataJson,
        };

        _db.Documents.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.Information(
            "Imported external content: {DisplayName} (ID {DocumentId}, Type={FileType}, {WordCount} words, status {Status})",
            displayName, entity.Id, entity.FileType, entity.WordCount, entity.IndexingStatus);

        // Associate with collection if specified
        if (collectionId.HasValue)
        {
            await AddToCollectionAsync(entity.Id, collectionId.Value, ct);
        }

        if (processed is not null)
        {
            RaisePendingIndexing(entity.Id, processed);
        }

        return entity;
    }

    /// <summary>
    /// Links a document to a collection and keeps the collection's denormalized
    /// <see cref="CollectionEntity.DocumentCount"/> in step. Returns false when the collection
    /// does not exist.
    /// </summary>
    private async Task<bool> AddToCollectionAsync(long documentId, long collectionId, CancellationToken ct)
    {
        var collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId, ct);
        if (collection is null)
        {
            return false;
        }

        _db.DocumentCollections.Add(new DocumentCollectionEntity
        {
            DocumentId = documentId,
            CollectionId = collectionId,
            AddedAt = DateTime.UtcNow
        });

        collection.DocumentCount += 1;
        collection.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Runs the processor, turning an extraction failure into an error message instead of an
    /// exception so the caller can record the document as failed with that reason.
    /// Cancellation still propagates.
    /// </summary>
    private async Task<(ProcessedDocument? Processed, string? Error)> TryExtractAsync(
        IDocumentProcessor processor,
        string filePath,
        CancellationToken ct)
    {
        try
        {
            return (await processor.ProcessAsync(filePath, ct), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Text extraction failed for {FilePath}", filePath);
            return (null, DescribeExtractionFailure(ex));
        }
    }

    /// <summary>
    /// User-facing reason for a failed extraction. Processor messages are already written
    /// for the user; anything else gets a prefix that says which step failed.
    /// </summary>
    private static string DescribeExtractionFailure(Exception ex)
        => ex is DocumentExtractionException ? ex.Message : $"Text extraction failed: {ex.Message}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(
        IReadOnlyList<string> filePaths,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var report = await ImportFilesWithReportAsync(filePaths, collectionId, allowDuplicates: false, progress, ct);
        return report.Imported.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<DocumentImportReport> ImportFilesWithReportAsync(
        IReadOnlyList<string> filePaths,
        long? collectionId = null,
        bool allowDuplicates = false,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var report = new DocumentImportReport();
        if (filePaths is null || filePaths.Count == 0)
        {
            return report;
        }

        var completed = 0;

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var entity = await ImportFileCoreAsync(filePath, collectionId, allowDuplicates, ct);
                report.Imported.Add(entity);
            }
            catch (DuplicateDocumentException ex)
            {
                report.Duplicates.Add(new DocumentImportDuplicate(filePath, ex.ExistingDocumentId, ex.ExistingFileName));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Record and continue with the remaining files rather than aborting the batch
                _logger.Warning(ex, "Failed to import file: {FilePath}", filePath);
                report.Failed.Add(new DocumentImportFailure(filePath, ex.Message));
            }

            completed++;
            progress?.Report(completed);
        }

        _logger.Information(
            "Batch import completed: {Imported}/{Total} files imported ({ExtractionFailed} without readable text), {Duplicates} duplicates skipped, {Failed} failed",
            report.Imported.Count, filePaths.Count, report.ExtractionFailedCount, report.Duplicates.Count, report.Failed.Count);
        return report;
    }

    /// <inheritdoc />
    public async Task<DocumentEntity?> GetDocumentAsync(long documentId)
    {
        return await _db.Documents
            .Include(d => d.DocumentCollections)
            .Include(d => d.DocumentTags)
            .FirstOrDefaultAsync(d => d.Id == documentId);
    }

    /// <inheritdoc />
    public async Task<string?> GetDocumentPreviewTextAsync(long documentId, int maxChars = 1800, CancellationToken ct = default)
    {
        var boundedChars = Math.Clamp(maxChars, 200, 4000);

        var summary = await _db.Documents
            .AsNoTracking()
            .Where(document => document.Id == documentId)
            .Select(document => document.Summary)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            var trimmedSummary = summary.Trim();
            return trimmedSummary.Length <= boundedChars
                ? trimmedSummary
                : $"{trimmedSummary[..boundedChars].TrimEnd()}...";
        }

        var chunkContent = await _db.DocumentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => chunk.Content)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(chunkContent))
        {
            return null;
        }

        var trimmedContent = chunkContent.Trim();
        return trimmedContent.Length <= boundedChars
            ? trimmedContent
            : $"{trimmedContent[..boundedChars].TrimEnd()}...";
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(
        string? fileTypeFilter = null,
        string? statusFilter = null)
    {
        // Delegate to the extended overload with default parameters
        return GetAllDocumentsAsync(fileTypeFilter, statusFilter,
            tagFilter: null, collectionId: null,
            importedAfter: null, importedBefore: null,
            sortBy: null, ct: default);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(
        string? fileTypeFilter = null,
        string? statusFilter = null,
        string? tagFilter = null,
        long? collectionId = null,
        DateTime? importedAfter = null,
        DateTime? importedBefore = null,
        string? sortBy = null,
        CancellationToken ct = default)
    {
        IQueryable<DocumentEntity> query = _db.Documents.AsNoTracking();

        // File type filter: one type, or a category chip ("code", "image") that covers every
        // extension its processor reads. FileType holds the extension without the dot, so the
        // categories never matched anything when compared as a type.
        if (!string.IsNullOrWhiteSpace(fileTypeFilter))
        {
            var fileTypes = DocumentFileTypeFilter.Resolve(fileTypeFilter).ToArray();
            query = query.Where(d => fileTypes.Contains(d.FileType));
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            var normalizedStatus = statusFilter.ToLowerInvariant();
            query = query.Where(d => d.IndexingStatus == normalizedStatus);
        }

        // Tag filter: join through DocumentTags -> Tags
        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            var normalizedTag = tagFilter.Trim().ToLowerInvariant();
            query = query.Where(d =>
                d.DocumentTags.Any(dt => dt.Tag.Name.ToLower() == normalizedTag));
        }

        // Collection filter: join through DocumentCollections
        if (collectionId.HasValue)
        {
            query = query.Where(d =>
                d.DocumentCollections.Any(dc => dc.CollectionId == collectionId.Value));
        }

        // Date range filters
        if (importedAfter.HasValue)
        {
            query = query.Where(d => d.ImportedAt >= importedAfter.Value);
        }

        if (importedBefore.HasValue)
        {
            query = query.Where(d => d.ImportedAt <= importedBefore.Value);
        }

        // Sorting
        var sort = (sortBy ?? "date").ToLowerInvariant();
        query = sort switch
        {
            "name" => query.OrderBy(d => d.FileName),
            "size" => query.OrderByDescending(d => d.FileSizeBytes),
            "type" => query.OrderBy(d => d.FileType).ThenByDescending(d => d.ImportedAt),
            _ => query.OrderByDescending(d => d.ImportedAt), // "date" or default
        };

        return await query.ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> GetDocumentsByCollectionAsync(long collectionId)
    {
        return await _db.DocumentCollections
            .Where(dc => dc.CollectionId == collectionId)
            .Select(dc => dc.Document)
            .OrderByDescending(d => d.ImportedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> GetRecentDocumentsAsync(int limit = 5, CancellationToken ct = default)
    {
        var normalizedLimit = Math.Max(1, limit);

        return await _db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.ImportedAt)
            .Take(normalizedLimit)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeleteDocumentAsync(long documentId)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document is null)
        {
            _logger.Warning("Attempted to delete non-existent document: {DocumentId}", documentId);
            return;
        }

        // Only the ids and embedding state are needed to clean up the vector store, so the
        // chunk text is never loaded just to be deleted.
        var embeddedChunkIds = await _db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId && c.IsEmbedded && c.VectorRowId.HasValue)
            .Select(c => c.Id)
            .ToListAsync();

        // Delete vector embeddings for all chunks if VectorStore is available
        if (_vectorStore is not null && embeddedChunkIds.Count > 0)
        {
            try
            {
                await _vectorStore.DeleteEmbeddingsForDocumentAsync(documentId, embeddedChunkIds);
                _logger.Debug("Deleted {Count} vector embeddings for document {DocumentId}", embeddedChunkIds.Count, documentId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to delete vector embeddings for document {DocumentId}", documentId);
                // Continue with entity deletion even if vector cleanup fails
            }
        }

        // Keyword hits carry their indexed text straight into search results and RAG
        // prompts, so FTS rows left behind would keep serving the deleted document's text.
        await RemoveFromKeywordIndexAsync(documentId, "delete");

        // Keep the denormalized document count of every collection it belonged to in step.
        var collectionIds = await _db.DocumentCollections
            .AsNoTracking()
            .Where(dc => dc.DocumentId == documentId)
            .Select(dc => dc.CollectionId)
            .ToListAsync();

        if (collectionIds.Count > 0)
        {
            var collections = await _db.Collections
                .Where(c => collectionIds.Contains(c.Id))
                .ToListAsync();

            foreach (var collection in collections)
            {
                collection.DocumentCount = Math.Max(0, collection.DocumentCount - 1);
                collection.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Tracked dependents are removed by EF, the rest by the database cascade.
        _db.Documents.Remove(document);
        await _db.SaveChangesAsync();

        // The cascade only runs when the connection enforces foreign keys, so sweep up any
        // rows that survived. Tracked instances were already removed by SaveChanges above,
        // so these set-based deletes cannot conflict with the change tracker.
        await _db.DocumentChunks.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync();
        await _db.DocumentCollections.Where(dc => dc.DocumentId == documentId).ExecuteDeleteAsync();
        await _db.DocumentTags.Where(dt => dt.DocumentId == documentId).ExecuteDeleteAsync();

        // Cached result sets may still reference the deleted document.
        _searchCacheService?.InvalidateForDocument(documentId);

        _logger.Information("Deleted document: {FileName} (ID {DocumentId})", document.FileName, documentId);
    }

    /// <summary>
    /// Removes a document's rows from the FTS5 keyword index. Non-fatal: the caller's
    /// primary operation proceeds even when the index cannot be updated.
    /// </summary>
    private async Task RemoveFromKeywordIndexAsync(long documentId, string operation, CancellationToken ct = default)
    {
        if (_keywordSearchService is null)
        {
            return;
        }

        try
        {
            await _keywordSearchService.RemoveDocumentFromFtsAsync(documentId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to remove document {DocumentId} from the keyword index during {Operation}",
                documentId, operation);
        }
    }

    /// <inheritdoc />
    public async Task ReindexDocumentAsync(long documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            throw new InvalidOperationException($"Document with ID {documentId} not found.");
        }

        // Verify the source file still exists
        if (!File.Exists(document.FilePath))
        {
            await MarkFailedAsync(document, $"Source file no longer exists: {document.FilePath}");
            throw new FileNotFoundException($"Source file no longer exists: {document.FilePath}", document.FilePath);
        }

        var processor = FindProcessorFor(document.FilePath);
        if (processor is null)
        {
            var error = $"No processor found for file type: {Path.GetExtension(document.FilePath)}";
            await MarkFailedAsync(document, error);
            throw new NotSupportedException(error);
        }

        // Hash and extract BEFORE touching the existing index data. If either throws, the
        // document keeps its current chunks and vectors, and no half-finished deletes are
        // left pending in the shared change tracker for some later SaveChanges to flush.
        string newHash;
        ProcessedDocument processed;
        try
        {
            newHash = await HashHelper.ComputeFileHashAsync(document.FilePath, ct);
            processed = await processor.ProcessAsync(document.FilePath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Re-index of document {DocumentId} failed during text extraction", documentId);
            await MarkFailedAsync(document, DescribeExtractionFailure(ex));
            throw;
        }

        // Extraction succeeded: remove the previous version's index data.
        var existingChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);

        var embeddedChunkIds = existingChunks
            .Where(c => c.IsEmbedded && c.VectorRowId.HasValue)
            .Select(c => c.Id)
            .ToList();

        if (_vectorStore is not null && embeddedChunkIds.Count > 0)
        {
            try
            {
                await _vectorStore.DeleteEmbeddingsForDocumentAsync(documentId, embeddedChunkIds, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "Failed to delete vector embeddings during re-index for document {DocumentId}", documentId);
            }
        }

        await RemoveFromKeywordIndexAsync(documentId, "re-index", ct);

        _db.DocumentChunks.RemoveRange(existingChunks);

        var fileInfo = new FileInfo(document.FilePath);

        // Update document metadata
        document.ContentHash = newHash;
        document.FileSizeBytes = fileInfo.Length;
        document.FileModifiedAt = fileInfo.LastWriteTimeUtc;
        document.PageCount = processed.PageCount;
        document.WordCount = processed.WordCount;
        document.ExtractedTitle = processed.ExtractedTitle;
        document.Language = processed.Language;
        document.MetadataJson = SerializeMetadata(processed.Metadata);
        document.ChunkCount = 0;
        document.IndexingStatus = "pending";
        document.IndexingError = null;
        document.LastIndexedAt = null;

        await _db.SaveChangesAsync(ct);

        _searchCacheService?.InvalidateForDocument(documentId);

        _logger.Information("Document {DocumentId} ({FileName}) reset to pending for re-indexing", documentId, document.FileName);

        // Hand the document to the indexing pipeline; without this it would sit in "pending"
        // until the next startup even though every caller reports it as queued.
        RaisePendingIndexing(documentId, processed);
    }

    /// <summary>
    /// Records a failure on the document. Saved without the caller's token so the status
    /// persists even when the failure was raised on the way out of a cancelled operation.
    /// </summary>
    private async Task MarkFailedAsync(DocumentEntity document, string error)
    {
        document.IndexingStatus = "failed";
        document.IndexingError = error;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Notifies subscribers (the indexing pipeline) that a document is waiting in "pending".
    /// A failing handler must not fail the import that raised the event.
    /// </summary>
    private void RaisePendingIndexing(long documentId, ProcessedDocument? extracted)
    {
        try
        {
            DocumentPendingIndexing?.Invoke(this, new DocumentPendingIndexingEventArgs(documentId, extracted));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "A DocumentPendingIndexing handler failed for document {DocumentId}", documentId);
        }
    }

    /// <inheritdoc />
    public async Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return null;
        }

        return await _db.Documents
            .FirstOrDefaultAsync(d => d.ContentHash == contentHash);
    }

    /// <inheritdoc />
    public async Task<long> GetTotalDocumentCountAsync()
    {
        return await _db.Documents.LongCountAsync();
    }

    /// <inheritdoc />
    public async Task<long> GetTotalStorageBytesAsync()
    {
        return await _db.Documents.SumAsync(d => d.FileSizeBytes);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetFileTypeDistributionAsync()
    {
        return await _db.Documents
            .GroupBy(d => d.FileType)
            .Select(g => new { FileType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FileType, x => x.Count);
    }

    /// <inheritdoc />
    public bool CanProcess(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return FindProcessorFor(filePath) is not null;
    }

    /// <inheritdoc />
    public IReadOnlySet<string> GetSupportedExtensions()
    {
        // Plugins activate and deactivate at run time, so their formats are added per call
        // instead of being cached with the built-in ones.
        var pluginProcessors = _pluginProcessors?.GetDocumentProcessors();
        if (pluginProcessors is null || pluginProcessors.Count == 0)
        {
            return _allSupportedExtensions.Value;
        }

        var extensions = new HashSet<string>(_allSupportedExtensions.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var processor in pluginProcessors)
        {
            extensions.UnionWith(processor.SupportedExtensions);
        }

        return extensions;
    }

    // ─── Duplicate Detection ────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<DuplicateCheckResult> CheckForDuplicateAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning("Duplicate check skipped — file does not exist: {FilePath}", filePath);
                return new DuplicateCheckResult { IsDuplicate = false };
            }

            // Calculate hash of the incoming file using the same helper as ImportFileAsync
            var hash = await HashHelper.ComputeFileHashAsync(filePath, ct);

            // Check for exact hash match against existing documents
            var exactMatch = await _db.Documents
                .FirstOrDefaultAsync(d => d.ContentHash == hash, ct);

            if (exactMatch is not null)
            {
                _logger.Information(
                    "Duplicate check: {FilePath} matches existing document {DocumentId} ({FileName})",
                    filePath, exactMatch.Id, exactMatch.FileName);

                return new DuplicateCheckResult
                {
                    IsDuplicate = true,
                    IsExactMatch = true,
                    ExistingDocumentId = exactMatch.Id,
                    ExistingFileName = exactMatch.FileName,
                    MatchScore = 1.0f
                };
            }

            return new DuplicateCheckResult { IsDuplicate = false };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Duplicate check failed for {FilePath}", filePath);
            return new DuplicateCheckResult { IsDuplicate = false };
        }
    }

    // ─── Bulk Operations ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task BulkDeleteAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)
    {
        if (documentIds is null || documentIds.Count == 0) return;

        _logger.Information("Starting bulk delete of {Count} documents", documentIds.Count);

        foreach (var id in documentIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DeleteDocumentAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete document {Id} in bulk operation", id);
            }
        }

        _logger.Information("Bulk delete completed for {Count} documents", documentIds.Count);
    }

    /// <inheritdoc />
    public async Task BulkReindexAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)
    {
        if (documentIds is null || documentIds.Count == 0) return;

        _logger.Information("Starting bulk re-index of {Count} documents", documentIds.Count);

        foreach (var id in documentIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ReindexDocumentAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to reindex document {Id} in bulk operation", id);
            }
        }

        _logger.Information("Bulk re-index completed for {Count} documents", documentIds.Count);
    }

    /// <inheritdoc />
    public async Task BulkAssignToCollectionAsync(IReadOnlyList<long> documentIds, long collectionId, CancellationToken ct = default)
    {
        if (documentIds is null || documentIds.Count == 0) return;

        _logger.Information("Starting bulk assign of {Count} documents to collection {CollectionId}",
            documentIds.Count, collectionId);

        // Verify collection exists first
        var collectionExists = await _db.Collections
            .AnyAsync(c => c.Id == collectionId, ct);

        if (!collectionExists)
        {
            _logger.Warning("Collection {CollectionId} not found; aborting bulk assign", collectionId);
            return;
        }

        foreach (var id in documentIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Check if association already exists
                var alreadyAssigned = await _db.DocumentCollections
                    .AnyAsync(dc => dc.DocumentId == id && dc.CollectionId == collectionId, ct);

                if (alreadyAssigned) continue;

                var documentExists = await _db.Documents.AnyAsync(d => d.Id == id, ct);
                if (!documentExists)
                {
                    _logger.Warning("Document {Id} not found; skipping in bulk assign", id);
                    continue;
                }

                await AddToCollectionAsync(id, collectionId, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to assign document {Id} to collection in bulk operation", id);
            }
        }

        _logger.Information("Bulk assign completed for {Count} documents to collection {CollectionId}",
            documentIds.Count, collectionId);
    }

    // ─── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Finds the first registered processor that can handle the given file path. Built-in
    /// processors win; a processor contributed by an active plugin handles only formats that
    /// no built-in processor accepts.
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

        return _pluginProcessors?.GetDocumentProcessors().FirstOrDefault(p => p.CanProcess(filePath));
    }

    /// <summary>
    /// Serializes document metadata to JSON, returning null if the metadata is empty.
    /// </summary>
    private static string? SerializeMetadata(Documents.Models.DocumentMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        // Only serialize if there is meaningful metadata
        if (metadata.Author is null
            && metadata.Subject is null
            && metadata.CreatedDate is null
            && metadata.ModifiedDate is null
            && metadata.Custom.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Maps common file extensions to MIME types.
    /// </summary>
    private static string? GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".log" => "text/plain",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".md" or ".markdown" => "text/markdown",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".ts" => "application/typescript",
            ".py" => "text/x-python",
            ".cs" => "text/x-csharp",
            ".java" => "text/x-java-source",
            ".cpp" or ".c" or ".h" => "text/x-c",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".swift" => "text/x-swift",
            ".kt" => "text/x-kotlin",
            ".rb" => "text/x-ruby",
            ".php" => "text/x-php",
            ".sql" => "application/sql",
            ".sh" => "application/x-sh",
            ".yaml" or ".yml" => "application/x-yaml",
            ".toml" => "application/toml",
            ".ini" or ".cfg" => "text/plain",
            ".xaml" => "application/xaml+xml",
            ".scss" => "text/x-scss",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tiff" => "image/tiff",
            _ => null
        };
    }
}

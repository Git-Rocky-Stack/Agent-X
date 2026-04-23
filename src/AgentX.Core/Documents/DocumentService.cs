using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Helpers;
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
    private readonly ISettingsService _settingsService;
    private readonly IVectorStore? _vectorStore;
    private readonly ILogger _logger;

    /// <summary>
    /// Lazily computed union of all supported extensions across every registered processor.
    /// </summary>
    private readonly Lazy<IReadOnlySet<string>> _allSupportedExtensions;

    public DocumentService(
        AgentXDbContext db,
        IEnumerable<IDocumentProcessor> processors,
        ISettingsService settingsService,
        ILogger logger,
        IVectorStore? vectorStore = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _processors = (processors ?? throw new ArgumentNullException(nameof(processors))).ToList().AsReadOnly();
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vectorStore = vectorStore;

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
    public async Task<DocumentEntity> ImportFileAsync(
        string filePath,
        long? collectionId = null,
        CancellationToken ct = default)
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
            _logger.Information(
                "Duplicate detected: {FilePath} matches existing document {DocumentId} ({FileName})",
                filePath, existingDoc.Id, existingDoc.FileName);
            throw new InvalidOperationException(
                $"A document with identical content already exists: '{existingDoc.FileName}' (ID {existingDoc.Id}).");
        }

        // 4. Find the appropriate processor
        var processor = FindProcessorFor(filePath);
        if (processor is null)
        {
            throw new NotSupportedException(
                $"No processor found for file type '{extension}'. Supported types: {string.Join(", ", GetSupportedExtensions())}");
        }

        // 5. Extract text and metadata
        _logger.Debug("Processing file with {Processor}: {FilePath}", processor.GetType().Name, filePath);
        var processed = await processor.ProcessAsync(filePath, ct);

        // 6. Gather file system metadata
        var fileInfo = new FileInfo(filePath);
        var metadataJson = SerializeMetadata(processed.Metadata);

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
            IndexingStatus = "pending",
            PageCount = processed.PageCount,
            WordCount = processed.WordCount,
            ExtractedTitle = processed.ExtractedTitle,
            Language = processed.Language,
            MetadataJson = metadataJson
        };

        _db.Documents.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.Information(
            "Imported document: {FileName} (ID {DocumentId}, {FileType}, {WordCount} words, {PageCount} pages)",
            entity.FileName, entity.Id, entity.FileType, entity.WordCount, entity.PageCount);

        // 8. Associate with collection if specified
        if (collectionId.HasValue)
        {
            var collectionExists = await _db.Collections
                .AnyAsync(c => c.Id == collectionId.Value, ct);

            if (!collectionExists)
            {
                _logger.Warning("Collection {CollectionId} not found; skipping collection association", collectionId.Value);
            }
            else
            {
                var docCollection = new DocumentCollectionEntity
                {
                    DocumentId = entity.Id,
                    CollectionId = collectionId.Value,
                    AddedAt = DateTime.UtcNow
                };

                _db.DocumentCollections.Add(docCollection);
                await _db.SaveChangesAsync(ct);

                _logger.Debug(
                    "Associated document {DocumentId} with collection {CollectionId}",
                    entity.Id, collectionId.Value);
            }
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

        var processed = await processor.ProcessAsync(filePath, ct);

        var fileInfo = new FileInfo(filePath);
        var contentHash = await HashHelper.ComputeFileHashAsync(filePath, ct);
        var metadataJson = SerializeMetadata(processed.Metadata);

        // Store the source URL in metadata if provided
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            processed.Metadata.Custom["sourceUrl"] = sourceUrl;
            metadataJson = SerializeMetadata(processed.Metadata);
        }

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
            IndexingStatus = "pending",
            PageCount = processed.PageCount,
            WordCount = processed.WordCount,
            ExtractedTitle = displayName,
            Language = processed.Language,
            MetadataJson = metadataJson,
        };

        _db.Documents.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.Information(
            "Imported external content: {DisplayName} (ID {DocumentId}, Type={FileType}, {WordCount} words)",
            displayName, entity.Id, entity.FileType, entity.WordCount);

        // Associate with collection if specified
        if (collectionId.HasValue)
        {
            var collectionExists = await _db.Collections
                .AnyAsync(c => c.Id == collectionId.Value, ct);

            if (collectionExists)
            {
                var docCollection = new DocumentCollectionEntity
                {
                    DocumentId = entity.Id,
                    CollectionId = collectionId.Value,
                    AddedAt = DateTime.UtcNow
                };

                _db.DocumentCollections.Add(docCollection);
                await _db.SaveChangesAsync(ct);
            }
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(
        IReadOnlyList<string> filePaths,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (filePaths is null || filePaths.Count == 0)
        {
            return Array.Empty<DocumentEntity>();
        }

        var results = new List<DocumentEntity>(filePaths.Count);
        var completed = 0;

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var entity = await ImportFileAsync(filePath, collectionId, ct);
                results.Add(entity);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and continue with remaining files rather than aborting the batch
                _logger.Warning(ex, "Failed to import file: {FilePath}", filePath);
            }

            completed++;
            progress?.Report(completed);
        }

        _logger.Information("Batch import completed: {Imported}/{Total} files imported", results.Count, filePaths.Count);
        return results.AsReadOnly();
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

        // File type filter
        if (!string.IsNullOrWhiteSpace(fileTypeFilter))
        {
            var normalizedFilter = fileTypeFilter.TrimStart('.').ToLowerInvariant();
            query = query.Where(d => d.FileType == normalizedFilter);
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
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document is null)
        {
            _logger.Warning("Attempted to delete non-existent document: {DocumentId}", documentId);
            return;
        }

        // Delete vector embeddings for all chunks if VectorStore is available
        if (_vectorStore is not null && document.Chunks.Count > 0)
        {
            var chunkIds = document.Chunks
                .Where(c => c.IsEmbedded && c.VectorRowId.HasValue)
                .Select(c => c.Id)
                .ToList();

            if (chunkIds.Count > 0)
            {
                try
                {
                    await _vectorStore.DeleteEmbeddingsForDocumentAsync(documentId, chunkIds);
                    _logger.Debug("Deleted {Count} vector embeddings for document {DocumentId}", chunkIds.Count, documentId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to delete vector embeddings for document {DocumentId}", documentId);
                    // Continue with entity deletion even if vector cleanup fails
                }
            }
        }

        // EF Core cascade delete will remove chunks, document-collection links, and tags
        _db.Documents.Remove(document);
        await _db.SaveChangesAsync();

        _logger.Information("Deleted document: {FileName} (ID {DocumentId})", document.FileName, documentId);
    }

    /// <inheritdoc />
    public async Task ReindexDocumentAsync(long documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            throw new InvalidOperationException($"Document with ID {documentId} not found.");
        }

        // Verify the source file still exists
        if (!File.Exists(document.FilePath))
        {
            document.IndexingStatus = "failed";
            document.IndexingError = $"Source file no longer exists: {document.FilePath}";
            await _db.SaveChangesAsync(ct);
            throw new FileNotFoundException($"Source file no longer exists: {document.FilePath}", document.FilePath);
        }

        // Delete existing vector embeddings
        if (_vectorStore is not null && document.Chunks.Count > 0)
        {
            var chunkIds = document.Chunks
                .Where(c => c.IsEmbedded && c.VectorRowId.HasValue)
                .Select(c => c.Id)
                .ToList();

            if (chunkIds.Count > 0)
            {
                try
                {
                    await _vectorStore.DeleteEmbeddingsForDocumentAsync(documentId, chunkIds);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to delete vector embeddings during re-index for document {DocumentId}", documentId);
                }
            }
        }

        // Remove existing chunks
        _db.DocumentChunks.RemoveRange(document.Chunks);

        // Recompute hash (file may have changed)
        var newHash = await HashHelper.ComputeFileHashAsync(document.FilePath, ct);

        // Re-extract text
        var processor = FindProcessorFor(document.FilePath);
        if (processor is null)
        {
            document.IndexingStatus = "failed";
            document.IndexingError = $"No processor found for file type: {Path.GetExtension(document.FilePath)}";
            await _db.SaveChangesAsync(ct);
            throw new NotSupportedException(document.IndexingError);
        }

        var processed = await processor.ProcessAsync(document.FilePath, ct);
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

        _logger.Information("Document {DocumentId} ({FileName}) reset to pending for re-indexing", documentId, document.FileName);
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
        return _allSupportedExtensions.Value;
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

                var docCollection = new DocumentCollectionEntity
                {
                    DocumentId = id,
                    CollectionId = collectionId,
                    AddedAt = DateTime.UtcNow
                };

                _db.DocumentCollections.Add(docCollection);
                await _db.SaveChangesAsync(ct);
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

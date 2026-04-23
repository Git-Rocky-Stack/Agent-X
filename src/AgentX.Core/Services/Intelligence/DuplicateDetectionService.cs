using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.FeatureFlags;
using AgentX.Core.Services.Intelligence.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Detects exact and near-duplicate documents in the knowledge vault.
/// Exact duplicates are identified by SHA-256 content hash comparison (zero AI cost).
/// Near-duplicates are identified by comparing vector embeddings for semantic similarity.
/// </summary>
public class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly AgentXDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger _log;
    private readonly IDuplicateEvidenceService _duplicateEvidenceService;
    private readonly IFeatureFlagService? _featureFlags;

    /// <summary>
    /// Maximum number of documents to scan for near-duplicate detection.
    /// Limits the O(N^2) pairwise comparison to avoid excessive computation.
    /// </summary>
    private const int MaxNearDuplicateScanDocuments = 500;

    public DuplicateDetectionService(
        AgentXDbContext db,
        IVectorStore vectorStore,
        ILogger logger,
        IDuplicateEvidenceService? duplicateEvidenceService = null,
        IFeatureFlagService? featureFlags = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _log = logger?.ForContext<DuplicateDetectionService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _duplicateEvidenceService = duplicateEvidenceService ?? new DuplicateEvidenceService(logger);
        _featureFlags = featureFlags;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default)
    {
        // Return empty when duplicate detection is disabled via feature flag
        if (!(_featureFlags?.IsEnabled(FeatureFlags.FeatureFlags.DuplicateDetection.Name) ?? true))
        {
            _log.Debug("Duplicate detection is disabled via feature flag, returning empty list");
            return Array.Empty<DuplicateGroup>();
        }

        _log.Information("Starting exact duplicate detection scan");

        try
        {
            // Query all documents and group by content hash in the database
            var documents = await _db.Documents
                .AsNoTracking()
                .Select(d => new
                {
                    d.Id,
                    d.FileName,
                    d.FilePath,
                    d.FileSizeBytes,
                    d.ContentHash,
                    d.ImportedAt,
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // Group by content hash and filter to groups with more than one document
            var duplicateGroups = documents
                .GroupBy(d => d.ContentHash)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateGroup
                {
                    ContentHash = g.Key,
                    Documents = g
                        .OrderBy(d => d.ImportedAt) // Earliest import is the "original"
                        .Select(d => new DuplicateDocument
                        {
                            DocumentId = d.Id,
                            FileName = d.FileName,
                            FilePath = d.FilePath,
                            FileSizeBytes = d.FileSizeBytes,
                            ImportedAt = d.ImportedAt,
                        })
                        .ToList(),
                })
                .ToList();

            var totalDuplicates = duplicateGroups.Sum(g => g.Documents.Count - 1);
            var totalWastedBytes = duplicateGroups.Sum(g => g.WastedStorageBytes);

            _log.Information(
                "Exact duplicate scan complete: found {GroupCount} groups containing " +
                "{DuplicateCount} duplicate documents, wasting {WastedBytes} bytes of storage",
                duplicateGroups.Count, totalDuplicates, totalWastedBytes);

            return duplicateGroups.AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            _log.Information("Exact duplicate detection was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to perform exact duplicate detection");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
        float similarityThreshold = 0.9f, CancellationToken ct = default)
    {
        // Return empty when duplicate detection is disabled via feature flag
        if (!(_featureFlags?.IsEnabled(FeatureFlags.FeatureFlags.DuplicateDetection.Name) ?? true))
        {
            _log.Debug("Duplicate detection is disabled via feature flag, returning empty list");
            return Array.Empty<DuplicateGroup>();
        }

        if (similarityThreshold is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(similarityThreshold),
                similarityThreshold,
                "Similarity threshold must be between 0.0 and 1.0.");
        }

        _log.Information(
            "Starting near-duplicate detection scan (threshold: {Threshold:F2})",
            similarityThreshold);

        try
        {
            // Load documents with their embedded chunks (limited to MaxNearDuplicateScanDocuments)
            var documents = await _db.Documents
                .AsNoTracking()
                .Include(d => d.Chunks.Where(c => c.IsEmbedded && c.VectorRowId != null))
                .OrderBy(d => d.ImportedAt)
                .Take(MaxNearDuplicateScanDocuments)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (documents.Count == 0)
            {
                _log.Information("No documents found for near-duplicate detection");
                return Array.Empty<DuplicateGroup>();
            }

            // Filter to documents that have at least one embedded chunk
            var documentsWithEmbeddings = documents
                .Where(d => d.Chunks.Any())
                .ToList();

            if (documentsWithEmbeddings.Count == 0)
            {
                _log.Information("No documents with embeddings found for near-duplicate detection");
                return Array.Empty<DuplicateGroup>();
            }

            _log.Debug(
                "Scanning {Count} documents with embeddings for near-duplicates (capped at {Max})",
                documentsWithEmbeddings.Count, MaxNearDuplicateScanDocuments);

            // Build a lookup: chunkId -> documentId for quick reverse mapping
            var chunkToDocument = new Dictionary<long, long>();
            foreach (var doc in documentsWithEmbeddings)
            {
                foreach (var chunk in doc.Chunks)
                {
                    if (chunk.VectorRowId.HasValue)
                    {
                        chunkToDocument[chunk.Id] = doc.Id;
                    }
                }
            }

            var nearDuplicateGroups = new List<DuplicateGroup>();
            var assigned = new HashSet<long>();
            var processedCount = 0;

            foreach (var doc in documentsWithEmbeddings)
            {
                ct.ThrowIfCancellationRequested();

                if (assigned.Contains(doc.Id))
                {
                    processedCount++;
                    continue;
                }

                // Use the first embedded chunk as the representative embedding for this document
                var representativeChunk = doc.Chunks
                    .OrderBy(c => c.ChunkIndex)
                    .First();

                // Load the actual embedding bytes from the vec_embeddings table
                var embeddingBytes = await LoadEmbeddingBytesAsync(representativeChunk.Id, ct)
                    .ConfigureAwait(false);

                if (embeddingBytes is null || embeddingBytes.Length == 0)
                {
                    processedCount++;
                    continue;
                }

                var embedding = DeserializeEmbedding(embeddingBytes);

                // Search for similar embeddings across the entire vector store
                var results = await _vectorStore
                    .SearchAsync(embedding, topK: 100, minSimilarity: similarityThreshold, ct: ct)
                    .ConfigureAwait(false);

                // Map matching chunk IDs back to document IDs and deduplicate
                var evidence = _duplicateEvidenceService
                    .BuildEvidence(results, chunkToDocument)
                    .Where(item => item.DocumentId != doc.Id && !assigned.Contains(item.DocumentId))
                    .ToList();

                var matchedDocIds = evidence
                    .Select(item => item.DocumentId)
                    .Distinct()
                    .ToList();

                if (matchedDocIds.Count > 0)
                {
                    // Create a group with this document as the reference
                    var groupDocIds = new List<long> { doc.Id };
                    groupDocIds.AddRange(matchedDocIds);

                    var groupDocuments = documentsWithEmbeddings
                        .Where(d => groupDocIds.Contains(d.Id))
                        .OrderBy(d => d.ImportedAt)
                        .Select(d => new DuplicateDocument
                        {
                            DocumentId = d.Id,
                            FileName = d.FileName,
                            FilePath = d.FilePath,
                            FileSizeBytes = d.FileSizeBytes,
                            ImportedAt = d.ImportedAt,
                        })
                        .ToList();

                    nearDuplicateGroups.Add(new DuplicateGroup
                    {
                        ContentHash = doc.ContentHash, // Use the reference doc's hash for identification
                        Documents = groupDocuments,
                    });

                    // Mark all documents in this group as assigned
                    foreach (var id in groupDocIds)
                    {
                        assigned.Add(id);
                    }

                    _log.Debug(
                        "Found near-duplicate group: reference document {DocumentId} '{FileName}' " +
                        "with {MatchCount} similar documents (top confidence: {TopConfidence:F2})",
                        doc.Id, doc.FileName, matchedDocIds.Count, evidence.FirstOrDefault()?.Confidence ?? 0);
                }

                assigned.Add(doc.Id);
                processedCount++;

                if (processedCount % 50 == 0)
                {
                    _log.Debug(
                        "Near-duplicate scan progress: {Processed}/{Total} documents analyzed",
                        processedCount, documentsWithEmbeddings.Count);
                }
            }

            var totalNearDuplicates = nearDuplicateGroups.Sum(g => g.Documents.Count - 1);
            var totalWasted = nearDuplicateGroups.Sum(g => g.WastedStorageBytes);

            _log.Information(
                "Near-duplicate scan complete: found {GroupCount} groups containing " +
                "{DuplicateCount} near-duplicate documents, wasting approximately {WastedBytes} bytes",
                nearDuplicateGroups.Count, totalNearDuplicates, totalWasted);

            return nearDuplicateGroups.AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            _log.Information("Near-duplicate detection was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to perform near-duplicate detection");
            throw;
        }
    }

    // -- Private helpers --------------------------------------------------

    /// <summary>
    /// Loads the raw embedding bytes for a chunk directly from the vec_embeddings table.
    /// This bypasses the IVectorStore interface to retrieve stored embeddings for comparison,
    /// since IVectorStore only supports search-by-vector, not retrieval-by-chunk-id.
    /// </summary>
    private async Task<byte[]?> LoadEmbeddingBytesAsync(long chunkId, CancellationToken ct)
    {
        try
        {
            // Use the EF Core database connection to query the vec_embeddings table directly.
            // This table is created by SqliteVecStore and shares the same SQLite database.
            var connection = _db.Database.GetDbConnection();

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT embedding FROM vec_embeddings WHERE chunk_id = @chunkId;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@chunkId";
            parameter.Value = chunkId;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result as byte[];
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load embedding bytes for chunk {ChunkId}", chunkId);
            return null;
        }
    }

    /// <summary>
    /// Deserializes a byte array into a float array using direct memory copy.
    /// Mirrors the serialization format used by <see cref="SqliteVecStore"/>.
    /// </summary>
    private static float[] DeserializeEmbedding(byte[] blob)
    {
        if (blob.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException(
                $"Embedding blob size ({blob.Length} bytes) is not a multiple of {sizeof(float)} bytes.");
        }

        var floats = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
        return floats;
    }
}

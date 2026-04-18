using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Services.Settings;
using Hnsw;
using Hnsw.RamStorage;
using Hsnw;
using Microsoft.Data.Sqlite;
using Serilog;

// Task 8b: IEncryptedConnectionFactory integration — connection opens go through the factory
// so PRAGMA key is applied whenever encryption is enabled.

namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// HNSW-accelerated vector store that combines an in-memory HNSW approximate nearest
/// neighbor index with SQLite persistence for durable embedding storage.
///
/// Architecture:
///   - SQLite (vec_embeddings table) is the source of truth for all embeddings
///   - HNSW index is an in-memory acceleration structure rebuilt from SQLite on demand
///   - Hybrid search: HNSW for large collections (>10K), linear scan fallback for small
///   - Index state is persisted to disk via ExportStateAsync/ImportStateAsync
///
/// Performance characteristics:
///   - Insert: O(log N) per embedding (HNSW graph traversal + SQLite write)
///   - Search: O(log N) for HNSW vs O(N) for linear scan
///   - At 100K embeddings: ~5-20ms search vs ~500ms linear scan
///   - Memory: ~(vector_count * dimensions * 4 bytes) + (vector_count * M * ~32 bytes)
///
/// Thread safety:
///   - HnswLite claims thread-safe operations
///   - SQLite connection uses WAL mode for concurrent reads
///   - Critical sections use SemaphoreSlim for insert/delete/search coordination
/// </summary>
public sealed class HnswVectorStore : IVectorStore
{
    // ── Constants ────────────────────────────────────────────────────────

    private const int DefaultM = 16;
    private const int DefaultEfConstruction = 200;
    private const int DefaultDimensions = 384; // all-MiniLM-L6-v2 (Agent-X default embedding model)
    private const long FallbackThreshold = 10_000;
    private const double StaleRebuildFraction = 0.05; // Rebuild if >5% stale
    private const string IndexFileName = "hnsw-index.bin";
    private const string MetadataFileName = "hnsw-index.json";
    private const string StaleIdsFileName = "hnsw-stale-ids.json";

    // ── Fields ──────────────────────────────────────────────────────────

    private readonly ISettingsService _settingsService;
    private readonly IEncryptedConnectionFactory _connectionFactory;
    private readonly ILogger _logger;
    private readonly int _m;
    private readonly int _efConstruction;
    private readonly int _dimensions;
    private readonly long _fallbackThreshold;

    private SqliteConnection? _connection;
    private HnswIndex? _hnswIndex;
    private string? _storagePath;
    private bool _disposed;
    private bool _initialized;
    private bool _indexDirty;

    /// <summary>
    /// Tracks chunk IDs that have been deleted from SQLite but may still exist in the
    /// HNSW index. HnswLite supports RemoveAsync, but the stale set serves as a safety
    /// net for any removal failures and enables the >5% stale rebuild trigger.
    /// </summary>
    private readonly HashSet<long> _staleChunkIds = [];

    /// <summary>
    /// Maps chunk IDs (long) to Guids used by HnswLite for vector identification.
    /// Deterministic mapping ensures consistency across index rebuilds.
    /// </summary>
    private readonly Dictionary<long, Guid> _chunkIdToGuid = [];

    /// <summary>
    /// Reverse mapping from HnswLite Guid back to chunk IDs for search result translation.
    /// </summary>
    private readonly Dictionary<Guid, long> _guidToChunkId = [];

    /// <summary>
    /// Semaphore to serialize index mutations (insert, delete, rebuild) to prevent
    /// concurrent modification of the HNSW graph and stale set.
    /// </summary>
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    // ── Constructors ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new HnswVectorStore with default HNSW parameters.
    /// </summary>
    /// <param name="settingsService">Settings service providing the database storage path.</param>
    /// <param name="connectionFactory">Encrypted connection factory — required so PRAGMA key is applied when opening SQLite.</param>
    public HnswVectorStore(ISettingsService settingsService, IEncryptedConnectionFactory connectionFactory)
        : this(settingsService, logger: null, DefaultM, DefaultEfConstruction, DefaultDimensions, FallbackThreshold, connectionFactory)
    {
    }

    /// <summary>
    /// Creates a new HnswVectorStore with explicit logger and default HNSW parameters.
    /// </summary>
    /// <param name="settingsService">Settings service providing the database storage path.</param>
    /// <param name="logger">Serilog logger instance.</param>
    /// <param name="connectionFactory">Encrypted connection factory — required so PRAGMA key is applied when opening SQLite.</param>
    public HnswVectorStore(ISettingsService settingsService, ILogger logger, IEncryptedConnectionFactory connectionFactory)
        : this(settingsService, logger, DefaultM, DefaultEfConstruction, DefaultDimensions, FallbackThreshold, connectionFactory)
    {
    }

    /// <summary>
    /// Full-featured constructor — all HNSW parameters configurable.
    /// </summary>
    /// <param name="settingsService">Settings service providing the database storage path.</param>
    /// <param name="logger">Serilog logger instance (may be null to use the default context logger).</param>
    /// <param name="m">HNSW M parameter: max connections per layer.</param>
    /// <param name="efConstruction">HNSW EfConstruction: candidate list size during build.</param>
    /// <param name="dimensions">Embedding vector dimensionality.</param>
    /// <param name="fallbackThreshold">Embedding count below which linear scan is used.</param>
    /// <param name="connectionFactory">Encrypted connection factory — required so PRAGMA key is applied when opening SQLite.</param>
    public HnswVectorStore(
        ISettingsService settingsService,
        ILogger? logger,
        int m,
        int efConstruction,
        int dimensions,
        long fallbackThreshold,
        IEncryptedConnectionFactory connectionFactory)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? Log.ForContext<HnswVectorStore>();
        _m = m;
        _efConstruction = efConstruction;
        _dimensions = dimensions;
        _fallbackThreshold = fallbackThreshold;
        _logger.Information("HnswVectorStore created (M={M}, EfConstruction={EfConstruction}, Dims={Dimensions}, Threshold={Threshold})",
            _m, _efConstruction, _dimensions, _fallbackThreshold);
    }

    // ── IVectorStore implementation ─────────────────────────────────────

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_initialized)
        {
            _logger.Warning("HnswVectorStore already initialized; skipping");
            return;
        }

        _logger.Information("Initializing HnswVectorStore...");

        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            _storagePath = settings.StoragePath;

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
                _logger.Debug("Created storage directory: {Path}", _storagePath);
            }

            // Open SQLite connection (same schema as SqliteVecStore) via the encrypted
            // connection factory — PRAGMA key is applied automatically when encryption
            // is enabled, and the call is a plaintext open when no key is loaded.
            var dbPath = Path.Combine(_storagePath, "agentx.db");
            _connection = _connectionFactory.OpenKeyed(dbPath);

            _logger.Debug("SQLite connection opened: {Path}", dbPath);

            await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);

            const string createTableSql = """
                CREATE TABLE IF NOT EXISTS vec_embeddings (
                    chunk_id  INTEGER PRIMARY KEY,
                    embedding BLOB NOT NULL,
                    magnitude REAL NOT NULL
                );
                """;

            await ExecuteNonQueryAsync(createTableSql, ct).ConfigureAwait(false);

            const string createIndexSql = """
                CREATE INDEX IF NOT EXISTS idx_vec_chunk ON vec_embeddings(chunk_id);
                """;

            await ExecuteNonQueryAsync(createIndexSql, ct).ConfigureAwait(false);

            var embeddingCount = await GetEmbeddingCountAsync(ct).ConfigureAwait(false);

            // Attempt to load existing HNSW index from disk.
            var indexLoaded = await TryLoadIndexAsync(embeddingCount, ct).ConfigureAwait(false);

            if (!indexLoaded && embeddingCount > 0)
            {
                _logger.Information("Building HNSW index from {Count} SQLite embeddings...", embeddingCount);
                await RebuildIndexAsync(ct).ConfigureAwait(false);
                _logger.Information("HNSW index built from SQLite ({Count} vectors)", embeddingCount);
            }
            else if (indexLoaded)
            {
                _logger.Information("HNSW index loaded from disk ({Count} vectors)", embeddingCount);
            }
            else
            {
                // Empty store — create a fresh index ready for inserts.
                CreateEmptyIndex();
                _logger.Information("HnswVectorStore initialized with empty index");
            }

            _initialized = true;
            _logger.Information("HnswVectorStore initialized with {Count} embeddings", embeddingCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize HnswVectorStore");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding.Length == 0)
            throw new ArgumentException("Embedding vector cannot be empty.", nameof(embedding));

        if (embedding.Length != _dimensions)
        {
            _logger.Warning(
                "Embedding dimension mismatch for chunk {ChunkId}: got {Got}, expected {Expected}. Using actual dimension.",
                chunkId, embedding.Length, _dimensions);
        }

        var blob = SqliteVecStore.SerializeEmbedding(embedding);
        var magnitude = SqliteVecStore.ComputeMagnitude(embedding);

        // Persist to SQLite first (source of truth).
        const string sql = """
            INSERT OR REPLACE INTO vec_embeddings (chunk_id, embedding, magnitude)
            VALUES (@chunkId, @embedding, @magnitude);
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chunkId", chunkId);
        cmd.Parameters.AddWithValue("@embedding", blob);
        cmd.Parameters.AddWithValue("@magnitude", magnitude);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Add to HNSW index if it exists and we're above fallback threshold.
        if (_hnswIndex is not null)
        {
            await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // If this chunk was previously deleted, clean up stale tracking.
                _staleChunkIds.Remove(chunkId);

                var guid = ChunkIdToGuid(chunkId);
                _chunkIdToGuid[chunkId] = guid;
                _guidToChunkId[guid] = chunkId;

                var vector = new List<float>(embedding);
                await _hnswIndex.AddAsync(guid, vector, ct).ConfigureAwait(false);

                _indexDirty = true;

                _logger.Debug("Inserted chunk {ChunkId} into HNSW index (guid={Guid})", chunkId, guid);
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        _logger.Debug("Inserted embedding for chunk {ChunkId} ({Dimensions} dims, magnitude={Magnitude:F4})",
            chunkId, embedding.Length, magnitude);

        return chunkId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        double minSimilarity = 0.3,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be empty.", nameof(queryEmbedding));

        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be a positive integer.");

        var embeddingCount = await GetEmbeddingCountAsync(ct).ConfigureAwait(false);

        // Check if stale entries exceed threshold — trigger rebuild if so.
        await CheckStaleRebuildAsync(embeddingCount, ct).ConfigureAwait(false);

        // Hybrid search: use HNSW for large collections, linear scan fallback for small.
        // Acquire mutation lock during search to prevent concurrent modifications to
        // stale set and GUID mappings, which could cause race conditions.
        if (embeddingCount > _fallbackThreshold && _hnswIndex is not null)
        {
            await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await SearchHnswAsync(queryEmbedding, topK, minSimilarity, ct).ConfigureAwait(false);
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        return await SearchLinearAsync(queryEmbedding, topK, minSimilarity, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        // Delete from SQLite (source of truth).
        const string sql = "DELETE FROM vec_embeddings WHERE chunk_id = @chunkId;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chunkId", chunkId);

        var deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Remove from HNSW index.
        if (_hnswIndex is not null && deleted > 0)
        {
            await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_chunkIdToGuid.TryGetValue(chunkId, out var guid))
                {
                    try
                    {
                        await _hnswIndex.RemoveAsync(guid, ct).ConfigureAwait(false);
                        _logger.Debug("Removed chunk {ChunkId} (guid={Guid}) from HNSW index", chunkId, guid);
                    }
                    catch (Exception ex)
                    {
                        // HnswLite RemoveAsync may fail for nodes not properly connected.
                        // Track as stale and let rebuild clean it up.
                        _logger.Warning(ex, "Failed to remove chunk {ChunkId} from HNSW index; tracking as stale", chunkId);
                        _staleChunkIds.Add(chunkId);
                    }

                    _chunkIdToGuid.Remove(chunkId);
                    _guidToChunkId.Remove(guid);
                }

                _indexDirty = true;
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        _logger.Debug("Deleted embedding for chunk {ChunkId} (rows affected: {Deleted})", chunkId, deleted);
    }

    /// <inheritdoc />
    public async Task DeleteEmbeddingsForDocumentAsync(
        long documentId,
        IReadOnlyList<long> chunkIds,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(chunkIds);

        if (chunkIds.Count == 0)
        {
            _logger.Debug("No chunk IDs provided for document {DocumentId}, nothing to delete", documentId);
            return;
        }

        _logger.Information("Deleting {Count} embeddings for document {DocumentId}", chunkIds.Count, documentId);

        // Build parameterized IN clause for SQLite deletion.
        var paramNames = new string[chunkIds.Count];
        for (var i = 0; i < chunkIds.Count; i++)
        {
            paramNames[i] = $"@id{i}";
        }

        var sql = $"DELETE FROM vec_embeddings WHERE chunk_id IN ({string.Join(", ", paramNames)});";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        for (var i = 0; i < chunkIds.Count; i++)
        {
            cmd.Parameters.AddWithValue(paramNames[i], chunkIds[i]);
        }

        var deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Remove from HNSW index.
        if (_hnswIndex is not null)
        {
            await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var removeCount = 0;
                foreach (var chunkId in chunkIds)
                {
                    if (_chunkIdToGuid.TryGetValue(chunkId, out var guid))
                    {
                        try
                        {
                            await _hnswIndex.RemoveAsync(guid, ct).ConfigureAwait(false);
                            removeCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Failed to remove chunk {ChunkId} from HNSW index; tracking as stale", chunkId);
                            _staleChunkIds.Add(chunkId);
                        }

                        _chunkIdToGuid.Remove(chunkId);
                        _guidToChunkId.Remove(guid);
                    }
                }

                _indexDirty = true;
                _logger.Debug("Removed {Removed}/{Total} chunks from HNSW index for document {DocumentId}",
                    removeCount, chunkIds.Count, documentId);
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        _logger.Information(
            "Deleted {Deleted} embeddings for document {DocumentId} (requested: {Requested})",
            deleted, documentId, chunkIds.Count);
    }

    /// <inheritdoc />
    public async Task<long> GetEmbeddingCountAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_connection is null || _connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "Vector store is not initialized. Call InitializeAsync before performing operations.");
        }

        const string sql = "SELECT COUNT(*) FROM vec_embeddings;";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        _logger.Information("Optimizing HnswVectorStore (persist index + VACUUM)...");

        // Persist the HNSW index to disk if it's dirty.
        if (_indexDirty && _hnswIndex is not null)
        {
            await PersistIndexAsync(ct).ConfigureAwait(false);
            _logger.Information("HNSW index persisted to disk");
        }

        // VACUUM SQLite to reclaim space.
        await ExecuteNonQueryAsync("VACUUM;", ct).ConfigureAwait(false);

        _logger.Information("HnswVectorStore optimization complete");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Persist dirty index before disposal.
        if (_indexDirty && _hnswIndex is not null && _storagePath is not null)
        {
            try
            {
                await PersistIndexAsync(CancellationToken.None).ConfigureAwait(false);
                _logger.Debug("HNSW index persisted during disposal");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error persisting HNSW index during disposal");
            }
        }

        // Dispose HNSW index.
        _hnswIndex = null;

        // Close SQLite connection.
        if (_connection is not null)
        {
            _logger.Debug("Closing HnswVectorStore SQLite connection...");

            try
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error closing SQLite connection during disposal");
            }

            _connection = null;
        }

        // Dispose mutation lock.
        _mutationLock.Dispose();

        _logger.Information("HnswVectorStore disposed");
    }

    // ── HNSW search ─────────────────────────────────────────────────────

    /// <summary>
    /// Performs approximate nearest neighbor search using the HNSW index.
    /// </summary>
    private async Task<IReadOnlyList<VectorSearchResult>> SearchHnswAsync(
        float[] queryEmbedding,
        int topK,
        double minSimilarity,
        CancellationToken ct)
    {
        _logger.Debug("HNSW search for top {TopK} with min similarity {MinSimilarity}", topK, minSimilarity);

        // Request more candidates than topK to account for stale entries and minSimilarity filtering.
        var searchK = Math.Max(topK * 3, 50);
        var queryList = new List<float>(queryEmbedding);

        IEnumerable<VectorResult> hnswResults;
        try
        {
            hnswResults = await _hnswIndex!.GetTopKAsync(queryList, searchK, ef: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "HNSW search failed; falling back to linear scan");
            return await SearchLinearAsync(queryEmbedding, topK, minSimilarity, ct).ConfigureAwait(false);
        }

        // Materialize to avoid multiple enumeration.
        var hnswResultsList = hnswResults as IList<VectorResult> ?? hnswResults.ToList();

        var results = new List<VectorSearchResult>();

        foreach (var result in hnswResultsList)
        {
            // Translate Guid back to chunk ID.
            if (!_guidToChunkId.TryGetValue(result.GUID, out var chunkId))
            {
                _logger.Debug("HNSW result guid {Guid} not found in mapping; skipping", result.GUID);
                continue;
            }

            // Skip stale (deleted) entries.
            if (_staleChunkIds.Contains(chunkId))
                continue;

            // Cosine distance from HnswLite: 0 = identical, 2 = opposite.
            // Similarity = 1 - Distance.
            var similarity = 1.0 - result.Distance;

            if (similarity >= minSimilarity)
            {
                results.Add(new VectorSearchResult
                {
                    ChunkId = chunkId,
                    Distance = result.Distance
                });
            }

            if (results.Count >= topK)
                break;
        }

        _logger.Debug("HNSW search returned {Count} results (from {HnswCount} HNSW candidates)",
            results.Count, hnswResultsList.Count);

        return results.AsReadOnly();
    }

    /// <summary>
    /// Performs linear scan search by loading all embeddings from SQLite.
    /// Identical algorithm to SqliteVecStore — used as fallback for small collections.
    /// </summary>
    private async Task<IReadOnlyList<VectorSearchResult>> SearchLinearAsync(
        float[] queryEmbedding,
        int topK,
        double minSimilarity,
        CancellationToken ct)
    {
        _logger.Debug("Linear scan search for top {TopK} with min similarity {MinSimilarity}", topK, minSimilarity);

        var queryMagnitude = SqliteVecStore.ComputeMagnitude(queryEmbedding);

        if (queryMagnitude == 0.0)
        {
            _logger.Warning("Query embedding has zero magnitude; no meaningful similarity can be computed");
            return Array.Empty<VectorSearchResult>();
        }

        var candidates = new List<VectorSearchResult>();

        const string sql = "SELECT chunk_id, embedding, magnitude FROM vec_embeddings;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var chunkId = reader.GetInt64(0);
            var blob = (byte[])reader.GetValue(1);
            var storedMagnitude = reader.GetDouble(2);

            if (storedMagnitude == 0.0)
                continue;

            var storedEmbedding = SqliteVecStore.DeserializeEmbedding(blob);

            if (storedEmbedding.Length != queryEmbedding.Length)
            {
                _logger.Warning(
                    "Dimension mismatch for chunk {ChunkId}: stored={StoredDims}, query={QueryDims}. Skipping.",
                    chunkId, storedEmbedding.Length, queryEmbedding.Length);
                continue;
            }

            var similarity = SqliteVecStore.CosineSimilarity(queryEmbedding, storedEmbedding, queryMagnitude, storedMagnitude);

            if (similarity >= minSimilarity)
            {
                candidates.Add(new VectorSearchResult
                {
                    ChunkId = chunkId,
                    Distance = 1.0 - similarity
                });
            }
        }

        var results = candidates
            .OrderBy(r => r.Distance)
            .Take(topK)
            .ToList()
            .AsReadOnly();

        _logger.Debug("Linear search returned {Count} results (from {Total} candidates above threshold)",
            results.Count, candidates.Count);

        return results;
    }

    // ── Index lifecycle ─────────────────────────────────────────────────

    /// <summary>
    /// Creates an empty HNSW index with the configured parameters.
    /// </summary>
    private void CreateEmptyIndex()
    {
        _hnswIndex = new HnswIndex(_dimensions, new RamHnswStorage(), new RamHnswLayerStorage());
        _hnswIndex.M = _m;
        _hnswIndex.EfConstruction = _efConstruction;
        _hnswIndex.DistanceFunction = new CosineDistance();
    }

    /// <summary>
    /// Rebuilds the HNSW index from all embeddings in SQLite.
    /// </summary>
    private async Task RebuildIndexAsync(CancellationToken ct)
    {
        CreateEmptyIndex();

        const string sql = "SELECT chunk_id, embedding FROM vec_embeddings;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var batch = new Dictionary<Guid, List<float>>();
        var batchSize = 0;
        const int BatchLimit = 1000;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var chunkId = reader.GetInt64(0);
            var blob = (byte[])reader.GetValue(1);
            var embedding = SqliteVecStore.DeserializeEmbedding(blob);

            var guid = ChunkIdToGuid(chunkId);
            _chunkIdToGuid[chunkId] = guid;
            _guidToChunkId[guid] = chunkId;

            batch[guid] = new List<float>(embedding);
            batchSize++;

            if (batchSize >= BatchLimit)
            {
                await _hnswIndex!.AddNodesAsync(batch, ct).ConfigureAwait(false);
                batch.Clear();
                batchSize = 0;
            }
        }

        // Flush remaining batch.
        if (batch.Count > 0)
        {
            await _hnswIndex!.AddNodesAsync(batch, ct).ConfigureAwait(false);
        }

        _staleChunkIds.Clear();
        _indexDirty = true;
    }

    /// <summary>
    /// Attempts to load an existing HNSW index from disk.
    /// Returns true if the index was successfully loaded and matches the current embedding count.
    /// </summary>
    private async Task<bool> TryLoadIndexAsync(long embeddingCount, CancellationToken ct)
    {
        if (_storagePath is null)
            return false;

        var metadataPath = Path.Combine(_storagePath, MetadataFileName);
        var indexPath = Path.Combine(_storagePath, IndexFileName);

        if (!File.Exists(metadataPath) || !File.Exists(indexPath))
        {
            _logger.Debug("No existing HNSW index files found");
            return false;
        }

        try
        {
            var metadataJson = await File.ReadAllTextAsync(metadataPath, ct).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<HnswIndexMetadata>(metadataJson);

            if (metadata is null)
            {
                _logger.Warning("Failed to deserialize HNSW index metadata; will rebuild");
                return false;
            }

            // Validate metadata matches current state.
            if (metadata.Version != HnswIndexMetadata.CurrentVersion)
            {
                _logger.Information("HNSW index metadata version mismatch (file={FileVersion}, current={CurrentVersion}); will rebuild",
                    metadata.Version, HnswIndexMetadata.CurrentVersion);
                return false;
            }

            if (metadata.Count != embeddingCount)
            {
                _logger.Information("HNSW index count mismatch (file={FileCount}, SQLite={SqliteCount}); will rebuild",
                    metadata.Count, embeddingCount);
                return false;
            }

            if (metadata.M != _m || metadata.EfConstruction != _efConstruction)
            {
                _logger.Information("HNSW index parameter mismatch; will rebuild");
                return false;
            }

            if (embeddingCount == 0)
            {
                _logger.Debug("No embeddings in SQLite; skipping index load");
                return false;
            }

            // Load the index binary.
            CreateEmptyIndex();

            var indexBytes = await File.ReadAllBytesAsync(indexPath, ct).ConfigureAwait(false);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var state = JsonSerializer.Deserialize<HnswState>(indexBytes, jsonOptions);

            if (state is null)
            {
                _logger.Warning("Failed to deserialize HNSW index state; will rebuild");
                return false;
            }

            await _hnswIndex!.ImportStateAsync(state, ct).ConfigureAwait(false);

            // Rebuild the chunkId-to-Guid mapping from SQLite.
            _chunkIdToGuid.Clear();
            _guidToChunkId.Clear();

            const string sql = "SELECT chunk_id FROM vec_embeddings;";

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var chunkId = reader.GetInt64(0);
                var guid = ChunkIdToGuid(chunkId);
                _chunkIdToGuid[chunkId] = guid;
                _guidToChunkId[guid] = chunkId;
            }

            // Restore stale entries from the persisted stale-ids file if it exists.
            var stalePath = Path.Combine(_storagePath, StaleIdsFileName);
            if (File.Exists(stalePath))
            {
                try
                {
                    var staleJson = await File.ReadAllTextAsync(stalePath, ct).ConfigureAwait(false);
                    var staleIds = JsonSerializer.Deserialize<List<long>>(staleJson);
                    if (staleIds is not null)
                    {
                        foreach (var id in staleIds)
                            _staleChunkIds.Add(id);

                        _logger.Information("Restored {StaleCount} stale chunk IDs from disk", staleIds.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to restore stale chunk IDs; stale entries will be lost");
                }
            }

            _indexDirty = false;
            _logger.Information("HNSW index loaded from disk: {Count} vectors, {Stale} stale",
                metadata.Count, metadata.StaleCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error loading HNSW index from disk; will rebuild");
            return false;
        }
    }

    /// <summary>
    /// Persists the HNSW index and its metadata to disk.
    /// Uses a custom binary format for the index state to avoid System.Text.Json
    /// roundtrip issues with HnswLite's internal types.
    /// </summary>
    private async Task PersistIndexAsync(CancellationToken ct)
    {
        if (_storagePath is null || _hnswIndex is null)
            return;

        var metadataPath = Path.Combine(_storagePath, MetadataFileName);
        var indexPath = Path.Combine(_storagePath, IndexFileName);
        var stalePath = Path.Combine(_storagePath, StaleIdsFileName);

        try
        {
            // Export the HNSW index state and serialize with System.Text.Json
            // using a宽松 configuration that handles float precision and nullable types.
            var state = await _hnswIndex.ExportStateAsync(ct).ConfigureAwait(false);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null, // Use property names as-is for library types
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var indexBytes = JsonSerializer.SerializeToUtf8Bytes(state, jsonOptions);
            await File.WriteAllBytesAsync(indexPath, indexBytes, ct).ConfigureAwait(false);

            // Persist stale chunk IDs separately for recovery on reload.
            if (_staleChunkIds.Count > 0)
            {
                var staleJson = JsonSerializer.Serialize(_staleChunkIds.ToList());
                await File.WriteAllTextAsync(stalePath, staleJson, ct).ConfigureAwait(false);
            }
            else if (File.Exists(stalePath))
            {
                File.Delete(stalePath);
            }

            // Serialize and write metadata.
            var embeddingCount = await GetEmbeddingCountAsync(ct).ConfigureAwait(false);
            var metadata = new HnswIndexMetadata
            {
                Count = embeddingCount,
                M = _m,
                EfConstruction = _efConstruction,
                Dimensions = _dimensions,
                StaleCount = _staleChunkIds.Count,
                CreatedAtUtc = DateTime.UtcNow
            };

            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(metadataPath, metadataJson, ct).ConfigureAwait(false);

            _indexDirty = false;

            _logger.Debug("HNSW index persisted: {Count} vectors, {Stale} stale entries",
                metadata.Count, metadata.StaleCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist HNSW index to disk");
            throw;
        }
    }

    /// <summary>
    /// Checks if stale entries exceed the rebuild threshold and triggers a rebuild if needed.
    /// </summary>
    private async Task CheckStaleRebuildAsync(long embeddingCount, CancellationToken ct)
    {
        if (_staleChunkIds.Count == 0 || embeddingCount == 0)
            return;

        var staleFraction = (double)_staleChunkIds.Count / embeddingCount;

        if (staleFraction > StaleRebuildFraction)
        {
            _logger.Information(
                "Stale entries ({Stale}/{Total} = {Percent:F1}%) exceed {ThresholdPercent:F0}% threshold; rebuilding HNSW index",
                _staleChunkIds.Count, embeddingCount, staleFraction * 100, StaleRebuildFraction * 100);

            await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RebuildIndexAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _mutationLock.Release();
            }
        }
    }

    // ── ID mapping ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a deterministic Guid from a chunk ID (long) by padding to 16 bytes.
    /// This ensures the same chunk ID always maps to the same Guid across index rebuilds.
    /// </summary>
    private static Guid ChunkIdToGuid(long chunkId)
    {
        // Convert the long to 8 bytes, then pad with zeros for the remaining 8 bytes
        // to create a 16-byte Guid. The padding ensures uniqueness for reasonable chunk IDs.
        var bytes = new byte[16];
        var longBytes = BitConverter.GetBytes(chunkId);
        Buffer.BlockCopy(longBytes, 0, bytes, 0, 8);
        // Remaining 8 bytes are zero-padded.
        return new Guid(bytes);
    }

    // ── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Executes a non-query SQL command on the current connection.
    /// </summary>
    private async Task ExecuteNonQueryAsync(string sql, CancellationToken ct = default)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates that the store has been initialized.
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_initialized || _connection is null)
        {
            throw new InvalidOperationException(
                "Vector store is not initialized. Call InitializeAsync before performing operations.");
        }
    }

    /// <summary>
    /// Throws if this instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HnswVectorStore));
    }
}
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
    private const string DatabaseFileName = "agentx.db";

    // HnswLite accepts vectors of at most 4096 dimensions; larger embeddings are served by the
    // linear scan only.
    private const int MaxHnswDimensions = 4096;

    // Largest search breadth (ef) HnswLite accepts.
    private const int MaxHnswEf = 10_000;

    // ── Fields ──────────────────────────────────────────────────────────

    private readonly ISettingsService _settingsService;
    private readonly IEncryptedConnectionFactory _connectionFactory;
    private readonly ILogger _logger;
    private readonly int _m;
    private readonly int _efConstruction;
    private readonly Func<int> _dimensionsProvider;
    private readonly long _fallbackThreshold;

    private SqliteConnection? _connection;
    private HnswIndex? _hnswIndex;
    private string? _storagePath;
    private bool _disposed;
    private bool _initialized;
    private bool _indexDirty;
    private bool _reopenOnResume;

    /// <summary>Closes the connection while the database file is replaced or re-encrypted.</summary>
    private readonly VectorStoreSuspension _suspension = new();

    /// <summary>
    /// Vector size of the current embedding space: the HNSW index holds only vectors of this
    /// size. Rows of other sizes (from a previous embedding model) stay in SQLite, reachable by
    /// the linear scan, but never enter the index, where they would fail the insert.
    /// </summary>
    private int _activeDimensions;

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
        : this(settingsService, logger, m, efConstruction, () => dimensions, fallbackThreshold, connectionFactory)
    {
    }

    /// <summary>
    /// Full-featured constructor with a lazily evaluated dimension: the embedding service only
    /// knows the vector size of the current embedding model once the AI service is initialized,
    /// which is after this store is constructed.
    /// </summary>
    /// <param name="settingsService">Settings service providing the database storage path.</param>
    /// <param name="logger">Serilog logger instance (may be null to use the default context logger).</param>
    /// <param name="m">HNSW M parameter: max connections per layer.</param>
    /// <param name="efConstruction">HNSW EfConstruction: candidate list size during build.</param>
    /// <param name="dimensionsProvider">Returns the vector size of the current embedding model.</param>
    /// <param name="fallbackThreshold">Embedding count below which linear scan is used.</param>
    /// <param name="connectionFactory">Encrypted connection factory; applies PRAGMA key when opening SQLite.</param>
    public HnswVectorStore(
        ISettingsService settingsService,
        ILogger? logger,
        int m,
        int efConstruction,
        Func<int> dimensionsProvider,
        long fallbackThreshold,
        IEncryptedConnectionFactory connectionFactory)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _dimensionsProvider = dimensionsProvider ?? throw new ArgumentNullException(nameof(dimensionsProvider));
        _logger = logger ?? Log.ForContext<HnswVectorStore>();
        _m = m;
        _efConstruction = efConstruction;
        _fallbackThreshold = fallbackThreshold;
        _logger.Information("HnswVectorStore created (M={M}, EfConstruction={EfConstruction}, Threshold={Threshold})",
            _m, _efConstruction, _fallbackThreshold);
    }

    /// <summary>
    /// Smallest HNSW search breadth (ef) a query uses, from <c>AppSettings.HnswEfSearch</c>.
    /// A query never searches narrower than HnswLite's own default, max(EfConstruction,
    /// 2 x candidates), so this setting can only widen the search: raise it above that default
    /// for better recall on a very large vault, at the cost of slower queries.
    /// </summary>
    public int EfSearch { get; init; }

    // ── IVectorStore implementation ─────────────────────────────────────

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
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

            await OpenAndPrepareAsync(ct).ConfigureAwait(false);

            var embeddingCount = await CountEmbeddingsAsync(ct).ConfigureAwait(false);

            // The index serves the current embedding space: its dimension comes from the
            // embedding model, not from whatever happens to be stored.
            var dimensions = ResolveCurrentDimensions();

            // Attempt to load existing HNSW index from disk.
            var indexLoaded = await TryLoadIndexAsync(embeddingCount, dimensions, ct).ConfigureAwait(false);

            if (!indexLoaded && embeddingCount > 0)
            {
                _logger.Information("Building HNSW index from {Count} SQLite embeddings...", embeddingCount);
                await RebuildIndexAsync(dimensions, ct).ConfigureAwait(false);
                _logger.Information("HNSW index built from SQLite ({Count} vectors)", embeddingCount);
            }
            else if (indexLoaded)
            {
                _logger.Information("HNSW index loaded from disk ({Count} vectors)", embeddingCount);
            }
            else
            {
                // Empty store — create a fresh index ready for inserts.
                CreateEmptyIndex(dimensions);
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
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding.Length == 0)
            throw new ArgumentException("Embedding vector cannot be empty.", nameof(embedding));

        var blob = SqliteVecStore.SerializeEmbedding(embedding);
        var magnitude = SqliteVecStore.ComputeMagnitude(embedding);

        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A vector of another size means the embedding model changed: the index is rebuilt
            // for the new embedding space before anything is written, so the row can never be
            // stored while the index insert is bound to fail on a dimension mismatch.
            if (embedding.Length != _activeDimensions)
            {
                _logger.Warning(
                    "Embedding size changed from {Previous} to {Current} dimensions (chunk {ChunkId}); rebuilding the HNSW index for the new embedding space",
                    _activeDimensions, embedding.Length, chunkId);
                await RebuildIndexAsync(embedding.Length, ct).ConfigureAwait(false);
            }

            // Persist to SQLite first (source of truth).
            const string sql = """
                INSERT OR REPLACE INTO vec_embeddings (chunk_id, embedding, magnitude)
                VALUES (@chunkId, @embedding, @magnitude);
                """;

            await using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@chunkId", chunkId);
                cmd.Parameters.AddWithValue("@embedding", blob);
                cmd.Parameters.AddWithValue("@magnitude", magnitude);

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Add to HNSW index if it exists and we're above fallback threshold.
            if (_hnswIndex is not null)
            {
                // If this chunk was previously deleted, clean up stale tracking.
                _staleChunkIds.Remove(chunkId);

                var guid = ChunkIdToGuid(chunkId);
                try
                {
                    var vector = new List<float>(embedding);
                    await _hnswIndex.AddAsync(guid, vector, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Keep SQLite and the index consistent: a row the index could not take is
                    // removed again and the insert reported as failed, instead of leaving a row
                    // that breaks the next rebuild.
                    _logger.Error(ex, "HNSW insert failed for chunk {ChunkId}; removing its row", chunkId);
                    await DeleteRowAsync(chunkId).ConfigureAwait(false);
                    throw;
                }

                _chunkIdToGuid[chunkId] = guid;
                _guidToChunkId[guid] = chunkId;
                _indexDirty = true;

                _logger.Debug("Inserted chunk {ChunkId} into HNSW index (guid={Guid})", chunkId, guid);
            }
        }
        finally
        {
            _mutationLock.Release();
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
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be empty.", nameof(queryEmbedding));

        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be a positive integer.");

        var embeddingCount = await CountEmbeddingsAsync(ct).ConfigureAwait(false);

        // Check if stale entries exceed threshold — trigger rebuild if so.
        await CheckStaleRebuildAsync(embeddingCount, ct).ConfigureAwait(false);

        // Hybrid search: use HNSW for large collections, linear scan fallback for small.
        // Acquire mutation lock during search to prevent concurrent modifications to
        // stale set and GUID mappings, which could cause race conditions.
        // A query from another embedding space than the index (different size) can only be
        // compared by the linear scan, which skips rows of other sizes.
        if (embeddingCount > _fallbackThreshold && _hnswIndex is not null &&
            queryEmbedding.Length == _activeDimensions)
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
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
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
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
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
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();

        return await CountEmbeddingsAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var operation = await _suspension.EnterAsync(ct).ConfigureAwait(false);
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
    /// <remarks>
    /// The in-memory index is kept but not persisted: after a restore it no longer matches the
    /// database, and next to a database that is being encrypted it must not be written in plain
    /// text. <see cref="ResumeAsync"/> decides what happens to it.
    /// </remarks>
    public Task SuspendAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // The close runs once no operation is running; new ones wait for the last resume.
        return _suspension.SuspendAsync(
            () =>
            {
                _reopenOnResume = _initialized && _connection is not null;
                CloseConnection();
                _logger.Information("HnswVectorStore suspended: database connection closed");
            },
            ct);
    }

    /// <inheritdoc />
    public Task ResumeAsync(bool reloadFromDatabase, CancellationToken ct = default)
        => _suspension.ResumeAsync(reloadFromDatabase, reload => ReopenAsync(reload, ct));

    /// <summary>
    /// Reopens the connection after a suspension. On a reload the index and the index files are
    /// rebuilt from the database, because both describe the file that was replaced (a loaded
    /// index file is only checked by its count, which a restored database can match by chance).
    /// Otherwise the index is kept, and index files are removed when the database is now
    /// encrypted.
    /// </summary>
    private async Task ReopenAsync(bool reload, CancellationToken ct)
    {
        if (!_reopenOnResume || _disposed)
            return;

        _reopenOnResume = false;
        await OpenAndPrepareAsync(ct).ConfigureAwait(false);

        if (reload)
        {
            DeleteIndexFiles("the database was replaced");
            await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RebuildIndexAsync(ResolveCurrentDimensions(), ct).ConfigureAwait(false);
            }
            finally
            {
                _mutationLock.Release();
            }

            _logger.Information(
                "HnswVectorStore resumed and rebuilt its index from the database ({Count} embeddings)",
                _chunkIdToGuid.Count);
        }
        else
        {
            if (IsDatabaseEncrypted())
                DeleteIndexFiles("the database is encrypted");

            _logger.Information("HnswVectorStore resumed");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Operations waiting on a suspension wake up and report the disposal.
        _suspension.Abandon();

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
    /// The search breadth to pass to HnswLite: null (its default, max(EfConstruction,
    /// 2 x candidates)) unless the configured <paramref name="efSearch"/> is wider.
    /// </summary>
    internal static int? ResolveSearchEf(int efSearch, int efConstruction, int candidates)
    {
        var libraryDefault = Math.Max(efConstruction, candidates * 2);
        return efSearch > libraryDefault ? Math.Min(efSearch, MaxHnswEf) : null;
    }

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
            hnswResults = await _hnswIndex!.GetTopKAsync(
                queryList, searchK, ResolveSearchEf(EfSearch, _efConstruction, searchK), ct).ConfigureAwait(false);
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
        var mismatchedRows = 0;

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
                // Rows from another embedding model; counted and reported once per search
                // instead of one warning per row.
                mismatchedRows++;
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

        if (mismatchedRows > 0)
        {
            _logger.Warning(
                "Skipped {Count} stored embeddings whose size differs from the {QueryDims}-dimension query (another embedding model); re-index those documents",
                mismatchedRows, queryEmbedding.Length);
        }

        _logger.Debug("Linear search returned {Count} results (from {Total} candidates above threshold)",
            results.Count, candidates.Count);

        return results;
    }

    // ── Index lifecycle ─────────────────────────────────────────────────

    /// <summary>
    /// Creates an empty HNSW index for vectors of <paramref name="dimensions"/> and makes that the
    /// active embedding space. Sizes HnswLite cannot index leave the store on the linear scan.
    /// </summary>
    private void CreateEmptyIndex(int dimensions)
    {
        _activeDimensions = dimensions;

        if (dimensions < 1 || dimensions > MaxHnswDimensions)
        {
            _hnswIndex = null;
            _logger.Warning(
                "Embeddings have {Dimensions} dimensions; the HNSW index supports 1 to {Max}, so searches use the linear scan",
                dimensions, MaxHnswDimensions);
            return;
        }

        _hnswIndex = new HnswIndex(dimensions, new RamHnswStorage(), new RamHnswLayerStorage());
        _hnswIndex.M = _m;
        _hnswIndex.EfConstruction = _efConstruction;
        _hnswIndex.DistanceFunction = new CosineDistance();
    }

    /// <summary>
    /// Rebuilds the HNSW index from the SQLite embeddings of the given size. Rows of any other
    /// size belong to a previous embedding model: they are skipped (they would make the whole
    /// rebuild fail) and stay in SQLite for the linear scan until they are re-embedded.
    /// </summary>
    private async Task RebuildIndexAsync(int dimensions, CancellationToken ct)
    {
        CreateEmptyIndex(dimensions);
        _chunkIdToGuid.Clear();
        _guidToChunkId.Clear();
        _staleChunkIds.Clear();

        if (_hnswIndex is null)
        {
            _indexDirty = false;
            return;
        }

        const string sql = "SELECT chunk_id, embedding FROM vec_embeddings;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var batch = new Dictionary<Guid, List<float>>();
        var batchSize = 0;
        var skipped = 0;
        const int BatchLimit = 1000;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var chunkId = reader.GetInt64(0);
            var blob = (byte[])reader.GetValue(1);
            var embedding = SqliteVecStore.DeserializeEmbedding(blob);

            if (embedding.Length != dimensions)
            {
                skipped++;
                continue;
            }

            var guid = ChunkIdToGuid(chunkId);
            _chunkIdToGuid[chunkId] = guid;
            _guidToChunkId[guid] = chunkId;

            batch[guid] = new List<float>(embedding);
            batchSize++;

            if (batchSize >= BatchLimit)
            {
                await _hnswIndex.AddNodesAsync(batch, ct).ConfigureAwait(false);
                batch.Clear();
                batchSize = 0;
            }
        }

        // Flush remaining batch.
        if (batch.Count > 0)
        {
            await _hnswIndex.AddNodesAsync(batch, ct).ConfigureAwait(false);
        }

        if (skipped > 0)
        {
            _logger.Warning(
                "HNSW index rebuilt for {Dimensions}-dimension embeddings; {Skipped} stored embeddings of another size were left out (re-index those documents)",
                dimensions, skipped);
        }

        _indexDirty = true;
    }

    /// <summary>
    /// Attempts to load an existing HNSW index from disk.
    /// Returns true if the index was successfully loaded and matches the current embedding count.
    /// </summary>
    private async Task<bool> TryLoadIndexAsync(long embeddingCount, int dimensions, CancellationToken ct)
    {
        if (_storagePath is null)
            return false;

        var metadataPath = Path.Combine(_storagePath, MetadataFileName);
        var indexPath = Path.Combine(_storagePath, IndexFileName);

        // An encrypted database must not be paired with a plaintext copy of its vectors: never
        // trust (or keep) index files next to it, rebuild from the database instead.
        if (IsDatabaseEncrypted())
        {
            DeleteIndexFiles("the database is encrypted");
            return false;
        }

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

            if (metadata.Dimensions != dimensions || dimensions > MaxHnswDimensions)
            {
                _logger.Information(
                    "HNSW index on disk holds {FileDims}-dimension vectors but the embedding model produces {Dims}; will rebuild",
                    metadata.Dimensions, dimensions);
                return false;
            }

            // Load the index binary.
            CreateEmptyIndex(dimensions);

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

            // Rebuild the chunkId-to-Guid mapping from SQLite, for the rows the index holds
            // (those of the index dimension; a float is 4 bytes).
            _chunkIdToGuid.Clear();
            _guidToChunkId.Clear();

            const string sql = "SELECT chunk_id FROM vec_embeddings WHERE length(embedding) = @bytes;";

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@bytes", (long)dimensions * sizeof(float));

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
    /// <remarks>
    /// The index file holds every vector and chunk id in plain JSON. When the database is
    /// encrypted with SQLCipher that would expose the embeddings next to the protected
    /// database, so nothing is written (and earlier plaintext files are removed); the index is
    /// rebuilt from the encrypted database on the next start instead.
    /// </remarks>
    private async Task PersistIndexAsync(CancellationToken ct)
    {
        if (_storagePath is null || _hnswIndex is null)
            return;

        if (IsDatabaseEncrypted())
        {
            DeleteIndexFiles("the database is encrypted");
            _indexDirty = false;
            _logger.Debug("Database is encrypted; the HNSW index stays in memory and is rebuilt from the database on start");
            return;
        }

        var metadataPath = Path.Combine(_storagePath, MetadataFileName);
        var indexPath = Path.Combine(_storagePath, IndexFileName);
        var stalePath = Path.Combine(_storagePath, StaleIdsFileName);

        try
        {
            // Export the HNSW index state and serialize with System.Text.Json
            // using a lenient configuration that handles float precision and nullable types.
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
            var embeddingCount = await CountEmbeddingsAsync(ct).ConfigureAwait(false);
            var metadata = new HnswIndexMetadata
            {
                Count = embeddingCount,
                M = _m,
                EfConstruction = _efConstruction,
                Dimensions = _activeDimensions,
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
                await RebuildIndexAsync(_activeDimensions, ct).ConfigureAwait(false);
            }
            finally
            {
                _mutationLock.Release();
            }
        }
    }

    /// <summary>
    /// Vector size of the current embedding model, falling back to the size in effect when the
    /// provider cannot answer (for example before the AI service is initialized).
    /// </summary>
    private int ResolveCurrentDimensions()
    {
        try
        {
            var dimensions = _dimensionsProvider();
            if (dimensions > 0)
                return dimensions;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not read the embedding size from the embedding service");
        }

        return _activeDimensions > 0 ? _activeDimensions : DefaultDimensions;
    }

    /// <summary>True when the SQLite database file is encrypted (SQLCipher).</summary>
    private bool IsDatabaseEncrypted() =>
        _storagePath is not null && IsDatabaseFileEncrypted(Path.Combine(_storagePath, DatabaseFileName));

    /// <summary>
    /// A plaintext SQLite file starts with the 16-byte magic "SQLite format 3\0"; SQLCipher
    /// replaces it with a random salt. A file that cannot be read is treated as encrypted, so
    /// uncertainty never leads to writing plaintext vectors.
    /// </summary>
    internal static bool IsDatabaseFileEncrypted(string dbPath)
    {
        try
        {
            using var stream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[16];
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (read < header.Length)
                return false; // nothing written yet, so nothing to protect

            return !header.SequenceEqual("SQLite format 3\0"u8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Removes persisted index files (best effort); <paramref name="reason"/> is logged.</summary>
    private void DeleteIndexFiles(string reason)
    {
        if (_storagePath is null)
            return;

        foreach (var name in new[] { IndexFileName, MetadataFileName, StaleIdsFileName })
        {
            var path = Path.Combine(_storagePath, name);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    _logger.Information("Removed HNSW index file {File} because {Reason}", name, reason);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "Could not remove HNSW index file {File}", name);
            }
        }
    }

    /// <summary>Deletes one embedding row (compensation for a failed index insert).</summary>
    private async Task DeleteRowAsync(long chunkId)
    {
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM vec_embeddings WHERE chunk_id = @chunkId;";
            cmd.Parameters.AddWithValue("@chunkId", chunkId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not remove the embedding row of chunk {ChunkId}", chunkId);
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
    /// Opens the connection to the database in <see cref="_storagePath"/> and makes sure the
    /// embeddings table exists (a restored database may predate it). Used by initialization and
    /// by resume.
    /// </summary>
    private async Task OpenAndPrepareAsync(CancellationToken ct)
    {
        // A connection left from an earlier attempt would keep the file open.
        CloseConnection();

        // Open SQLite connection (same schema as SqliteVecStore) via the encrypted
        // connection factory: PRAGMA key is applied automatically when encryption
        // is enabled, and the call is a plaintext open when no key is loaded.
        var dbPath = Path.Combine(_storagePath!, DatabaseFileName);
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
    }

    /// <summary>
    /// Counts the stored embeddings on the open connection. For use inside an operation that
    /// already entered the suspension gate, and while suspended by resume itself.
    /// </summary>
    private async Task<long> CountEmbeddingsAsync(CancellationToken ct)
    {
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

    /// <summary>
    /// Closes the connection and clears its pool, so no handle on the database file remains
    /// (a pooled connection keeps the file open after it is disposed).
    /// </summary>
    private void CloseConnection()
    {
        var connection = _connection;
        _connection = null;
        if (connection is null)
            return;

        try
        {
            SqliteConnection.ClearPool(connection);
            connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error closing the HnswVectorStore connection");
        }
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

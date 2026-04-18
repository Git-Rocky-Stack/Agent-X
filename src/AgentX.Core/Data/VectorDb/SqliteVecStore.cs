using System.Buffers;
using AgentX.Core.Services.Settings;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// Pure-SQLite vector store implementation that stores embeddings as BLOBs in a regular
/// table and computes cosine similarity in C# after loading candidates from the database.
///
/// This approach avoids the native sqlite-vec extension dependency, making the application
/// portable across all Windows machines without requiring additional native libraries.
///
/// Schema:
///   vec_embeddings (
///     chunk_id  INTEGER PRIMARY KEY,
///     embedding BLOB NOT NULL,          -- float[] serialized via Buffer.BlockCopy
///     magnitude REAL NOT NULL           -- pre-computed L2 norm for fast cosine similarity
///   )
///
/// Performance characteristics:
/// - Insert: O(1) per embedding
/// - Search: O(N) where N is the total embedding count (full scan with C# dot product)
/// - Suitable for collections up to ~100K embeddings on modern hardware
/// </summary>
public sealed class SqliteVecStore : IVectorStore
{
    private readonly ISettingsService _settingsService;
    private readonly IEncryptedConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    private SqliteConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Creates a new SqliteVecStore.
    /// </summary>
    /// <param name="settingsService">Settings service providing the database storage path.</param>
    /// <param name="connectionFactory">Encrypted connection factory — required so PRAGMA key is applied when opening SQLite.</param>
    public SqliteVecStore(ISettingsService settingsService, IEncryptedConnectionFactory connectionFactory)
        : this(settingsService, logger: null, connectionFactory)
    {
    }

    /// <summary>
    /// Creates a new SqliteVecStore with an explicit logger.
    /// </summary>
    /// <param name="settingsService">Settings service providing the database storage path.</param>
    /// <param name="logger">Serilog logger instance (may be null to use the default context logger).</param>
    /// <param name="connectionFactory">Encrypted connection factory — required so PRAGMA key is applied when opening SQLite.</param>
    public SqliteVecStore(ISettingsService settingsService, ILogger? logger, IEncryptedConnectionFactory connectionFactory)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? Log.ForContext<SqliteVecStore>();
        _logger.Information("SqliteVecStore created");
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        _logger.Information("Initializing SqliteVecStore...");

        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            var storagePath = settings.StoragePath;

            // Ensure the storage directory exists.
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
                _logger.Debug("Created storage directory: {Path}", storagePath);
            }

            var dbPath = Path.Combine(storagePath, "agentx.db");
            // Route through IEncryptedConnectionFactory so PRAGMA key is applied when
            // encryption is enabled. When no key is loaded, the factory performs a
            // plaintext open.
            _connection = _connectionFactory.OpenKeyed(dbPath);

            _logger.Debug("SQLite connection opened: {Path}", dbPath);

            // Enable WAL mode for better concurrent read/write performance.
            await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);

            // Create the embeddings table if it does not exist.
            const string createTableSql = """
                CREATE TABLE IF NOT EXISTS vec_embeddings (
                    chunk_id  INTEGER PRIMARY KEY,
                    embedding BLOB NOT NULL,
                    magnitude REAL NOT NULL
                );
                """;

            await ExecuteNonQueryAsync(createTableSql, ct).ConfigureAwait(false);

            // Create index for chunk_id lookups (the PRIMARY KEY already provides this,
            // but we create an explicit index name for clarity in EXPLAIN plans).
            const string createIndexSql = """
                CREATE INDEX IF NOT EXISTS idx_vec_chunk ON vec_embeddings(chunk_id);
                """;

            await ExecuteNonQueryAsync(createIndexSql, ct).ConfigureAwait(false);

            var count = await GetEmbeddingCountAsync(ct).ConfigureAwait(false);
            _logger.Information("SqliteVecStore initialized with {Count} existing embeddings", count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize SqliteVecStore");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureConnection();
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding.Length == 0)
            throw new ArgumentException("Embedding vector cannot be empty.", nameof(embedding));

        var blob = SerializeEmbedding(embedding);
        var magnitude = ComputeMagnitude(embedding);

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
        EnsureConnection();
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be empty.", nameof(queryEmbedding));

        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be a positive integer.");

        var queryMagnitude = ComputeMagnitude(queryEmbedding);

        if (queryMagnitude == 0.0)
        {
            _logger.Warning("Query embedding has zero magnitude; no meaningful similarity can be computed");
            return Array.Empty<VectorSearchResult>();
        }

        _logger.Debug("Searching for top {TopK} embeddings with min similarity {MinSimilarity}",
            topK, minSimilarity);

        // Load all embeddings from the database and compute cosine similarity in C#.
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

            // Skip entries with zero magnitude (degenerate embeddings).
            if (storedMagnitude == 0.0)
                continue;

            var storedEmbedding = DeserializeEmbedding(blob);

            // Ensure dimension compatibility.
            if (storedEmbedding.Length != queryEmbedding.Length)
            {
                _logger.Warning(
                    "Dimension mismatch for chunk {ChunkId}: stored={StoredDims}, query={QueryDims}. Skipping.",
                    chunkId, storedEmbedding.Length, queryEmbedding.Length);
                continue;
            }

            var similarity = CosineSimilarity(queryEmbedding, storedEmbedding, queryMagnitude, storedMagnitude);

            if (similarity >= minSimilarity)
            {
                candidates.Add(new VectorSearchResult
                {
                    ChunkId = chunkId,
                    // Distance = 1.0 - similarity, so that the Similarity property
                    // (defined as 1.0 - Distance) returns the correct cosine similarity.
                    Distance = 1.0 - similarity
                });
            }
        }

        // Sort by similarity descending (which is distance ascending) and take topK.
        var results = candidates
            .OrderBy(r => r.Distance)
            .Take(topK)
            .ToList()
            .AsReadOnly();

        _logger.Debug("Search returned {Count} results (from {Total} candidates above threshold)",
            results.Count, candidates.Count);

        return results;
    }

    /// <inheritdoc />
    public async Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureConnection();

        const string sql = "DELETE FROM vec_embeddings WHERE chunk_id = @chunkId;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chunkId", chunkId);

        var deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        _logger.Debug("Deleted embedding for chunk {ChunkId} (rows affected: {Deleted})", chunkId, deleted);
    }

    /// <inheritdoc />
    public async Task DeleteEmbeddingsForDocumentAsync(
        long documentId,
        IReadOnlyList<long> chunkIds,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureConnection();
        ArgumentNullException.ThrowIfNull(chunkIds);

        if (chunkIds.Count == 0)
        {
            _logger.Debug("No chunk IDs provided for document {DocumentId}, nothing to delete", documentId);
            return;
        }

        _logger.Information("Deleting {Count} embeddings for document {DocumentId}", chunkIds.Count, documentId);

        // Build a parameterized IN clause to prevent SQL injection.
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

        _logger.Information(
            "Deleted {Deleted} embeddings for document {DocumentId} (requested: {Requested})",
            deleted, documentId, chunkIds.Count);
    }

    /// <inheritdoc />
    public async Task<long> GetEmbeddingCountAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureConnection();

        const string sql = "SELECT COUNT(*) FROM vec_embeddings;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureConnection();

        _logger.Information("Optimizing vector store (VACUUM)...");

        await ExecuteNonQueryAsync("VACUUM;", ct).ConfigureAwait(false);

        _logger.Information("Vector store optimization complete");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_connection is not null)
        {
            _logger.Debug("Closing SqliteVecStore connection...");

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

        _logger.Information("SqliteVecStore disposed");
    }

    // ── Embedding serialization ─────────────────────────────────────────

    /// <summary>
    /// Serializes a float array to a byte array using direct memory copy.
    /// Each float is 4 bytes, so the resulting byte array is 4x the float array length.
    /// </summary>
    internal static byte[] SerializeEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Deserializes a byte array back to a float array using direct memory copy.
    /// </summary>
    internal static float[] DeserializeEmbedding(byte[] blob)
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

    // ── Vector math ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the L2 (Euclidean) magnitude of a vector: sqrt(sum of squares).
    /// </summary>
    internal static double ComputeMagnitude(float[] vector)
    {
        var sumOfSquares = 0.0;

        // Process in chunks for better CPU cache utilization on large vectors.
        for (var i = 0; i < vector.Length; i++)
        {
            double val = vector[i];
            sumOfSquares += val * val;
        }

        return Math.Sqrt(sumOfSquares);
    }

    /// <summary>
    /// Computes cosine similarity between two vectors using pre-computed magnitudes.
    ///
    /// cosine_similarity = dot(a, b) / (|a| * |b|)
    ///
    /// Returns a value in the range [-1.0, 1.0] where:
    ///   1.0 = identical direction
    ///   0.0 = orthogonal (no similarity)
    ///  -1.0 = opposite direction
    /// </summary>
    internal static double CosineSimilarity(
        float[] vectorA,
        float[] vectorB,
        double magnitudeA,
        double magnitudeB)
    {
        if (magnitudeA == 0.0 || magnitudeB == 0.0)
            return 0.0;

        var dotProduct = 0.0;

        for (var i = 0; i < vectorA.Length; i++)
        {
            dotProduct += (double)vectorA[i] * vectorB[i];
        }

        return dotProduct / (magnitudeA * magnitudeB);
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
    /// Validates that the database connection is open and ready.
    /// </summary>
    private void EnsureConnection()
    {
        if (_connection is null || _connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "Vector store is not initialized. Call InitializeAsync before performing operations.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteVecStore));
    }
}

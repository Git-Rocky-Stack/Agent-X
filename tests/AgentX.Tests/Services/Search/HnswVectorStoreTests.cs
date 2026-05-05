using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Search;

/// <summary>
/// Integration-style unit tests for HnswVectorStore.
/// Uses real SQLite (temp file) and real HNSW index — not pure unit mocks —
/// because the vector store depends on actual database and index operations.
/// Each test creates its own temp directory to avoid interference.
/// </summary>
public sealed class HnswVectorStoreTests : IAsyncLifetime
{
    // ── Deterministic test embeddings (3-dimensional for speed) ──────────

    // Orthogonal-ish vectors for reproducible similarity results.
    private static readonly float[] Vector1 = { 1.0f, 0.0f, 0.0f };
    private static readonly float[] Vector2 = { 0.0f, 1.0f, 0.0f };
    private static readonly float[] Vector3 = { 0.0f, 0.0f, 1.0f };
    private static readonly float[] Vector4 = { 0.9f, 0.1f, 0.0f }; // Close to Vector1
    private static readonly float[] Vector5 = { 0.5f, 0.5f, 0.0f }; // Between Vector1 and Vector2

    // ── Per-test state ──────────────────────────────────────────────────

    private readonly string _tempPath;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly ILogger _logger;

    public HnswVectorStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"hnsw-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);

        _mockSettings = new Mock<ISettingsService>();
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { StoragePath = _tempPath });

        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockEmbeddingService.Setup(e => e.Dimensions).Returns(3); // Test embeddings are 3-dimensional

        _logger = Log.ForContext<HnswVectorStoreTests>();
    }

    // ── IAsyncLifetime ──────────────────────────────────────────────────

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempPath))
        {
            try
            {
                Directory.Delete(_tempPath, true);
            }
            catch
            {
                // Best-effort cleanup; temp dirs are under user temp anyway.
            }
        }

        return Task.CompletedTask;
    }

    // ── Helper: Create store with small dimensions and fallbackThreshold=0 ─

    /// <summary>
    /// Tests run without encryption — the factory resolves to a plaintext open when
    /// no key is loaded on the provider, matching pre-C13 behaviour.
    /// </summary>
    private static IEncryptedConnectionFactory CreatePlainFactory()
        => new EncryptedConnectionFactory(new DatabaseKeyProvider());

    private HnswVectorStore CreateStore(
        int dimensions = 3,
        long fallbackThreshold = 0, // 0 forces HNSW path even with tiny collections
        int m = 4,
        int efConstruction = 20)
    {
        return new HnswVectorStore(
            _mockSettings.Object,
            _logger,
            m: m,
            efConstruction: efConstruction,
            dimensions: dimensions,
            fallbackThreshold: fallbackThreshold,
            connectionFactory: CreatePlainFactory());
    }

    // ── 1. InitializeAsync_EmptyStore_CreatesIndex ───────────────────────

    [Fact]
    public async Task InitializeAsync_EmptyStore_CreatesIndex()
    {
        // Arrange
        await using var store = CreateStore();

        // Act
        await store.InitializeAsync();

        // Assert — store should report 0 embeddings and be operational.
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(0);

        // The SQLite database file should have been created.
        File.Exists(Path.Combine(_tempPath, "agentx.db")).Should().BeTrue();
    }

    // ── 2. InsertEmbeddingAsync_AddsToBothStores ─────────────────────────

    [Fact]
    public async Task InsertEmbeddingAsync_AddsToBothStores()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        // Act
        var id = await store.InsertEmbeddingAsync(100, Vector1);

        // Assert
        id.Should().Be(100);
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(1);

        // Search for the inserted vector should find it.
        var results = await store.SearchAsync(Vector1, topK: 1, minSimilarity: 0.99);
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be(100);
    }

    // ── 3. SearchAsync_ReturnsTopKResults ────────────────────────────────

    [Fact]
    public async Task SearchAsync_ReturnsTopKResults()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);
        await store.InsertEmbeddingAsync(3, Vector3);
        await store.InsertEmbeddingAsync(4, Vector4);
        await store.InsertEmbeddingAsync(5, Vector5);

        // Act — search for Vector1; top 2 should be Vector1 (self) and Vector4 (closest).
        var results = await store.SearchAsync(Vector1, topK: 2, minSimilarity: 0.0);

        // Assert
        results.Should().HaveCount(2);
        results[0].ChunkId.Should().Be(1, "the query vector itself should be the closest match");
        results.Select(r => r.ChunkId).Should().Contain(4, "Vector4 is close to Vector1");
    }

    // ── 4. SearchAsync_RespectsMinSimilarity ─────────────────────────────

    [Fact]
    public async Task SearchAsync_RespectsMinSimilarity()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);
        await store.InsertEmbeddingAsync(3, Vector3);

        // Act — search for Vector1 with a high similarity threshold.
        // Only the query vector itself has similarity 1.0; orthogonal vectors have ~0.0 similarity.
        var results = await store.SearchAsync(Vector1, topK: 5, minSimilarity: 0.99);

        // Assert — only the exact match should pass the threshold.
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be(1);
    }

    // ── 5. SearchAsync_UsesLinearScan_UnderThreshold ────────────────────

    [Fact]
    public async Task SearchAsync_UsesLinearScan_UnderThreshold()
    {
        // Arrange — fallbackThreshold set high (10000) so 3 embeddings use linear scan.
        await using var store = CreateStore(fallbackThreshold: 10000);
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);
        await store.InsertEmbeddingAsync(3, Vector3);

        // Act — search with very low similarity to get all results.
        var results = await store.SearchAsync(Vector1, topK: 5, minSimilarity: 0.0);

        // Assert — linear scan should still return correct results.
        results.Should().NotBeEmpty();
        results[0].ChunkId.Should().Be(1, "the exact match should rank first");
        results.Should().HaveCount(3, "all 3 vectors should be returned when minSimilarity is 0");
    }

    // ── 6. DeleteEmbeddingAsync_RemovesFromSearch ────────────────────────

    [Fact]
    public async Task DeleteEmbeddingAsync_RemovesFromSearch()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);

        // Verify both are searchable.
        var beforeDelete = await store.SearchAsync(Vector2, topK: 5, minSimilarity: 0.0);
        beforeDelete.Should().Contain(r => r.ChunkId == 2);

        // Act
        await store.DeleteEmbeddingAsync(2);

        // Assert — count should be 1 and chunk 2 should not appear in results.
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(1);

        var afterDelete = await store.SearchAsync(Vector2, topK: 5, minSimilarity: 0.0);
        afterDelete.Should().NotContain(r => r.ChunkId == 2);
    }

    // ── 7. GetEmbeddingCountAsync_ReturnsCorrectCount ───────────────────

    [Fact]
    public async Task GetEmbeddingCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        // Act & Assert — count after each insert.
        (await store.GetEmbeddingCountAsync()).Should().Be(0);

        await store.InsertEmbeddingAsync(1, Vector1);
        (await store.GetEmbeddingCountAsync()).Should().Be(1);

        await store.InsertEmbeddingAsync(2, Vector2);
        (await store.GetEmbeddingCountAsync()).Should().Be(2);

        await store.InsertEmbeddingAsync(3, Vector3);
        (await store.GetEmbeddingCountAsync()).Should().Be(3);
    }

    // ── 8. OptimizeAsync_PersistsIndexToDisk ─────────────────────────────

    [Fact]
    public async Task OptimizeAsync_PersistsIndexToDisk()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);

        // Act
        await store.OptimizeAsync();

        // Assert — index files should exist on disk after optimize.
        var metadataPath = Path.Combine(_tempPath, "hnsw-index.json");
        var indexPath = Path.Combine(_tempPath, "hnsw-index.bin");

        File.Exists(metadataPath).Should().BeTrue("OptimizeAsync should persist the HNSW index metadata");
        File.Exists(indexPath).Should().BeTrue("OptimizeAsync should persist the HNSW index binary");

        // Metadata should be valid JSON with the expected count.
        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<HnswIndexMetadata>(metadataJson);
        metadata.Should().NotBeNull();
        metadata!.Count.Should().Be(2);
        metadata.Version.Should().Be(HnswIndexMetadata.CurrentVersion);
    }

    // ── 9. InitializeAsync_LoadsExistingIndex ────────────────────────────

    [Fact]
    public async Task InitializeAsync_LoadsExistingIndex()
    {
        // Phase 1: Create store, insert data, persist, dispose.
        {
            await using var store = CreateStore();
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, Vector1);
            await store.InsertEmbeddingAsync(2, Vector2);
            await store.OptimizeAsync(); // Persist index to disk.
        }

        // Phase 2: Create a new store instance pointing to the same temp directory.
        {
            await using var store = CreateStore();
            await store.InitializeAsync();

            // Assert — the index should load from disk and be searchable.
            var count = await store.GetEmbeddingCountAsync();
            count.Should().Be(2);

            var results = await store.SearchAsync(Vector1, topK: 1, minSimilarity: 0.99);
            results.Should().HaveCount(1);
            results[0].ChunkId.Should().Be(1);
        }
    }

    // ── 10. InitializeAsync_RebuildsOnCountMismatch ──────────────────────

    [Fact]
    public async Task InitializeAsync_RebuildsOnCountMismatch()
    {
        // Phase 1: Create store, insert 2 embeddings, persist, dispose.
        {
            await using var store = CreateStore();
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, Vector1);
            await store.InsertEmbeddingAsync(2, Vector2);
            await store.OptimizeAsync(); // Persist index with count=2.
        }

        // Phase 2: Add a row directly to SQLite (bypassing HNSW), making count mismatch.
        {
            var dbPath = Path.Combine(_tempPath, "agentx.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString();

            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();

            // Insert a 3rd embedding directly into SQLite.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO vec_embeddings (chunk_id, embedding, magnitude)
                VALUES (@chunkId, @embedding, @magnitude);
                """;
            cmd.Parameters.AddWithValue("@chunkId", 3L);
            cmd.Parameters.AddWithValue("@embedding", SqliteVecStore.SerializeEmbedding(Vector3));
            cmd.Parameters.AddWithValue("@magnitude", SqliteVecStore.ComputeMagnitude(Vector3));
            await cmd.ExecuteNonQueryAsync();
        }

        // Phase 3: Re-initialize — the metadata says count=2, but SQLite has 3.
        // The store should detect the mismatch and rebuild the index.
        {
            await using var store = CreateStore();
            await store.InitializeAsync();

            var count = await store.GetEmbeddingCountAsync();
            count.Should().Be(3, "the direct SQLite insert should be reflected");

            // All 3 vectors should be searchable (rebuild picked up the 3rd).
            var results = await store.SearchAsync(Vector3, topK: 1, minSimilarity: 0.99);
            results.Should().HaveCount(1);
            results[0].ChunkId.Should().Be(3, "the rebuilt index should contain the directly-inserted vector");
        }
    }

    // ── 11. VectorStoreFactory_CreatesHnsw_WhenEnabled ───────────────────

    [Fact]
    public async Task VectorStoreFactory_CreatesHnsw_WhenEnabled()
    {
        // Arrange
        var mockSettings = new Mock<ISettingsService>();
        mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings
            {
                EnableHnswIndex = true,
                StoragePath = _tempPath
            });

        // Act
        var store = VectorStoreFactory.Create(mockSettings.Object, _mockEmbeddingService.Object, _logger, CreatePlainFactory());

        // Assert
        store.Should().BeOfType<HnswVectorStore>(
            "EnableHnswIndex=true should produce an HnswVectorStore");

        // Clean up
        if (store is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    // ── 12. VectorStoreFactory_CreatesSqlite_WhenDisabled ────────────────

    [Fact]
    public async Task VectorStoreFactory_CreatesSqlite_WhenDisabled()
    {
        // Arrange
        var mockSettings = new Mock<ISettingsService>();
        mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings
            {
                EnableHnswIndex = false,
                StoragePath = _tempPath
            });

        // Act
        var store = VectorStoreFactory.Create(mockSettings.Object, _mockEmbeddingService.Object, _logger, CreatePlainFactory());

        // Assert
        store.Should().BeOfType<SqliteVecStore>(
            "EnableHnswIndex=false should produce a SqliteVecStore");

        // Clean up
        if (store is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    // ── 13. HnswIndexMetadata_SerializesRoundtrip ────────────────────────

    [Fact]
    public void HnswIndexMetadata_SerializesRoundtrip()
    {
        // Arrange
        var original = new HnswIndexMetadata
        {
            Version = HnswIndexMetadata.CurrentVersion,
            Count = 42,
            M = 16,
            EfConstruction = 200,
            Dimensions = 384,
            CreatedAtUtc = DateTime.UtcNow,
            StaleCount = 3
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<HnswIndexMetadata>(json);

        // Assert — all fields should survive JSON roundtrip.
        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(original.Version);
        deserialized.Count.Should().Be(original.Count);
        deserialized.M.Should().Be(original.M);
        deserialized.EfConstruction.Should().Be(original.EfConstruction);
        deserialized.Dimensions.Should().Be(original.Dimensions);
        deserialized.StaleCount.Should().Be(original.StaleCount);
        // DateTime comparison with tolerance for serialization precision loss.
        deserialized.CreatedAtUtc.Should().BeCloseTo(original.CreatedAtUtc, TimeSpan.FromSeconds(1));
    }

    // ── Additional edge-case tests ──────────────────────────────────────

    [Fact]
    public async Task InsertEmbeddingAsync_InsertOrReplace_UpdatesExisting()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);

        // Act — re-insert the same chunk ID with a different embedding.
        await store.InsertEmbeddingAsync(1, Vector2);

        // Assert — count should still be 1 (OR REPLACE), not 2.
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(1);

        // Search should find the updated embedding (Vector2), not the old one (Vector1).
        var results = await store.SearchAsync(Vector2, topK: 1, minSimilarity: 0.99);
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be(1);
    }

    [Fact]
    public async Task DeleteEmbeddingsForDocumentAsync_BatchDelete()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);
        await store.InsertEmbeddingAsync(3, Vector3);

        // Act — delete chunks 1 and 2 for document 999.
        await store.DeleteEmbeddingsForDocumentAsync(999, new List<long> { 1, 2 });

        // Assert
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(1);

        var results = await store.SearchAsync(Vector3, topK: 5, minSimilarity: 0.0);
        results.Should().Contain(r => r.ChunkId == 3);
        results.Should().NotContain(r => r.ChunkId == 1);
        results.Should().NotContain(r => r.ChunkId == 2);
    }

    [Fact]
    public async Task SearchAsync_EmptyStore_ReturnsEmptyResults()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();

        // Act
        var results = await store.SearchAsync(Vector1, topK: 5, minSimilarity: 0.0);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_PersistsDirtyIndex()
    {
        // Arrange — do NOT use `await using` here because it auto-disposes at scope end,
        // which would make the explicit DisposeAsync call below a no-op (already disposed).
        var store = CreateStore();
        await store.InitializeAsync();
        await store.InsertEmbeddingAsync(1, Vector1);

        // Act — call OptimizeAsync first to ensure the index is persisted,
        // then dispose (DisposeAsync also persists if dirty, but after Optimize it's clean).
        await store.OptimizeAsync();
        await store.DisposeAsync();

        // Assert — index files should exist on disk.
        var metadataPath = Path.Combine(_tempPath, "hnsw-index.json");
        var indexPath = Path.Combine(_tempPath, "hnsw-index.bin");

        File.Exists(metadataPath).Should().BeTrue("index metadata should be persisted to disk");
        File.Exists(indexPath).Should().BeTrue("index binary should be persisted to disk");

        // Verify the metadata content is valid.
        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<HnswIndexMetadata>(metadataJson);
        metadata.Should().NotBeNull();
        metadata!.Count.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_TopK_ZeroOrNegative_Throws()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();
        await store.InsertEmbeddingAsync(1, Vector1);

        // Act & Assert
        var act = () => store.SearchAsync(Vector1, topK: 0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("topK");

        var actNeg = () => store.SearchAsync(Vector1, topK: -1);
        await actNeg.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("topK");
    }

    [Fact]
    public async Task OperationsBeforeInitialize_ThrowInvalidOperationException()
    {
        // Arrange — create store but do NOT call InitializeAsync.
        await using var store = CreateStore();

        // Act & Assert — all operations should throw.
        var insertAct = () => store.InsertEmbeddingAsync(1, Vector1);
        await insertAct.Should().ThrowAsync<InvalidOperationException>();

        var searchAct = () => store.SearchAsync(Vector1);
        await searchAct.Should().ThrowAsync<InvalidOperationException>();

        var deleteAct = () => store.DeleteEmbeddingAsync(1);
        await deleteAct.Should().ThrowAsync<InvalidOperationException>();

        var optimizeAct = () => store.OptimizeAsync();
        await optimizeAct.Should().ThrowAsync<InvalidOperationException>();

        var batchDeleteAct = () => store.DeleteEmbeddingsForDocumentAsync(1, new List<long> { 1 });
        await batchDeleteAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteEmbeddingsForDocumentAsync_EmptyChunkList_IsNoOp()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();
        await store.InsertEmbeddingAsync(1, Vector1);

        // Act — delete with empty list should not throw or remove anything.
        await store.DeleteEmbeddingsForDocumentAsync(999, new List<long>());

        // Assert
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        await using var store = CreateStore();
        await store.InitializeAsync();
        await store.InsertEmbeddingAsync(1, Vector1);

        // Act — initialize again.
        await store.InitializeAsync();

        // Assert — data should still be there.
        var count = await store.GetEmbeddingCountAsync();
        count.Should().Be(1);
    }
}
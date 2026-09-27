using System.Security.Cryptography;
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
/// The persisted HNSW index is plain JSON holding every vector and chunk id. Next to a SQLCipher
/// database it must not exist; the index is rebuilt from the encrypted database instead.
/// </summary>
[Collection("SqlCipher")]
public sealed class HnswVectorStoreEncryptionTests : IDisposable
{
    private static readonly float[] Vector1 = { 1.0f, 0.0f, 0.0f };
    private static readonly float[] Vector2 = { 0.0f, 1.0f, 0.0f };

    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"hnsw-enc-{Guid.NewGuid():N}");
    private readonly Mock<ISettingsService> _settings = new();
    private readonly DatabaseKeyProvider _keyProvider = new();

    public HnswVectorStoreEncryptionTests()
    {
        Directory.CreateDirectory(_tempPath);
        _settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings { StoragePath = _tempPath });
        _keyProvider.Set(DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempPath, recursive: true); } catch { /* best-effort */ }
    }

    private HnswVectorStore CreateStore() => new(
        _settings.Object,
        Log.Logger,
        m: 4,
        efConstruction: 20,
        dimensions: 3,
        fallbackThreshold: 0,
        connectionFactory: new EncryptedConnectionFactory(_keyProvider));

    private string IndexFile => Path.Combine(_tempPath, "hnsw-index.bin");
    private string MetadataFile => Path.Combine(_tempPath, "hnsw-index.json");

    [Fact]
    public async Task Encrypted_database_never_gets_a_plaintext_index_file()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.InsertEmbeddingAsync(1, Vector1);
        await store.InsertEmbeddingAsync(2, Vector2);

        await store.OptimizeAsync();
        await store.DisposeAsync();

        HnswVectorStore.IsDatabaseFileEncrypted(Path.Combine(_tempPath, "agentx.db")).Should().BeTrue();
        File.Exists(IndexFile).Should().BeFalse("the vectors would sit unencrypted next to the SQLCipher database");
        File.Exists(MetadataFile).Should().BeFalse();
    }

    [Fact]
    public async Task Leftover_plaintext_index_is_removed_and_the_index_rebuilt_from_the_database()
    {
        {
            var store = CreateStore();
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, Vector1);
            await store.InsertEmbeddingAsync(2, Vector2);
            await store.DisposeAsync();
        }

        // Files from before encryption was switched on.
        await File.WriteAllTextAsync(IndexFile, "{\"leaked\":[1,0,0]}");
        await File.WriteAllTextAsync(MetadataFile, "{\"version\":1,\"count\":2,\"m\":4,\"efConstruction\":20,\"dimensions\":3}");
        SqliteConnection.ClearAllPools();

        await using var reopened = CreateStore();
        await reopened.InitializeAsync();

        File.Exists(IndexFile).Should().BeFalse();
        File.Exists(MetadataFile).Should().BeFalse();
        var results = await reopened.SearchAsync(Vector2, topK: 1, minSimilarity: 0.99);
        results.Should().ContainSingle().Which.ChunkId.Should().Be(2);
    }

    [Fact]
    public async Task Resuming_after_encryption_reopens_with_the_new_key_and_drops_plaintext_index_files()
    {
        // Turning encryption on suspends the store, encrypts its database file and sets the key.
        var keys = new DatabaseKeyProvider();
        var store = new HnswVectorStore(
            _settings.Object,
            Log.Logger,
            m: 4,
            efConstruction: 20,
            dimensions: 3,
            fallbackThreshold: 0,
            connectionFactory: new EncryptedConnectionFactory(keys));
        var dbPath = Path.Combine(_tempPath, "agentx.db");

        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, Vector1);
            await store.OptimizeAsync();
            File.Exists(IndexFile).Should().BeTrue("the database is still plaintext");

            await store.SuspendAsync();
            var key = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            await new DatabaseEncryptionMigrator().MigrateToEncryptedAsync(dbPath, key);
            keys.Set(key);
            await store.ResumeAsync(reloadFromDatabase: false);

            File.Exists(IndexFile).Should().BeFalse("the vectors would sit unencrypted next to the SQLCipher database");
            File.Exists(MetadataFile).Should().BeFalse();
            (await store.SearchAsync(Vector1, topK: 1, minSimilarity: 0.99)).Should().ContainSingle().Which.ChunkId.Should().Be(1);
            await store.InsertEmbeddingAsync(2, Vector2);
            (await store.GetEmbeddingCountAsync()).Should().Be(2);
        }

        HnswVectorStore.IsDatabaseFileEncrypted(dbPath).Should().BeTrue();
        File.Exists(IndexFile).Should().BeFalse();
    }

    [Fact]
    public void Plaintext_database_header_is_recognized()
    {
        var dbPath = Path.Combine(_tempPath, "plain.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (x INTEGER);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        HnswVectorStore.IsDatabaseFileEncrypted(dbPath).Should().BeFalse();
        HnswVectorStore.IsDatabaseFileEncrypted(Path.Combine(_tempPath, "missing.db"))
            .Should().BeTrue("an unreadable database is treated as protected");
    }
}

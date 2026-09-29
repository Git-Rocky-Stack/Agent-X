using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Security;

/// <summary>
/// SE3: enabling encryption must leave the marker and the database file in agreement, and must
/// release the shared application connection so the file can be swapped on Windows.
/// </summary>
[Collection("SqlCipher")]
public sealed class DatabaseEncryptionManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"agentx-encmgr-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly DatabaseKeyProvider _keyProvider = new();
    private readonly EncryptionStateFile _stateFile;
    private readonly AgentXDbContext _db;

    public DatabaseEncryptionManagerTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "agentx.db");
        _stateFile = new EncryptionStateFile(Path.Combine(_dir, "encryption.info.json"));

        // Mirrors the app: the context opens its own connection through EnsureKeyApplied and keeps it.
        var options = new DbContextOptionsBuilder<AgentXDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AgentXDbContext(options, new EncryptedConnectionFactory(_keyProvider));
        _db.EnsureKeyApplied();
        _db.Database.EnsureCreated();
        _db.Conversations.Add(new ConversationEntity { Title = "before", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private DatabaseEncryptionManager CreateSut(
        IDatabaseEncryptionMigrator? migrator = null,
        IEncryptionStateFile? stateFile = null,
        IVectorStore? vectorStore = null)
    {
        var markers = stateFile ?? _stateFile;
        return new DatabaseEncryptionManager(
            _db,
            new DatabaseKeyService(markers, new FakeDpapiEncryptionService()),
            migrator ?? new DatabaseEncryptionMigrator(markers),
            _keyProvider,
            markers,
            vectorStore);
    }

    /// <summary>The vector store the app runs: its own connection to the live file, keyed by the same provider.</summary>
    private SqliteVecStore CreateVectorStore()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => new AppSettings { StoragePath = _dir });
        return new SqliteVecStore(settings.Object, new EncryptedConnectionFactory(_keyProvider));
    }

    [Fact]
    public async Task EnableEncryptionAsync_encrypts_the_live_database_and_keeps_it_usable()
    {
        var sut = CreateSut();

        var enabled = await sut.EnableEncryptionAsync();

        enabled.Should().BeTrue();
        sut.IsEncryptionEnabled.Should().BeTrue();
        sut.ProvisionedMode.Should().Be(KeyStorageMode.DpapiWrapped);
        _keyProvider.Current.Should().NotBeNull();
        IsPlaintext(_dbPath).Should().BeFalse();

        // The shared connection was reopened with the new key: reads and writes keep working.
        _db.Conversations.Add(new ConversationEntity { Title = "after", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        (await _db.Conversations.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task EnableEncryptionAsync_releases_the_shared_connection_before_migrating()
    {
        var spy = new SpyMigrator(new DatabaseEncryptionMigrator(_stateFile), _dbPath);

        await CreateSut(migrator: spy).EnableEncryptionAsync();

        // SQLite opens files without FILE_SHARE_DELETE, so an open handle makes the swap fail on
        // Windows (and the old code wrote the marker anyway).
        spy.DatabaseWasOpenDuringMigration.Should().BeFalse();
    }

    /// <summary>
    /// Upper bound for waits that include SQLCipher work (export, swap, keyed reopen). This test
    /// checks ordering, not speed: on a loaded CI runner a single migration takes 10 to 13 seconds,
    /// and a 30-second bound on the whole enable failed there although the gate behaved.
    /// </summary>
    private static readonly TimeSpan SqlCipherWorkTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task EnableEncryptionAsync_holds_the_database_gate_while_the_file_is_migrated()
    {
        var migrator = new PausingMigrator(new DatabaseEncryptionMigrator(_stateFile));

        // Another flow of the shared context (status polling, indexing), started before the
        // migration so it does not share the migration's ownership of the gate.
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherFlow = Task.Run(async () =>
        {
            await migrator.Started.Task;
            queryStarted.SetResult();
            return await _db.Conversations.CountAsync();
        });

        var enable = CreateSut(migrator: migrator).EnableEncryptionAsync();
        await queryStarted.Task.WaitAsync(SqlCipherWorkTimeout);
        await Task.Delay(300);

        // Without the gate the query reopened the connection on the plaintext file while it was
        // being exported and swapped: the swap failed on Windows, and writes made then were lost.
        otherFlow.IsCompleted.Should().BeFalse("EF work from another flow must wait while the file is migrated");

        migrator.Continue.SetResult();
        (await enable.WaitAsync(SqlCipherWorkTimeout)).Should().BeTrue();
        (await otherFlow.WaitAsync(SqlCipherWorkTimeout)).Should().Be(1, "it runs on the encrypted file once the key is applied");
    }

    [Fact]
    public async Task EnableEncryptionAsync_closes_the_vector_store_and_reopens_it_with_the_new_key()
    {
        var store = CreateVectorStore();
        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, new[] { 1f, 0f, 0f });
            var spy = new SpyMigrator(new DatabaseEncryptionMigrator(_stateFile), _dbPath);

            (await CreateSut(migrator: spy, vectorStore: store).EnableEncryptionAsync()).Should().BeTrue();

            spy.DatabaseWasOpenDuringMigration.Should().BeFalse("the vector store's own connection blocked the swap on Windows");
            await store.InsertEmbeddingAsync(2, new[] { 0f, 1f, 0f });
            (await store.SearchAsync(new[] { 0f, 1f, 0f }, topK: 5, minSimilarity: 0.5)).Select(r => r.ChunkId)
                .Should().Equal(new long[] { 2 });
        }

        // The insert made after encryption landed in the encrypted live file, not in the
        // plaintext file the store had open before.
        IsPlaintext(_dbPath).Should().BeFalse();
        using var keyed = new EncryptedConnectionFactory(_keyProvider).OpenKeyed(_dbPath);
        using var count = keyed.CreateCommand();
        count.CommandText = "SELECT count(*) FROM vec_embeddings;";
        Convert.ToInt64(count.ExecuteScalar()).Should().Be(2);
        SqliteConnection.ClearPool(keyed);
    }

    [Fact]
    public async Task EnableEncryptionAsync_when_the_marker_cannot_be_written_leaves_a_consistent_plaintext_database()
    {
        var failingMarker = new FailingWriteStateFile(_stateFile);
        var sut = CreateSut(stateFile: failingMarker);

        var act = () => sut.EnableEncryptionAsync();

        await act.Should().ThrowAsync<IOException>();
        _stateFile.Exists().Should().BeFalse("no marker may claim encryption for a plaintext file");
        IsPlaintext(_dbPath).Should().BeTrue();
        _keyProvider.Current.Should().BeNull();
        (await _db.Conversations.CountAsync()).Should().Be(1, "the connection is reopened without a key");
    }

    [Fact]
    public async Task EnableEncryptionAsync_is_a_no_op_when_already_enabled()
    {
        var sut = CreateSut();
        await sut.EnableEncryptionAsync();
        var encrypted = ReadAllBytesShared(_dbPath);

        var second = await sut.EnableEncryptionAsync();

        second.Should().BeFalse();
        ReadAllBytesShared(_dbPath).Should().Equal(encrypted);
        (await _db.Conversations.CountAsync()).Should().Be(1);
    }

    // The shared connection keeps the database open for writing after enabling encryption, and
    // Windows refuses File.ReadAllBytes (FileShare.Read) while another handle can write.
    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static bool IsPlaintext(string path)
    {
        var header = new byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Read(header, 0, 16) == 16 && System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3\0";
    }

    private sealed class SpyMigrator : IDatabaseEncryptionMigrator
    {
        private readonly IDatabaseEncryptionMigrator _inner;
        private readonly string _dbPath;

        public SpyMigrator(IDatabaseEncryptionMigrator inner, string dbPath)
        {
            _inner = inner;
            _dbPath = dbPath;
        }

        public bool? DatabaseWasOpenDuringMigration { get; private set; }

        public Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key)
            => MigrateToEncryptedAsync(dbPath, key, null);

        public Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key, Func<Task>? commitAsync)
        {
            DatabaseWasOpenDuringMigration = FileHandleProbe.IsOpenByThisProcess(_dbPath);
            return _inner.MigrateToEncryptedAsync(dbPath, key, commitAsync);
        }

        public void RecoverIfNeeded(string dbPath) => _inner.RecoverIfNeeded(dbPath);
    }

    /// <summary>Signals when the migration starts and holds it until the test continues it.</summary>
    private sealed class PausingMigrator : IDatabaseEncryptionMigrator
    {
        private readonly IDatabaseEncryptionMigrator _inner;

        public PausingMigrator(IDatabaseEncryptionMigrator inner) => _inner = inner;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key)
            => MigrateToEncryptedAsync(dbPath, key, null);

        public async Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key, Func<Task>? commitAsync)
        {
            Started.TrySetResult();
            await Continue.Task.ConfigureAwait(false);
            await _inner.MigrateToEncryptedAsync(dbPath, key, commitAsync).ConfigureAwait(false);
        }

        public void RecoverIfNeeded(string dbPath) => _inner.RecoverIfNeeded(dbPath);
    }

    private sealed class FailingWriteStateFile : IEncryptionStateFile
    {
        private readonly IEncryptionStateFile _inner;

        public FailingWriteStateFile(IEncryptionStateFile inner) => _inner = inner;

        public bool Exists() => _inner.Exists();
        public EncryptionStateInfo? Read() => _inner.Read();
        public Task WriteAsync(EncryptionStateInfo info) => throw new IOException("disk full");
        public void Delete() => _inner.Delete();
        public string? MoveAside() => _inner.MoveAside();
    }
}

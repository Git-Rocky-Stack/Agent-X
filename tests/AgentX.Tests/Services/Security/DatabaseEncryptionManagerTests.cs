using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Security;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    private DatabaseEncryptionManager CreateSut(IDatabaseEncryptionMigrator? migrator = null, IEncryptionStateFile? stateFile = null)
    {
        var markers = stateFile ?? _stateFile;
        return new DatabaseEncryptionManager(
            _db,
            new DatabaseKeyService(markers, new FakeDpapiEncryptionService()),
            migrator ?? new DatabaseEncryptionMigrator(markers),
            _keyProvider,
            markers);
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

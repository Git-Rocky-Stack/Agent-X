using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Backup;

/// <summary>
/// End-to-end create and restore against a file-backed live database, the way the app runs it:
/// the shared context opens its own (optionally keyed) connection through EnsureKeyApplied and the
/// file is in WAL mode, as the vector store sets it. Covers BK2 (encrypted restore), BK4 (backup
/// scope and released handles) and BK5 (staged, verified, reversible restore). Windows-only file
/// sharing rules cannot be reproduced on Linux, so handle release is asserted directly with
/// <see cref="FileHandleProbe"/>.
/// </summary>
[Collection("SqlCipher")]
public sealed class BackupRestoreRoundTripTests : IDisposable
{
    private readonly List<Harness> _harnesses = new();

    public void Dispose()
    {
        foreach (var harness in _harnesses)
            harness.Dispose();
    }

    private Harness NewHarness(DatabaseKeyMaterial? key = null, IVectorStore? vectorStore = null, bool withVectorStore = false)
    {
        var harness = new Harness(key, vectorStore, withVectorStore);
        _harnesses.Add(harness);
        return harness;
    }

    private static DatabaseKeyMaterial NewKey()
        => DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);

    // --- Restore: the swap ---

    [Fact]
    public async Task Restore_replaces_the_live_database_and_the_app_connection_keeps_working()
    {
        var h = NewHarness();
        h.AddConversation("in the backup");
        var backup = await h.CreateBackupAsync();
        h.AddConversation("added after the backup");

        var result = await h.Service.RestoreFromBackupAsync(backup, password: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.RestoredConversationCount.Should().Be(1);
        result.RequiresRestart.Should().BeTrue();
        (await h.Db.Conversations.Select(c => c.Title).ToListAsync()).Should().Equal("in the backup");

        // The live connection was reopened on the restored file and accepts writes.
        h.AddConversation("after restore");
        (await h.Db.Conversations.CountAsync()).Should().Be(2);
        h.LeftoverRestoreFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_encrypted_archive_with_its_password()
    {
        var h = NewHarness();
        h.AddConversation("secret");
        var backup = await h.CreateBackupAsync(password: "S3cret!");
        h.AddConversation("later");

        var result = await h.Service.RestoreFromBackupAsync(backup, "S3cret!");

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await h.Db.Conversations.CountAsync()).Should().Be(1);
        (await h.Service.ValidateBackupAsync(backup, "S3cret!")).Should().BeTrue();
        (await h.Service.ValidateBackupAsync(backup, "nope")).Should().BeFalse();
        (await h.Service.IsEncryptedBackupAsync(backup)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("wrong", "password is incorrect")]
    public async Task Restore_encrypted_archive_without_the_right_password_changes_nothing(string? password, string expected)
    {
        var h = NewHarness();
        h.AddConversation("one");
        var backup = await h.CreateBackupAsync(password: "right");
        h.AddConversation("two");

        var result = await h.Service.RestoreFromBackupAsync(backup, password);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expected);
        (await h.Db.Conversations.CountAsync()).Should().Be(2);
        h.LeftoverRestoreFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_of_a_plaintext_backup_into_an_encrypted_installation_keeps_the_database_encrypted()
    {
        var source = NewHarness();
        source.AddConversation("from plaintext");
        var backup = await source.CreateBackupAsync();
        var target = NewHarness(NewKey());
        target.AddConversation("target 1");
        target.AddConversation("target 2");

        var result = await target.Service.RestoreFromBackupAsync(backup, password: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        HasPlaintextHeader(target.DbPath).Should().BeFalse("encryption stays on after restoring an older plaintext backup");
        (await target.Db.Conversations.Select(c => c.Title).ToListAsync()).Should().Equal("from plaintext");
    }

    [Fact]
    public async Task Restore_of_an_encrypted_backup_into_a_plaintext_installation_fails_and_changes_nothing()
    {
        var source = NewHarness(NewKey());
        source.AddConversation("encrypted");
        var backup = await source.CreateBackupAsync();
        var target = NewHarness();
        target.AddConversation("keep me");

        var result = await target.Service.RestoreFromBackupAsync(backup, password: null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("encrypted");
        (await target.Db.Conversations.Select(c => c.Title).ToListAsync()).Should().Equal("keep me");
        target.LeftoverRestoreFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_of_a_backup_encrypted_with_another_key_fails_and_changes_nothing()
    {
        var source = NewHarness(NewKey());
        source.AddConversation("other key");
        var backup = await source.CreateBackupAsync();
        var target = NewHarness(NewKey());
        target.AddConversation("keep me");

        var result = await target.Service.RestoreFromBackupAsync(backup, password: null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("database key");
        (await target.Db.Conversations.Select(c => c.Title).ToListAsync()).Should().Equal("keep me");
    }

    [Fact]
    public async Task Restore_of_a_damaged_database_entry_fails_before_touching_the_live_database()
    {
        var h = NewHarness();
        h.AddConversation("keep me");
        var archive = Path.Combine(h.BackupDir, "damaged.agentxbak");
        await File.WriteAllBytesAsync(archive, BuildArchive(
            ("database/agentx.db", new byte[] { 1, 2, 3 }),
            ("manifest.json", Encoding.UTF8.GetBytes("{\"version\":1}"))));

        var result = await h.Service.RestoreFromBackupAsync(archive, password: null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("damaged");
        (await h.Db.Conversations.CountAsync()).Should().Be(1);
        h.LeftoverRestoreFiles().Should().BeEmpty();
    }

    // --- Restore: other users of the database file ---

    [Fact]
    public async Task Restore_holds_the_database_gate_so_other_flows_wait_for_the_swap()
    {
        var h = NewHarness();
        h.AddConversation("in the backup");
        var backup = await h.CreateBackupAsync();
        h.AddConversation("added after the backup");

        // Another flow of the shared context (status polling, indexing), started before the
        // restore so it does not share the restore's ownership of the gate.
        var swapReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherFlow = Task.Run(async () =>
        {
            await swapReached.Task;
            queryStarted.SetResult();
            return await h.Db.Conversations.CountAsync();
        });

        bool? otherFlowFinishedDuringSwap = null;
        var progress = new CallbackProgress(p =>
        {
            if (p.Phase != "Verifying restored data")
                return;

            swapReached.SetResult();
            queryStarted.Task.Wait(TimeSpan.FromSeconds(5));
            otherFlowFinishedDuringSwap = otherFlow.Wait(TimeSpan.FromMilliseconds(300));
        });

        var result = await h.Service.RestoreFromBackupAsync(backup, password: null, progress);

        result.Success.Should().BeTrue(result.ErrorMessage);
        otherFlowFinishedDuringSwap.Should().BeFalse(
            "EF work from another flow must wait while the connection is released and the file swapped");
        (await otherFlow.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1, "it runs once the restored database is in place");
    }

    [Fact]
    public async Task Restore_closes_the_vector_store_during_the_swap_and_reloads_the_restored_vectors()
    {
        var h = NewHarness(withVectorStore: true);
        var store = h.VectorStore!;
        await store.InitializeAsync();
        await store.InsertEmbeddingAsync(1, new[] { 1f, 0f, 0f });
        h.AddConversation("in the backup");
        var backup = await h.CreateBackupAsync();
        await store.DeleteEmbeddingAsync(1);
        await store.InsertEmbeddingAsync(2, new[] { 0f, 1f, 0f });

        // The replaced database is renamed to the safety copy, so a handle that survives the swap
        // shows up there. On Windows such a handle makes the swap fail with "database in use".
        bool? replacedFileStillOpen = null;
        var progress = new CallbackProgress(p =>
        {
            if (p.Phase == "Verifying restored data")
                replacedFileStillOpen = FileHandleProbe.IsOpenByThisProcess(h.DbPath + ".pre-restore");
        });

        var result = await h.Service.RestoreFromBackupAsync(backup, password: null, progress);

        result.Success.Should().BeTrue(result.ErrorMessage);
        replacedFileStillOpen.Should().BeFalse("the vector store's own connection must be closed for the swap");
        (await store.SearchAsync(new[] { 1f, 0f, 0f }, topK: 5, minSimilarity: 0.5)).Select(r => r.ChunkId)
            .Should().Equal(new long[] { 1 }, "the store reads the restored vectors");
        (await store.SearchAsync(new[] { 0f, 1f, 0f }, topK: 5, minSimilarity: 0.5)).Should().BeEmpty();
        (await store.GetEmbeddingCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Restore_reports_a_warning_when_the_vector_store_cannot_reopen()
    {
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.ResumeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("cannot open"));
        var h = NewHarness(vectorStore: store.Object);
        h.AddConversation("in the backup");
        var backup = await h.CreateBackupAsync();

        var result = await h.Service.RestoreFromBackupAsync(backup, password: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.WarningMessages.Should().Contain(w => w.Contains("semantic search index", StringComparison.Ordinal));
        store.Verify(s => s.SuspendAsync(It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.ResumeAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Restore_that_fails_before_the_swap_leaves_the_vector_store_alone()
    {
        var store = new Mock<IVectorStore>();
        var h = NewHarness(vectorStore: store.Object);
        var archive = Path.Combine(h.BackupDir, "damaged.agentxbak");
        await File.WriteAllBytesAsync(archive, BuildArchive(
            ("database/agentx.db", new byte[] { 1, 2, 3 }),
            ("manifest.json", Encoding.UTF8.GetBytes("{\"version\":1}"))));

        var result = await h.Service.RestoreFromBackupAsync(archive, password: null);

        result.Success.Should().BeFalse();
        store.Verify(s => s.SuspendAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Restore: document files ---

    [Fact]
    public async Task Restore_brings_back_documents_and_skips_settings_keys_and_logs_from_older_archives()
    {
        var h = NewHarness();
        h.AddConversation("db");
        var databaseBytes = await ReadDatabaseEntryAsync(await h.CreateBackupAsync());
        File.WriteAllText(Path.Combine(h.StorageDir, "settings.json"), "live settings");
        var legacyArchive = Path.Combine(h.BackupDir, "legacy.agentxbak");
        // Older builds zipped the whole storage folder under documents/.
        await File.WriteAllBytesAsync(legacyArchive, BuildArchive(
            ("database/agentx.db", databaseBytes),
            ("manifest.json", Encoding.UTF8.GetBytes("{\"version\":1}")),
            ("documents/WebImports/article.md", Encoding.UTF8.GetBytes("# Article")),
            ("documents/settings.json", Encoding.UTF8.GetBytes("archived settings")),
            ("documents/encryption.info.json", Encoding.UTF8.GetBytes("{}")),
            ("documents/Logs/agentx-20260101.log", Encoding.UTF8.GetBytes("log"))));

        var result = await h.Service.RestoreFromBackupAsync(legacyArchive, password: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        File.ReadAllText(Path.Combine(h.StorageDir, "WebImports", "article.md")).Should().Be("# Article");
        File.ReadAllText(Path.Combine(h.StorageDir, "settings.json")).Should().Be("live settings");
        File.Exists(Path.Combine(h.StorageDir, "encryption.info.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(h.StorageDir, "Logs")).Should().BeFalse();
        result.WarningMessages.Should().Contain(w => w.Contains("not documents"));
        h.LeftoverRestoreFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_that_fails_while_installing_documents_rolls_back_database_and_documents()
    {
        var h = NewHarness();
        h.AddConversation("backup era");
        var databaseBytes = await ReadDatabaseEntryAsync(await h.CreateBackupAsync());
        h.AddConversation("current era");
        var existing = Path.Combine(h.StorageDir, "WebImports", "a-existing.md");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "current version");
        // A folder where a restored file must go makes the install step fail after the swap.
        Directory.CreateDirectory(Path.Combine(h.StorageDir, "WebImports", "b-blocked.md"));
        var archive = Path.Combine(h.BackupDir, "blocked.agentxbak");
        await File.WriteAllBytesAsync(archive, BuildArchive(
            ("database/agentx.db", databaseBytes),
            ("manifest.json", Encoding.UTF8.GetBytes("{\"version\":1}")),
            ("documents/WebImports/a-existing.md", Encoding.UTF8.GetBytes("backup version")),
            ("documents/WebImports/b-blocked.md", Encoding.UTF8.GetBytes("cannot land"))));

        var result = await h.Service.RestoreFromBackupAsync(archive, password: null);

        result.Success.Should().BeFalse();
        File.ReadAllText(existing).Should().Be("current version");
        (await h.Db.Conversations.CountAsync()).Should().Be(2, "the previous database was put back");
        h.AddConversation("still writable");
        h.LeftoverRestoreFiles().Should().BeEmpty();
    }

    // --- Create ---

    [Fact]
    public async Task CreateBackup_releases_its_pooled_connections_and_removes_the_database_copy()
    {
        var h = NewHarness();
        h.AddConversation("x");

        await h.CreateBackupAsync();

        // The copy used to stay open in the connection pool, so on Windows the archive step hit a
        // sharing violation and the copy (possibly plaintext) could not be deleted from %TEMP%.
        var copies = h.Factory.OpenedPaths.Where(p => p.EndsWith(".tmp", StringComparison.Ordinal)).ToList();
        copies.Should().ContainSingle();
        File.Exists(copies[0]).Should().BeFalse();
        FileHandleProbe.IsOpenByThisProcess(copies[0]).Should().BeFalse();
        Directory.GetFiles(h.BackupDir, "*.partial").Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBackup_names_the_archive_with_the_gregorian_year_under_a_thai_culture()
    {
        var h = NewHarness();
        var original = CultureInfo.CurrentCulture;
        string archive;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            archive = await h.CreateBackupAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Path.GetFileName(archive).Should().StartWith($"agentx-backup-{DateTime.UtcNow.Year}-");
    }

    [Fact]
    public async Task CreateBackup_twice_in_the_same_second_keeps_both_archives()
    {
        var h = NewHarness();

        var first = await h.CreateBackupAsync();
        var second = await h.CreateBackupAsync();

        second.Should().NotBe(first);
        File.Exists(first).Should().BeTrue();
        File.Exists(second).Should().BeTrue();
    }

    // --- Helpers ---

    private static bool HasPlaintextHeader(string path)
    {
        var header = new byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Read(header, 0, 16) == 16 && Encoding.ASCII.GetString(header) == "SQLite format 3\0";
    }

    private static async Task<byte[]> ReadDatabaseEntryAsync(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        await using var entry = archive.GetEntry("database/agentx.db")!.Open();
        using var buffer = new MemoryStream();
        await entry.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static byte[] BuildArchive(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(data, 0, data.Length);
            }
        }

        return ms.ToArray();
    }

    /// <summary>Runs a callback synchronously on the reporting thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class CallbackProgress : IProgress<BackupProgress>
    {
        private readonly Action<BackupProgress> _onReport;

        public CallbackProgress(Action<BackupProgress> onReport) => _onReport = onReport;

        public void Report(BackupProgress value) => _onReport(value);
    }

    /// <summary>Real factory that records which files were opened.</summary>
    private sealed class RecordingFactory : IEncryptedConnectionFactory
    {
        private readonly EncryptedConnectionFactory _inner;

        public RecordingFactory(IDatabaseKeyProvider keys) => _inner = new EncryptedConnectionFactory(keys);

        public List<string> OpenedPaths { get; } = new();

        public SqliteConnection OpenKeyed(string dbPath)
        {
            lock (OpenedPaths)
                OpenedPaths.Add(dbPath);
            return _inner.OpenKeyed(dbPath);
        }

        public void ApplyKey(SqliteConnection openConnection) => _inner.ApplyKey(openConnection);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"agentx-bakrt-{Guid.NewGuid():N}");

        /// <param name="key">Database key, or null for a plaintext installation.</param>
        /// <param name="vectorStore">A vector store to hand to the service.</param>
        /// <param name="withVectorStore">
        /// Creates a real SqliteVecStore on the live database file, the way the app runs it (its own
        /// long-lived connection, through the same keyed factory). Not initialized.
        /// </param>
        public Harness(DatabaseKeyMaterial? key, IVectorStore? vectorStore = null, bool withVectorStore = false)
        {
            var appDir = Path.Combine(_root, "app");
            StorageDir = Path.Combine(_root, "storage");
            BackupDir = Path.Combine(_root, "backups");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(StorageDir);
            Directory.CreateDirectory(BackupDir);
            DbPath = Path.Combine(appDir, "agentx.db");

            var keys = new DatabaseKeyProvider();
            keys.Set(key);
            Factory = new RecordingFactory(keys);

            var options = new DbContextOptionsBuilder<AgentXDbContext>().UseSqlite($"Data Source={DbPath}").Options;
            Db = new AgentXDbContext(options, Factory);
            Db.EnsureKeyApplied();
            Db.Database.EnsureCreated();
            Db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

            var settings = new Mock<ISettingsService>();
            settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => new AppSettings { StoragePath = StorageDir });

            if (withVectorStore)
            {
                // The vector store opens <StoragePath>/agentx.db, which is the live database here.
                var vectorSettings = new Mock<ISettingsService>();
                vectorSettings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => new AppSettings { StoragePath = appDir });
                vectorStore = new SqliteVecStore(vectorSettings.Object, Factory);
            }

            VectorStore = vectorStore;
            Service = new BackupService(Db, settings.Object, Factory, vectorStore);
        }

        public IVectorStore? VectorStore { get; }

        public string DbPath { get; }
        public string StorageDir { get; }
        public string BackupDir { get; }
        public RecordingFactory Factory { get; }
        public AgentXDbContext Db { get; }
        public BackupService Service { get; }

        public void AddConversation(string title)
        {
            Db.Conversations.Add(new ConversationEntity { Title = title, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            Db.SaveChanges();
        }

        public async Task<string> CreateBackupAsync(string? password = null)
        {
            var result = await Service.CreateBackupAsync(new BackupOptions
            {
                DestinationPath = BackupDir,
                IncludeDocuments = true,
                EncryptionPassword = password,
            });
            result.Success.Should().BeTrue(result.ErrorMessage);
            return result.BackupFilePath!;
        }

        /// <summary>Staging, safety and rollback files a finished restore must not leave behind.</summary>
        public IEnumerable<string> LeftoverRestoreFiles()
            => Directory.GetFiles(Path.GetDirectoryName(DbPath)!)
                .Where(f => f.Contains(".restore", StringComparison.Ordinal) || f.Contains(".pre-restore", StringComparison.Ordinal))
                .Concat(Directory.GetDirectories(StorageDir).Where(d => Path.GetFileName(d).StartsWith(".restore", StringComparison.Ordinal)));

        public void Dispose()
        {
            VectorStore?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Db.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }
}

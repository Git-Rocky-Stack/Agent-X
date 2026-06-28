using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Backup;

/// <summary>
/// Behavioural coverage for <see cref="BackupService"/> — the create / restore / validate /
/// history / delete / estimate / scheduled-backup surface. Complements
/// <see cref="BackupServiceSecurityTests"/> (which exercises the pure static crypto + path-guard
/// helpers) by driving the real service end-to-end.
///
/// <para><b>Harness design.</b> The service has no path-injection seam: its source database path is
/// the hardcoded <c>%LocalAppData%\AgentX\agentx.db</c>. The SQLite Online Backup API copy is taken
/// through the injectable <see cref="IEncryptedConnectionFactory"/>, so the harness mocks the factory
/// to (a) redirect the <i>source</i> open to a seeded throwaway temp database — never the real user
/// DB — and (b) honour the generated <i>destination</i> temp path. Every write target
/// (<see cref="BackupOptions.DestinationPath"/>, the documents storage path) is a per-test temp
/// directory. A full <see cref="BackupService.CreateBackupAsync"/> therefore round-trips safely,
/// producing a real <c>.agentxbak</c> archive on disk.</para>
///
/// <para><b>Restore.</b> <see cref="BackupService.RestoreFromBackupAsync"/>'s success path writes the
/// extracted database to that same hardcoded real user-profile path and swaps the live EF
/// connection — with no seam to redirect it, exercising it would clobber the developer's real
/// Agent-X database. These tests therefore cover only restore's guard, validation, encrypted, and
/// error branches (all of which return <i>before</i> any database write); the file-swap body is a
/// deliberate, safety-bounded residual.</para>
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    // ── Harness ──────────────────────────────────────────────────────────────

    private sealed class BackupHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public AgentXDbContext Db { get; }
        public Mock<ISettingsService> Settings { get; } = new(MockBehavior.Strict);
        public Mock<IEncryptedConnectionFactory> ConnFactory { get; } = new(MockBehavior.Strict);
        public BackupService Service { get; }

        /// <summary>Seeded, valid SQLite file the mocked factory hands back for the "source" open.</summary>
        public string SourceDbPath { get; }

        /// <summary>Per-test temp directory backups are written into.</summary>
        public string DestDir { get; }

        /// <summary>Per-test temp directory used as the documents storage path.</summary>
        public string StorageDir { get; }

        /// <summary>The settings object returned by the mock; mutable so tests can tweak StoragePath.</summary>
        public AppSettings CurrentSettings { get; }

        public BackupHarness(bool createStorageDir = true)
        {
            Db = Factory.CreateContext();

            DestDir = Path.Combine(Path.GetTempPath(), "agentx-bak-dest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DestDir);

            StorageDir = Path.Combine(Path.GetTempPath(), "agentx-bak-store-" + Guid.NewGuid().ToString("N"));
            if (createStorageDir)
                Directory.CreateDirectory(StorageDir);

            // Seed a valid, non-empty SQLite database the BackupDatabase copy can read.
            SourceDbPath = Path.Combine(Path.GetTempPath(), "agentx-bak-src-" + Guid.NewGuid().ToString("N") + ".db");
            using (var seed = new SqliteConnection($"Data Source={SourceDbPath};Pooling=False"))
            {
                seed.Open();
                using var cmd = seed.CreateCommand();
                cmd.CommandText = "CREATE TABLE meta(k TEXT PRIMARY KEY, v TEXT); INSERT INTO meta VALUES('schema','1');";
                cmd.ExecuteNonQuery();
            }

            CurrentSettings = new AppSettings { StoragePath = StorageDir };
            Settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => CurrentSettings);

            // Source open → seeded temp DB; destination temp copy (".tmp") → honour the requested path.
            ConnFactory
                .Setup(f => f.OpenKeyed(It.IsAny<string>()))
                .Returns<string>(p =>
                {
                    var target = p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ? p : SourceDbPath;
                    var conn = new SqliteConnection($"Data Source={target};Pooling=False");
                    conn.Open();
                    return conn;
                });

            Service = new BackupService(Db, Settings.Object, ConnFactory.Object);
        }

        /// <summary>Applies a seed action against a fresh context that shares the in-memory database.</summary>
        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        /// <summary>A fresh context over the same in-memory database for assertions.</summary>
        public AgentXDbContext Fresh() => Factory.CreateContext();

        public string AddStorageFile(string relativePath, byte[] bytes)
        {
            var full = Path.Combine(StorageDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
            return full;
        }

        public void Dispose()
        {
            try { Service.StopScheduledBackups(); } catch { /* best effort */ }
            Db.Dispose();
            Factory.Dispose();
            SqliteConnection.ClearAllPools();
            TryDeleteDir(DestDir);
            TryDeleteDir(StorageDir);
            TryDeleteFile(SourceDbPath);
        }
    }

    private readonly List<BackupHarness> _harnesses = new();

    private BackupHarness NewHarness(bool createStorageDir = true)
    {
        var h = new BackupHarness(createStorageDir);
        _harnesses.Add(h);
        return h;
    }

    public void Dispose()
    {
        foreach (var h in _harnesses)
            h.Dispose();
    }

    /// <summary>Synchronous progress collector — deterministic, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class CollectingProgress : IProgress<BackupProgress>
    {
        private readonly List<BackupProgress> _items = new();
        public IReadOnlyList<BackupProgress> Items
        {
            get { lock (_items) { return _items.ToList(); } }
        }

        public void Report(BackupProgress value)
        {
            lock (_items) { _items.Add(value); }
        }
    }

    // ── Constructor guards ───────────────────────────────────────────────────

    [Fact]
    public void Ctor_NullDbContext_Throws()
    {
        using var h = NewHarness();
        var act = () => new BackupService(null!, h.Settings.Object, h.ConnFactory.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Ctor_NullSettingsService_Throws()
    {
        using var h = NewHarness();
        var act = () => new BackupService(h.Db, null!, h.ConnFactory.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settingsService");
    }

    [Fact]
    public void Ctor_NullConnectionFactory_Throws()
    {
        using var h = NewHarness();
        var act = () => new BackupService(h.Db, h.Settings.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    // ── CreateBackupAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBackupAsync_NullOptions_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.CreateBackupAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateBackupAsync_Unencrypted_ProducesValidArchiveAndHistoryRecord()
    {
        var h = NewHarness();

        var result = await h.Service.CreateBackupAsync(new BackupOptions
        {
            DestinationPath = h.DestDir,
            IncludeDocuments = false,
            Notes = "nightly",
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.BackupFilePath.Should().NotBeNull();
        File.Exists(result.BackupFilePath!).Should().BeTrue();
        result.BackupId.Should().BeGreaterThan(0);

        // Archive contains the required entries.
        using (var archive = ZipFile.OpenRead(result.BackupFilePath!))
        {
            archive.GetEntry("database/agentx.db").Should().NotBeNull();
            archive.GetEntry("manifest.json").Should().NotBeNull();
        }

        // History row persisted.
        using var ctx = h.Fresh();
        var row = ctx.Backups.Single();
        row.IsValid.Should().BeTrue();
        row.Notes.Should().Be("nightly");
        row.FilePath.Should().Be(result.BackupFilePath);
        row.BackupType.Should().Be("manual");
    }

    [Fact]
    public async Task CreateBackupAsync_WithDocuments_IncludesFilesAndExcludesDatabaseSidecars()
    {
        var h = NewHarness();
        h.AddStorageFile("notes/a.txt", Encoding.UTF8.GetBytes("alpha"));
        h.AddStorageFile("b.bin", new byte[] { 1, 2, 3, 4 });
        // Sidecar database files must be excluded by the backup builder.
        h.AddStorageFile("agentx.db", new byte[] { 9 });
        h.AddStorageFile("agentx.db-wal", new byte[] { 9 });

        var result = await h.Service.CreateBackupAsync(new BackupOptions
        {
            DestinationPath = h.DestDir,
            IncludeDocuments = true,
        });

        result.Success.Should().BeTrue(result.ErrorMessage);

        using var archive = ZipFile.OpenRead(result.BackupFilePath!);
        var docEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("documents/", StringComparison.Ordinal))
            .Select(e => e.FullName)
            .ToList();

        docEntries.Should().Contain("documents/notes/a.txt");
        docEntries.Should().Contain("documents/b.bin");
        docEntries.Should().NotContain(e => e.EndsWith(".db") || e.EndsWith(".db-wal"));
    }

    [Fact]
    public async Task CreateBackupAsync_Encrypted_ProducesAuthenticatedArchiveThatDecrypts()
    {
        var h = NewHarness();
        const string password = "S3cur3-P@ss";

        var result = await h.Service.CreateBackupAsync(new BackupOptions
        {
            DestinationPath = h.DestDir,
            IncludeDocuments = false,
            EncryptionPassword = password,
        });

        result.Success.Should().BeTrue(result.ErrorMessage);

        var bytes = await File.ReadAllBytesAsync(result.BackupFilePath!);
        bytes.Take(8).Should().Equal(Encoding.ASCII.GetBytes("AGXENC2\0"));

        // Decrypting with the password yields the inner ZIP with the expected entries.
        var zipBytes = BackupService.DecryptBytes(bytes, password);
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        archive.GetEntry("database/agentx.db").Should().NotBeNull();
        archive.GetEntry("manifest.json").Should().NotBeNull();

        // The encrypted archive still passes structural validation.
        (await h.Service.ValidateBackupAsync(result.BackupFilePath!)).Should().BeTrue();
    }

    [Fact]
    public async Task CreateBackupAsync_ManifestReflectsRecordCounts()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(new ConversationEntity { Title = "c1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            ctx.Conversations.Add(new ConversationEntity { Title = "c2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            ctx.Workflows.Add(new WorkflowEntity { Name = "w1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        });

        var result = await h.Service.CreateBackupAsync(new BackupOptions
        {
            DestinationPath = h.DestDir,
            IncludeDocuments = false,
        });

        result.Success.Should().BeTrue(result.ErrorMessage);

        using var archive = ZipFile.OpenRead(result.BackupFilePath!);
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var doc = await JsonDocument.ParseAsync(manifestStream);
        var root = doc.RootElement;

        root.GetProperty("conversationCount").GetInt32().Should().Be(2);
        root.GetProperty("workflowCount").GetInt32().Should().Be(1);
        root.GetProperty("documentCount").GetInt32().Should().Be(0);
        root.GetProperty("version").GetInt32().Should().Be(1);
        root.GetProperty("includesDocuments").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CreateBackupAsync_ReportsProgressThroughCompletion()
    {
        var h = NewHarness();
        var progress = new CollectingProgress();

        var result = await h.Service.CreateBackupAsync(
            new BackupOptions { DestinationPath = h.DestDir, IncludeDocuments = false },
            progress);

        result.Success.Should().BeTrue(result.ErrorMessage);
        progress.Items.Should().NotBeEmpty();
        progress.Items.Should().Contain(p => p.Phase == "Complete" && p.PercentComplete == 100);
        progress.Items.Select(p => p.PercentComplete).Should().OnlyContain(p => p >= 0 && p <= 100);
    }

    [Fact]
    public async Task CreateBackupAsync_IncludeDocumentsButStorageMissing_SucceedsWithoutDocuments()
    {
        var h = NewHarness(createStorageDir: false);
        h.CurrentSettings.StoragePath = Path.Combine(Path.GetTempPath(), "agentx-missing-" + Guid.NewGuid().ToString("N"));

        var result = await h.Service.CreateBackupAsync(new BackupOptions
        {
            DestinationPath = h.DestDir,
            IncludeDocuments = true,
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var archive = ZipFile.OpenRead(result.BackupFilePath!);
        archive.Entries.Should().NotContain(e => e.FullName.StartsWith("documents/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateBackupAsync_CancelledToken_ReturnsCancelledResult()
    {
        var h = NewHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await h.Service.CreateBackupAsync(
            new BackupOptions { DestinationPath = h.DestDir, IncludeDocuments = false },
            progress: null,
            cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled");
        using var ctx = h.Fresh();
        ctx.Backups.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBackupAsync_WhenDependencyThrows_ReturnsFailureResult()
    {
        var h = NewHarness();
        h.Settings.Setup(s => s.GetSettingsAsync()).ThrowsAsync(new InvalidOperationException("settings exploded"));

        var result = await h.Service.CreateBackupAsync(new BackupOptions
        {
            DestinationPath = h.DestDir,
            IncludeDocuments = false,
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("settings exploded");
    }

    // ── RestoreFromBackupAsync (guard / validation / encrypted / error only) ──

    [Fact]
    public async Task RestoreFromBackupAsync_NullPath_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.RestoreFromBackupAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RestoreFromBackupAsync_MissingFile_ReturnsFailure()
    {
        var h = NewHarness();
        var missing = Path.Combine(h.DestDir, "does-not-exist.agentxbak");

        var result = await h.Service.RestoreFromBackupAsync(missing);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RestoreFromBackupAsync_CorruptArchive_FailsValidation()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "corrupt.agentxbak");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("this is not a zip"));

        var result = await h.Service.RestoreFromBackupAsync(path);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("validation");
    }

    [Fact]
    public async Task RestoreFromBackupAsync_EncryptedArchive_ReturnsGuidanceError()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "enc.agentxbak");
        var blob = BackupService.EncryptBytes(BuildPlainArchiveBytes(), "pw");
        await File.WriteAllBytesAsync(path, blob);

        var result = await h.Service.RestoreFromBackupAsync(path);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("encrypted");
    }

    [Fact]
    public async Task RestoreFromBackupAsync_CancelledBeforeDatabaseSwap_ReturnsCancelled()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "valid.agentxbak");
        await File.WriteAllBytesAsync(path, BuildPlainArchiveBytes());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await h.Service.RestoreFromBackupAsync(path, progress: null, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled");
    }

    // ── GetBackupHistoryAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetBackupHistoryAsync_ReturnsNewestFirst()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Backups.Add(new BackupEntity { FileName = "old", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            ctx.Backups.Add(new BackupEntity { FileName = "new", CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
            ctx.Backups.Add(new BackupEntity { FileName = "mid", CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) });
        });

        var history = await h.Service.GetBackupHistoryAsync();

        history.Select(b => b.FileName).Should().ContainInOrder("new", "mid", "old");
    }

    [Fact]
    public async Task GetBackupHistoryAsync_Empty_ReturnsEmptyList()
    {
        var h = NewHarness();
        (await h.Service.GetBackupHistoryAsync()).Should().BeEmpty();
    }

    // ── DeleteBackupAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBackupAsync_RemovesRecordAndArchiveFile()
    {
        var h = NewHarness();
        var archivePath = Path.Combine(h.DestDir, "to-delete.agentxbak");
        await File.WriteAllBytesAsync(archivePath, new byte[] { 1, 2, 3 });

        long id = 0;
        h.Seed(ctx =>
        {
            var e = new BackupEntity { FileName = "to-delete", FilePath = archivePath, CreatedAt = DateTime.UtcNow };
            ctx.Backups.Add(e);
            ctx.SaveChanges();
            id = e.Id;
        });

        await h.Service.DeleteBackupAsync(id);

        File.Exists(archivePath).Should().BeFalse();
        using var ctx = h.Fresh();
        ctx.Backups.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteBackupAsync_FileAlreadyGone_RemovesRecord()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var e = new BackupEntity
            {
                FileName = "ghost",
                FilePath = Path.Combine(h.DestDir, "already-gone.agentxbak"),
                CreatedAt = DateTime.UtcNow,
            };
            ctx.Backups.Add(e);
            ctx.SaveChanges();
            id = e.Id;
        });

        await h.Service.DeleteBackupAsync(id);

        using var ctx = h.Fresh();
        ctx.Backups.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteBackupAsync_UnknownId_IsNoOp()
    {
        var h = NewHarness();
        var act = () => h.Service.DeleteBackupAsync(99999);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteBackupAsync_LockedFile_StillRemovesRecord()
    {
        var h = NewHarness();
        var archivePath = Path.Combine(h.DestDir, "locked.agentxbak");
        await File.WriteAllBytesAsync(archivePath, new byte[] { 1, 2, 3 });

        long id = 0;
        h.Seed(ctx =>
        {
            var e = new BackupEntity { FileName = "locked", FilePath = archivePath, CreatedAt = DateTime.UtcNow };
            ctx.Backups.Add(e);
            ctx.SaveChanges();
            id = e.Id;
        });

        // Hold an exclusive lock so File.Delete throws — the service must swallow it and still
        // remove the history record.
        using (var _ = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await h.Service.DeleteBackupAsync(id);
        }

        using var ctx = h.Fresh();
        ctx.Backups.Should().BeEmpty();
    }

    // ── EstimateBackupSizeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task EstimateBackupSizeAsync_SumsDocumentFilesExcludingDatabaseSidecars()
    {
        var h = NewHarness();
        h.AddStorageFile("a.txt", new byte[1024]);
        h.AddStorageFile("nested/b.bin", new byte[2048]);
        h.AddStorageFile("agentx.db", new byte[4096]);      // excluded
        h.AddStorageFile("agentx.db-shm", new byte[4096]);  // excluded
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(new ConversationEntity { Title = "c", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        });

        var estimate = await h.Service.EstimateBackupSizeAsync();

        // 3072 bytes of real documents; the 8 KB of sidecars must not be counted.
        estimate.DocumentsSizeMB.Should().BeApproximately(3072 / (1024.0 * 1024.0), 0.0001);
        estimate.TotalEstimatedMB.Should().BeApproximately(estimate.DatabaseSizeMB + estimate.DocumentsSizeMB, 0.0001);
    }

    [Fact]
    public async Task EstimateBackupSizeAsync_MissingStorageFolder_ReportsZeroDocuments()
    {
        var h = NewHarness(createStorageDir: false);
        h.CurrentSettings.StoragePath = Path.Combine(Path.GetTempPath(), "agentx-none-" + Guid.NewGuid().ToString("N"));

        var estimate = await h.Service.EstimateBackupSizeAsync();

        estimate.DocumentsSizeMB.Should().Be(0);
    }

    // ── ValidateBackupAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ValidateBackupAsync_NullPath_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.ValidateBackupAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateBackupAsync_MissingFile_ReturnsFalse()
    {
        var h = NewHarness();
        (await h.Service.ValidateBackupAsync(Path.Combine(h.DestDir, "nope.agentxbak"))).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_EmptyFile_ReturnsFalse()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "empty.agentxbak");
        await File.WriteAllBytesAsync(path, Array.Empty<byte>());
        (await h.Service.ValidateBackupAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_NotAZip_ReturnsFalse()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "garbage.agentxbak");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("definitely not a zip archive"));
        (await h.Service.ValidateBackupAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_CompleteArchive_ReturnsTrue()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "good.agentxbak");
        await File.WriteAllBytesAsync(path, BuildPlainArchiveBytes());
        (await h.Service.ValidateBackupAsync(path)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBackupAsync_MissingDatabaseEntry_ReturnsFalse()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "nodb.agentxbak");
        await File.WriteAllBytesAsync(path, BuildArchiveBytes(("manifest.json", new byte[] { 1 })));
        (await h.Service.ValidateBackupAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_MissingManifestEntry_ReturnsFalse()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "nomanifest.agentxbak");
        await File.WriteAllBytesAsync(path, BuildArchiveBytes(("database/agentx.db", new byte[] { 1 })));
        (await h.Service.ValidateBackupAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_EncryptedArchive_ReturnsTrue()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "enc.agentxbak");
        await File.WriteAllBytesAsync(path, BackupService.EncryptBytes(BuildPlainArchiveBytes(), "pw"));
        (await h.Service.ValidateBackupAsync(path)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBackupAsync_UnsafeDocumentEntry_ReturnsFalse()
    {
        var h = NewHarness();
        var path = Path.Combine(h.DestDir, "evil.agentxbak");
        await File.WriteAllBytesAsync(path, BuildArchiveBytes(
            ("database/agentx.db", new byte[] { 1 }),
            ("manifest.json", new byte[] { 2 }),
            ("documents/../escape.txt", Encoding.UTF8.GetBytes("pwned"))));
        (await h.Service.ValidateBackupAsync(path)).Should().BeFalse();
    }

    // ── TryValidateDocumentEntries (null guard) ──────────────────────────────

    [Fact]
    public void TryValidateDocumentEntries_NullArchive_Throws()
    {
        var act = () => BackupService.TryValidateDocumentEntries(null!, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Encrypt / Decrypt guard branches (beyond the security suite) ──────────

    [Fact]
    public void EncryptBytes_NullPlaintext_Throws()
    {
        var act = () => BackupService.EncryptBytes(null!, "pw");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EncryptBytes_BlankPassword_Throws()
    {
        var act = () => BackupService.EncryptBytes(new byte[] { 1 }, "   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecryptBytes_NullData_Throws()
    {
        var act = () => BackupService.DecryptBytes(null!, "pw");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DecryptBytes_BlankPassword_Throws()
    {
        var act = () => BackupService.DecryptBytes(new byte[] { 1, 2, 3 }, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecryptBytes_UnrecognisedMagic_Throws()
    {
        var act = () => BackupService.DecryptBytes(Encoding.ASCII.GetBytes("NOPE\0\0\0\0and some more bytes"), "pw");
        act.Should().Throw<InvalidOperationException>().WithMessage("*not a recognised*");
    }

    [Fact]
    public void DecryptBytes_V2HeaderTooShort_Throws()
    {
        // Valid V2 magic but truncated before salt/nonce/tag are present.
        var data = Encoding.ASCII.GetBytes("AGXENC2\0").Concat(new byte[4]).ToArray();
        var act = () => BackupService.DecryptBytes(data, "pw");
        act.Should().Throw<InvalidOperationException>().WithMessage("*too short*");
    }

    [Fact]
    public void DecryptBytes_LegacyHeaderTooShort_Throws()
    {
        var data = Encoding.ASCII.GetBytes("AGXENC\0\0").Concat(new byte[4]).ToArray();
        var act = () => BackupService.DecryptBytes(data, "pw");
        act.Should().Throw<InvalidOperationException>().WithMessage("*too short*");
    }

    // ── Scheduled backups: config loading + lifecycle ────────────────────────

    [Fact]
    public async Task StartScheduledBackupsAsync_NoConfig_DoesNotStartLoop()
    {
        var h = NewHarness();
        h.Settings.Setup(s => s.GetValueAsync<string>("BackupScheduleConfig")).ReturnsAsync((string?)null);

        await h.Service.StartScheduledBackupsAsync();

        // Disabled by default → calling stop is a safe no-op.
        var act = () => h.Service.StopScheduledBackups();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StartScheduledBackupsAsync_MalformedConfig_FallsBackToDisabled()
    {
        var h = NewHarness();
        h.Settings.Setup(s => s.GetValueAsync<string>("BackupScheduleConfig")).ReturnsAsync("{ not valid json");

        var act = () => h.Service.StartScheduledBackupsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartScheduledBackupsAsync_NullJsonLiteral_FallsBackToDisabled()
    {
        var h = NewHarness();
        h.Settings.Setup(s => s.GetValueAsync<string>("BackupScheduleConfig")).ReturnsAsync("null");

        var act = () => h.Service.StartScheduledBackupsAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartScheduledBackupsAsync_EnabledConfig_StartsAndCanBeStopped()
    {
        var h = NewHarness();
        // Long interval (weekly) — the loop arms its timer but never fires within the test.
        var config = JsonSerializer.Serialize(new BackupScheduleConfig
        {
            Enabled = true,
            IntervalHours = 168,
            MaxBackupsToKeep = 3,
            DestinationPath = h.DestDir,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        h.Settings.Setup(s => s.GetValueAsync<string>("BackupScheduleConfig")).ReturnsAsync(config);

        await h.Service.StartScheduledBackupsAsync();
        // A second start must cancel the first cleanly.
        await h.Service.StartScheduledBackupsAsync();

        var act = () => h.Service.StopScheduledBackups();
        act.Should().NotThrow();
    }

    [Fact]
    public void StopScheduledBackups_WhenNoneRunning_IsNoOp()
    {
        var h = NewHarness();
        var act = () => h.Service.StopScheduledBackups();
        act.Should().NotThrow();
    }

    // ── EnforceRetentionPolicyAsync (private; reached by reflection) ──────────

    [Fact]
    public async Task EnforceRetentionPolicy_DeletesOldestScheduledBeyondCap()
    {
        var h = NewHarness();
        var paths = new List<string>();
        h.Seed(ctx =>
        {
            for (var i = 0; i < 5; i++)
            {
                var p = Path.Combine(h.DestDir, $"sched-{i}.agentxbak");
                File.WriteAllBytes(p, new byte[] { (byte)i });
                paths.Add(p);
                ctx.Backups.Add(new BackupEntity
                {
                    FileName = $"sched-{i}",
                    FilePath = p,
                    BackupType = "scheduled",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                });
            }
            // A manual backup must never be touched by retention.
            ctx.Backups.Add(new BackupEntity { FileName = "manual", BackupType = "manual", CreatedAt = DateTime.UtcNow });
        });

        await InvokeEnforceRetentionAsync(h.Service, maxToKeep: 2);

        using var ctx = h.Fresh();
        var remaining = ctx.Backups.Where(b => b.BackupType == "scheduled").Select(b => b.FileName).ToList();
        // Newest two scheduled survive (days 4 and 3); the three oldest are pruned.
        remaining.Should().BeEquivalentTo(new[] { "sched-4", "sched-3" });
        ctx.Backups.Count(b => b.BackupType == "manual").Should().Be(1);

        // Oldest three archive files removed from disk.
        File.Exists(paths[0]).Should().BeFalse();
        File.Exists(paths[1]).Should().BeFalse();
        File.Exists(paths[2]).Should().BeFalse();
    }

    [Fact]
    public async Task EnforceRetentionPolicy_NonPositiveCap_KeepsEverything()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Backups.Add(new BackupEntity { FileName = "s1", BackupType = "scheduled", CreatedAt = DateTime.UtcNow });
            ctx.Backups.Add(new BackupEntity { FileName = "s2", BackupType = "scheduled", CreatedAt = DateTime.UtcNow });
        });

        await InvokeEnforceRetentionAsync(h.Service, maxToKeep: 0);

        using var ctx = h.Fresh();
        ctx.Backups.Count(b => b.BackupType == "scheduled").Should().Be(2);
    }

    [Fact]
    public async Task EnforceRetentionPolicy_WithinCap_KeepsEverything()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Backups.Add(new BackupEntity { FileName = "s1", BackupType = "scheduled", CreatedAt = DateTime.UtcNow });
            ctx.Backups.Add(new BackupEntity { FileName = "s2", BackupType = "scheduled", CreatedAt = DateTime.UtcNow });
        });

        await InvokeEnforceRetentionAsync(h.Service, maxToKeep: 5);

        using var ctx = h.Fresh();
        ctx.Backups.Count(b => b.BackupType == "scheduled").Should().Be(2);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Task InvokeEnforceRetentionAsync(BackupService service, int maxToKeep)
    {
        var method = typeof(BackupService).GetMethod(
            "EnforceRetentionPolicyAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(service, new object[] { maxToKeep, CancellationToken.None })!;
    }

    /// <summary>Builds a minimal but structurally valid plain archive (db + manifest entries).</summary>
    private static byte[] BuildPlainArchiveBytes() => BuildArchiveBytes(
        ("database/agentx.db", new byte[] { 1, 2, 3 }),
        ("manifest.json", Encoding.UTF8.GetBytes("{\"version\":1}")));

    private static byte[] BuildArchiveBytes(params (string name, byte[] data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var s = entry.Open();
                s.Write(data, 0, data.Length);
            }
        }
        return ms.ToArray();
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort temp cleanup */ }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort temp cleanup */ }
    }
}

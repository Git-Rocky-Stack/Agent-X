using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgentX.Tests.Services.Security;

[Collection("SqlCipher")]
public class DatabaseEncryptionMigratorTests
{
    /// <summary>
    /// Creates a temporary directory that is automatically deleted on disposal.
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentx-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
    [Fact]
    public async Task MigrateToEncryptedAsync_converts_plaintext_db_to_encrypted_preserving_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-{Guid.NewGuid():N}.db");
        try
        {
            // Arrange: create plaintext DB with data
            using (var plain = new SqliteConnection($"Data Source={dbPath}"))
            {
                plain.Open();
                using var cmd = plain.CreateCommand();
                cmd.CommandText = "CREATE TABLE docs (id INT, title TEXT); INSERT INTO docs VALUES (1, 'hello'), (2, 'world');";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var key = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            IDatabaseEncryptionMigrator sut = new DatabaseEncryptionMigrator();

            // Act
            await sut.MigrateToEncryptedAsync(dbPath, key);

            // Assert 1: plaintext open of the converted file throws ErrorCode 26
            SqliteConnection.ClearAllPools();
            using (var plainAfter = new SqliteConnection($"Data Source={dbPath}"))
            {
                plainAfter.Open();
                using var cmd = plainAfter.CreateCommand();
                cmd.CommandText = "SELECT title FROM docs WHERE id=1";
                var act = () => cmd.ExecuteScalar();
                act.Should().Throw<SqliteException>()
                   .Where(ex => ex.SqliteErrorCode == 26);
            }
            SqliteConnection.ClearAllPools();

            // Assert 2: opening with PRAGMA key returns the original rows
            using (var encr = new SqliteConnection($"Data Source={dbPath}"))
            {
                encr.Open();
                using var keyCmd = encr.CreateCommand();
                keyCmd.CommandText = $@"PRAGMA key = ""x'{key.HexKey}'"";";
                keyCmd.ExecuteNonQuery();

                using var readCmd = encr.CreateCommand();
                readCmd.CommandText = "SELECT COUNT(*) FROM docs";
                var count = Convert.ToInt64(readCmd.ExecuteScalar());
                count.Should().Be(2);

                using var titleCmd = encr.CreateCommand();
                titleCmd.CommandText = "SELECT title FROM docs WHERE id=1";
                var title = (string)titleCmd.ExecuteScalar()!;
                title.Should().Be("hello");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_preserves_original_file_on_failure()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-{Guid.NewGuid():N}.db");
        try
        {
            using (var plain = new SqliteConnection($"Data Source={dbPath}"))
            {
                plain.Open();
                using var cmd = plain.CreateCommand();
                cmd.CommandText = "CREATE TABLE docs (id INT); INSERT INTO docs VALUES (1);";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var originalSize = new FileInfo(dbPath).Length;

            // Empty key triggers a failure during validation or ATTACH KEY
            var badKey = new DatabaseKeyMaterial("", KeyStorageMode.DpapiWrapped);
            IDatabaseEncryptionMigrator sut = new DatabaseEncryptionMigrator();

            var act = async () => await sut.MigrateToEncryptedAsync(dbPath, badKey);
            await act.Should().ThrowAsync<Exception>();

            // Original file still intact, same size, still plaintext readable
            File.Exists(dbPath).Should().BeTrue();
            new FileInfo(dbPath).Length.Should().Be(originalSize);

            SqliteConnection.ClearAllPools();
            using var plainAfter = new SqliteConnection($"Data Source={dbPath}");
            plainAfter.Open();
            using var readCmd = plainAfter.CreateCommand();
            readCmd.CommandText = "SELECT id FROM docs";
            var id = Convert.ToInt64(readCmd.ExecuteScalar());
            id.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_throws_FileNotFound_when_source_missing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-missing-{Guid.NewGuid():N}.db");
        // Do NOT create the file.

        var key = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
        IDatabaseEncryptionMigrator sut = new DatabaseEncryptionMigrator();

        var act = async () => await sut.MigrateToEncryptedAsync(dbPath, key);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_checkpoints_wal_before_export()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-wal-{Guid.NewGuid():N}.db");
        var hexKey = new string('A', 64);

        try
        {
            // Create DB in WAL mode, insert a row, deliberately skip checkpoint.
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var walCmd = conn.CreateCommand();
                walCmd.CommandText = "PRAGMA journal_mode=WAL";
                await walCmd.ExecuteScalarAsync();

                using var tableCmd = conn.CreateCommand();
                tableCmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT); INSERT INTO t VALUES(1,'wal-data')";
                await tableCmd.ExecuteNonQueryAsync();

                // Do NOT checkpoint — data sits in WAL.
            }

            SqliteConnection.ClearAllPools();

            var key = new DatabaseKeyMaterial(hexKey, KeyStorageMode.DpapiWrapped);
            var migrator = new DatabaseEncryptionMigrator();
            await migrator.MigrateToEncryptedAsync(dbPath, key);

            // Verify encrypted DB has the WAL-originated row.
            SqliteConnection.ClearAllPools();
            using (var verify = new SqliteConnection($"Data Source={dbPath}"))
            {
                await verify.OpenAsync();
                using var k = verify.CreateCommand();
                k.CommandText = $@"PRAGMA key = ""x'{hexKey}'"";";
                await k.ExecuteNonQueryAsync();

                using var probe = verify.CreateCommand();
                probe.CommandText = "SELECT v FROM t WHERE id=1";
                var result = await probe.ExecuteScalarAsync();
                Assert.Equal("wal-data", result);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task MigrateToEncryptedAsync_throws_ArgumentException_when_key_empty()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-mig-{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllBytes(dbPath, new byte[] { 0 }); // any file; error should fire before we try to read
            var emptyKey = new DatabaseKeyMaterial("", KeyStorageMode.DpapiWrapped);
            IDatabaseEncryptionMigrator sut = new DatabaseEncryptionMigrator();

            var act = async () => await sut.MigrateToEncryptedAsync(dbPath, emptyKey);
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void RecoverIfNeeded_restores_plaintext_backup_when_no_main_db()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "test.db");
        var backupPath = dbPath + ".plain.bak";

        // Simulate kill-window: only the backup exists, main db is missing.
        File.WriteAllText(dbPath, "fake-sqlite-content");
        File.Move(dbPath, backupPath);

        var migrator = new DatabaseEncryptionMigrator();
        migrator.RecoverIfNeeded(dbPath);

        Assert.True(File.Exists(dbPath));
        Assert.False(File.Exists(backupPath));
        Assert.Equal("fake-sqlite-content", File.ReadAllText(dbPath));
    }

    [Fact]
    public void RecoverIfNeeded_removes_orphaned_temp_when_main_db_intact()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "test.db");
        var tempPath = dbPath + ".enc.tmp";

        // Main DB intact, orphaned temp from a prior failed attempt.
        File.WriteAllText(dbPath, "real-content");
        File.WriteAllText(tempPath, "stale-temp");

        var migrator = new DatabaseEncryptionMigrator();
        migrator.RecoverIfNeeded(dbPath);

        Assert.True(File.Exists(dbPath));
        Assert.Equal("real-content", File.ReadAllText(dbPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void RecoverIfNeeded_is_noop_when_no_artifacts()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "test.db");
        File.WriteAllText(dbPath, "normal-content");

        var migrator = new DatabaseEncryptionMigrator();
        migrator.RecoverIfNeeded(dbPath);

        Assert.Equal("normal-content", File.ReadAllText(dbPath));
    }
}

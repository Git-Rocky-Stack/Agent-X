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
}

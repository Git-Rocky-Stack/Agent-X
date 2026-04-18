using System;
using System.IO;
using System.Security.Cryptography;
using AgentX.Core.Data;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgentX.Tests.Data;

[Collection("SqlCipher")]
public class EncryptedConnectionFactoryTests
{
    [Fact]
    public void OpenKeyed_without_key_returns_plaintext_connection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentx-encfactory-{Guid.NewGuid():N}.db");
        try
        {
            var keyProvider = new FakeKeyProvider(null);
            IEncryptedConnectionFactory sut = new EncryptedConnectionFactory(keyProvider);

            using var conn = sut.OpenKeyed(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (v INT); INSERT INTO t VALUES (1); SELECT v FROM t";
            var v = (long)cmd.ExecuteScalar()!;
            v.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OpenKeyed_with_key_creates_encrypted_db_that_requires_same_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentx-encfactory-{Guid.NewGuid():N}.db");
        try
        {
            var key = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            var keyProvider = new FakeKeyProvider(key);
            IEncryptedConnectionFactory sut = new EncryptedConnectionFactory(keyProvider);

            using (var create = sut.OpenKeyed(path))
            {
                using var cmd = create.CreateCommand();
                cmd.CommandText = "CREATE TABLE t (v INT); INSERT INTO t VALUES (42);";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            using (var reopen = sut.OpenKeyed(path))
            {
                using var cmd = reopen.CreateCommand();
                cmd.CommandText = "SELECT v FROM t";
                var v = (long)cmd.ExecuteScalar()!;
                v.Should().Be(42);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OpenKeyed_with_wrong_key_throws_SqliteException_26_on_first_read()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentx-encfactory-{Guid.NewGuid():N}.db");
        try
        {
            var rightKey = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
            var wrongKey = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);

            using (var create = new EncryptedConnectionFactory(new FakeKeyProvider(rightKey)).OpenKeyed(path))
            {
                using var cmd = create.CreateCommand();
                cmd.CommandText = "CREATE TABLE t (v INT); INSERT INTO t VALUES (42);";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            using var reopen = new EncryptedConnectionFactory(new FakeKeyProvider(wrongKey)).OpenKeyed(path);
            using var readCmd = reopen.CreateCommand();
            readCmd.CommandText = "SELECT v FROM t";
            var act = () => readCmd.ExecuteScalar();

            act.Should().Throw<SqliteException>()
               .Where(ex => ex.SqliteErrorCode == 26)
               .WithMessage("*file is not a database*");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyKey_on_closed_connection_throws_InvalidOperationException()
    {
        var key = DatabaseKeyMaterial.FromBytes(RandomNumberGenerator.GetBytes(32), KeyStorageMode.DpapiWrapped);
        var sut = new EncryptedConnectionFactory(new FakeKeyProvider(key));

        using var conn = new SqliteConnection("Data Source=:memory:");  // not opened
        var act = () => sut.ApplyKey(conn);

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class FakeKeyProvider : IDatabaseKeyProvider
    {
        private readonly DatabaseKeyMaterial? _key;
        public FakeKeyProvider(DatabaseKeyMaterial? key) => _key = key;
        public DatabaseKeyMaterial? Current => _key;
    }
}

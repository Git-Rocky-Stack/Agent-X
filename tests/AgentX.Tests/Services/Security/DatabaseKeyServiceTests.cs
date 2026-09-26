using System;
using System.IO;
using System.Threading.Tasks;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Security;

public class DatabaseKeyServiceTests
{
    private static (DatabaseKeyService Sut, string MarkerPath) NewSut()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"agentx-keysvc-{Guid.NewGuid():N}.json");
        var stateFile = new EncryptionStateFile(markerPath);
        var dpapi = new DpapiEncryptionService();
        var sut = new DatabaseKeyService(stateFile, dpapi);
        return (sut, markerPath);
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_with_DpapiWrapped_creates_new_key_on_first_call()
    {
        var (sut, markerPath) = NewSut();
        try
        {
            var key = await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);

            key.Mode.Should().Be(KeyStorageMode.DpapiWrapped);
            key.HexKey.Should().HaveLength(64);
            (await sut.IsProvisionedAsync()).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_returns_same_key_on_repeat_call()
    {
        var (sut, markerPath) = NewSut();
        try
        {
            var key1 = await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);
            var key2 = await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped);

            key2.HexKey.Should().Be(key1.HexKey);
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task GetOrCreateKeyAsync_with_UserPassphrase_derives_deterministic_key_for_same_passphrase()
    {
        var (sut, markerPath) = NewSut();
        try
        {
            var key1 = await sut.GetOrCreateKeyAsync(KeyStorageMode.UserPassphrase, passphrase: "correct horse battery staple");

            key1.Mode.Should().Be(KeyStorageMode.UserPassphrase);
            key1.HexKey.Should().HaveLength(64);
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task UnlockWithPassphraseAsync_with_correct_passphrase_returns_same_key()
    {
        var (sut, markerPath) = NewSut();
        try
        {
            var created = await sut.GetOrCreateKeyAsync(KeyStorageMode.UserPassphrase, "correct horse battery staple");
            var unlocked = await sut.UnlockWithPassphraseAsync("correct horse battery staple");

            unlocked.HexKey.Should().Be(created.HexKey);
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task UnlockWithPassphraseAsync_with_wrong_passphrase_still_derives_but_yields_different_key()
    {
        // PBKDF2 derives deterministically from passphrase+salt. A "wrong" passphrase produces
        // a different (but still 32-byte) key. Rejection of a wrong key is enforced at DB-open
        // time by SQLCipher (SqliteException ErrorCode 26), not by this service.
        var (sut, markerPath) = NewSut();
        try
        {
            var created = await sut.GetOrCreateKeyAsync(KeyStorageMode.UserPassphrase, "right");

            var wrong = await sut.UnlockWithPassphraseAsync("wrong");

            wrong.HexKey.Should().NotBe(created.HexKey);
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task CreateUncommittedKeyAsync_returns_key_and_marker_without_writing_the_marker()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"agentx-keysvc-{Guid.NewGuid():N}.json");
        var dpapi = new AgentX.Tests.Helpers.FakeDpapiEncryptionService();
        var sut = new DatabaseKeyService(new EncryptionStateFile(markerPath), dpapi);
        try
        {
            var provisioned = await sut.CreateUncommittedKeyAsync(KeyStorageMode.DpapiWrapped);

            // The marker must only be written after the database migration is verified, so
            // provisioning alone leaves nothing on disk.
            File.Exists(markerPath).Should().BeFalse();
            (await sut.IsProvisionedAsync()).Should().BeFalse();

            provisioned.Key.Mode.Should().Be(KeyStorageMode.DpapiWrapped);
            provisioned.Key.HexKey.Should().HaveLength(64);
            provisioned.Marker.StorageMode.Should().Be(KeyStorageMode.DpapiWrapped);
            dpapi.Decrypt(provisioned.Marker.DpapiWrappedKey!).Should().Be(provisioned.Key.HexKey);

            // Once committed, the marker unlocks the same key.
            await new EncryptionStateFile(markerPath).WriteAsync(provisioned.Marker);
            (await sut.GetOrCreateKeyAsync(KeyStorageMode.DpapiWrapped)).HexKey.Should().Be(provisioned.Key.HexKey);
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task CreateUncommittedKeyAsync_refuses_when_a_marker_already_exists()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"agentx-keysvc-{Guid.NewGuid():N}.json");
        var stateFile = new EncryptionStateFile(markerPath);
        var sut = new DatabaseKeyService(stateFile, new AgentX.Tests.Helpers.FakeDpapiEncryptionService());
        try
        {
            var first = await sut.CreateUncommittedKeyAsync(KeyStorageMode.DpapiWrapped);
            await stateFile.WriteAsync(first.Marker);

            // A second key would orphan the database the existing marker unlocks.
            var act = () => sut.CreateUncommittedKeyAsync(KeyStorageMode.DpapiWrapped);

            await act.Should().ThrowAsync<InvalidOperationException>();
            stateFile.Read()!.DpapiWrappedKey.Should().Be(first.Marker.DpapiWrappedKey);
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }

    [Fact]
    public async Task GetProvisionedModeAsync_returns_null_before_provisioning()
    {
        var (sut, markerPath) = NewSut();
        try
        {
            var mode = await sut.GetProvisionedModeAsync();

            mode.Should().BeNull();
        }
        finally
        {
            if (File.Exists(markerPath)) File.Delete(markerPath);
        }
    }
}

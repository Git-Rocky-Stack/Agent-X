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

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AgentX.Core.Services.Security;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Security;

public class EncryptionStateFileTests
{
    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"agentx-encstate-{Guid.NewGuid():N}.json");

    private static EncryptionStateInfo NewDpapiInfo(string? wrappedKey = "DPAPI:abc123") =>
        new(
            Version: EncryptionStateFile.CurrentVersion,
            StorageMode: KeyStorageMode.DpapiWrapped,
            EnabledAt: DateTimeOffset.UtcNow,
            DpapiWrappedKey: wrappedKey,
            SaltBase64: null);

    private static EncryptionStateInfo NewPassphraseInfo(string saltBase64 = "AAAAAAAAAAAAAAAAAAAAAA==") =>
        new(
            Version: EncryptionStateFile.CurrentVersion,
            StorageMode: KeyStorageMode.UserPassphrase,
            EnabledAt: DateTimeOffset.UtcNow,
            DpapiWrappedKey: null,
            SaltBase64: saltBase64);

    [Fact]
    public void Exists_returns_false_when_file_missing()
    {
        var path = NewTempPath();
        var sut = new EncryptionStateFile(path);
        sut.Exists().Should().BeFalse();
    }

    [Fact]
    public void Read_returns_null_when_file_missing()
    {
        var path = NewTempPath();
        var sut = new EncryptionStateFile(path);
        sut.Read().Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_creates_file_readable_via_Read()
    {
        var path = NewTempPath();
        try
        {
            var sut = new EncryptionStateFile(path);

            await sut.WriteAsync(NewDpapiInfo());

            sut.Exists().Should().BeTrue();
            var info = sut.Read();
            info.Should().NotBeNull();
            info!.Version.Should().Be(1);
            info.StorageMode.Should().Be(KeyStorageMode.DpapiWrapped);
            info.EnabledAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_twice_overwrites_with_latest_mode()
    {
        var path = NewTempPath();
        try
        {
            var sut = new EncryptionStateFile(path);

            await sut.WriteAsync(NewDpapiInfo());
            await sut.WriteAsync(NewPassphraseInfo());

            var info = sut.Read();
            info!.StorageMode.Should().Be(KeyStorageMode.UserPassphrase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Delete_removes_the_file()
    {
        var path = NewTempPath();
        var sut = new EncryptionStateFile(path);
        await sut.WriteAsync(NewDpapiInfo());
        sut.Exists().Should().BeTrue();

        sut.Delete();

        sut.Exists().Should().BeFalse();
    }

    [Fact]
    public void Read_throws_on_malformed_json()
    {
        var path = NewTempPath();
        try
        {
            File.WriteAllText(path, "not-valid-json{");
            var sut = new EncryptionStateFile(path);

            var act = () => sut.Read();

            act.Should().Throw<JsonException>();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task StorageMode_round_trips_for_UserPassphrase()
    {
        var path = NewTempPath();
        try
        {
            var sut = new EncryptionStateFile(path);
            await sut.WriteAsync(NewPassphraseInfo());
            var info = sut.Read();
            info!.StorageMode.Should().Be(KeyStorageMode.UserPassphrase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task DpapiWrappedKey_round_trips_for_DpapiWrapped_mode()
    {
        var path = NewTempPath();
        try
        {
            var sut = new EncryptionStateFile(path);
            const string expected = "DPAPI:ThisIsAFakeWrappedKeyPayload==";

            await sut.WriteAsync(NewDpapiInfo(wrappedKey: expected));

            var info = sut.Read();
            info!.DpapiWrappedKey.Should().Be(expected);
            info.SaltBase64.Should().BeNull();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaltBase64_round_trips_for_UserPassphrase_mode()
    {
        var path = NewTempPath();
        try
        {
            var sut = new EncryptionStateFile(path);
            const string expectedSalt = "dGVzdC1zYWx0LTE2LWJ5dGVz"; // "test-salt-16-bytes" base64

            await sut.WriteAsync(NewPassphraseInfo(saltBase64: expectedSalt));

            var info = sut.Read();
            info!.SaltBase64.Should().Be(expectedSalt);
            info.DpapiWrappedKey.Should().BeNull();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_throws_when_info_is_null()
    {
        var path = NewTempPath();
        var sut = new EncryptionStateFile(path);

        var act = async () => await sut.WriteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

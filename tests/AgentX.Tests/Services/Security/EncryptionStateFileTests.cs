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

            await sut.WriteAsync(KeyStorageMode.DpapiWrapped);

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

            await sut.WriteAsync(KeyStorageMode.DpapiWrapped);
            await sut.WriteAsync(KeyStorageMode.UserPassphrase);

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
        await sut.WriteAsync(KeyStorageMode.DpapiWrapped);
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
            await sut.WriteAsync(KeyStorageMode.UserPassphrase);
            var info = sut.Read();
            info!.StorageMode.Should().Be(KeyStorageMode.UserPassphrase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

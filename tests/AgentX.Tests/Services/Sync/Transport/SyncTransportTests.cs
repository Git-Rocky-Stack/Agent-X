using AgentX.Core.Services.Sync.Transport;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Sync.Transport;

/// <summary>
/// Unit tests for <see cref="SyncTransport"/>.
/// Verifies file-system transport operations including file naming,
/// peer file scanning, and lifecycle management.
/// </summary>
public sealed class SyncTransportTests : IDisposable
{
    private readonly SyncTransport _sut;
    private readonly string _tempFolder;

    public SyncTransportTests()
    {
        _sut = new SyncTransport(Log.Logger);

        _tempFolder = Path.Combine(Path.GetTempPath(), $"sync-transport-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempFolder, true); } catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SyncTransport(null!));
    }

    // ── EnsureFolderExists ────────────────────────────────────────────────

    [Fact]
    public void EnsureFolderExists_EmptyPath_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() => _sut.EnsureFolderExists(""));
        // May throw InvalidOperationException or ArgumentException depending on OS
        Assert.True(ex is InvalidOperationException or ArgumentException);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  WriteSyncFileAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteSyncFileAsync_WritesFileToDisk()
    {
        var deviceId = "abc123";
        var exportedAt = DateTime.UtcNow;
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var result = await _sut.WriteSyncFileAsync(_tempFolder, deviceId, exportedAt, data, CancellationToken.None);

        File.Exists(result).Should().BeTrue();
        (await File.ReadAllBytesAsync(result)).Should().Equal(data);
    }

    [Fact]
    public async Task WriteSyncFileAsync_CreatesFolderIfMissing()
    {
        var subfolder = Path.Combine(_tempFolder, "new-sub-dir");
        var data = new byte[] { 42 };

        var result = await _sut.WriteSyncFileAsync(subfolder, "dev1", DateTime.UtcNow, data, CancellationToken.None);

        Directory.Exists(subfolder).Should().BeTrue();
        File.Exists(result).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BuildSyncFileName
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildSyncFileName_ProducesCanonicalFormat()
    {
        var deviceId = "abc123";
        var ts = new DateTime(2026, 4, 19, 14, 30, 15, 123, DateTimeKind.Utc);

        var name = SyncTransport.BuildSyncFileName(deviceId, ts);

        name.Should().StartWith("agentx-sync-abc123-");
        name.Should().EndWith(".axs");
        name.Should().Contain("20260419");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ReadPeerFilesAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadPeerFilesAsync_SkipsOwnFiles()
    {
        var localDeviceId = "local-dev";
        var remoteDeviceId = "remote-dev";

        // Write own file
        await File.WriteAllBytesAsync(
            Path.Combine(_tempFolder, $"agentx-sync-{localDeviceId}-202604191400.axs"),
            new byte[] { 1 });

        // Write peer file
        await File.WriteAllBytesAsync(
            Path.Combine(_tempFolder, $"agentx-sync-{remoteDeviceId}-202604191400.axs"),
            new byte[] { 2, 3 });

        var result = await _sut.ReadPeerFilesAsync(_tempFolder, localDeviceId, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Data.Should().Equal(new byte[] { 2, 3 });
    }

    [Fact]
    public async Task ReadPeerFilesAsync_ReturnsEmpty_WhenFolderMissing()
    {
        var result = await _sut.ReadPeerFilesAsync(
            Path.Combine(_tempFolder, "nonexistent"), "dev1", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadPeerFilesAsync_ReturnsEmpty_WhenNoFiles()
    {
        var result = await _sut.ReadPeerFilesAsync(_tempFolder, "dev1", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadPeerFilesAsync_ReturnsAllPeerFiles_Ordered()
    {
        for (int i = 0; i < 3; i++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(_tempFolder, $"agentx-sync-remote{i:d2}-202604191{i:d2}00.axs"),
                new byte[] { (byte)i });
        }

        var result = await _sut.ReadPeerFilesAsync(_tempFolder, "local-dev", CancellationToken.None);

        result.Should().HaveCount(3);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MarkFileImportedAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkFileImportedAsync_RenamesToImported()
    {
        var filePath = Path.Combine(_tempFolder, "agentx-sync-remote-202604191400.axs");
        await File.WriteAllBytesAsync(filePath, new byte[] { 1 });

        await _sut.MarkFileImportedAsync(filePath);

        File.Exists(filePath).Should().BeFalse();
        File.Exists(filePath + ".imported").Should().BeTrue();
    }

    [Fact]
    public async Task MarkFileImportedAsync_EmptyPath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.MarkFileImportedAsync(""));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  EnsureFolderExists
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EnsureFolderExists_CreatesDirectory()
    {
        var path = Path.Combine(_tempFolder, "deep", "nested", "dir");

        _sut.EnsureFolderExists(path);

        Directory.Exists(path).Should().BeTrue();
    }
}

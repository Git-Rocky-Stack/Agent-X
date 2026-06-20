using AgentX.Core.Services.Sync.ConflictResolution;
using AgentX.Core.Services.Sync.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Sync.ConflictResolution;

/// <summary>
/// Unit tests for <see cref="SyncConflictResolver"/>.
/// Verifies conflict detection and resolution strategies.
/// </summary>
public sealed class SyncConflictResolverTests
{
    private readonly SyncConflictResolver _sut;

    public SyncConflictResolverTests()
    {
        _sut = new SyncConflictResolver(Log.Logger);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SyncConflictResolver(null!));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DetectConflictsAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DetectConflictsAsync_NoSyncBaseline_ReturnsEmpty()
    {
        var incoming = CreateChangeSet("remote-dev");

        var result = await _sut.DetectConflictsAsync(
            incoming,
            lastSyncAt: null,
            "local-dev",
            (_, _) => Task.FromResult<DateTime?>(DateTime.UtcNow));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_SameDeviceId_ReturnsEmpty()
    {
        var incoming = CreateChangeSet("local-dev");

        var result = await _sut.DetectConflictsAsync(
            incoming,
            DateTime.UtcNow.AddHours(-1),
            "local-dev",
            (_, _) => Task.FromResult<DateTime?>(DateTime.UtcNow));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_EntityNotLocal_ReturnsEmpty()
    {
        var incoming = CreateChangeSet("remote-dev");

        var result = await _sut.DetectConflictsAsync(
            incoming,
            DateTime.UtcNow.AddHours(-1),
            "local-dev",
            (_, _) => Task.FromResult<DateTime?>(null));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_LocalNotModifiedSinceSync_ReturnsEmpty()
    {
        var syncTime = DateTime.UtcNow;
        var incoming = CreateChangeSet("remote-dev");

        // Local modified BEFORE the last sync
        var localModifiedAt = syncTime.AddHours(-2);

        var result = await _sut.DetectConflictsAsync(
            incoming,
            syncTime,
            "local-dev",
            (_, _) => Task.FromResult<DateTime?>(localModifiedAt));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_BothSidesModified_ReturnsConflict()
    {
        var syncTime = DateTime.UtcNow.AddHours(-1);
        var incoming = CreateChangeSet("remote-dev");

        // Local modified AFTER the last sync
        var localModifiedAt = syncTime.AddMinutes(30);

        var result = await _sut.DetectConflictsAsync(
            incoming,
            syncTime,
            "local-dev",
            (_, _) => Task.FromResult<DateTime?>(localModifiedAt));

        result.Should().HaveCount(1);
        result[0].EntityType.Should().Be("DocumentEntity");
        result[0].EntityId.Should().Be(42);
        result[0].Resolution.Should().Be(SyncResolution.Pending);
        result[0].LocalChange.Should().NotBeNull();
        result[0].RemoteChange.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectConflictsAsync_MultipleConflicts_Detected()
    {
        var syncTime = DateTime.UtcNow.AddHours(-1);
        var incoming = new SyncChangeSet
        {
            DeviceId = "remote-dev",
            ExportedAt = DateTime.UtcNow,
            Changes =
            [
                new SyncChange
                {
                    EntityType = "DocumentEntity", EntityId = 1,
                    ChangeType = SyncChangeType.Updated, Timestamp = DateTime.UtcNow,
                },
                new SyncChange
                {
                    EntityType = "CollectionEntity", EntityId = 2,
                    ChangeType = SyncChangeType.Updated, Timestamp = DateTime.UtcNow,
                },
            ],
        };

        var localModifiedAt = syncTime.AddMinutes(30);

        var result = await _sut.DetectConflictsAsync(
            incoming,
            syncTime,
            "local-dev",
            (_, _) => Task.FromResult<DateTime?>(localModifiedAt));

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DetectConflictsAsync_NullIncoming_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DetectConflictsAsync(null!, DateTime.UtcNow, "dev", (_, _) => Task.FromResult<DateTime?>(null)));
    }

    [Fact]
    public async Task DetectConflictsAsync_NullCallback_Throws()
    {
        var incoming = CreateChangeSet("remote-dev");
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DetectConflictsAsync(incoming, DateTime.UtcNow, "dev", null!));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ResolveConflict
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveConflict_KeepLocal_ReturnsNull()
    {
        var conflict = CreateConflict();

        var result = _sut.ResolveConflict(conflict, SyncResolution.KeepLocal);

        result.Should().BeNull();
        conflict.Resolution.Should().Be(SyncResolution.KeepLocal);
    }

    [Fact]
    public void ResolveConflict_KeepRemote_ReturnsRemoteChange()
    {
        var conflict = CreateConflict();

        var result = _sut.ResolveConflict(conflict, SyncResolution.KeepRemote);

        result.Should().NotBeNull();
        result.Should().BeSameAs(conflict.RemoteChange);
        conflict.Resolution.Should().Be(SyncResolution.KeepRemote);
    }

    [Fact]
    public void ResolveConflict_Merged_WithData_ReturnsRemoteChange()
    {
        var conflict = CreateConflict();
        conflict.RemoteChange.SerializedData = "{\"merged\":true}";

        var result = _sut.ResolveConflict(conflict, SyncResolution.Merged);

        result.Should().NotBeNull();
        result.Should().BeSameAs(conflict.RemoteChange);
        conflict.Resolution.Should().Be(SyncResolution.Merged);
    }

    [Fact]
    public void ResolveConflict_Merged_WithoutData_ReturnsNull()
    {
        var conflict = CreateConflict();
        conflict.RemoteChange.SerializedData = null;

        var result = _sut.ResolveConflict(conflict, SyncResolution.Merged);

        result.Should().BeNull();
        conflict.Resolution.Should().Be(SyncResolution.Merged);
    }

    [Fact]
    public void ResolveConflict_Pending_Throws()
    {
        var conflict = CreateConflict();
        Assert.Throws<ArgumentException>(
            () => _sut.ResolveConflict(conflict, SyncResolution.Pending));
    }

    [Fact]
    public void ResolveConflict_NullConflict_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.ResolveConflict(null!, SyncResolution.KeepLocal));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SyncChangeSet CreateChangeSet(string deviceId) => new()
    {
        DeviceId = deviceId,
        ExportedAt = DateTime.UtcNow,
        Changes =
        [
            new SyncChange
            {
                EntityType = "DocumentEntity",
                EntityId   = 42,
                ChangeType = SyncChangeType.Updated,
                Timestamp  = DateTime.UtcNow,
                SerializedData = "{\"title\":\"Remote\"}",
            },
        ],
    };

    private static SyncConflict CreateConflict() => new()
    {
        EntityType = "DocumentEntity",
        EntityId = 42,
        LocalChange = new SyncChange
        {
            EntityType = "DocumentEntity",
            EntityId = 42,
            ChangeType = SyncChangeType.Updated,
            Timestamp = DateTime.UtcNow,
        },
        RemoteChange = new SyncChange
        {
            EntityType = "DocumentEntity",
            EntityId = 42,
            ChangeType = SyncChangeType.Updated,
            Timestamp = DateTime.UtcNow,
            SerializedData = "{\"title\":\"Remote\"}",
        },
        Resolution = SyncResolution.Pending,
    };
}

using AgentX.Core.Services.Sync.ConflictResolution;
using AgentX.Core.Services.Sync.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Sync.ConflictResolution;

/// <summary>
/// Unit tests for <see cref="SyncConflictResolver"/>.
/// Verifies the last-writer-wins decision, conflict detection and resolution strategies.
/// </summary>
public sealed class SyncConflictResolverTests
{
    private readonly SyncConflictResolver _sut;

    public SyncConflictResolverTests()
    {
        _sut = new SyncConflictResolver(Log.Logger);
    }

    // ---- Constructor ----

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SyncConflictResolver(null!));
    }

    // ---- Decide (last writer wins) ----

    [Fact]
    public void Decide_NoLocalCopy_AppliesAnUpsert()
    {
        var change = Change(SyncChangeType.Created, DateTime.UtcNow);

        _sut.Decide(change, "remote-dev", localModifiedAt: null, "local-dev")
            .Should().Be(SyncDecision.ApplyRemote);
    }

    [Fact]
    public void Decide_NoLocalCopy_DeletionHasNothingToDo()
    {
        var change = Change(SyncChangeType.Deleted, DateTime.UtcNow);

        _sut.Decide(change, "remote-dev", localModifiedAt: null, "local-dev")
            .Should().Be(SyncDecision.AlreadyCurrent);
    }

    [Fact]
    public void Decide_RemoteNewerThanLocal_AppliesRemote()
    {
        var local = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var change = Change(SyncChangeType.Updated, local.AddMinutes(5));

        _sut.Decide(change, "remote-dev", local, "local-dev")
            .Should().Be(SyncDecision.ApplyRemote);
    }

    [Fact]
    public void Decide_LocalNewerThanRemote_KeepsLocal()
    {
        // The case that used to swap edits: the local copy was changed after the remote one,
        // so the older remote edit must not overwrite it, whatever the last-sync clock says.
        var local = new DateTime(2026, 3, 1, 10, 5, 0, DateTimeKind.Utc);
        var change = Change(SyncChangeType.Updated, local.AddMinutes(-5));

        _sut.Decide(change, "remote-dev", local, "local-dev")
            .Should().Be(SyncDecision.KeepLocal);
    }

    [Fact]
    public void Decide_EqualTimestamps_TieBreakIsDeterministicAndConverges()
    {
        var at = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var change = Change(SyncChangeType.Updated, at);

        // Device "b" beats device "a" on both sides, so both installations end with b's version.
        _sut.Decide(change, remoteDeviceId: "b", at, localDeviceId: "a").Should().Be(SyncDecision.ApplyRemote);
        _sut.Decide(change, remoteDeviceId: "a", at, localDeviceId: "b").Should().Be(SyncDecision.AlreadyCurrent);
    }

    [Fact]
    public void Decide_UnspecifiedAndUtcKinds_CompareOnTheSameClock()
    {
        // Database values come back without a kind; JSON values may carry Utc. Same instant.
        var utc = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        var change = Change(SyncChangeType.Updated, utc);

        _sut.Decide(change, "a", unspecified, "b").Should().Be(SyncDecision.AlreadyCurrent);
    }

    [Fact]
    public void Decide_OwnDevice_IsAlreadyCurrent()
    {
        var change = Change(SyncChangeType.Updated, DateTime.UtcNow);

        _sut.Decide(change, "local-dev", DateTime.UtcNow.AddDays(-1), "LOCAL-DEV")
            .Should().Be(SyncDecision.AlreadyCurrent);
    }

    [Fact]
    public void Decide_NullChange_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Decide(null!, "a", null, "b"));
    }

    // ---- DetectConflictsAsync ----

    [Fact]
    public async Task DetectConflictsAsync_NoPriorSyncBaseline_StillDetectsNewerLocalCopies()
    {
        // There is no wall-clock baseline any more: detection works on a fresh install or
        // after a restart, purely from the two modification timestamps.
        var incoming = CreateChangeSet("remote-dev", remoteTimestamp: DateTime.UtcNow.AddHours(-2));

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            _ => Task.FromResult<SyncLocalVersion?>(new SyncLocalVersion(7, DateTime.UtcNow)));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task DetectConflictsAsync_SameDeviceId_ReturnsEmpty()
    {
        var incoming = CreateChangeSet("local-dev", remoteTimestamp: DateTime.UtcNow.AddHours(-2));

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            _ => Task.FromResult<SyncLocalVersion?>(new SyncLocalVersion(7, DateTime.UtcNow)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_EntityNotLocal_ReturnsEmpty()
    {
        var incoming = CreateChangeSet("remote-dev", remoteTimestamp: DateTime.UtcNow);

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            _ => Task.FromResult<SyncLocalVersion?>(null));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_EntityWithoutModificationTime_ReturnsEmpty()
    {
        // Documents and tags are merged, never overwritten, so they never conflict.
        var incoming = CreateChangeSet("remote-dev", remoteTimestamp: DateTime.UtcNow.AddHours(-2));

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            _ => Task.FromResult<SyncLocalVersion?>(new SyncLocalVersion(7, null)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_RemoteNewer_ReturnsEmpty()
    {
        var incoming = CreateChangeSet("remote-dev", remoteTimestamp: DateTime.UtcNow);

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            _ => Task.FromResult<SyncLocalVersion?>(new SyncLocalVersion(7, DateTime.UtcNow.AddHours(-2))));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_LocalNewer_ReturnsConflictKeyedByLocalId()
    {
        var remoteAt = DateTime.UtcNow.AddHours(-1);
        var localAt = remoteAt.AddMinutes(30);
        var incoming = CreateChangeSet("remote-dev", remoteTimestamp: remoteAt);

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            _ => Task.FromResult<SyncLocalVersion?>(new SyncLocalVersion(7, localAt)));

        result.Should().HaveCount(1);
        result[0].EntityType.Should().Be("DocumentEntity");
        result[0].EntityId.Should().Be(7, "a conflict names the LOCAL row, not the sender's id 42");
        result[0].Resolution.Should().Be(SyncResolution.KeepLocal);
        result[0].LocalChange.Timestamp.Should().Be(localAt);
        result[0].RemoteChange.EntityId.Should().Be(42);
    }

    [Fact]
    public async Task DetectConflictsAsync_MultipleConflicts_Detected()
    {
        var remoteAt = DateTime.UtcNow.AddHours(-1);
        var incoming = new SyncChangeSet
        {
            DeviceId = "remote-dev",
            ExportedAt = DateTime.UtcNow,
            Changes =
            [
                new SyncChange
                {
                    EntityType = "CollectionEntity", EntityId = 1,
                    ChangeType = SyncChangeType.Updated, Timestamp = remoteAt,
                },
                new SyncChange
                {
                    EntityType = "ConversationEntity", EntityId = 2,
                    ChangeType = SyncChangeType.Updated, Timestamp = remoteAt,
                },
            ],
        };

        var result = await _sut.DetectConflictsAsync(
            incoming,
            "local-dev",
            change => Task.FromResult<SyncLocalVersion?>(new SyncLocalVersion(change.EntityId + 100, remoteAt.AddMinutes(30))));

        result.Should().HaveCount(2);
        result.Select(c => c.EntityId).Should().BeEquivalentTo(new[] { 101L, 102L });
    }

    [Fact]
    public async Task DetectConflictsAsync_NullIncoming_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DetectConflictsAsync(null!, "dev", _ => Task.FromResult<SyncLocalVersion?>(null)));
    }

    [Fact]
    public async Task DetectConflictsAsync_NullCallback_Throws()
    {
        var incoming = CreateChangeSet("remote-dev", DateTime.UtcNow);
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DetectConflictsAsync(incoming, "dev", null!));
    }

    private static SyncChange Change(SyncChangeType type, DateTime timestamp) => new()
    {
        EntityType = "CollectionEntity",
        EntityId = 3,
        ChangeType = type,
        Timestamp = timestamp,
        NaturalKey = "Shared",
    };
    // ---- ResolveConflict ----

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

    // ---- Helpers ----

    private static SyncChangeSet CreateChangeSet(string deviceId, DateTime remoteTimestamp) => new()
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
                Timestamp  = remoteTimestamp,
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

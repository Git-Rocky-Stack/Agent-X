using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Codec;
using AgentX.Core.Services.Sync.ConflictResolution;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Sync.Transport;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Sync;

/// <summary>
/// Unit tests for <see cref="SyncService"/> — the thin orchestrator that composes
/// <see cref="ISyncTransport"/>, <see cref="ISyncPackageCodec"/> and
/// <see cref="ISyncConflictResolver"/> over a real <see cref="AgentXDbContext"/>
/// (AX-QA-009 coverage uplift — the last of the four 0%-covered Core services).
///
/// The three sub-services are mocked (they own their own test suites under
/// Services/Sync/{Codec,ConflictResolution,Transport}); these tests drive the
/// orchestration: configuration persistence, change collection, conflict skipping,
/// upsert/delete application, audit logging, status transitions and the auto-sync
/// lifecycle, against an in-memory SQLite database via <see cref="TestDbContextFactory"/>.
///
/// The auto-sync <c>RunAutoSyncLoopAsync</c> cycle body and <c>ImportPeerFilesAsync</c>
/// are only reachable through a <see cref="PeriodicTimer"/> whose minimum tick is one
/// minute, so the peer-import branches are exercised by reflection-invoking the private
/// method directly — there is no injected clock seam, and adding one purely for tests
/// would be a larger production change than the behaviour warrants.
/// </summary>
public sealed class SyncServiceTests
{
    // The service serialises entities with camelCase naming + ignore-null; mirror it
    // exactly so the SerializedData we hand the service round-trips through its own
    // deserialiser (UpsertEntityAsync / Deserialise).
    private static readonly JsonSerializerOptions SyncJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, SyncJson);

    // ══════════════════════════════════════════════════════════════════════════
    //  Harness
    // ══════════════════════════════════════════════════════════════════════════

    private sealed class SyncHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; }
        public AgentXDbContext Db { get; }
        public Serilog.ILogger Logger { get; }
        public Mock<ISyncTransport> Transport { get; }
        public Mock<ISyncPackageCodec> Codec { get; }
        public Mock<ISyncConflictResolver> Resolver { get; }
        public SyncService Service { get; }

        public SyncHarness()
        {
            Factory = new TestDbContextFactory();
            Db = Factory.CreateContext();
            Logger = new LoggerConfiguration().CreateLogger();
            Transport = new Mock<ISyncTransport>();
            Codec = new Mock<ISyncPackageCodec>();
            Resolver = new Mock<ISyncConflictResolver>();

            // Default: no conflicts. Individual tests override as needed.
            Resolver
                .Setup(r => r.DetectConflictsAsync(
                    It.IsAny<SyncChangeSet>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<string>(),
                    It.IsAny<Func<string, long, Task<DateTime?>>>()))
                .ReturnsAsync((IReadOnlyList<SyncConflict>)Array.Empty<SyncConflict>());

            Service = new SyncService(Db, Logger, Transport.Object, Codec.Object, Resolver.Object);
        }

        /// <summary>Wires the codec + transport so an export reaches WriteSyncFileAsync.</summary>
        public void SetupExportPipeline(string writtenPath = @"C:\sync-test\agentx-sync-x.axs")
        {
            Codec.Setup(c => c.Serialise(It.IsAny<SyncChangeSet>())).Returns(new byte[] { 1, 2, 3 });
            Codec.Setup(c => c.Encrypt(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(new byte[] { 4, 5, 6 });
            Transport
                .Setup(t => t.WriteSyncFileAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                    It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(writtenPath);
        }

        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        public AgentXDbContext Fresh() => Factory.CreateContext();

        public void Dispose()
        {
            Db.Dispose();
            Factory.Dispose();
            (Logger as IDisposable)?.Dispose();
        }
    }

    // ── Configuration / change builders ─────────────────────────────────────────

    private static SyncConfiguration ValidConfig(
        string? folder = @"C:\sync-test",
        string key = "passphrase-123",
        bool autoSync = false,
        int interval = 30,
        SyncScope scope = SyncScope.All,
        string? selectedIds = null) => new()
        {
            SyncFolderPath = folder ?? string.Empty,
            EncryptionKey = key,
            AutoSyncEnabled = autoSync,
            SyncIntervalMinutes = interval,
            SyncScope = scope,
            SelectedCollectionIds = selectedIds,
        };

    private static SyncChange PromptCreate(long id, string name = "p", string content = "c")
    {
        var now = DateTime.UtcNow;
        var entity = new SystemPromptEntity
        {
            Id = id,
            Name = name,
            Content = content,
            Category = "General",
            CreatedAt = now,
            UpdatedAt = now,
        };
        return new SyncChange
        {
            EntityType = nameof(SystemPromptEntity),
            EntityId = id,
            ChangeType = SyncChangeType.Created,
            Timestamp = now,
            SerializedData = Json(entity),
        };
    }

    private static SyncChange Change(
        string entityType, long id, SyncChangeType type, string? data) => new()
        {
            EntityType = entityType,
            EntityId = id,
            ChangeType = type,
            Timestamp = DateTime.UtcNow,
            SerializedData = data,
        };

    private static SyncChangeSet ChangeSet(params SyncChange[] changes) => new()
    {
        DeviceId = "remote-device",
        ExportedAt = DateTime.UtcNow,
        Changes = changes.ToList(),
        Version = 1,
    };

    /// <summary>
    /// Reflection bridge for the timer-gated private auto-sync internals. There is no
    /// public path to <c>ImportPeerFilesAsync</c> / <c>RunAutoSyncLoopAsync</c> short of a
    /// one-minute PeriodicTimer tick, so we invoke them directly to cover their branches.
    /// </summary>
    private static Task InvokePrivateAsync(SyncService service, string method, params object[] args)
    {
        var mi = typeof(SyncService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(SyncService), method);
        return (Task)mi.Invoke(service, args)!;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Constructor null-guards
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        using var h = new SyncHarness();
        Action act = () => new SyncService(null!, h.Logger, h.Transport.Object, h.Codec.Object, h.Resolver.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var h = new SyncHarness();
        Action act = () => new SyncService(h.Db, null!, h.Transport.Object, h.Codec.Object, h.Resolver.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullTransport_Throws()
    {
        using var h = new SyncHarness();
        Action act = () => new SyncService(h.Db, h.Logger, null!, h.Codec.Object, h.Resolver.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("transport");
    }

    [Fact]
    public void Constructor_NullCodec_Throws()
    {
        using var h = new SyncHarness();
        Action act = () => new SyncService(h.Db, h.Logger, h.Transport.Object, null!, h.Resolver.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("codec");
    }

    [Fact]
    public void Constructor_NullConflictResolver_Throws()
    {
        using var h = new SyncHarness();
        Action act = () => new SyncService(h.Db, h.Logger, h.Transport.Object, h.Codec.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("conflictResolver");
    }

    [Fact]
    public void Status_Initial_IsIdle()
    {
        using var h = new SyncHarness();
        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
        h.Service.Status.PendingChanges.Should().Be(0);
        h.Service.Status.ErrorMessage.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ConfigureAsync / GetConfigurationAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConfigureAsync_NullConfig_Throws()
    {
        using var h = new SyncHarness();
        Func<Task> act = () => h.Service.ConfigureAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public async Task GetConfigurationAsync_NoConfiguration_ReturnsNull()
    {
        using var h = new SyncHarness();
        (await h.Service.GetConfigurationAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ConfigureAsync_ThenGet_RoundTripsConfiguration()
    {
        using var h = new SyncHarness();
        var config = ValidConfig(
            folder: @"D:\shared", key: "key", autoSync: true, interval: 15,
            scope: SyncScope.SelectedCollections, selectedIds: "1,2,3");

        await h.Service.ConfigureAsync(config);

        var loaded = await h.Service.GetConfigurationAsync();
        loaded.Should().NotBeNull();
        loaded!.SyncFolderPath.Should().Be(@"D:\shared");
        loaded.EncryptionKey.Should().Be("key");
        loaded.AutoSyncEnabled.Should().BeTrue();
        loaded.SyncIntervalMinutes.Should().Be(15);
        loaded.SyncScope.Should().Be(SyncScope.SelectedCollections);
        loaded.SelectedCollectionIds.Should().Be("1,2,3");
    }

    [Fact]
    public async Task ConfigureAsync_CalledTwice_UpdatesExistingRow()
    {
        using var h = new SyncHarness();

        await h.Service.ConfigureAsync(ValidConfig(folder: @"C:\first"));
        await h.Service.ConfigureAsync(ValidConfig(folder: @"C:\second"));

        var loaded = await h.Service.GetConfigurationAsync();
        loaded!.SyncFolderPath.Should().Be(@"C:\second");

        // The upsert must update in place — exactly one settings row for the config key.
        using var ctx = h.Fresh();
        (await ctx.UserSettings.CountAsync(s => s.Key == "SyncConfiguration")).Should().Be(1);
    }

    [Fact]
    public async Task GetConfigurationAsync_MalformedStoredJson_ReturnsNull()
    {
        using var h = new SyncHarness();
        h.Seed(ctx => ctx.UserSettings.Add(new UserSettingsEntity
        {
            Key = "SyncConfiguration",
            Value = "{ this is not valid json",
            ValueType = "json",
            UpdatedAt = DateTime.UtcNow,
        }));

        (await h.Service.GetConfigurationAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetConfigurationAsync_StoredNullJson_ReturnsNull()
    {
        using var h = new SyncHarness();
        // Literal "null" deserialises to a null config without throwing.
        h.Seed(ctx => ctx.UserSettings.Add(new UserSettingsEntity
        {
            Key = "SyncConfiguration",
            Value = "null",
            ValueType = "json",
            UpdatedAt = DateTime.UtcNow,
        }));

        (await h.Service.GetConfigurationAsync()).Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ExportChangesAsync — required-configuration guards
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportChangesAsync_NotConfigured_ThrowsAndLogsFailure()
    {
        using var h = new SyncHarness();

        Func<Task> act = () => h.Service.ExportChangesAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("not been configured");
        h.Service.Status.SyncState.Should().Be(SyncState.Error);

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle();
        history[0].IsSuccess.Should().BeFalse();
        history[0].Direction.Should().Be("export");
    }

    [Fact]
    public async Task ExportChangesAsync_EmptyFolderPath_Throws()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(folder: ""));

        Func<Task> act = () => h.Service.ExportChangesAsync();
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("SyncFolderPath");
    }

    [Fact]
    public async Task ExportChangesAsync_EmptyEncryptionKey_Throws()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(key: ""));

        Func<Task> act = () => h.Service.ExportChangesAsync();
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("EncryptionKey");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ExportChangesAsync — collection + happy path
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportChangesAsync_FullExport_CollectsEveryEntityType()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var now = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 1,
                FileName = "f.txt",
                FilePath = @"C:\f.txt",
                FileType = "txt",
                ContentHash = "h",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            });
            ctx.Collections.Add(new CollectionEntity { Id = 1, Name = "c", CreatedAt = now, UpdatedAt = now });
            ctx.Tags.Add(new TagEntity { Id = 1, Name = "tag", CreatedAt = now });
            ctx.Conversations.Add(new ConversationEntity { Id = 1, Title = "t", ModelId = "m", CreatedAt = now, UpdatedAt = now });
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 1,
                DocumentId = 1,
                StartOffset = 0,
                EndOffset = 5,
                HighlightedText = "hi",
                Color = "yellow",
                CreatedAt = now,
                UpdatedAt = now,
            });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 1, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now });
        });

        var result = await h.Service.ExportChangesAsync();

        result.Changes.Should().HaveCount(6);
        result.Changes.Select(c => c.EntityType).Should().BeEquivalentTo(new[]
        {
            nameof(DocumentEntity), nameof(CollectionEntity), nameof(TagEntity),
            nameof(ConversationEntity), nameof(AnnotationEntity), nameof(SystemPromptEntity),
        });
        result.Changes.Should().OnlyContain(c => c.ChangeType == SyncChangeType.Created);
        result.DeviceId.Should().NotBeNullOrWhiteSpace();

        h.Transport.Verify(t => t.EnsureFolderExists(@"C:\sync-test"), Times.Once);
        h.Transport.Verify(t => t.WriteSyncFileAsync(
            @"C:\sync-test", It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);

        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
        h.Service.Status.LastSyncAt.Should().NotBeNull();

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "export" && l.IsSuccess && l.ChangesApplied == 6);
    }

    [Fact]
    public async Task ExportChangesAsync_Incremental_OnlyCollectsChangesAfterSince()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        h.Seed(ctx =>
        {
            // Stale: last updated before the cutoff → excluded.
            ctx.SystemPrompts.Add(new SystemPromptEntity
            {
                Id = 1,
                Name = "old",
                Content = "c",
                Category = "General",
                CreatedAt = cutoff.AddMinutes(-60),
                UpdatedAt = cutoff.AddMinutes(-30),
            });
            // Created before cutoff but updated after → included as an Update.
            ctx.SystemPrompts.Add(new SystemPromptEntity
            {
                Id = 2,
                Name = "fresh",
                Content = "c",
                Category = "General",
                CreatedAt = cutoff.AddMinutes(-60),
                UpdatedAt = cutoff.AddMinutes(5),
            });
        });

        var result = await h.Service.ExportChangesAsync(cutoff);

        result.Changes.Should().ContainSingle();
        result.Changes[0].EntityId.Should().Be(2);
        result.Changes[0].ChangeType.Should().Be(SyncChangeType.Updated);
    }

    [Fact]
    public async Task ExportChangesAsync_Incremental_ClassifiesUpdatesAcrossEntityTypes()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var before = cutoff.AddMinutes(-30);
        var after = cutoff.AddMinutes(5);

        h.Seed(ctx =>
        {
            // Created before the cutoff but modified after → classified as Updated.
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 1,
                FileName = "f.txt",
                FilePath = @"C:\f.txt",
                FileType = "txt",
                ContentHash = "h",
                ImportedAt = after,
                FileModifiedAt = after,
                IndexingStatus = "completed",
            });
            ctx.Collections.Add(new CollectionEntity { Id = 1, Name = "c", CreatedAt = before, UpdatedAt = after });
            ctx.Conversations.Add(new ConversationEntity { Id = 1, Title = "t", ModelId = "m", CreatedAt = before, UpdatedAt = after });
            // Annotation parent — imported before the cutoff so it is itself excluded.
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 2,
                FileName = "parent.txt",
                FilePath = @"C:\parent.txt",
                FileType = "txt",
                ContentHash = "hp",
                ImportedAt = before,
                FileModifiedAt = before,
                IndexingStatus = "completed",
            });
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 1,
                DocumentId = 2,
                StartOffset = 0,
                EndOffset = 1,
                HighlightedText = "x",
                Color = "yellow",
                CreatedAt = before,
                UpdatedAt = after,
            });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 1, Name = "p", Content = "c", Category = "General", CreatedAt = before, UpdatedAt = after });
        });

        var result = await h.Service.ExportChangesAsync(cutoff);

        result.Changes.Should().Contain(c => c.EntityType == nameof(DocumentEntity) && c.EntityId == 1);
        result.Changes.Should().NotContain(c => c.EntityType == nameof(DocumentEntity) && c.EntityId == 2);
        result.Changes.Single(c => c.EntityType == nameof(DocumentEntity)).ChangeType.Should().Be(SyncChangeType.Updated);
        result.Changes.Single(c => c.EntityType == nameof(CollectionEntity)).ChangeType.Should().Be(SyncChangeType.Updated);
        result.Changes.Single(c => c.EntityType == nameof(ConversationEntity)).ChangeType.Should().Be(SyncChangeType.Updated);
        result.Changes.Single(c => c.EntityType == nameof(AnnotationEntity)).ChangeType.Should().Be(SyncChangeType.Updated);
        result.Changes.Single(c => c.EntityType == nameof(SystemPromptEntity)).ChangeType.Should().Be(SyncChangeType.Updated);
    }

    [Fact]
    public async Task ExportChangesAsync_SelectedCollectionsScope_FiltersToChosenCollections()
    {
        using var h = new SyncHarness();
        // "abc" is an unparseable token — it must be ignored, leaving only collection 10.
        await h.Service.ConfigureAsync(ValidConfig(scope: SyncScope.SelectedCollections, selectedIds: "10, abc"));
        h.SetupExportPipeline();

        var now = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            ctx.Collections.Add(new CollectionEntity { Id = 10, Name = "chosen", CreatedAt = now, UpdatedAt = now });
            ctx.Collections.Add(new CollectionEntity { Id = 20, Name = "other", CreatedAt = now, UpdatedAt = now });
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 100,
                FileName = "in.txt",
                FilePath = @"C:\in.txt",
                FileType = "txt",
                ContentHash = "h1",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            });
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 200,
                FileName = "out.txt",
                FilePath = @"C:\out.txt",
                FileType = "txt",
                ContentHash = "h2",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            });
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = 100, CollectionId = 10, AddedAt = now });
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = 200, CollectionId = 20, AddedAt = now });
        });

        var result = await h.Service.ExportChangesAsync();

        result.Changes.Should().HaveCount(2);
        result.Changes.Should().Contain(c => c.EntityType == nameof(DocumentEntity) && c.EntityId == 100);
        result.Changes.Should().Contain(c => c.EntityType == nameof(CollectionEntity) && c.EntityId == 10);
        result.Changes.Should().NotContain(c => c.EntityId == 200 || c.EntityId == 20);
    }

    [Fact]
    public async Task ExportChangesAsync_SelectedScopeWithEmptyIds_ExportsEverything()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(scope: SyncScope.SelectedCollections, selectedIds: null));
        h.SetupExportPipeline();

        var now = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            ctx.Collections.Add(new CollectionEntity { Id = 1, Name = "c", CreatedAt = now, UpdatedAt = now });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 1, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now });
        });

        var result = await h.Service.ExportChangesAsync();

        // Empty selection collapses to "All": no collection filter applied.
        result.Changes.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportChangesAsync_UsesStoredDeviceId_WhenPresent()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();
        h.Seed(ctx => ctx.UserSettings.Add(new UserSettingsEntity
        {
            Key = "SyncDeviceId",
            Value = "stable-device-42",
            ValueType = "json",
            UpdatedAt = DateTime.UtcNow,
        }));

        var result = await h.Service.ExportChangesAsync();

        result.DeviceId.Should().Be("stable-device-42");
    }

    [Fact]
    public async Task ExportChangesAsync_NoStoredDeviceId_GeneratesAndPersistsOne()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var result = await h.Service.ExportChangesAsync();

        result.DeviceId.Should().NotBeNullOrWhiteSpace();
        using var ctx = h.Fresh();
        var stored = await ctx.UserSettings.FirstOrDefaultAsync(s => s.Key == "SyncDeviceId");
        stored.Should().NotBeNull();
        stored!.Value.Should().Be(result.DeviceId);
    }

    [Fact]
    public async Task ExportChangesAsync_NotifiesStatusSubscribers()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var states = new List<SyncState>();
        h.Service.StatusChanged += s => states.Add(s.SyncState);

        await h.Service.ExportChangesAsync();

        states.Should().ContainInOrder(SyncState.Syncing, SyncState.Idle);
    }

    [Fact]
    public async Task ExportChangesAsync_StatusSubscriberThrows_ExportStillSucceeds()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        h.Service.StatusChanged += _ => throw new InvalidOperationException("subscriber boom");

        // The throwing subscriber must be swallowed inside SetStatus.
        var result = await h.Service.ExportChangesAsync();
        result.Should().NotBeNull();
        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task ExportChangesAsync_Cancelled_ThrowsAndLogsCancellation()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => h.Service.ExportChangesAsync(since: null, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Service.Status.ErrorMessage.Should().Be("Export was cancelled.");

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "export" && !l.IsSuccess);
    }

    [Fact]
    public async Task ExportChangesAsync_CodecThrows_SetsErrorStatusAndRethrows()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.Codec.Setup(c => c.Serialise(It.IsAny<SyncChangeSet>())).Throws(new InvalidOperationException("codec down"));

        Func<Task> act = () => h.Service.ExportChangesAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("codec down");
        h.Service.Status.SyncState.Should().Be(SyncState.Error);
        h.Service.Status.ErrorMessage.Should().Be("codec down");

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "export" && !l.IsSuccess);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ImportChangesAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportChangesAsync_NullChangeSet_Throws()
    {
        using var h = new SyncHarness();
        Func<Task> act = () => h.Service.ImportChangesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("changeSet");
    }

    [Fact]
    public async Task ImportChangesAsync_AppliesNonConflictingChanges()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(ChangeSet(PromptCreate(1, "imported")));

        applied.Should().Be(1);
        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
        h.Service.Status.LastSyncAt.Should().NotBeNull();

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(1L))!.Name.Should().Be("imported");

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && l.IsSuccess && l.ChangesApplied == 1);
    }

    [Fact]
    public async Task ImportChangesAsync_AppliesEveryUpsertEntityType()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;

        // Parent document for the annotation's required FK (must already exist on SaveChanges).
        h.Seed(ctx => ctx.Documents.Add(new DocumentEntity
        {
            Id = 800,
            FileName = "p.txt",
            FilePath = @"C:\p.txt",
            FileType = "txt",
            ContentHash = "ph",
            ImportedAt = now,
            FileModifiedAt = now,
            IndexingStatus = "completed",
        }));

        var doc = new DocumentEntity
        {
            Id = 802,
            FileName = "d.txt",
            FilePath = @"C:\d.txt",
            FileType = "txt",
            ContentHash = "dh",
            ImportedAt = now,
            FileModifiedAt = now,
            IndexingStatus = "completed",
        };
        var col = new CollectionEntity { Id = 803, Name = "col", CreatedAt = now, UpdatedAt = now };
        var tag = new TagEntity { Id = 804, Name = "tag-804", CreatedAt = now };
        var conv = new ConversationEntity { Id = 805, Title = "t", ModelId = "m", CreatedAt = now, UpdatedAt = now };
        var prompt = new SystemPromptEntity { Id = 806, Name = "sp", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now };
        var ann = new AnnotationEntity
        {
            Id = 801,
            DocumentId = 800,
            StartOffset = 0,
            EndOffset = 3,
            HighlightedText = "hi",
            Color = "yellow",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var changeSet = ChangeSet(
            Change(nameof(DocumentEntity), 802, SyncChangeType.Created, Json(doc)),
            Change(nameof(CollectionEntity), 803, SyncChangeType.Created, Json(col)),
            Change(nameof(TagEntity), 804, SyncChangeType.Created, Json(tag)),
            Change(nameof(ConversationEntity), 805, SyncChangeType.Created, Json(conv)),
            Change(nameof(SystemPromptEntity), 806, SyncChangeType.Created, Json(prompt)),
            Change(nameof(AnnotationEntity), 801, SyncChangeType.Created, Json(ann)));

        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(6);
        using var ctx = h.Fresh();
        (await ctx.Documents.FindAsync(802L)).Should().NotBeNull();
        (await ctx.Collections.FindAsync(803L)).Should().NotBeNull();
        (await ctx.Tags.FindAsync(804L)).Should().NotBeNull();
        (await ctx.Conversations.FindAsync(805L)).Should().NotBeNull();
        (await ctx.SystemPrompts.FindAsync(806L)).Should().NotBeNull();
        (await ctx.Annotations.FindAsync(801L)).Should().NotBeNull();
    }

    [Fact]
    public async Task ImportChangesAsync_ExistingEntity_IsUpdatedInPlace()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 5,
            Name = "original",
            Content = "old",
            Category = "General",
            CreatedAt = now,
            UpdatedAt = now,
        }));

        var updated = new SystemPromptEntity { Id = 5, Name = "updated", Content = "new", Category = "Writing", CreatedAt = now, UpdatedAt = now };
        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 5, SyncChangeType.Updated, Json(updated))));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        var row = await ctx.SystemPrompts.FindAsync(5L);
        row!.Name.Should().Be("updated");
        row.Content.Should().Be("new");
        row.Category.Should().Be("Writing");
        (await ctx.SystemPrompts.CountAsync()).Should().Be(1); // updated, not duplicated
    }

    [Fact]
    public async Task ImportChangesAsync_DeletedChange_RemovesExistingEntity()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 7,
            Name = "doomed",
            Content = "c",
            Category = "General",
            CreatedAt = now,
            UpdatedAt = now,
        }));

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 7, SyncChangeType.Deleted, null)));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(7L)).Should().BeNull();
    }

    [Fact]
    public async Task ImportChangesAsync_DeletesEveryEntityType()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 700,
                FileName = "parent.txt",
                FilePath = @"C:\parent.txt",
                FileType = "txt",
                ContentHash = "p",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            }); // annotation parent — kept
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 701,
                DocumentId = 700,
                StartOffset = 0,
                EndOffset = 2,
                HighlightedText = "x",
                Color = "yellow",
                CreatedAt = now,
                UpdatedAt = now,
            });
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 702,
                FileName = "doomed.txt",
                FilePath = @"C:\doomed.txt",
                FileType = "txt",
                ContentHash = "d",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            });
            ctx.Collections.Add(new CollectionEntity { Id = 703, Name = "c", CreatedAt = now, UpdatedAt = now });
            ctx.Tags.Add(new TagEntity { Id = 704, Name = "tag-del", CreatedAt = now });
            ctx.Conversations.Add(new ConversationEntity { Id = 705, Title = "t", ModelId = "m", CreatedAt = now, UpdatedAt = now });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 706, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now });
        });

        var changeSet = ChangeSet(
            Change(nameof(AnnotationEntity), 701, SyncChangeType.Deleted, null),
            Change(nameof(DocumentEntity), 702, SyncChangeType.Deleted, null),
            Change(nameof(CollectionEntity), 703, SyncChangeType.Deleted, null),
            Change(nameof(TagEntity), 704, SyncChangeType.Deleted, null),
            Change(nameof(ConversationEntity), 705, SyncChangeType.Deleted, null),
            Change(nameof(SystemPromptEntity), 706, SyncChangeType.Deleted, null));

        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(6);
        using var ctx = h.Fresh();
        (await ctx.Annotations.FindAsync(701L)).Should().BeNull();
        (await ctx.Documents.FindAsync(702L)).Should().BeNull();
        (await ctx.Collections.FindAsync(703L)).Should().BeNull();
        (await ctx.Tags.FindAsync(704L)).Should().BeNull();
        (await ctx.Conversations.FindAsync(705L)).Should().BeNull();
        (await ctx.SystemPrompts.FindAsync(706L)).Should().BeNull();
        (await ctx.Documents.FindAsync(700L)).Should().NotBeNull(); // parent untouched
    }

    [Fact]
    public async Task ImportChangesAsync_DeleteMissingEntity_IsNoOp()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 999, SyncChangeType.Deleted, null)));

        // The change is counted as processed; nothing exists to delete.
        applied.Should().Be(1);
    }

    [Fact]
    public async Task ImportChangesAsync_EmptySerializedData_SkipsApplication()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 1, SyncChangeType.Updated, "")));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_NullDeserializedPayload_SkipsApplication()
    {
        using var h = new SyncHarness();

        // "null" is non-whitespace, so it reaches the deserialiser and yields a null entity.
        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 1, SyncChangeType.Created, "null")));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_UnrecognizedUpsertEntityType_IsSkipped()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change("MysteryEntity", 1, SyncChangeType.Created, "{}")));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_UnrecognizedDeleteEntityType_IsSkipped()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change("MysteryEntity", 1, SyncChangeType.Deleted, null)));

        applied.Should().Be(1);
    }

    [Fact]
    public async Task ImportChangesAsync_PerChangeFailure_IsSwallowed_OthersApplied()
    {
        using var h = new SyncHarness();

        var changeSet = ChangeSet(
            Change(nameof(SystemPromptEntity), 1, SyncChangeType.Created, "{ malformed json"),
            PromptCreate(2, "valid"));

        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(1); // only the valid change counted
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(2L)).Should().NotBeNull();
        (await ctx.SystemPrompts.FindAsync(1L)).Should().BeNull();
    }

    [Fact]
    public async Task ImportChangesAsync_WithConflicts_SkipsConflictingAndSetsConflictStatus()
    {
        using var h = new SyncHarness();
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 1 };
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                It.IsAny<SyncChangeSet>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<Func<string, long, Task<DateTime?>>>()))
            .ReturnsAsync((IReadOnlyList<SyncConflict>)new List<SyncConflict> { conflict });

        var changeSet = ChangeSet(PromptCreate(1, "conflicted"), PromptCreate(2, "clean"));
        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(1); // id 1 skipped as a conflict; id 2 applied
        h.Service.Status.SyncState.Should().Be(SyncState.Conflict);
        h.Service.Status.PendingChanges.Should().Be(1);
        h.Service.Status.ErrorMessage.Should().Contain("conflict");

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(1L)).Should().BeNull();
        (await ctx.SystemPrompts.FindAsync(2L)).Should().NotBeNull();

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && l.ConflictsDetected == 1);
    }

    [Fact]
    public async Task ImportChangesAsync_Cancelled_ThrowsAndLogsCancellation()
    {
        using var h = new SyncHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => h.Service.ImportChangesAsync(ChangeSet(PromptCreate(1)), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Service.Status.ErrorMessage.Should().Be("Import was cancelled.");

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && !l.IsSuccess);
    }

    [Fact]
    public async Task ImportChangesAsync_ResolverThrows_SetsErrorStatusAndRethrows()
    {
        using var h = new SyncHarness();
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                It.IsAny<SyncChangeSet>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<Func<string, long, Task<DateTime?>>>()))
            .ThrowsAsync(new InvalidOperationException("resolver down"));

        Func<Task> act = () => h.Service.ImportChangesAsync(ChangeSet(PromptCreate(1)));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("resolver down");
        h.Service.Status.SyncState.Should().Be(SyncState.Error);
        h.Service.Status.ErrorMessage.Should().Be("resolver down");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DetectConflictsAsync (public)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DetectConflictsAsync_NullIncoming_Throws()
    {
        using var h = new SyncHarness();
        Func<Task> act = () => h.Service.DetectConflictsAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("incoming");
    }

    [Fact]
    public async Task DetectConflictsAsync_DelegatesToResolverWithLocalDeviceId()
    {
        using var h = new SyncHarness();
        var incoming = ChangeSet(PromptCreate(1));
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 1 };
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                incoming, It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<Func<string, long, Task<DateTime?>>>()))
            .ReturnsAsync((IReadOnlyList<SyncConflict>)new List<SyncConflict> { conflict });

        var result = await h.Service.DetectConflictsAsync(incoming);

        result.Should().ContainSingle().Which.Should().BeSameAs(conflict);
        h.Resolver.Verify(r => r.DetectConflictsAsync(
            incoming, It.IsAny<DateTime?>(),
            It.Is<string>(s => !string.IsNullOrWhiteSpace(s)),
            It.IsAny<Func<string, long, Task<DateTime?>>>()), Times.Once);
    }

    [Fact]
    public async Task DetectConflictsAsync_LocalTimestampCallback_ResolvesEveryEntityType()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        var docTime = now.AddMinutes(-1);
        var colTime = now.AddMinutes(-2);
        var tagTime = now.AddMinutes(-3);
        var convTime = now.AddMinutes(-4);
        var annTime = now.AddMinutes(-5);
        var promptTime = now.AddMinutes(-6);

        h.Seed(ctx =>
        {
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 1,
                FileName = "f",
                FilePath = @"C:\f",
                FileType = "txt",
                ContentHash = "h",
                ImportedAt = docTime,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            });
            ctx.Collections.Add(new CollectionEntity { Id = 1, Name = "c", CreatedAt = now, UpdatedAt = colTime });
            ctx.Tags.Add(new TagEntity { Id = 1, Name = "t", CreatedAt = tagTime });
            ctx.Conversations.Add(new ConversationEntity { Id = 1, Title = "t", ModelId = "m", CreatedAt = now, UpdatedAt = convTime });
            ctx.Documents.Add(new DocumentEntity
            {
                Id = 2,
                FileName = "p",
                FilePath = @"C:\p",
                FileType = "txt",
                ContentHash = "h2",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            });
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 1,
                DocumentId = 2,
                StartOffset = 0,
                EndOffset = 1,
                HighlightedText = "x",
                Color = "yellow",
                CreatedAt = now,
                UpdatedAt = annTime,
            });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 1, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = promptTime });
        });

        // Drive the real local-timestamp resolver callback the service passes to the resolver.
        var captured = new Dictionary<string, DateTime?>();
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                It.IsAny<SyncChangeSet>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<Func<string, long, Task<DateTime?>>>()))
            .Returns(async (SyncChangeSet _, DateTime? __, string ___, Func<string, long, Task<DateTime?>> getLocal) =>
            {
                captured[nameof(DocumentEntity)] = await getLocal(nameof(DocumentEntity), 1);
                captured[nameof(CollectionEntity)] = await getLocal(nameof(CollectionEntity), 1);
                captured[nameof(TagEntity)] = await getLocal(nameof(TagEntity), 1);
                captured[nameof(ConversationEntity)] = await getLocal(nameof(ConversationEntity), 1);
                captured[nameof(AnnotationEntity)] = await getLocal(nameof(AnnotationEntity), 1);
                captured[nameof(SystemPromptEntity)] = await getLocal(nameof(SystemPromptEntity), 1);
                captured["Unknown"] = await getLocal("MysteryEntity", 1);
                return (IReadOnlyList<SyncConflict>)Array.Empty<SyncConflict>();
            });

        await h.Service.DetectConflictsAsync(ChangeSet(PromptCreate(1)));

        captured[nameof(DocumentEntity)].Should().BeCloseTo(docTime, TimeSpan.FromSeconds(1));
        captured[nameof(CollectionEntity)].Should().BeCloseTo(colTime, TimeSpan.FromSeconds(1));
        captured[nameof(TagEntity)].Should().BeCloseTo(tagTime, TimeSpan.FromSeconds(1));
        captured[nameof(ConversationEntity)].Should().BeCloseTo(convTime, TimeSpan.FromSeconds(1));
        captured[nameof(AnnotationEntity)].Should().BeCloseTo(annTime, TimeSpan.FromSeconds(1));
        captured[nameof(SystemPromptEntity)].Should().BeCloseTo(promptTime, TimeSpan.FromSeconds(1));
        captured["Unknown"].Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ResolveConflictAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveConflictAsync_NullConflict_Throws()
    {
        using var h = new SyncHarness();
        Func<Task> act = () => h.Service.ResolveConflictAsync(null!, SyncResolution.KeepLocal);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("conflict");
    }

    [Fact]
    public async Task ResolveConflictAsync_PendingResolution_Throws()
    {
        using var h = new SyncHarness();
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 1 };
        Func<Task> act = () => h.Service.ResolveConflictAsync(conflict, SyncResolution.Pending);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resolution");
    }

    [Fact]
    public async Task ResolveConflictAsync_ResolverReturnsChange_AppliesIt()
    {
        using var h = new SyncHarness();
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 11 };
        h.Resolver.Setup(r => r.ResolveConflict(conflict, SyncResolution.KeepRemote))
                  .Returns(PromptCreate(11, "resolved"));

        await h.Service.ResolveConflictAsync(conflict, SyncResolution.KeepRemote);

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(11L))!.Name.Should().Be("resolved");
    }

    [Fact]
    public async Task ResolveConflictAsync_ResolverReturnsNull_AppliesNothing()
    {
        using var h = new SyncHarness();
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 12 };
        h.Resolver.Setup(r => r.ResolveConflict(conflict, SyncResolution.KeepLocal))
                  .Returns((SyncChange?)null);

        await h.Service.ResolveConflictAsync(conflict, SyncResolution.KeepLocal);

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResolveConflictAsync_ClearsConflictStateWhenLastConflictResolved()
    {
        using var h = new SyncHarness();

        // Drive an import that yields exactly one conflict → Conflict state, 1 pending.
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 20 };
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                It.IsAny<SyncChangeSet>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<Func<string, long, Task<DateTime?>>>()))
            .ReturnsAsync((IReadOnlyList<SyncConflict>)new List<SyncConflict> { conflict });

        await h.Service.ImportChangesAsync(ChangeSet(PromptCreate(20, "conflicted")));
        h.Service.Status.SyncState.Should().Be(SyncState.Conflict);
        h.Service.Status.PendingChanges.Should().Be(1);

        // Resolving the last conflict returns the row to Idle.
        h.Resolver.Setup(r => r.ResolveConflict(conflict, SyncResolution.KeepRemote))
                  .Returns(PromptCreate(20, "resolved"));

        await h.Service.ResolveConflictAsync(conflict, SyncResolution.KeepRemote);

        h.Service.Status.PendingChanges.Should().Be(0);
        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
        h.Service.Status.ErrorMessage.Should().BeNull();

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(20L))!.Name.Should().Be("resolved");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GetSyncHistoryAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSyncHistoryAsync_NoHistory_ReturnsEmpty()
    {
        using var h = new SyncHarness();
        (await h.Service.GetSyncHistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetSyncHistoryAsync_ReturnsNewestFirst_RespectingLimit()
    {
        using var h = new SyncHarness();
        var baseTime = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            for (var i = 0; i < 5; i++)
            {
                ctx.SyncLogs.Add(new SyncLogEntity
                {
                    SyncedAt = baseTime.AddMinutes(i),
                    Direction = "export",
                    DurationMs = 1,
                    IsSuccess = true,
                });
            }
        });

        var history = await h.Service.GetSyncHistoryAsync(limit: 3);

        history.Should().HaveCount(3);
        history.Should().BeInDescendingOrder(l => l.SyncedAt);
        history[0].SyncedAt.Should().BeCloseTo(baseTime.AddMinutes(4), TimeSpan.FromSeconds(1));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Auto-sync lifecycle
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StartAutoSyncAsync_NoConfiguration_DoesNotStart()
    {
        using var h = new SyncHarness();

        await h.Service.StartAutoSyncAsync();        // returns without starting a loop
        await h.Service.StopAutoSyncAsync();         // safe no-op afterwards

        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task StartAutoSyncAsync_AutoSyncDisabled_DoesNotStart()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(autoSync: false));

        await h.Service.StartAutoSyncAsync();
        await h.Service.StopAutoSyncAsync();

        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task StartAutoSyncAsync_Enabled_StartsThenStopsCleanly()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(autoSync: true, interval: 0)); // clamps to 1 min

        await h.Service.StartAutoSyncAsync();   // launches the background loop (won't tick in-test)
        await h.Service.StartAutoSyncAsync();   // idempotent: replaces the running loop
        await h.Service.StopAutoSyncAsync();    // cancels + disposes the CTS

        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task StopAutoSyncAsync_NeverStarted_IsNoOp()
    {
        using var h = new SyncHarness();
        await h.Service.StopAutoSyncAsync(); // _autoSyncCts is null → early return
    }

    [Fact]
    public async Task RunAutoSyncLoop_AlreadyCancelledToken_ExitsCleanly()
    {
        using var h = new SyncHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Invoking the loop with a cancelled token exercises the timer setup + the
        // OperationCanceledException exit path without waiting for a real tick.
        await InvokePrivateAsync(h.Service, "RunAutoSyncLoopAsync", 1, cts.Token);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ImportPeerFilesAsync (timer-gated private — reached via reflection)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportPeerFiles_ValidFile_DecryptsImportsAndMarksImported()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\peer.axs", FileName = "peer", Data = new byte[] { 9 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(true);
        h.Codec.Setup(c => c.Decrypt(payload.Data, It.IsAny<string>())).Returns(new byte[] { 10 });
        h.Codec.Setup(c => c.Deserialise(It.IsAny<byte[]>())).Returns(ChangeSet(PromptCreate(321, "from-peer")));
        h.Transport.Setup(t => t.MarkFileImportedAsync(payload.FilePath)).Returns(Task.CompletedTask);

        await InvokePrivateAsync(h.Service, "ImportPeerFilesAsync", CancellationToken.None);

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.FindAsync(321L))!.Name.Should().Be("from-peer");
        h.Codec.Verify(c => c.Decrypt(payload.Data, It.IsAny<string>()), Times.Once);
        h.Transport.Verify(t => t.MarkFileImportedAsync(payload.FilePath), Times.Once);
    }

    [Fact]
    public async Task ImportPeerFiles_InvalidHeader_SkipsWithoutDecrypting()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\bad.axs", FileName = "bad", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(false);

        await InvokePrivateAsync(h.Service, "ImportPeerFilesAsync", CancellationToken.None);

        h.Codec.Verify(c => c.Decrypt(It.IsAny<byte[]>(), It.IsAny<string>()), Times.Never);
        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportPeerFiles_DecryptCryptographicException_SkipsGracefully()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\enc.axs", FileName = "enc", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(true);
        h.Codec.Setup(c => c.Decrypt(payload.Data, It.IsAny<string>())).Throws(new CryptographicException("wrong passphrase"));

        // Must not throw — a bad passphrase is logged and the file left in place.
        await InvokePrivateAsync(h.Service, "ImportPeerFilesAsync", CancellationToken.None);

        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportPeerFiles_DecryptGenericException_SkipsGracefully()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\err.axs", FileName = "err", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(true);
        h.Codec.Setup(c => c.Decrypt(payload.Data, It.IsAny<string>())).Throws(new InvalidOperationException("boom"));

        await InvokePrivateAsync(h.Service, "ImportPeerFilesAsync", CancellationToken.None);

        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportPeerFiles_Cancelled_Rethrows()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\cancel.axs", FileName = "cancel", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The per-file cancellation check rethrows out of the peer-import loop.
        Func<Task> act = () => InvokePrivateAsync(h.Service, "ImportPeerFilesAsync", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

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
/// Unit tests for <see cref="SyncService"/>, the thin orchestrator that composes
/// <see cref="ISyncTransport"/>, <see cref="ISyncPackageCodec"/> and
/// <see cref="ISyncConflictResolver"/> over a real <see cref="AgentXDbContext"/>
/// (AX-QA-009 coverage uplift, the last of the four 0%-covered Core services).
///
/// Transport and codec are mocked (they own their own test suites under
/// Services/Sync/{Codec,Transport}); the conflict resolver is the real last-writer-wins
/// implementation unless a test asks for a mock, because the decision is part of what an
/// import must get right. These tests drive the orchestration: configuration persistence,
/// change collection and scope, natural-key matching (including two independent
/// installations whose numeric ids collide), per-change failure isolation, audit logging,
/// persisted watermarks, status transitions, peer-file passes and the auto-sync lifecycle,
/// against an in-memory SQLite database via <see cref="TestDbContextFactory"/>.
///
/// Peer files are imported through the public <see cref="SyncService.ImportNowAsync"/> and
/// <see cref="SyncService.SyncNowAsync"/>; only the timer-driven loop body is still reached by
/// reflection, because its first tick is at least a minute away.
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

    // ---- Harness ----

    private sealed class SyncHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; }
        public AgentXDbContext Db { get; }
        public Serilog.ILogger Logger { get; }
        public Mock<ISyncTransport> Transport { get; }
        public Mock<ISyncPackageCodec> Codec { get; }
        public Mock<ISyncConflictResolver> Resolver { get; }
        public SyncService Service { get; }

        /// <param name="mockResolver">
        /// False (the default) wires the real last-writer-wins <see cref="SyncConflictResolver"/>,
        /// because the decision is part of what an import must get right. True wires
        /// <see cref="Resolver"/> for tests that stub or verify the resolver.
        /// </param>
        public SyncHarness(bool mockResolver = false)
        {
            Factory = new TestDbContextFactory();
            Db = Factory.CreateContext();
            Logger = new LoggerConfiguration().CreateLogger();
            Transport = new Mock<ISyncTransport>();
            Codec = new Mock<ISyncPackageCodec>();
            Resolver = new Mock<ISyncConflictResolver>();

            ISyncConflictResolver resolver = mockResolver ? Resolver.Object : new SyncConflictResolver(Logger);
            Service = new SyncService(Db, Logger, Transport.Object, Codec.Object, resolver);
        }

        /// <summary>
        /// Wires the export pipeline and records every change set handed to the codec, and makes
        /// the peer scan return nothing unless a test sets up a peer file.
        /// </summary>
        public List<SyncChangeSet> CaptureExports()
        {
            var exported = new List<SyncChangeSet>();
            Codec.Setup(c => c.Serialise(It.IsAny<SyncChangeSet>()))
                 .Callback((SyncChangeSet cs) => exported.Add(cs))
                 .Returns(new byte[] { 1, 2, 3 });
            Codec.Setup(c => c.Encrypt(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(new byte[] { 4, 5, 6 });
            Transport
                .Setup(t => t.WriteSyncFileAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                    It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(@"C:\sync-test\agentx-sync-x.axs");
            Transport
                .Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<SyncFilePayload>)Array.Empty<SyncFilePayload>());
            return exported;
        }

        /// <summary>Makes the peer scan return one readable file that decodes to <paramref name="changeSet"/>.</summary>
        public SyncFilePayload SetupPeerFile(string name, SyncChangeSet changeSet)
        {
            var payload = new SyncFilePayload { FilePath = $@"C:\sync-test\{name}.axs", FileName = name, Data = new byte[] { 9 } };
            Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
            Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(true);
            Codec.Setup(c => c.Decrypt(payload.Data, It.IsAny<string>())).Returns(new byte[] { 10 });
            Codec.Setup(c => c.Deserialise(It.IsAny<byte[]>())).Returns(changeSet);
            Transport.Setup(t => t.MarkFileImportedAsync(payload.FilePath)).Returns(Task.CompletedTask);
            return payload;
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

    // ---- Configuration / change builders ----

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

    private static DocumentEntity Doc(long id, string hash, string? fileName = null, string? filePath = null)
    {
        var now = DateTime.UtcNow;
        var name = fileName ?? $"{hash}.txt";
        return new DocumentEntity
        {
            Id = id,
            FileName = name,
            FilePath = filePath ?? $"/sync-test-docs/{name}",
            FileType = "txt",
            ContentHash = hash,
            ImportedAt = now,
            FileModifiedAt = now,
            IndexingStatus = "pending",
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
    /// Reflection bridge for the timer-gated private auto-sync loop. There is no public path
    /// to <c>RunAutoSyncLoopAsync</c> short of a one-minute wait, so it is invoked directly
    /// to cover its cancellation exit.
    /// </summary>
    private static Task InvokePrivateAsync(SyncService service, string method, params object[] args)
    {
        var mi = typeof(SyncService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(SyncService), method);
        return (Task)mi.Invoke(service, args)!;
    }

    // ---- Constructor null-guards ----

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

    // ---- ConfigureAsync / GetConfigurationAsync ----

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

        // The upsert must update in place, exactly one settings row for the config key.
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

    // ---- ExportChangesAsync, required-configuration guards ----

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

    // ---- ExportChangesAsync, collection + happy path ----

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
    public async Task ExportChangesAsync_CarriesNoMessagesOrSettings_AsTheSyncPageSays()
    {
        // The Sync page promised to synchronize "settings" when nothing exports them, and chat
        // messages and document files do not travel either. The hint now says what an export
        // carries, and this pins that to the export.
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var now = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(new ConversationEntity { Id = 1, Title = "t", ModelId = "m", CreatedAt = now, UpdatedAt = now });
            ctx.Messages.Add(new MessageEntity { Id = 1, ConversationId = 1, Role = "user", Content = "private question", Timestamp = now });
            ctx.UserSettings.Add(new UserSettingsEntity { Id = 100, Key = "Theme", Value = "Light", UpdatedAt = now });
        });

        var result = await h.Service.ExportChangesAsync();

        result.Changes.Select(c => c.EntityType).Should().Equal(nameof(ConversationEntity));
        result.Changes.Single().SerializedData.Should().NotContain("private question");

        var hint = ReswLocalization.For("en-US").GetString("Sync_NotConfiguredHint.Text");
        hint.Should().Contain("document list, collections, tags, annotations, conversations and system prompts")
            .And.EndWith("Document files, chat messages and settings are not synced.");
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
            // Stale: last updated before the cutoff -> excluded.
            ctx.SystemPrompts.Add(new SystemPromptEntity
            {
                Id = 1,
                Name = "old",
                Content = "c",
                Category = "General",
                CreatedAt = cutoff.AddMinutes(-60),
                UpdatedAt = cutoff.AddMinutes(-30),
            });
            // Created before cutoff but updated after -> included as an Update.
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
            // Created before the cutoff but modified after -> classified as Updated.
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
            // Annotation parent, imported before the cutoff so it is itself excluded.
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
        // "abc" is an unparseable token, it must be ignored, leaving only collection 10.
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

    // ---- ExportChangesAsync: natural keys and scope ----

    [Fact]
    public async Task ExportChangesAsync_WritesNaturalKeysAndReferences()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupExportPipeline();

        var created = new DateTime(2026, 2, 1, 9, 30, 0, 123, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Collections.Add(new CollectionEntity { Id = 1, Name = "Work", CreatedAt = created, UpdatedAt = created });
            ctx.Collections.Add(new CollectionEntity { Id = 2, Name = "Archive", ParentCollectionId = 1, CreatedAt = created, UpdatedAt = created });
            ctx.Documents.Add(Doc(5, "hash-5"));
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 9,
                DocumentId = 5,
                StartOffset = 3,
                EndOffset = 8,
                HighlightedText = "hello",
                Color = "yellow",
                CreatedAt = created,
                UpdatedAt = created,
            });
            ctx.Conversations.Add(new ConversationEntity { Id = 1, Title = "Root", ModelId = "m", CreatedAt = created, UpdatedAt = created });
            ctx.Conversations.Add(new ConversationEntity { Id = 2, Title = "Branch", ModelId = "m", ParentConversationId = 1, CreatedAt = created.AddMinutes(1), UpdatedAt = created.AddMinutes(1) });
        });

        var result = await h.Service.ExportChangesAsync();

        result.Version.Should().Be(SyncChangeSet.CurrentVersion);
        result.Changes.Single(c => c.EntityType == nameof(DocumentEntity)).NaturalKey.Should().Be("sha:hash-5");
        result.Changes.Single(c => c.EntityType == nameof(CollectionEntity) && c.EntityId == 2)
            .NaturalKey.Should().Be("Work" + SyncNaturalKeys.Separator + "Archive");

        var annotation = result.Changes.Single(c => c.EntityType == nameof(AnnotationEntity));
        annotation.References!["DocumentId"].Should().Be("sha:hash-5");
        annotation.NaturalKey.Should().Be(SyncNaturalKeys.ForAnnotation("sha:hash-5", 3, 8, "hello"));

        var branch = result.Changes.Single(c => c.EntityType == nameof(ConversationEntity) && c.EntityId == 2);
        branch.References!["ParentConversationId"].Should().Be(SyncNaturalKeys.ForConversation(created, "Root"));
    }

    [Fact]
    public async Task ExportChangesAsync_SelectedScope_ExportsOnlyTheChosenCollectionsContent()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(scope: SyncScope.SelectedCollections, selectedIds: "2"));
        h.SetupExportPipeline();

        var now = DateTime.UtcNow;
        h.Seed(ctx =>
        {
            ctx.Collections.Add(new CollectionEntity { Id = 1, Name = "Parent", CreatedAt = now, UpdatedAt = now });
            ctx.Collections.Add(new CollectionEntity { Id = 2, Name = "Chosen", ParentCollectionId = 1, CreatedAt = now, UpdatedAt = now });
            ctx.Collections.Add(new CollectionEntity { Id = 3, Name = "Other", CreatedAt = now, UpdatedAt = now });
            ctx.Documents.Add(Doc(10, "in"));
            ctx.Documents.Add(Doc(20, "out"));
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = 10, CollectionId = 2, AddedAt = now });
            ctx.DocumentCollections.Add(new DocumentCollectionEntity { DocumentId = 20, CollectionId = 3, AddedAt = now });
            ctx.Annotations.Add(new AnnotationEntity { Id = 1, DocumentId = 10, EndOffset = 1, HighlightedText = "in", Color = "yellow", CreatedAt = now, UpdatedAt = now });
            ctx.Annotations.Add(new AnnotationEntity { Id = 2, DocumentId = 20, EndOffset = 1, HighlightedText = "secret", Color = "yellow", CreatedAt = now, UpdatedAt = now });
            ctx.Tags.Add(new TagEntity { Id = 1, Name = "tag", CreatedAt = now });
            ctx.Conversations.Add(new ConversationEntity { Id = 1, Title = "private chat", ModelId = "m", CreatedAt = now, UpdatedAt = now });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 1, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now });
        });

        var result = await h.Service.ExportChangesAsync();

        result.Changes.Select(c => (c.EntityType, c.EntityId)).Should().BeEquivalentTo(new[]
        {
            (nameof(CollectionEntity), 1L), // ancestor, so the hierarchy can be rebuilt
            (nameof(CollectionEntity), 2L),
            (nameof(DocumentEntity), 10L),
            (nameof(AnnotationEntity), 1L),
        });
        result.Changes.Should().NotContain(c => c.SerializedData!.Contains("secret"),
            "an annotation on a document outside the chosen collections must not leave the machine");
    }

    // ---- ImportChangesAsync ----

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
        (await ctx.SystemPrompts.SingleAsync()).Name.Should().Be("imported");

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && l.IsSuccess && l.ChangesApplied == 1);
    }

    [Fact]
    public async Task ImportChangesAsync_AppliesEveryUpsertEntityType()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;

        // The annotation's document exists locally under a DIFFERENT id than on the sender.
        h.Seed(ctx => ctx.Documents.Add(Doc(800, "ph")));

        var doc = Doc(802, "dh");
        var col = new CollectionEntity { Id = 803, Name = "col", CreatedAt = now, UpdatedAt = now };
        var tag = new TagEntity { Id = 804, Name = "tag-804", CreatedAt = now };
        var conv = new ConversationEntity { Id = 805, Title = "t", ModelId = "m", CreatedAt = now, UpdatedAt = now };
        var prompt = new SystemPromptEntity { Id = 806, Name = "sp", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now };
        var ann = new AnnotationEntity
        {
            Id = 801,
            DocumentId = 4242, // the sender's id for the document; meaningless here
            StartOffset = 0,
            EndOffset = 3,
            HighlightedText = "hi",
            Color = "yellow",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var annotationChange = Change(nameof(AnnotationEntity), 801, SyncChangeType.Created, Json(ann));
        annotationChange.References = new Dictionary<string, string> { ["DocumentId"] = "sha:ph" };

        var changeSet = ChangeSet(
            Change(nameof(DocumentEntity), 802, SyncChangeType.Created, Json(doc)),
            Change(nameof(CollectionEntity), 803, SyncChangeType.Created, Json(col)),
            Change(nameof(TagEntity), 804, SyncChangeType.Created, Json(tag)),
            Change(nameof(ConversationEntity), 805, SyncChangeType.Created, Json(conv)),
            Change(nameof(SystemPromptEntity), 806, SyncChangeType.Created, Json(prompt)),
            annotationChange);

        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(6);
        using var ctx = h.Fresh();
        (await ctx.Documents.CountAsync(d => d.ContentHash == "dh")).Should().Be(1);
        (await ctx.Collections.CountAsync(c => c.Name == "col")).Should().Be(1);
        (await ctx.Tags.CountAsync(t => t.Name == "tag-804")).Should().Be(1);
        (await ctx.Conversations.CountAsync(c => c.Title == "t")).Should().Be(1);
        (await ctx.SystemPrompts.CountAsync(p => p.Name == "sp")).Should().Be(1);
        (await ctx.Annotations.SingleAsync()).DocumentId.Should().Be(800, "the reference is translated to the local document");
    }

    [Fact]
    public async Task ImportChangesAsync_ExistingEntityWithSameNaturalKey_IsUpdatedInPlace()
    {
        using var h = new SyncHarness();
        var old = DateTime.UtcNow.AddHours(-1);
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 5,
            Name = "shared",
            Content = "old",
            Category = "General",
            CreatedAt = old,
            UpdatedAt = old,
        }));

        // Different sender id, same name, newer edit.
        var newer = DateTime.UtcNow;
        var updated = new SystemPromptEntity { Id = 77, Name = "shared", Content = "new", Category = "Writing", CreatedAt = old, UpdatedAt = newer };
        var change = Change(nameof(SystemPromptEntity), 77, SyncChangeType.Updated, Json(updated));
        change.Timestamp = newer;

        var applied = await h.Service.ImportChangesAsync(ChangeSet(change));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        var row = await ctx.SystemPrompts.SingleAsync();
        row.Id.Should().Be(5, "the local row keeps its own id");
        row.Content.Should().Be("new");
        row.Category.Should().Be("Writing");
    }

    [Fact]
    public async Task ImportChangesAsync_SameRemoteIdAsUnrelatedLocalRow_InsertsInsteadOfOverwriting()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 5,
            Name = "local only",
            Content = "mine",
            Category = "General",
            CreatedAt = now,
            UpdatedAt = now,
        }));

        // The sender's prompt 5 is a different prompt that happens to share the id.
        var remote = new SystemPromptEntity { Id = 5, Name = "remote only", Content = "theirs", Category = "General", CreatedAt = now, UpdatedAt = now.AddMinutes(1) };
        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 5, SyncChangeType.Updated, Json(remote))));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        var rows = await ctx.SystemPrompts.OrderBy(p => p.Id).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(new { Id = 5L, Name = "local only", Content = "mine" }, o => o.ExcludingMissingMembers());
        rows[1].Name.Should().Be("remote only");
        rows[1].Id.Should().NotBe(5);
    }

    [Fact]
    public async Task ImportChangesAsync_BetweenTwoIndependentInstalls_NeverMatchesRowsByNumericId()
    {
        using var a = new SyncHarness();
        using var b = new SyncHarness();
        await a.Service.ConfigureAsync(ValidConfig());
        a.SetupExportPipeline();

        var aTime = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);
        var bTime = new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc);
        a.Seed(ctx =>
        {
            ctx.Conversations.Add(new ConversationEntity { Id = 12, Title = "Budget", ModelId = "m", CreatedAt = aTime, UpdatedAt = aTime });
            ctx.Documents.Add(Doc(5, "a-five", fileName: "a.pdf"));
        });
        b.Seed(ctx =>
        {
            ctx.Conversations.Add(new ConversationEntity { Id = 12, Title = "Recipes", ModelId = "m", CreatedAt = bTime, UpdatedAt = bTime, MessageCount = 3 });
            ctx.Documents.Add(Doc(5, "b-five", fileName: "b.pdf"));
        });

        var fromA = await a.Service.ExportChangesAsync();
        await b.Service.ImportChangesAsync(fromA);

        using var ctx = b.Fresh();
        var conversation12 = await ctx.Conversations.SingleAsync(c => c.Id == 12);
        conversation12.Title.Should().Be("Recipes", "A's conversation 12 is a different conversation");
        conversation12.MessageCount.Should().Be(3);
        (await ctx.Documents.SingleAsync(d => d.Id == 5)).ContentHash.Should().Be("b-five");

        (await ctx.Conversations.CountAsync()).Should().Be(2);
        (await ctx.Conversations.SingleAsync(c => c.Title == "Budget")).Id.Should().NotBe(12);
        (await ctx.Documents.CountAsync()).Should().Be(2);
        (await ctx.Documents.SingleAsync(d => d.ContentHash == "a-five")).Id.Should().NotBe(5);
    }

    [Fact]
    public async Task ImportChangesAsync_ConcurrentEditsOnTwoInstalls_ConvergeOnTheNewerVersion()
    {
        using var a = new SyncHarness();
        using var b = new SyncHarness();
        await a.Service.ConfigureAsync(ValidConfig());
        await b.Service.ConfigureAsync(ValidConfig());
        a.SetupExportPipeline();
        b.SetupExportPipeline();

        var created = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        var aEdit = created.AddHours(1);
        var bEdit = created.AddHours(2);
        a.Seed(ctx => ctx.Collections.Add(new CollectionEntity { Id = 3, Name = "Shared", Description = "Alpha", CreatedAt = created, UpdatedAt = aEdit }));
        b.Seed(ctx => ctx.Collections.Add(new CollectionEntity { Id = 8, Name = "Shared", Description = "Beta", CreatedAt = created, UpdatedAt = bEdit }));

        // One cycle each way. Before the fix the older edit overwrote the newer one on the
        // first import, so the two machines swapped values permanently.
        await b.Service.ImportChangesAsync(await a.Service.ExportChangesAsync());
        await a.Service.ImportChangesAsync(await b.Service.ExportChangesAsync());

        using (var ctx = a.Fresh())
            (await ctx.Collections.SingleAsync()).Description.Should().Be("Beta");
        using (var ctx = b.Fresh())
            (await ctx.Collections.SingleAsync()).Description.Should().Be("Beta");

        (await b.Service.GetSyncHistoryAsync()).Should().Contain(l => l.Direction == "import" && l.ConflictsDetected == 1 && l.ConflictsResolved == 1);
    }

    [Fact]
    public async Task ImportChangesAsync_OlderRemoteChange_KeepsNewerLocalCopy_EvenOnAFreshInstance()
    {
        // No LastSyncAt baseline exists on a fresh instance (as after a restart); the decision
        // is made from the two modification timestamps alone.
        using var h = new SyncHarness();
        var localEdit = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 1,
            Name = "p1",
            Content = "local edit",
            Category = "General",
            CreatedAt = localEdit.AddDays(-1),
            UpdatedAt = localEdit,
        }));

        var stale = new SystemPromptEntity { Id = 1, Name = "p1", Content = "stale remote", Category = "General", CreatedAt = localEdit.AddDays(-1), UpdatedAt = localEdit.AddMinutes(-10) };
        var staleChange = Change(nameof(SystemPromptEntity), 1, SyncChangeType.Updated, Json(stale));
        staleChange.Timestamp = stale.UpdatedAt;

        var applied = await h.Service.ImportChangesAsync(ChangeSet(staleChange, PromptCreate(2, "clean")));

        applied.Should().Be(1, "only the new prompt is applied");
        h.Service.Status.SyncState.Should().Be(SyncState.Idle, "last writer wins resolves the conflict automatically");

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.SingleAsync(p => p.Name == "p1")).Content.Should().Be("local edit");
        (await ctx.SystemPrompts.CountAsync(p => p.Name == "clean")).Should().Be(1);

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && l.ConflictsDetected == 1 && l.IsSuccess);
    }

    [Fact]
    public async Task ImportChangesAsync_TagWithExistingName_IsMatchedInsteadOfViolatingTheUniqueIndex()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.Tags.Add(new TagEntity { Id = 3, Name = "finance", CreatedAt = now }));

        var remote = new TagEntity { Id = 90, Name = "finance", ColorHex = "#FFB000", CreatedAt = now.AddDays(-2) };
        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(TagEntity), 90, SyncChangeType.Created, Json(remote))));

        applied.Should().Be(1, "the missing colour is merged into the local tag");
        using var ctx = h.Fresh();
        var tag = await ctx.Tags.SingleAsync();
        tag.Id.Should().Be(3);
        tag.ColorHex.Should().Be("#FFB000");
    }

    [Fact]
    public async Task ImportChangesAsync_FailedSave_IsRolledBack_AndDoesNotPoisonLaterSaves()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.Tags.Add(new TagEntity { Id = 1, Name = "finance", CreatedAt = now }));

        // A conversation with a null title violates the NOT NULL constraint when saved.
        var broken = Change(
            nameof(ConversationEntity), 9, SyncChangeType.Created,
            "{\"title\":null,\"modelId\":\"m\",\"createdAt\":\"2026-01-01T00:00:00\",\"updatedAt\":\"2026-01-01T00:00:00\"}");
        var tag = Change(nameof(TagEntity), 2, SyncChangeType.Created, Json(new TagEntity { Id = 2, Name = "finance", CreatedAt = now }));

        var applied = await h.Service.ImportChangesAsync(ChangeSet(tag, broken, PromptCreate(3, "after the failure")));

        applied.Should().Be(1, "the prompt after the failed change is still applied");
        h.Service.Status.SyncState.Should().Be(SyncState.Error);
        h.Service.Status.PendingChanges.Should().Be(1);
        h.Service.Status.ErrorMessage.Should().Contain("will be retried");

        h.Db.ChangeTracker.Entries().Should().OnlyContain(e => e.State == EntityState.Unchanged,
            "the failed insert must not stay tracked on the shared context");

        // An unrelated save through the same shared context still works.
        h.Db.Tags.Add(new TagEntity { Name = "later", CreatedAt = now });
        await h.Db.SaveChangesAsync();

        using var ctx = h.Fresh();
        (await ctx.Tags.CountAsync()).Should().Be(2);
        (await ctx.SystemPrompts.CountAsync(p => p.Name == "after the failure")).Should().Be(1);
        (await ctx.Conversations.CountAsync()).Should().Be(0);

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && !l.IsSuccess && l.ErrorMessage!.Contains("retried"));
    }

    [Fact]
    public async Task ImportChangesAsync_Document_IsInsertedNotIndexed_PendingOnlyWhenTheFileExistsHere()
    {
        using var h = new SyncHarness();
        var existingFile = Path.Combine(Path.GetTempPath(), $"agentx-sync-doc-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(existingFile, "content");

        try
        {
            var present = Doc(1, "present", filePath: existingFile);
            present.IndexingStatus = "completed";
            present.ChunkCount = 12;
            present.LastIndexedAt = DateTime.UtcNow;
            var missing = Doc(2, "missing", filePath: Path.Combine(Path.GetTempPath(), $"not-here-{Guid.NewGuid():N}.pdf"));
            missing.IndexingStatus = "completed";
            missing.ChunkCount = 7;

            var applied = await h.Service.ImportChangesAsync(ChangeSet(
                Change(nameof(DocumentEntity), 1, SyncChangeType.Created, Json(present)),
                Change(nameof(DocumentEntity), 2, SyncChangeType.Created, Json(missing))));

            applied.Should().Be(2);
            using var ctx = h.Fresh();
            var presentRow = await ctx.Documents.SingleAsync(d => d.ContentHash == "present");
            presentRow.IndexingStatus.Should().Be("pending");
            presentRow.ChunkCount.Should().Be(0);
            presentRow.LastIndexedAt.Should().BeNull();

            var missingRow = await ctx.Documents.SingleAsync(d => d.ContentHash == "missing");
            missingRow.IndexingStatus.Should().Be("failed");
            missingRow.IndexingError.Should().Contain("not found");
            missingRow.ChunkCount.Should().Be(0);

            (await ctx.Documents.CountAsync(d => d.IndexingStatus == "completed")).Should().Be(0,
                "chunks are not synced, so nothing may claim to be indexed");
        }
        finally
        {
            File.Delete(existingFile);
        }
    }

    [Fact]
    public async Task ImportChangesAsync_MatchingDocument_KeepsTheLocalRow_AndFillsAMissingSummary()
    {
        using var h = new SyncHarness();
        h.Seed(ctx =>
        {
            var local = Doc(4, "same-content", filePath: "/local/path.pdf");
            local.IndexingStatus = "completed";
            local.ChunkCount = 3;
            ctx.Documents.Add(local);
        });

        var remote = Doc(99, "same-content", filePath: "/remote/path.pdf");
        remote.Summary = "remote summary";
        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(DocumentEntity), 99, SyncChangeType.Updated, Json(remote))));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        var row = await ctx.Documents.SingleAsync();
        row.Id.Should().Be(4);
        row.FilePath.Should().Be("/local/path.pdf");
        row.IndexingStatus.Should().Be("completed");
        row.ChunkCount.Should().Be(3);
        row.Summary.Should().Be("remote summary");
    }

    [Fact]
    public async Task ImportChangesAsync_NewConversation_StartsWithoutMessages()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        var remote = new ConversationEntity { Id = 5, Title = "Remote chat", ModelId = "m", CreatedAt = now, UpdatedAt = now, MessageCount = 12, BranchPointMessageId = 44 };

        await h.Service.ImportChangesAsync(ChangeSet(Change(nameof(ConversationEntity), 5, SyncChangeType.Created, Json(remote))));

        using var ctx = h.Fresh();
        var row = await ctx.Conversations.SingleAsync();
        row.MessageCount.Should().Be(0, "messages are not synced, so the count must not claim twelve");
        row.BranchPointMessageId.Should().BeNull("message ids are local to each installation");
    }

    [Fact]
    public async Task ImportChangesAsync_CollectionWhoseParentIsMissing_RecreatesTheAncestorByName()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        var child = new CollectionEntity { Id = 20, Name = "Archive", ParentCollectionId = 10, CreatedAt = now, UpdatedAt = now };
        var change = Change(nameof(CollectionEntity), 20, SyncChangeType.Created, Json(child));
        change.NaturalKey = "Work" + SyncNaturalKeys.Separator + "Archive";

        var applied = await h.Service.ImportChangesAsync(ChangeSet(change));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        var work = await ctx.Collections.SingleAsync(c => c.Name == "Work");
        var archive = await ctx.Collections.SingleAsync(c => c.Name == "Archive");
        archive.ParentCollectionId.Should().Be(work.Id);
        work.ParentCollectionId.Should().BeNull();
    }

    [Fact]
    public async Task ImportChangesAsync_DeletedChange_RemovesTheEntityMatchedByNaturalKey()
    {
        using var h = new SyncHarness();
        var old = DateTime.UtcNow.AddHours(-1);
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 7,
            Name = "doomed",
            Content = "c",
            Category = "General",
            CreatedAt = old,
            UpdatedAt = old,
        }));

        var deletion = Change(nameof(SystemPromptEntity), 1234, SyncChangeType.Deleted, null);
        deletion.NaturalKey = "doomed";
        var applied = await h.Service.ImportChangesAsync(ChangeSet(deletion));

        applied.Should().Be(1);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_DeletionWithoutNaturalKey_IsRejected_NeverAppliedByNumericId()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Id = 7,
            Name = "survivor",
            Content = "c",
            Category = "General",
            CreatedAt = now,
            UpdatedAt = now,
        }));

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 7, SyncChangeType.Deleted, null)));

        applied.Should().Be(0);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.SingleAsync()).Name.Should().Be("survivor");
    }

    [Fact]
    public async Task ImportChangesAsync_DeletesEveryEntityType()
    {
        using var h = new SyncHarness();
        var old = new DateTime(2026, 1, 1, 12, 0, 0, 500, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Documents.Add(Doc(700, "p")); // annotation parent, kept
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 701,
                DocumentId = 700,
                StartOffset = 0,
                EndOffset = 2,
                HighlightedText = "x",
                Color = "yellow",
                CreatedAt = old,
                UpdatedAt = old,
            });
            ctx.Documents.Add(Doc(702, "d"));
            ctx.Collections.Add(new CollectionEntity { Id = 703, Name = "c", CreatedAt = old, UpdatedAt = old });
            ctx.Tags.Add(new TagEntity { Id = 704, Name = "tag-del", CreatedAt = old });
            ctx.Conversations.Add(new ConversationEntity { Id = 705, Title = "t", ModelId = "m", CreatedAt = old, UpdatedAt = old });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 706, Name = "p", Content = "c", Category = "General", CreatedAt = old, UpdatedAt = old });
        });

        SyncChange Deletion(string type, string key)
        {
            var change = Change(type, 1, SyncChangeType.Deleted, null);
            change.NaturalKey = key;
            return change;
        }

        var changeSet = ChangeSet(
            Deletion(nameof(AnnotationEntity), SyncNaturalKeys.ForAnnotation("sha:p", 0, 2, "x")),
            Deletion(nameof(DocumentEntity), "sha:d"),
            Deletion(nameof(CollectionEntity), "c"),
            Deletion(nameof(TagEntity), "tag-del"),
            Deletion(nameof(ConversationEntity), SyncNaturalKeys.ForConversation(old, "t")),
            Deletion(nameof(SystemPromptEntity), "p"));

        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(6);
        using var ctx = h.Fresh();
        (await ctx.Annotations.CountAsync()).Should().Be(0);
        (await ctx.Documents.Select(d => d.Id).ToListAsync()).Should().Equal(700L); // parent untouched
        (await ctx.Collections.CountAsync()).Should().Be(0);
        (await ctx.Tags.CountAsync()).Should().Be(0);
        (await ctx.Conversations.CountAsync()).Should().Be(0);
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_DeleteMissingEntity_IsNoOp()
    {
        using var h = new SyncHarness();

        var deletion = Change(nameof(SystemPromptEntity), 999, SyncChangeType.Deleted, null);
        deletion.NaturalKey = "never existed";
        var applied = await h.Service.ImportChangesAsync(ChangeSet(deletion));

        applied.Should().Be(0);
        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
        h.Service.Status.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ImportChangesAsync_EmptySerializedData_IsRejectedAndReported()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 1, SyncChangeType.Updated, "")));

        applied.Should().Be(0);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);

        var history = await h.Service.GetSyncHistoryAsync();
        history.Should().ContainSingle(l => l.Direction == "import" && !l.IsSuccess && l.ErrorMessage!.Contains("skipped"));
    }

    [Fact]
    public async Task ImportChangesAsync_NullDeserializedPayload_IsRejected()
    {
        using var h = new SyncHarness();

        // "null" is non-whitespace, so it reaches the deserialiser and yields a null entity.
        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change(nameof(SystemPromptEntity), 1, SyncChangeType.Created, "null")));

        applied.Should().Be(0);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_UnrecognizedUpsertEntityType_IsRejected()
    {
        using var h = new SyncHarness();

        var applied = await h.Service.ImportChangesAsync(
            ChangeSet(Change("MysteryEntity", 1, SyncChangeType.Created, "{}")));

        applied.Should().Be(0);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_UnrecognizedDeleteEntityType_IsNotApplied()
    {
        using var h = new SyncHarness();

        var deletion = Change("MysteryEntity", 1, SyncChangeType.Deleted, null);
        deletion.NaturalKey = "whatever";
        var applied = await h.Service.ImportChangesAsync(ChangeSet(deletion));

        applied.Should().Be(0);
    }

    [Fact]
    public async Task ImportChangesAsync_MalformedChange_IsSkipped_OthersApplied()
    {
        using var h = new SyncHarness();

        var changeSet = ChangeSet(
            Change(nameof(SystemPromptEntity), 1, SyncChangeType.Created, "{ malformed json"),
            PromptCreate(2, "valid"));

        var applied = await h.Service.ImportChangesAsync(changeSet);

        applied.Should().Be(1); // only the valid change counted
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.SingleAsync()).Name.Should().Be("valid");
    }

    [Fact]
    public async Task ImportChangesAsync_AnnotationWhoseDocumentIsNotHere_IsRejected()
    {
        using var h = new SyncHarness();
        var now = DateTime.UtcNow;
        var ann = new AnnotationEntity { Id = 1, DocumentId = 5, EndOffset = 1, HighlightedText = "x", Color = "yellow", CreatedAt = now, UpdatedAt = now };
        var change = Change(nameof(AnnotationEntity), 1, SyncChangeType.Created, Json(ann));
        change.References = new Dictionary<string, string> { ["DocumentId"] = "sha:unknown" };

        var applied = await h.Service.ImportChangesAsync(ChangeSet(change));

        applied.Should().Be(0);
        using var ctx = h.Fresh();
        (await ctx.Annotations.CountAsync()).Should().Be(0);
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
    public async Task ImportChangesAsync_ResolverThrows_CountsTheChangeAsFailed()
    {
        using var h = new SyncHarness(mockResolver: true);
        var now = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 1, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = now }));
        h.Resolver
            .Setup(r => r.Decide(It.IsAny<SyncChange>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("resolver down"));

        var applied = await h.Service.ImportChangesAsync(ChangeSet(PromptCreate(1, "p")));

        applied.Should().Be(0);
        h.Service.Status.SyncState.Should().Be(SyncState.Error);
        h.Service.Status.PendingChanges.Should().Be(1);
    }

    // ---- DetectConflictsAsync (public) ----

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
        using var h = new SyncHarness(mockResolver: true);
        var incoming = ChangeSet(PromptCreate(1));
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 1 };
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                incoming, It.IsAny<string>(), It.IsAny<Func<SyncChange, Task<SyncLocalVersion?>>>()))
            .ReturnsAsync((IReadOnlyList<SyncConflict>)new List<SyncConflict> { conflict });

        var result = await h.Service.DetectConflictsAsync(incoming);

        result.Should().ContainSingle().Which.Should().BeSameAs(conflict);
        h.Resolver.Verify(r => r.DetectConflictsAsync(
            incoming,
            It.Is<string>(s => !string.IsNullOrWhiteSpace(s)),
            It.IsAny<Func<SyncChange, Task<SyncLocalVersion?>>>()), Times.Once);
    }

    [Fact]
    public async Task DetectConflictsAsync_ReportsIncomingChangesOlderThanTheLocalCopy()
    {
        using var h = new SyncHarness();
        var localEdit = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 3, Name = "shared", Content = "c", Category = "General", CreatedAt = localEdit, UpdatedAt = localEdit }));

        var older = new SystemPromptEntity { Id = 50, Name = "shared", Content = "old", Category = "General", CreatedAt = localEdit, UpdatedAt = localEdit.AddHours(-1) };
        var change = Change(nameof(SystemPromptEntity), 50, SyncChangeType.Updated, Json(older));
        change.Timestamp = older.UpdatedAt;

        var conflicts = await h.Service.DetectConflictsAsync(ChangeSet(change, PromptCreate(51, "brand new")));

        conflicts.Should().ContainSingle();
        conflicts[0].EntityId.Should().Be(3, "the conflict names the local row");
        conflicts[0].Resolution.Should().Be(SyncResolution.KeepLocal);
    }

    [Fact]
    public async Task DetectConflictsAsync_LocalVersionCallback_MatchesEveryEntityTypeByNaturalKey()
    {
        using var h = new SyncHarness(mockResolver: true);
        var now = DateTime.UtcNow;
        var colTime = now.AddMinutes(-2);
        var convCreated = now.AddDays(-1);
        var convTime = now.AddMinutes(-4);
        var annTime = now.AddMinutes(-5);
        var promptTime = now.AddMinutes(-6);

        h.Seed(ctx =>
        {
            ctx.Documents.Add(Doc(11, "doc-hash"));
            ctx.Collections.Add(new CollectionEntity { Id = 12, Name = "c", CreatedAt = now, UpdatedAt = colTime });
            ctx.Tags.Add(new TagEntity { Id = 13, Name = "t", CreatedAt = now });
            ctx.Conversations.Add(new ConversationEntity { Id = 14, Title = "chat", ModelId = "m", CreatedAt = convCreated, UpdatedAt = convTime });
            ctx.Annotations.Add(new AnnotationEntity
            {
                Id = 15,
                DocumentId = 11,
                StartOffset = 0,
                EndOffset = 1,
                HighlightedText = "x",
                Color = "yellow",
                CreatedAt = now,
                UpdatedAt = annTime,
            });
            ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 16, Name = "p", Content = "c", Category = "General", CreatedAt = now, UpdatedAt = promptTime });
        });

        SyncChange Keyed(string type, string key)
        {
            var change = Change(type, 999, SyncChangeType.Updated, null);
            change.NaturalKey = key;
            return change;
        }

        var captured = new Dictionary<string, SyncLocalVersion?>();
        h.Resolver
            .Setup(r => r.DetectConflictsAsync(
                It.IsAny<SyncChangeSet>(), It.IsAny<string>(), It.IsAny<Func<SyncChange, Task<SyncLocalVersion?>>>()))
            .Returns(async (SyncChangeSet _, string __, Func<SyncChange, Task<SyncLocalVersion?>> getLocal) =>
            {
                captured[nameof(DocumentEntity)] = await getLocal(Keyed(nameof(DocumentEntity), "sha:doc-hash"));
                captured[nameof(CollectionEntity)] = await getLocal(Keyed(nameof(CollectionEntity), "c"));
                captured[nameof(TagEntity)] = await getLocal(Keyed(nameof(TagEntity), "t"));
                captured[nameof(ConversationEntity)] = await getLocal(Keyed(nameof(ConversationEntity), SyncNaturalKeys.ForConversation(convCreated, "chat")));
                captured[nameof(AnnotationEntity)] = await getLocal(Keyed(nameof(AnnotationEntity), SyncNaturalKeys.ForAnnotation("sha:doc-hash", 0, 1, "x")));
                captured[nameof(SystemPromptEntity)] = await getLocal(Keyed(nameof(SystemPromptEntity), "p"));
                captured["Unknown"] = await getLocal(Keyed("MysteryEntity", "x"));
                captured["NoMatch"] = await getLocal(Keyed(nameof(SystemPromptEntity), "not here"));
                return (IReadOnlyList<SyncConflict>)Array.Empty<SyncConflict>();
            });

        await h.Service.DetectConflictsAsync(ChangeSet(PromptCreate(1)));

        captured[nameof(DocumentEntity)].Should().BeEquivalentTo(new SyncLocalVersion(11, null));
        captured[nameof(CollectionEntity)]!.EntityId.Should().Be(12);
        captured[nameof(CollectionEntity)]!.ModifiedAt.Should().BeCloseTo(colTime, TimeSpan.FromSeconds(1));
        captured[nameof(TagEntity)].Should().BeEquivalentTo(new SyncLocalVersion(13, null));
        captured[nameof(ConversationEntity)]!.EntityId.Should().Be(14);
        captured[nameof(ConversationEntity)]!.ModifiedAt.Should().BeCloseTo(convTime, TimeSpan.FromSeconds(1));
        captured[nameof(AnnotationEntity)]!.EntityId.Should().Be(15);
        captured[nameof(AnnotationEntity)]!.ModifiedAt.Should().BeCloseTo(annTime, TimeSpan.FromSeconds(1));
        captured[nameof(SystemPromptEntity)]!.EntityId.Should().Be(16);
        captured[nameof(SystemPromptEntity)]!.ModifiedAt.Should().BeCloseTo(promptTime, TimeSpan.FromSeconds(1));
        captured["Unknown"].Should().BeNull();
        captured["NoMatch"].Should().BeNull();
    }

    // ---- ResolveConflictAsync ----

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
        using var h = new SyncHarness(mockResolver: true);
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 11 };
        h.Resolver.Setup(r => r.ResolveConflict(conflict, SyncResolution.KeepRemote))
                  .Returns(PromptCreate(11, "resolved"));

        await h.Service.ResolveConflictAsync(conflict, SyncResolution.KeepRemote);

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.SingleAsync()).Name.Should().Be("resolved");
    }

    [Fact]
    public async Task ResolveConflictAsync_ResolverReturnsNull_AppliesNothing()
    {
        using var h = new SyncHarness(mockResolver: true);
        var conflict = new SyncConflict { EntityType = nameof(SystemPromptEntity), EntityId = 12 };
        h.Resolver.Setup(r => r.ResolveConflict(conflict, SyncResolution.KeepLocal))
                  .Returns((SyncChange?)null);

        await h.Service.ResolveConflictAsync(conflict, SyncResolution.KeepLocal);

        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepRemote_OverridesANewerLocalCopy()
    {
        using var h = new SyncHarness();
        var localEdit = DateTime.UtcNow;
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity { Id = 20, Name = "shared", Content = "local", Category = "General", CreatedAt = localEdit, UpdatedAt = localEdit }));

        var older = new SystemPromptEntity { Id = 3, Name = "shared", Content = "remote wins by choice", Category = "General", CreatedAt = localEdit, UpdatedAt = localEdit.AddHours(-1) };
        var remoteChange = Change(nameof(SystemPromptEntity), 3, SyncChangeType.Updated, Json(older));
        remoteChange.Timestamp = older.UpdatedAt;

        var conflicts = await h.Service.DetectConflictsAsync(ChangeSet(remoteChange));
        await h.Service.ResolveConflictAsync(conflicts.Single(), SyncResolution.KeepRemote);

        using var ctx = h.Fresh();
        var row = await ctx.SystemPrompts.SingleAsync();
        row.Id.Should().Be(20);
        row.Content.Should().Be("remote wins by choice");
    }

    // ---- GetSyncHistoryAsync ----

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

    // ---- Auto-sync lifecycle ----

    [Fact]
    public async Task StartAutoSyncAsync_NoConfiguration_DoesNotStart()
    {
        using var h = new SyncHarness();

        await h.Service.StartAutoSyncAsync();        // returns without starting a loop
        h.Service.IsAutoSyncRunning.Should().BeFalse();
        await h.Service.StopAutoSyncAsync();         // safe no-op afterwards

        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task StartAutoSyncAsync_AutoSyncDisabled_DoesNotStart()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(autoSync: false));

        await h.Service.StartAutoSyncAsync();
        h.Service.IsAutoSyncRunning.Should().BeFalse();
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
        h.Service.IsAutoSyncRunning.Should().BeTrue();
        await h.Service.StopAutoSyncAsync();    // cancels, awaits the loop and disposes the CTS
        h.Service.IsAutoSyncRunning.Should().BeFalse();

        h.Service.Status.SyncState.Should().Be(SyncState.Idle);
    }

    [Fact]
    public async Task StopAutoSyncAsync_NeverStarted_IsNoOp()
    {
        using var h = new SyncHarness();
        await h.Service.StopAutoSyncAsync(); // _autoSyncCts is null, early return
    }

    [Fact]
    public async Task RunAutoSyncLoop_AlreadyCancelledToken_ExitsCleanly()
    {
        using var h = new SyncHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Invoking the loop with a cancelled token exercises the first-delay wait and the
        // OperationCanceledException exit path without waiting for a real tick.
        await InvokePrivateAsync(h.Service, "RunAutoSyncLoopAsync", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), cts.Token);
    }

    [Fact]
    public async Task ResumeAutoSyncAsync_WhenAutoSyncIsDisabled_IsANoOp()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(autoSync: false));

        await h.Service.ResumeAutoSyncAsync();

        h.Service.IsAutoSyncRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeAutoSyncAsync_WhenAutoSyncIsEnabled_StartsTheLoop()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig(autoSync: true, interval: 30));

        await h.Service.ResumeAutoSyncAsync();

        h.Service.IsAutoSyncRunning.Should().BeTrue();
        await h.Service.StopAutoSyncAsync();
        h.Service.IsAutoSyncRunning.Should().BeFalse();
    }

    // ---- Watermarks and restarts ----

    [Fact]
    public async Task SyncNowAsync_ExportsOnlyChangesSinceThePersistedWatermark_AcrossARestart()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        var exported = h.CaptureExports();

        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Name = "before first pass",
            Content = "c",
            Category = "General",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        }));

        var first = await h.Service.SyncNowAsync();
        first.ExportedChanges.Should().Be(1);

        // Simulate an application restart: a new service instance over the same database.
        using var db = h.Factory.CreateContext();
        var restarted = new SyncService(db, h.Logger, h.Transport.Object, h.Codec.Object, new SyncConflictResolver(h.Logger));
        await restarted.GetConfigurationAsync();
        restarted.Status.LastSyncAt.Should().NotBeNull("the last sync time survives a restart");

        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
        {
            Name = "after first pass",
            Content = "c",
            Category = "General",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }));

        var second = await restarted.SyncNowAsync();

        second.ExportedChanges.Should().Be(1, "a restart must not trigger a full re-export");
        exported[^1].Changes.Should().ContainSingle().Which.NaturalKey.Should().Be("after first pass");
    }

    [Fact]
    public async Task SyncNowAsync_EditMadeWhileThePassRuns_IsExportedByTheNextPass()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        var exported = h.CaptureExports();

        // The edit lands after the export collected its changes but before the pass ends
        // (during the import phase). The old code moved the export watermark to the END of the
        // import, so this edit was never exported.
        h.Transport
            .Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity
            {
                Name = "edited during pass",
                Content = "c",
                Category = "General",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            })))
            .ReturnsAsync((IReadOnlyList<SyncFilePayload>)Array.Empty<SyncFilePayload>());

        await h.Service.SyncNowAsync();
        h.Transport
            .Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SyncFilePayload>)Array.Empty<SyncFilePayload>());
        await h.Service.SyncNowAsync();

        exported.SelectMany(cs => cs.Changes).Should().Contain(c => c.NaturalKey == "edited during pass");
    }

    [Fact]
    public async Task SyncNowAsync_NothingChanged_WritesNoFile()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.CaptureExports();

        var result = await h.Service.SyncNowAsync();

        result.ExportedChanges.Should().Be(0);
        h.Transport.Verify(t => t.WriteSyncFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- ImportNowAsync / SyncNowAsync: peer files ----

    [Fact]
    public async Task SyncNowAsync_ExportsThenImportsPeerFiles()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.CaptureExports();
        h.Seed(ctx => ctx.SystemPrompts.Add(new SystemPromptEntity { Name = "local", Content = "c", Category = "General", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }));
        var payload = h.SetupPeerFile("peer", ChangeSet(PromptCreate(321, "from-peer")));

        var result = await h.Service.SyncNowAsync();

        result.ExportedChanges.Should().Be(1);
        result.PeerFilesFound.Should().Be(1);
        result.PeerFilesImported.Should().Be(1);
        result.ChangesApplied.Should().Be(1);
        result.HasProblems.Should().BeFalse();
        h.Transport.Verify(t => t.MarkFileImportedAsync(payload.FilePath), Times.Once);
    }

    [Fact]
    public async Task ImportNowAsync_ValidFile_DecryptsImportsAndMarksImported()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        var payload = h.SetupPeerFile("peer", ChangeSet(PromptCreate(321, "from-peer")));

        var result = await h.Service.ImportNowAsync();

        result.PeerFilesImported.Should().Be(1);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.SingleAsync()).Name.Should().Be("from-peer");
        h.Codec.Verify(c => c.Decrypt(payload.Data, It.IsAny<string>()), Times.Once);
        h.Transport.Verify(t => t.MarkFileImportedAsync(payload.FilePath), Times.Once);
        h.Transport.Verify(t => t.WriteSyncFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never, "an import-only pass does not export");
    }

    [Fact]
    public async Task ImportNowAsync_FileWithAFailedChange_IsLeftInPlace_AndImportedOnceItApplies()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var broken = Change(
            nameof(ConversationEntity), 9, SyncChangeType.Created,
            "{\"title\":null,\"modelId\":\"m\",\"createdAt\":\"2026-01-01T00:00:00\",\"updatedAt\":\"2026-01-01T00:00:00\"}");
        var payload = h.SetupPeerFile("peer", ChangeSet(broken, PromptCreate(2, "good")));

        var first = await h.Service.ImportNowAsync();

        first.PeerFilesPendingRetry.Should().Be(1);
        first.PeerFilesImported.Should().Be(0);
        first.ChangesFailed.Should().Be(1);
        first.HasProblems.Should().BeTrue();
        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never,
            "a file with a failed change is retried, never renamed to .imported");

        // Next cycle: the change now applies (for example after a transient database error).
        var fixedPayload = ChangeSet(PromptCreate(2, "good"));
        h.Codec.Setup(c => c.Deserialise(It.IsAny<byte[]>())).Returns(fixedPayload);

        var second = await h.Service.ImportNowAsync();

        second.PeerFilesImported.Should().Be(1);
        h.Transport.Verify(t => t.MarkFileImportedAsync(payload.FilePath), Times.Once);
        using var ctx = h.Fresh();
        (await ctx.SystemPrompts.CountAsync(p => p.Name == "good")).Should().Be(1, "the retried change is recognised, not duplicated");
    }

    [Fact]
    public async Task ImportNowAsync_FileWithOnlyUnapplicableChanges_IsMarkedImported_AndReported()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        var payload = h.SetupPeerFile("peer", ChangeSet(Change("MysteryEntity", 1, SyncChangeType.Created, "{}")));

        var result = await h.Service.ImportNowAsync();

        result.ChangesRejected.Should().Be(1);
        result.PeerFilesImported.Should().Be(1, "retrying a change that can never apply would not help");
        result.HasProblems.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().Contain("unrecognised entity type");
        h.Transport.Verify(t => t.MarkFileImportedAsync(payload.FilePath), Times.Once);
    }

    [Fact]
    public async Task ImportNowAsync_InvalidHeader_IsReportedWithoutDecrypting()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\bad.axs", FileName = "bad", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(false);

        var result = await h.Service.ImportNowAsync();

        result.PeerFilesUnreadable.Should().Be(1);
        h.Codec.Verify(c => c.Decrypt(It.IsAny<byte[]>(), It.IsAny<string>()), Times.Never);
        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportNowAsync_DecryptCryptographicException_IsReportedAndTheFileKept()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\enc.axs", FileName = "enc", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(true);
        h.Codec.Setup(c => c.Decrypt(payload.Data, It.IsAny<string>())).Throws(new CryptographicException("wrong passphrase"));

        var result = await h.Service.ImportNowAsync();

        result.PeerFilesUnreadable.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("passphrase");
        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportNowAsync_DecryptGenericException_IsReportedAndTheFileKept()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());

        var payload = new SyncFilePayload { FilePath = @"C:\sync-test\err.axs", FileName = "err", Data = new byte[] { 1 } };
        h.Transport.Setup(t => t.ReadPeerFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<SyncFilePayload>)new[] { payload });
        h.Codec.Setup(c => c.IsValidHeader(payload.Data)).Returns(true);
        h.Codec.Setup(c => c.Decrypt(payload.Data, It.IsAny<string>())).Throws(new InvalidOperationException("boom"));

        var result = await h.Service.ImportNowAsync();

        result.PeerFilesUnreadable.Should().Be(1);
        h.Transport.Verify(t => t.MarkFileImportedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportNowAsync_Cancelled_Rethrows()
    {
        using var h = new SyncHarness();
        await h.Service.ConfigureAsync(ValidConfig());
        h.SetupPeerFile("cancel", ChangeSet(PromptCreate(1)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => h.Service.ImportNowAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ImportNowAsync_NotConfigured_Throws()
    {
        using var h = new SyncHarness();

        Func<Task> act = () => h.Service.ImportNowAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("not been configured");
    }
}

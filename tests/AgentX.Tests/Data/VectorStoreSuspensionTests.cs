using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Data;

/// <summary>
/// The vector stores keep their own long-lived connection to the database file, so on Windows a
/// restore or the encryption migration refused the swap with "database in use", and on any
/// platform the store kept reading the replaced file. Suspending closes that connection once the
/// running operations finish, makes new operations wait, and resuming reopens it on the file that
/// is live then (reloading what the store derived from the previous file when asked).
/// </summary>
public sealed class VectorStoreSuspensionTests : IDisposable
{
    private static readonly float[] VectorA = { 1.0f, 0.0f, 0.0f };
    private static readonly float[] VectorB = { 0.0f, 1.0f, 0.0f };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agentx-vsuspend-{Guid.NewGuid():N}");

    public VectorStoreSuspensionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // --- The gate itself ---

    [Fact]
    public async Task Suspend_waits_for_the_running_operation_and_then_closes()
    {
        var suspension = new VectorStoreSuspension();
        var running = await suspension.EnterAsync(CancellationToken.None);
        var closed = false;

        var suspend = suspension.SuspendAsync(() => closed = true, CancellationToken.None);
        await Task.Delay(100);
        suspend.IsCompleted.Should().BeFalse("the connection must not close under a running operation");
        closed.Should().BeFalse();

        running.Dispose();
        await suspend.WaitAsync(TimeSpan.FromSeconds(5));

        closed.Should().BeTrue();
        suspension.IsSuspended.Should().BeTrue();
    }

    [Fact]
    public async Task Operations_wait_until_the_last_nested_suspension_ends()
    {
        var suspension = new VectorStoreSuspension();
        await suspension.SuspendAsync(() => { }, CancellationToken.None);
        await suspension.SuspendAsync(() => { }, CancellationToken.None);
        var reloads = new List<bool>();

        var waiting = suspension.EnterAsync(CancellationToken.None).AsTask();
        await suspension.ResumeAsync(reloadFromDatabase: true, reload => { reloads.Add(reload); return Task.CompletedTask; });
        await Task.Delay(100);
        waiting.IsCompleted.Should().BeFalse("an overlapping file operation is still in progress");
        reloads.Should().BeEmpty();

        await suspension.ResumeAsync(reloadFromDatabase: false, reload => { reloads.Add(reload); return Task.CompletedTask; });
        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();

        reloads.Should().Equal(new[] { true }, "a reload asked for by any suspension is honored once, by the last resume");
        suspension.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_suspension_leaves_the_store_in_service()
    {
        var suspension = new VectorStoreSuspension();
        var running = await suspension.EnterAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => suspension.SuspendAsync(() => throw new InvalidOperationException("must not close"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        suspension.IsSuspended.Should().BeFalse();
        (await suspension.EnterAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        running.Dispose();
    }

    [Fact]
    public async Task A_failed_reopen_still_releases_the_waiting_operations()
    {
        var suspension = new VectorStoreSuspension();
        await suspension.SuspendAsync(() => { }, CancellationToken.None);
        var waiting = suspension.EnterAsync(CancellationToken.None).AsTask();

        var act = () => suspension.ResumeAsync(false, _ => throw new IOException("cannot open"));

        await act.Should().ThrowAsync<IOException>();
        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    // --- SqliteVecStore ---

    [Fact]
    public async Task SqliteVecStore_suspend_releases_the_file_and_resume_reads_the_replacement()
    {
        var live = NewStorage("live");
        var replacement = NewStorage("replacement");
        await SeedAsync(new SqliteVecStore(Settings(replacement), PlainFactory()), chunkId: 7, VectorB);

        var store = new SqliteVecStore(Settings(live), PlainFactory());
        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, VectorA);

            await store.SuspendAsync();
            FileHandleProbe.IsOpenByThisProcess(Database(live)).Should().BeFalse(
                "an open handle makes File.Replace fail on Windows");
            ReplaceDatabase(Database(replacement), Database(live));
            await store.ResumeAsync(reloadFromDatabase: true);

            var results = await store.SearchAsync(VectorB, topK: 5, minSimilarity: 0.5);
            results.Select(r => r.ChunkId).Should().Equal(new long[] { 7 }, "the store must read the file that is live now");
            (await store.GetEmbeddingCountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task SqliteVecStore_operations_called_while_suspended_wait_for_resume()
    {
        var live = NewStorage("live");
        var store = new SqliteVecStore(Settings(live), PlainFactory());
        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, VectorA);
            await store.SuspendAsync();

            var count = store.GetEmbeddingCountAsync();
            await Task.Delay(150);
            count.IsCompleted.Should().BeFalse("the connection is closed; the call waits instead of failing");

            await store.ResumeAsync(reloadFromDatabase: false);
            (await count.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(1);
        }
    }

    [Fact]
    public async Task SqliteVecStore_that_was_never_initialized_stays_uninitialized_after_resume()
    {
        var live = NewStorage("live");
        var store = new SqliteVecStore(Settings(live), PlainFactory());
        await using (store)
        {
            await store.SuspendAsync();
            await store.ResumeAsync(reloadFromDatabase: true);

            File.Exists(Database(live)).Should().BeFalse("resume opens only what was open before");
            var act = () => store.GetEmbeddingCountAsync();
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    // --- HnswVectorStore ---

    [Fact]
    public async Task HnswVectorStore_reload_rebuilds_the_index_from_the_replacement_and_drops_old_index_files()
    {
        var live = NewStorage("live");
        var replacement = NewStorage("replacement");
        await SeedAsync(Hnsw(replacement), chunkId: 7, VectorB);

        var store = Hnsw(live);
        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, VectorA);
            await store.OptimizeAsync(); // persists index files describing chunk 1

            await store.SuspendAsync();
            FileHandleProbe.IsOpenByThisProcess(Database(live)).Should().BeFalse();
            ReplaceDatabase(Database(replacement), Database(live));
            await store.ResumeAsync(reloadFromDatabase: true);

            // The index files matched the replacement's count (one vector), so loading them would
            // have served the replaced database's vectors.
            File.Exists(Path.Combine(live, "hnsw-index.json")).Should().BeFalse();
            (await store.SearchAsync(VectorB, topK: 5, minSimilarity: 0.5)).Select(r => r.ChunkId)
                .Should().Equal(new long[] { 7 });
            (await store.SearchAsync(VectorA, topK: 5, minSimilarity: 0.5)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task HnswVectorStore_resume_without_reload_keeps_serving_the_same_vectors()
    {
        var live = NewStorage("live");
        var store = Hnsw(live);
        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(1, VectorA);

            await store.SuspendAsync();
            FileHandleProbe.IsOpenByThisProcess(Database(live)).Should().BeFalse();
            await store.ResumeAsync(reloadFromDatabase: false);

            (await store.SearchAsync(VectorA, topK: 5, minSimilarity: 0.5)).Select(r => r.ChunkId)
                .Should().Equal(new long[] { 1 });
            await store.InsertEmbeddingAsync(2, VectorB);
            (await store.GetEmbeddingCountAsync()).Should().Be(2);
        }
    }

    // --- Helpers ---

    private string NewStorage(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Database(string storage) => Path.Combine(storage, "agentx.db");

    private static ISettingsService Settings(string storage)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => new AppSettings { StoragePath = storage });
        return settings.Object;
    }

    private static IEncryptedConnectionFactory PlainFactory() => new EncryptedConnectionFactory(new DatabaseKeyProvider());

    private static HnswVectorStore Hnsw(string storage) => new(
        Settings(storage),
        Log.Logger,
        m: 4,
        efConstruction: 20,
        dimensions: 3,
        fallbackThreshold: 0, // forces the HNSW path even for one vector
        connectionFactory: PlainFactory());

    private static async Task SeedAsync(IVectorStore store, long chunkId, float[] embedding)
    {
        await using (store)
        {
            await store.InitializeAsync();
            await store.InsertEmbeddingAsync(chunkId, embedding);
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Stands in for the restore's swap: another database is renamed over the live file, so a
    /// connection that stayed open would keep reading the replaced file.
    /// </summary>
    private static void ReplaceDatabase(string source, string live)
    {
        foreach (var sidecar in new[] { "-wal", "-shm" })
        {
            if (File.Exists(live + sidecar))
                File.Delete(live + sidecar);
        }

        var incoming = live + ".incoming";
        File.Copy(source, incoming, overwrite: true);
        File.Move(incoming, live, overwrite: true);
    }
}

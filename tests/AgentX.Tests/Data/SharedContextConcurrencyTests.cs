using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace AgentX.Tests.Data;

/// <summary>
/// The app shares one <see cref="AgentXDbContext"/> between the UI and background work (the
/// indexing loop, the local REST API, status-bar polling, scheduled backup and sync). With EF's
/// stock detector, overlapping operations threw "A second operation was started on this context
/// instance", and a rejected save stayed tracked and broke every later save. These tests pin the
/// replacement behavior: overlapping work waits its turn, nested sections do not deadlock, a
/// stale ownership token cannot slip past the gate, and a failed save discards its own changes.
/// </summary>
public sealed class SharedContextConcurrencyTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Context_UsesTheSerializingDetector()
    {
        using var db = _factory.CreateContext();

        db.GetService<IConcurrencyDetector>().Should().BeOfType<SerializingConcurrencyDetector>();
    }

    [Fact]
    public async Task OverlappingQueriesRawSqlAndSaves_OnOneContext_AllSucceed()
    {
        using var db = _factory.CreateContext();
        const int workerCount = 16;
        const int rounds = 25;

        // The app initializes its shared context on one thread at startup (EnsureKeyApplied and
        // the migration run) before anything else touches it; EF's lazy first-use setup is not
        // thread-safe and is not what these tests are about.
        _ = await db.Collections.CountAsync();

        var workers = Enumerable.Range(0, workerCount).Select(worker => Task.Run(async () =>
        {
            for (var round = 0; round < rounds; round++)
            {
                switch (worker % 4)
                {
                    case 0:
                        _ = await db.Collections.AsNoTracking().CountAsync();
                        _ = await db.Collections.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
                        break;
                    case 1:
                        _ = db.Collections.AsNoTracking().Count();
                        _ = db.Collections.AsNoTracking().OrderBy(c => c.Id).Take(5).ToList();
                        _ = await db.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.Name.StartsWith("w"));
                        break;
                    case 2:
                        await db.Database.ExecuteSqlRawAsync(
                            "INSERT INTO collections (Name, CreatedAt, UpdatedAt, DocumentCount, SortOrder) VALUES ({0}, {1}, {1}, 0, 0)",
                            $"raw-{worker}-{round}",
                            DateTime.UtcNow);
                        break;
                    default:
                        await db.SaveChangesAsync();
                        _ = await db.Collections.AsNoTracking().AnyAsync(c => c.Id < 0);
                        break;
                }
            }
        }));

        var act = () => Task.WhenAll(workers);

        await act.Should().NotThrowAsync();
        // A quarter of the workers insert one row per round.
        (await db.Collections.CountAsync()).Should().Be(workerCount / 4 * rounds);
    }

    [Fact]
    public async Task EnterDatabaseGate_ExcludesEfOperationsUntilDisposed()
    {
        using var db = _factory.CreateContext();
        _ = await db.Collections.CountAsync();
        var queryFinished = false;
        var startQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Started before the gate is taken, so it is an unrelated flow (work started inside a
        // gated region shares that region's ownership by design).
        var query = Task.Run(async () =>
        {
            await startQuery.Task;
            _ = await db.Collections.CountAsync();
            queryFinished = true;
        });

        using (db.EnterDatabaseGate())
        {
            startQuery.SetResult();
            await Task.Delay(200);
            queryFinished.Should().BeFalse("raw ADO.NET work holding the gate must not interleave with EF queries");
        }

        await query.WaitAsync(TimeSpan.FromSeconds(5));
        queryFinished.Should().BeTrue();
    }

    [Fact]
    public async Task FailedSave_DiscardsItsPendingChanges_SoTheNextSaveSucceeds()
    {
        using var db = _factory.CreateContext();
        db.Tags.Add(new TagEntity { Name = "important", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Tags.Add(new TagEntity { Name = "important", CreatedAt = DateTime.UtcNow });
        var duplicate = () => db.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();

        db.Collections.Add(new CollectionEntity { Name = "after", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var next = () => db.SaveChangesAsync();

        await next.Should().NotThrowAsync("the rejected tag insert must not be replayed by an unrelated save");
        (await db.Tags.CountAsync()).Should().Be(1);
        (await db.Collections.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void FailedSave_RevertsModifiedAndDeletedEntries()
    {
        using var db = _factory.CreateContext();
        var keep = new TagEntity { Name = "keep", CreatedAt = DateTime.UtcNow };
        var other = new TagEntity { Name = "other", CreatedAt = DateTime.UtcNow };
        db.Tags.AddRange(keep, other);
        db.SaveChanges();

        keep.Name = "other";
        var collide = () => db.SaveChanges();
        collide.Should().Throw<DbUpdateException>();

        db.Entry(keep).State.Should().Be(EntityState.Unchanged);
        keep.Name.Should().Be("keep");

        db.Tags.Remove(other);
        db.SaveChanges();
        db.Tags.AsNoTracking().Select(t => t.Name).Should().BeEquivalentTo(new[] { "keep" });
    }

    [Fact]
    public async Task NestedSectionsInOneFlow_AreReentrant()
    {
        var detector = new SerializingConcurrencyDetector();

        using (detector.EnterCriticalSection())
        using (detector.EnterCriticalSection())
        using (detector.EnterCriticalSection())
        {
        }

        var reenter = Task.Run(() =>
        {
            using (detector.EnterCriticalSection())
            {
            }
        });

        var completed = await Task.WhenAny(reenter, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(reenter, "the gate must be fully released after the outermost section exits");
    }

    [Fact]
    public async Task SecondFlow_WaitsUntilTheFirstExits()
    {
        var detector = new SerializingConcurrencyDetector();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            var section = detector.EnterCriticalSection();
            firstEntered.SetResult();
            await releaseFirst.Task;
            section.Dispose();
        });

        await firstEntered.Task;
        var secondEntered = false;
        var second = Task.Run(() =>
        {
            using (detector.EnterCriticalSection())
            {
                secondEntered = true;
            }
        });

        await Task.Delay(200);
        secondEntered.Should().BeFalse("the gate is still held by the first flow");

        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        secondEntered.Should().BeTrue();
    }

    [Fact]
    public async Task ChildTaskWithAStaleToken_CannotBypassTheGate()
    {
        var detector = new SerializingConcurrencyDetector();
        var startChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task child;

        // The child inherits the parent's ownership token through the execution context...
        using (detector.EnterCriticalSection())
        {
            child = Task.Run(async () =>
            {
                await startChild.Task;
                using (detector.EnterCriticalSection())
                {
                }
            });
        }

        // ...but by the time it runs, the parent has released and another flow owns the gate.
        var thirdHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseThird = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = Task.Run(async () =>
        {
            var section = detector.EnterCriticalSection();
            thirdHolds.SetResult();
            await releaseThird.Task;
            section.Dispose();
        });

        await thirdHolds.Task;
        startChild.SetResult();
        await Task.Delay(200);
        child.IsCompleted.Should().BeFalse("a stale token must not count as ownership");

        releaseThird.SetResult();
        await Task.WhenAll(child, third).WaitAsync(TimeSpan.FromSeconds(5));
    }
}

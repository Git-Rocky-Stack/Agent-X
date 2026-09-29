using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;

// EF1001: this file deliberately builds on EF Core's QueryCompiler, an API EF marks as internal.
// It is the only point where a whole query execution can be observed, which is what closing the
// race described below requires. EF Core is pinned (AgentX.Core.csproj), and a signature change
// in a future EF release fails this file at compile time rather than at runtime.
#pragma warning disable EF1001

namespace AgentX.Core.Data;

/// <summary>
/// Extends <see cref="SerializingConcurrencyDetector"/>'s gate from EF's per-row critical
/// sections to whole query executions.
/// <para>
/// EF enters its concurrency detector once per enumerator step, but it disposes an enumerator
/// outside any critical section, and it returns the pooled relational command to the connection
/// before the reader is disposed. With one context shared across threads, a second query could
/// rent that command while the first query is still tearing it down, and the two readers'
/// disposal then races on the connection's command list. So scalar executions (Count, First,
/// Any, the Async variants, ExecuteUpdate/Delete) hold the gate for their whole run, which
/// includes their internal enumerator disposal, and sequence results hold it while their
/// enumerator is disposed. Sequence enumeration itself is not held open between rows, so a
/// caller that never disposes an enumerator cannot freeze every other database user.
/// </para>
/// </summary>
internal sealed class SerializingQueryCompiler : QueryCompiler
{
    private static readonly ConcurrentDictionary<Type, Func<object, IConcurrencyDetector, object>> SequenceWrappers = new();
    private static readonly ConcurrentDictionary<Type, Func<Task, ConcurrencyDetectorCriticalSectionDisposer, Task>> TaskWrappers = new();

    private readonly IConcurrencyDetector _gate;

    public SerializingQueryCompiler(
        IQueryContextFactory queryContextFactory,
        ICompiledQueryCache compiledQueryCache,
        ICompiledQueryCacheKeyGenerator compiledQueryCacheKeyGenerator,
        IDatabase database,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger,
        ICurrentDbContext currentContext,
        IEvaluatableExpressionFilter evaluatableExpressionFilter,
        IModel model,
        IConcurrencyDetector concurrencyDetector)
        : base(
            queryContextFactory,
            compiledQueryCache,
            compiledQueryCacheKeyGenerator,
            database,
            logger,
            currentContext,
            evaluatableExpressionFilter,
            model)
    {
        _gate = concurrencyDetector;
    }

    /// <summary>
    /// Registers the serializing detector and this compiler on an options builder.
    /// </summary>
    internal static void Register(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IConcurrencyDetector, SerializingConcurrencyDetector>();
        optionsBuilder.ReplaceService<IQueryCompiler, SerializingQueryCompiler>();
    }

    /// <inheritdoc />
    public override TResult Execute<TResult>(Expression query)
    {
        if (TryGetElementType(typeof(TResult), typeof(IEnumerable<>), out var elementType))
        {
            var sequence = base.Execute<TResult>(query);
            return sequence is null
                ? sequence
                : (TResult)WrapSequence(typeof(GatedEnumerable<>), elementType, sequence);
        }

        using (_gate.EnterCriticalSection())
        {
            return base.Execute<TResult>(query);
        }
    }

    /// <inheritdoc />
    public override TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken = default)
    {
        if (TryGetElementType(typeof(TResult), typeof(IAsyncEnumerable<>), out var elementType))
        {
            var sequence = base.ExecuteAsync<TResult>(query, cancellationToken);
            return sequence is null
                ? sequence
                : (TResult)WrapSequence(typeof(GatedAsyncEnumerable<>), elementType, sequence);
        }

        var section = _gate.EnterCriticalSection();
        TResult result;
        try
        {
            result = base.ExecuteAsync<TResult>(query, cancellationToken);
        }
        catch
        {
            section.Dispose();
            throw;
        }

        if (result is Task { IsCompleted: false } pending)
        {
            // The continuation releases the gate once the query finishes. SQLite completes
            // synchronously, so in practice this path is only a safety net.
            return (TResult)(object)ReleaseWhenCompletedAsync(typeof(TResult), pending, section);
        }

        section.Dispose();
        return result;
    }

    private static bool TryGetElementType(Type resultType, Type sequenceDefinition, out Type elementType)
    {
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == sequenceDefinition)
        {
            elementType = resultType.GetGenericArguments()[0];
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    private object WrapSequence(Type wrapperDefinition, Type elementType, object sequence)
    {
        var closedType = wrapperDefinition.MakeGenericType(elementType);
        var factory = SequenceWrappers.GetOrAdd(closedType, static type =>
        {
            var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
            return (inner, gate) => constructor.Invoke([inner, gate]);
        });

        return factory(sequence, _gate);
    }

    // VSTHRD003: awaiting EF's own in-flight query task is the purpose of these wrappers; they
    // hold the database gate until that task finishes and never resume on a captured context.
#pragma warning disable VSTHRD003
    private static Task ReleaseWhenCompletedAsync(
        Type taskType,
        Task pending,
        ConcurrencyDetectorCriticalSectionDisposer section)
    {
        var wrapper = TaskWrappers.GetOrAdd(taskType, static type =>
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Task<>))
            {
                return ReleaseAfterAsync;
            }

            var method = typeof(SerializingQueryCompiler)
                .GetMethod(nameof(ReleaseAfterTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type.GetGenericArguments()[0]);
            return (Func<Task, ConcurrencyDetectorCriticalSectionDisposer, Task>)method.CreateDelegate(
                typeof(Func<Task, ConcurrencyDetectorCriticalSectionDisposer, Task>));
        });

        return wrapper(pending, section);
    }

    private static async Task ReleaseAfterAsync(Task pending, ConcurrencyDetectorCriticalSectionDisposer section)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        finally
        {
            section.Dispose();
        }
    }

    private static async Task<T> ReleaseAfterTypedAsync<T>(Task pending, ConcurrencyDetectorCriticalSectionDisposer section)
    {
        try
        {
            return await ((Task<T>)pending).ConfigureAwait(false);
        }
        finally
        {
            section.Dispose();
        }
    }
#pragma warning restore VSTHRD003

    private sealed class GatedEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _inner;
        private readonly IConcurrencyDetector _gate;

        public GatedEnumerable(IEnumerable<T> inner, IConcurrencyDetector gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public IEnumerator<T> GetEnumerator() => new GatedEnumerator(_inner.GetEnumerator(), _gate);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class GatedEnumerator : IEnumerator<T>
        {
            private readonly IEnumerator<T> _inner;
            private readonly IConcurrencyDetector _gate;

            public GatedEnumerator(IEnumerator<T> inner, IConcurrencyDetector gate)
            {
                _inner = inner;
                _gate = gate;
            }

            public T Current => _inner.Current;

            object? IEnumerator.Current => Current;

            // Each step enters the gate through EF's own detector call.
            public bool MoveNext() => _inner.MoveNext();

            public void Reset() => _inner.Reset();

            public void Dispose()
            {
                using (_gate.EnterCriticalSection())
                {
                    _inner.Dispose();
                }
            }
        }
    }

    private sealed class GatedAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IAsyncEnumerable<T> _inner;
        private readonly IConcurrencyDetector _gate;

        public GatedAsyncEnumerable(IAsyncEnumerable<T> inner, IConcurrencyDetector gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new GatedAsyncEnumerator(_inner.GetAsyncEnumerator(cancellationToken), _gate);

        private sealed class GatedAsyncEnumerator : IAsyncEnumerator<T>
        {
            private readonly IAsyncEnumerator<T> _inner;
            private readonly IConcurrencyDetector _gate;

            public GatedAsyncEnumerator(IAsyncEnumerator<T> inner, IConcurrencyDetector gate)
            {
                _inner = inner;
                _gate = gate;
            }

            public T Current => _inner.Current;

            // Each step enters the gate through EF's own detector call.
            public ValueTask<bool> MoveNextAsync() => _inner.MoveNextAsync();

            public async ValueTask DisposeAsync()
            {
                var section = _gate.EnterCriticalSection();
                try
                {
                    await _inner.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    section.Dispose();
                }
            }
        }
    }
}

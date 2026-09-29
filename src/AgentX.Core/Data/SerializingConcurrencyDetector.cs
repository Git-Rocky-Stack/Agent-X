using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AgentX.Core.Data;

/// <summary>
/// Replaces EF Core's default <see cref="IConcurrencyDetector"/> so that overlapping use of one
/// <see cref="AgentXDbContext"/> waits its turn instead of throwing.
/// <para>
/// The app registers a single long-lived context (App.xaml.cs), and it is reached from the UI
/// thread and from background work at the same time: the indexing loop, the local REST API,
/// status-bar polling, scheduled backup and sync, connector timers. EF Core's stock detector
/// throws "A second operation was started on this context instance" the moment two of those
/// overlap. This detector turns every EF critical section into a mutually exclusive region, so
/// concurrent callers serialize. <see cref="SerializingQueryCompiler"/> and
/// <see cref="AgentXDbContext"/> widen the same gate to cover whole query executions, query
/// enumerator disposal, whole SaveChanges calls and opted-in raw ADO.NET work.
/// </para>
/// <para>
/// Re-entrancy: EF nests critical sections inside one logical operation, and the wider regions
/// above wrap EF's own sections. A flow that already owns the gate may therefore enter again
/// without waiting. Ownership is tracked with a per-acquisition token in an
/// <see cref="AsyncLocal{T}"/>, compared against the token of the current owner, so a task that
/// merely inherited a token from a flow that has since released the gate cannot slip past it.
/// </para>
/// <para>
/// Limits: the change tracker is still shared. Adding or removing entities on one thread while
/// another thread saves is not made safe by this gate; keep such work on one flow.
/// </para>
/// </summary>
public sealed class SerializingConcurrencyDetector : IConcurrencyDetector
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<object?> _flowToken = new();
    private object? _ownerToken;
    private int _depth;

    /// <inheritdoc />
    public ConcurrencyDetectorCriticalSectionDisposer EnterCriticalSection()
    {
        var token = _flowToken.Value;
        if (token is not null && ReferenceEquals(token, Volatile.Read(ref _ownerToken)))
        {
            Interlocked.Increment(ref _depth);
            return new ConcurrencyDetectorCriticalSectionDisposer(this);
        }

        _gate.Wait();

        token = new object();
        _flowToken.Value = token;
        Volatile.Write(ref _ownerToken, token);
        Volatile.Write(ref _depth, 1);
        return new ConcurrencyDetectorCriticalSectionDisposer(this);
    }

    /// <inheritdoc />
    public void ExitCriticalSection()
    {
        if (Interlocked.Decrement(ref _depth) != 0)
        {
            return;
        }

        Volatile.Write(ref _ownerToken, null);
        _flowToken.Value = null;
        _gate.Release();
    }
}

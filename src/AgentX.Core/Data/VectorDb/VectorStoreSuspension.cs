namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// Lets a vector store close its database connection while the database file is replaced
/// (restore) or re-encrypted, without failing the operations that arrive in the meantime.
/// <para>
/// Every public store operation enters before it touches the connection and leaves when it is
/// done. The first <see cref="SuspendAsync"/> stops new operations from entering, waits until
/// the running ones have left and then lets the store close its connection; the matching last
/// <see cref="ResumeAsync"/> lets the store reopen and then lets the waiting operations continue.
/// Suspensions nest, so two file operations that overlap cannot reopen the store under each
/// other. A store method enters once per call and never calls another entering method while
/// inside: a suspension that started in between would wait for it forever.
/// </para>
/// </summary>
internal sealed class VectorStoreSuspension
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private int _running;

    // Guarded by _sync. Both are set while the gate is closed: waiting operations await the
    // first, the suspender awaits the second.
    private TaskCompletionSource? _reopened;
    private TaskCompletionSource? _drained;

    // Guarded by _lifecycle.
    private int _depth;
    private bool _reloadRequested;

    /// <summary>True while at least one suspension is in effect.</summary>
    public bool IsSuspended
    {
        get
        {
            lock (_sync)
            {
                return _reopened is not null;
            }
        }
    }

    /// <summary>
    /// Enters an operation, first waiting while the store is suspended. Dispose the result to leave.
    /// </summary>
    public async ValueTask<IDisposable> EnterAsync(CancellationToken ct)
    {
        while (true)
        {
            Task reopened;
            lock (_sync)
            {
                if (_reopened is null)
                {
                    _running++;
                    return new Operation(this);
                }

                reopened = _reopened.Task;
            }

            await reopened.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts a suspension. The first one closes the gate, waits for the running operations to
    /// leave and then runs <paramref name="closeConnection"/>; a nested one only counts. When
    /// <paramref name="ct"/> is cancelled while waiting, the gate opens again and nothing counts.
    /// </summary>
    public async Task SuspendAsync(Action closeConnection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(closeConnection);

        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_depth == 0)
            {
                Task drained;
                lock (_sync)
                {
                    _reopened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (_running == 0)
                    {
                        _drained.TrySetResult();
                    }

                    drained = _drained.Task;
                }

                try
                {
                    await drained.WaitAsync(ct).ConfigureAwait(false);
                    closeConnection();
                }
                catch
                {
                    OpenGate();
                    throw;
                }
            }

            _depth++;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Ends one suspension. The last one runs <paramref name="reopen"/>, passing whether any of
    /// the suspensions asked for a reload, and then lets the waiting operations in, also when
    /// <paramref name="reopen"/> fails (they then report that the store is not initialized
    /// instead of waiting forever). Does nothing when the store is not suspended.
    /// </summary>
    public async Task ResumeAsync(bool reloadFromDatabase, Func<bool, Task> reopen)
    {
        ArgumentNullException.ThrowIfNull(reopen);

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_depth == 0)
            {
                return;
            }

            _reloadRequested |= reloadFromDatabase;
            if (--_depth > 0)
            {
                return;
            }

            var reload = _reloadRequested;
            _reloadRequested = false;
            try
            {
                await reopen(reload).ConfigureAwait(false);
            }
            finally
            {
                OpenGate();
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Lets every waiting operation in without reopening. Used when the store is disposed, so the
    /// waiting operations report the disposal instead of waiting forever.
    /// </summary>
    public void Abandon() => OpenGate();

    private void OpenGate()
    {
        TaskCompletionSource? reopened;
        lock (_sync)
        {
            reopened = _reopened;
            _reopened = null;
            _drained = null;
        }

        reopened?.TrySetResult();
    }

    private void Leave()
    {
        lock (_sync)
        {
            _running--;
            if (_running == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    private sealed class Operation : IDisposable
    {
        private VectorStoreSuspension? _owner;

        public Operation(VectorStoreSuspension owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Leave();
    }
}

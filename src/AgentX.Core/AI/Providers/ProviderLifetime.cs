namespace AgentX.Core.AI.Providers;

/// <summary>
/// Counts in-flight provider operations so that <c>Dispose</c> can defer releasing the
/// provider's resources (native model memory, the HTTP client) until the last running call
/// has finished. Without this, re-initializing the AI service while a chat is streaming or a
/// document is being embedded disposes the provider underneath the active call: the stream
/// breaks for HTTP providers and the built-in provider frees native memory that llama.cpp is
/// still reading.
/// </summary>
internal sealed class ProviderLifetime
{
    private readonly object _sync = new();
    private readonly string _ownerName;
    private int _activeOperations;
    private bool _disposeRequested;
    private Action? _pendingRelease;

    public ProviderLifetime(string ownerName)
    {
        _ownerName = ownerName;
    }

    /// <summary>True once <see cref="RequestDispose"/> has been called.</summary>
    public bool IsDisposeRequested
    {
        get
        {
            lock (_sync)
            {
                return _disposeRequested;
            }
        }
    }

    /// <summary>Number of operations currently running (diagnostics and tests).</summary>
    public int ActiveOperations
    {
        get
        {
            lock (_sync)
            {
                return _activeOperations;
            }
        }
    }

    /// <summary>
    /// Registers the start of an operation. Throws <see cref="ObjectDisposedException"/> once
    /// disposal has been requested, so no new work starts on a retiring provider.
    /// </summary>
    public void Enter()
    {
        lock (_sync)
        {
            if (_disposeRequested)
                throw new ObjectDisposedException(_ownerName);

            _activeOperations++;
        }
    }

    /// <summary>
    /// Registers the end of an operation started with <see cref="Enter"/>. The last operation
    /// to finish after disposal was requested runs the deferred release.
    /// </summary>
    public void Exit()
    {
        Action? release = null;
        lock (_sync)
        {
            if (_activeOperations > 0)
                _activeOperations--;

            if (_disposeRequested && _activeOperations == 0)
            {
                release = _pendingRelease;
                _pendingRelease = null;
            }
        }

        release?.Invoke();
    }

    /// <summary>
    /// Requests disposal. <paramref name="release"/> runs immediately when nothing is in flight,
    /// otherwise when the last in-flight operation exits. Returns false when disposal had
    /// already been requested (the release is not scheduled twice).
    /// </summary>
    public bool RequestDispose(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);

        lock (_sync)
        {
            if (_disposeRequested)
                return false;

            _disposeRequested = true;
            if (_activeOperations > 0)
            {
                _pendingRelease = release;
                return true;
            }
        }

        release();
        return true;
    }
}

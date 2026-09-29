using System.Collections.Concurrent;

namespace AgentX.Tests.Helpers;

/// <summary>
/// A <see cref="SynchronizationContext"/> that runs every posted callback on one dedicated
/// thread, like the WinUI dispatcher. View-model tests run a command on it and check that
/// bound properties are only written from that thread: an <c>await ... ConfigureAwait(false)</c>
/// in a command body resumes on the thread pool instead, which WinUI's x:Bind rejects.
/// </summary>
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;

    public SingleThreadSynchronizationContext()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "Test UI thread" };
        _thread.Start();
    }

    /// <summary>The managed id of the thread that plays the UI thread.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            _queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            // Disposed: a late continuation (a command's IsRunning update) still has to run,
            // and throwing from Post would surface on whichever thread completed its task.
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Synchronous dispatch is not used by these tests.");

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Starts <paramref name="action"/> on the UI thread (so its awaits capture this context)
    /// and completes when it does.
    /// </summary>
    public Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, null);
        return completion.Task;
    }

    public void Dispose() => _queue.CompleteAdding();

    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }
}

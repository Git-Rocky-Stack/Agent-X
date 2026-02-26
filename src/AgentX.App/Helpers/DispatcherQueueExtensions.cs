using Microsoft.UI.Dispatching;

namespace AgentX.App.Helpers;

/// <summary>
/// Extension methods for <see cref="DispatcherQueue"/> that provide async-friendly
/// and exception-safe ways to dispatch work onto the UI thread.
/// </summary>
public static class DispatcherQueueExtensions
{
    /// <summary>
    /// Enqueues an <see cref="Action"/> onto the dispatcher queue and returns a
    /// <see cref="Task"/> that completes when the action has finished executing.
    /// </summary>
    /// <param name="dispatcher">The dispatcher queue to enqueue work on.</param>
    /// <param name="action">The action to execute on the dispatcher thread.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="dispatcher"/> or <paramref name="action"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the work could not be enqueued (e.g., the dispatcher has been shut down).
    /// </exception>
    public static Task EnqueueAsync(this DispatcherQueue dispatcher, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);

        var tcs = new TaskCompletionSource();

        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException(
                "Failed to enqueue work on the DispatcherQueue. The dispatcher may have been shut down."));
        }

        return tcs.Task;
    }

    /// <summary>
    /// Enqueues a <see cref="Func{T}"/> onto the dispatcher queue and returns a
    /// <see cref="Task{T}"/> that completes with the function's return value.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="dispatcher">The dispatcher queue to enqueue work on.</param>
    /// <param name="function">The function to execute on the dispatcher thread.</param>
    /// <returns>A task representing the asynchronous operation, containing the function result.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="dispatcher"/> or <paramref name="function"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the work could not be enqueued (e.g., the dispatcher has been shut down).
    /// </exception>
    public static Task<T> EnqueueAsync<T>(this DispatcherQueue dispatcher, Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(function);

        var tcs = new TaskCompletionSource<T>();

        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                var result = function();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException(
                "Failed to enqueue work on the DispatcherQueue. The dispatcher may have been shut down."));
        }

        return tcs.Task;
    }

    /// <summary>
    /// Attempts to enqueue an <see cref="Action"/> onto the dispatcher queue, swallowing
    /// any exceptions thrown during execution. Useful for fire-and-forget UI updates
    /// where failure should not crash the application.
    /// </summary>
    /// <param name="dispatcher">The dispatcher queue to enqueue work on.</param>
    /// <param name="action">The action to execute on the dispatcher thread.</param>
    /// <returns>
    /// <c>true</c> if the work was successfully enqueued; <c>false</c> if the enqueue
    /// failed (e.g., the dispatcher has been shut down) or if the dispatcher was null.
    /// </returns>
    public static bool TryEnqueueSafe(this DispatcherQueue? dispatcher, Action action)
    {
        if (dispatcher is null || action is null)
            return false;

        try
        {
            return dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[DispatcherQueueExtensions] TryEnqueueSafe caught exception: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DispatcherQueueExtensions] TryEnqueueSafe failed to enqueue: {ex.Message}");
            return false;
        }
    }
}

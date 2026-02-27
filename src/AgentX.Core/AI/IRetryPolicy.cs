namespace AgentX.Core.AI;

/// <summary>
/// Provides retry logic with exponential backoff for transient AI operation failures.
/// Handles Ollama timeouts, network blips, and temporary model loading delays.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Executes an async operation with retry logic.
    /// Retries on transient failures (HTTP timeouts, connection refused, 503)
    /// but NOT on permanent failures (400 bad request, model not found).
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        int maxRetries = 3,
        CancellationToken ct = default);

    /// <summary>
    /// Executes an async void operation with retry logic.
    /// </summary>
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        int maxRetries = 3,
        CancellationToken ct = default);

    /// <summary>
    /// Determines if an exception is transient (worth retrying).
    /// </summary>
    bool IsTransient(Exception exception);
}

using System.Net.Http;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Retry policy implementation that uses exponential backoff with random jitter
/// to handle transient AI operation failures. Designed for resilience against
/// Ollama timeouts, network interruptions, and temporary model loading delays.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly ILogger _logger;
    private readonly Random _jitterRandom = new();

    /// <summary>
    /// Base delay before the first retry. Subsequent retries double this value.
    /// </summary>
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum jitter factor applied to each delay (plus or minus 25%).
    /// </summary>
    private const double JitterFactor = 0.25;

    /// <summary>
    /// Creates a new <see cref="ExponentialBackoffRetryPolicy"/> with the specified logger.
    /// </summary>
    /// <param name="logger">Serilog logger for recording retry activity.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public ExponentialBackoffRetryPolicy(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        int maxRetries = 3,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (string.IsNullOrWhiteSpace(operationName))
            throw new ArgumentException("Operation name cannot be null or empty.", nameof(operationName));

        _logger.Debug("Starting operation {OperationName} with up to {MaxRetries} retries",
            operationName, maxRetries);

        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxRetries, operationName, ct))
            {
                var delay = ComputeDelay(attempt);

                _logger.Warning(
                    "Retry {Attempt}/{MaxRetries} for operation {OperationName} after {DelayMs}ms — {ExceptionMessage}",
                    attempt + 1, maxRetries, operationName, (int)delay.TotalMilliseconds, ex.Message);

                await Task.Delay(delay, ct).ConfigureAwait(false);
                attempt++;
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        int maxRetries = 3,
        CancellationToken ct = default)
    {
        // Delegate to the generic overload by wrapping the void operation
        // to return a sentinel value. This avoids duplicating the retry loop.
        await ExecuteAsync<object?>(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            operationName,
            maxRetries,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        // Permanent failures — never retry these.
        if (exception is ArgumentException
            or ArgumentNullException
            or InvalidOperationException
            or NotSupportedException)
        {
            return false;
        }

        // Network-level errors are always transient.
        if (exception is HttpRequestException)
            return true;

        // Connection resets, broken pipes, and similar I/O issues.
        if (exception is IOException)
            return true;

        // Timeout exceptions from HttpClient or internal timeouts.
        // TaskCanceledException is thrown by HttpClient on timeout and wraps
        // a TimeoutException in its InnerException.  We treat it as transient
        // ONLY when the caller's CancellationToken has NOT been canceled —
        // otherwise the user explicitly requested cancellation.
        if (exception is TaskCanceledException tce)
            return !tce.CancellationToken.IsCancellationRequested;

        if (exception is OperationCanceledException oce)
            return !oce.CancellationToken.IsCancellationRequested;

        // Catch-all: inspect the message for well-known transient indicators.
        var message = exception.Message;
        if (!string.IsNullOrEmpty(message))
        {
            if (message.Contains("503", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Determines whether the current exception warrants a retry based on
    /// transience, remaining attempts, and cancellation state.
    /// When retries are exhausted the final exception is logged at Error level
    /// and the method returns false so the exception propagates to the caller.
    /// </summary>
    private bool ShouldRetry(
        Exception exception,
        int attempt,
        int maxRetries,
        string operationName,
        CancellationToken ct)
    {
        // If the caller requested cancellation, never retry.
        if (ct.IsCancellationRequested)
            return false;

        if (!IsTransient(exception))
            return false;

        if (attempt >= maxRetries)
        {
            _logger.Error(
                exception,
                "All {MaxRetries} retries exhausted for operation {OperationName}. Total attempts: {TotalAttempts}",
                maxRetries, operationName, attempt + 1);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Computes the delay for the given attempt using exponential backoff
    /// (500ms, 1000ms, 2000ms, ...) with random jitter of +/-25%.
    /// </summary>
    private TimeSpan ComputeDelay(int attempt)
    {
        // Exponential: BaseDelay * 2^attempt
        var exponentialMs = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);

        // Jitter: multiply by a random factor in [1 - JitterFactor, 1 + JitterFactor]
        double jitterMultiplier;
        lock (_jitterRandom)
        {
            // NextDouble() returns [0.0, 1.0), scale to [-JitterFactor, +JitterFactor]
            jitterMultiplier = 1.0 + ((_jitterRandom.NextDouble() * 2.0 * JitterFactor) - JitterFactor);
        }

        var delayMs = exponentialMs * jitterMultiplier;

        return TimeSpan.FromMilliseconds(delayMs);
    }
}

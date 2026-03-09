namespace AgentX.Core.Exceptions;

/// <summary>
/// Thrown when a data export operation fails.
/// Carries the target export format (e.g., "PDF", "CSV", "JSON") so callers
/// can provide format-specific diagnostics or retry logic.
/// </summary>
public class ExportException : AgentXException
{
    private const string Code = "EXPORT_ERROR";

    /// <summary>
    /// The export format that was being produced when the error occurred
    /// (e.g., "PDF", "CSV", "JSON"), or <c>null</c> if unknown.
    /// </summary>
    public string? ExportFormat { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ExportException"/>.
    /// </summary>
    /// <param name="format">The export format that failed (e.g., "PDF").</param>
    /// <param name="message">A technical description of what went wrong.</param>
    public ExportException(string? format, string message)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: format is not null
                ? $"Failed to export data as {format}. {message}"
                : $"Export failed. {message}",
            inner: null)
    {
        ExportFormat = format;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ExportException"/> with an inner exception.
    /// </summary>
    /// <param name="format">The export format that failed (e.g., "PDF").</param>
    /// <param name="message">A technical description of what went wrong.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public ExportException(string? format, string message, Exception inner)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: format is not null
                ? $"Failed to export data as {format}. {message}"
                : $"Export failed. {message}",
            inner: inner)
    {
        ExportFormat = format;
    }
}

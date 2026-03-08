namespace AgentX.Core.Services.Export.Models;

/// <summary>
/// Describes the outcome of an export operation, including the output
/// file location, size, and any error information.
/// </summary>
public class ExportResult
{
    /// <summary>
    /// Indicates whether the export completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The absolute path to the generated export file.
    /// Null when <see cref="Success"/> is false.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// The size of the generated file in bytes.
    /// Zero when <see cref="Success"/> is false.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// A human-readable error message when the export fails.
    /// Null when <see cref="Success"/> is true.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful export result.
    /// </summary>
    /// <param name="filePath">The absolute path to the generated file.</param>
    /// <param name="fileSize">The file size in bytes.</param>
    public static ExportResult Ok(string filePath, long fileSize) => new()
    {
        Success = true,
        FilePath = filePath,
        FileSize = fileSize,
    };

    /// <summary>
    /// Creates a failed export result with an error message.
    /// </summary>
    /// <param name="errorMessage">A description of what went wrong.</param>
    public static ExportResult Fail(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
    };
}

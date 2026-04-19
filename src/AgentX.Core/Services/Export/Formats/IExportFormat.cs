using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formats;

/// <summary>
/// Strategy interface for export format implementations.
/// Each format (Markdown, HTML, PDF, JSON, etc.) implements this interface
/// to provide consistent rendering behavior.
/// </summary>
public interface IExportFormat
{
    /// <summary>
    /// The format type this implementation handles.
    /// </summary>
    ExportFormat Format { get; }

    /// <summary>
    /// Gets the file extension for this format (including the dot).
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Renders the export data to the specified format.
    /// </summary>
    /// <typeparam name="T">The type of data being exported.</typeparam>
    /// <param name="data">The data to render.</param>
    /// <param name="options">Export options controlling output format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered content as a string or byte array.</returns>
    Task<object> RenderAsync<T>(T data, ExportOptions options, CancellationToken ct = default);

    /// <summary>
    /// Determines if this format can handle the specified data type.
    /// </summary>
    /// <typeparam name="T">The type of data to check.</typeparam>
    /// <returns>True if this format supports the data type.</returns>
    bool Supports<T>();
}

using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Focused interface for conversation export formatters.
/// Each implementation handles a single output format (Markdown, PlainText, CSV, etc.)
/// and produces the exact string content for that format.
/// </summary>
public interface IExportFormatter
{
    /// <summary>
    /// The format type this formatter handles.
    /// </summary>
    ExportFormat Format { get; }

    /// <summary>
    /// The file extension for this format, including the leading dot (e.g. ".md", ".txt", ".csv").
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// The MIME type for this format (e.g. "text/markdown", "text/plain", "text/csv").
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Formats a single conversation into the target format.
    /// </summary>
    /// <param name="conversation">The conversation entity with messages to export.</param>
    /// <param name="options">Export options controlling output detail level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The formatted string content.</returns>
    Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Formats multiple conversations into the target format.
    /// </summary>
    /// <param name="conversations">The conversation entities to export.</param>
    /// <param name="options">Export options controlling output detail level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The formatted string content for all conversations.</returns>
    Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default);
}

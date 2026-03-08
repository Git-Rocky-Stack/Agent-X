using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export;

/// <summary>
/// Exports conversations, search results, and document collections to various
/// file formats (Markdown, HTML, PDF, JSON, PlainText). Handles formatting,
/// file I/O, and error recovery for all export operations.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports a single conversation to the specified format.
    /// </summary>
    /// <param name="conversationId">The ID of the conversation to export.</param>
    /// <param name="options">Export configuration (format, output path, content flags).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ExportResult"/> indicating success or failure.</returns>
    Task<ExportResult> ExportConversationAsync(
        long conversationId,
        ExportOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Exports multiple conversations into a single file. Each conversation is
    /// separated by a clear section divider in the output.
    /// </summary>
    /// <param name="conversationIds">The IDs of the conversations to export.</param>
    /// <param name="options">Export configuration (format, output path, content flags).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ExportResult"/> indicating success or failure.</returns>
    Task<ExportResult> ExportConversationsAsync(
        IReadOnlyList<long> conversationIds,
        ExportOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Exports RAG search results, including the original query, each result's
    /// content, source document, relevance score, and citations.
    /// </summary>
    /// <param name="query">The search query that produced these results.</param>
    /// <param name="results">The search result items to export.</param>
    /// <param name="options">Export configuration (format, output path, content flags).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ExportResult"/> indicating success or failure.</returns>
    Task<ExportResult> ExportSearchResultsAsync(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Exports a document collection as a ZIP file containing a JSON manifest
    /// of document metadata and references.
    /// </summary>
    /// <param name="collectionId">The ID of the collection to export.</param>
    /// <param name="options">Export configuration (output path).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ExportResult"/> indicating success or failure.</returns>
    Task<ExportResult> ExportCollectionAsync(
        long collectionId,
        ExportOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Formats a single conversation as a Markdown string without writing to disk.
    /// Useful for clipboard copy or preview operations.
    /// </summary>
    /// <param name="conversationId">The ID of the conversation to format.</param>
    /// <param name="includeMeta">When true, metadata (model, tokens, timestamps) is included.</param>
    /// <returns>The formatted Markdown string, or an empty string if the conversation is not found.</returns>
    Task<string> FormatConversationAsMarkdown(long conversationId, bool includeMeta);

    /// <summary>
    /// Formats a single conversation as a styled HTML string without writing to disk.
    /// Useful for clipboard copy or preview operations.
    /// </summary>
    /// <param name="conversationId">The ID of the conversation to format.</param>
    /// <param name="includeMeta">When true, metadata (model, tokens, timestamps) is included.</param>
    /// <returns>The formatted HTML string, or an empty string if the conversation is not found.</returns>
    Task<string> FormatConversationAsHtml(long conversationId, bool includeMeta);
}

using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as styled, responsive HTML documents with dark mode
/// support. Delegates to <see cref="HtmlExport"/> for rendering and adapts the
/// output to the <see cref="IExportFormatter"/> interface.
/// </summary>
public sealed class HtmlFormatter : IExportFormatter
{
    private readonly HtmlExport _htmlExport = new();

    public ExportFormat Format => ExportFormat.Html;
    public string FileExtension => ".html";
    public string MimeType => "text/html";

    /// <inheritdoc />
    public async Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _htmlExport.RenderAsync(conversation, options, ct);
        return (string)result;
    }

    /// <inheritdoc />
    public async Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _htmlExport.RenderAsync(
            (IReadOnlyList<ConversationEntity>)conversations, options, ct);
        return (string)result;
    }
}

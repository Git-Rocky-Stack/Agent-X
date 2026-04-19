using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations and search results as structured JSON for archival
/// purposes. Delegates to <see cref="JsonExport"/> for rendering and adapts
/// the output to the <see cref="IExportFormatter"/> interface.
/// </summary>
public sealed class JsonFormatter : IExportFormatter
{
    private readonly JsonExport _jsonExport = new();

    public ExportFormat Format => ExportFormat.Json;
    public string FileExtension => ".json";
    public string MimeType => "application/json";

    /// <inheritdoc />
    public async Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _jsonExport.RenderAsync(conversation, options, ct);
        return (string)result;
    }

    /// <inheritdoc />
    public async Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _jsonExport.RenderAsync(
            (IEnumerable<ConversationEntity>)conversations, options, ct);
        return (string)result;
    }
}

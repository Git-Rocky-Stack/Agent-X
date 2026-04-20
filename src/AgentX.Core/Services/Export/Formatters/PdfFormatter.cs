using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using Serilog;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as professional PDF documents using QuestPDF.
/// Delegates to <see cref="PdfExport"/> for rendering and returns the
/// binary output as a base64-encoded string per the <see cref="IExportFormatter"/>
/// binary convention.
/// </summary>
public sealed class PdfFormatter : IExportFormatter
{
    private readonly PdfExport _pdfExport;

    public PdfFormatter()
    {
        _pdfExport = new PdfExport(Log.ForContext<PdfFormatter>());
    }

    public ExportFormat Format => ExportFormat.Pdf;
    public string FileExtension => ".pdf";
    public string MimeType => "application/pdf";

    /// <inheritdoc />
    public async Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _pdfExport.RenderAsync(conversation, options, ct);
        return Convert.ToBase64String((byte[])result);
    }

    /// <inheritdoc />
    public async Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await _pdfExport.RenderAsync(
            (IReadOnlyList<ConversationEntity>)conversations, options, ct);
        return Convert.ToBase64String((byte[])result);
    }
}

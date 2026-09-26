namespace AgentX.Core.Services.Export.Models;

/// <summary>
/// Configuration options that control the content, formatting,
/// and destination of an export operation.
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// The desired output format (Markdown, HTML, PDF, JSON, PlainText, Csv, Docx, or Pptx).
    /// </summary>
    public ExportFormat Format { get; set; } = ExportFormat.Markdown;

    /// <summary>
    /// When true, citation references and footnotes are included in the export. Only messages
    /// that carry stored citations (<c>MessageEntity.CitationsJson</c>) have any; chat does not
    /// store them yet, so the export dialog does not offer this option.
    /// </summary>
    public bool IncludeCitations { get; set; } = true;

    /// <summary>
    /// When true, additional metadata (model ID, token counts, generation time) is included.
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// When true, message timestamps are displayed alongside each message.
    /// </summary>
    public bool IncludeTimestamps { get; set; } = true;

    /// <summary>
    /// When true, the AI model identifier is shown for assistant messages that carry one
    /// (<c>MessageEntity.ModelId</c>, which chat does not store yet).
    /// </summary>
    public bool IncludeModelInfo { get; set; } = false;

    /// <summary>
    /// The absolute file path where the export should be saved.
    /// If null, a default path based on the app's storage directory will be used.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// An optional title override for the exported document.
    /// If null, the conversation title or a generated title is used.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// When set, a single-conversation export is structured according to the specified
    /// template (Research Report, Executive Summary, Annotated Bibliography). Templates
    /// produce Markdown, so they apply to Markdown exports only; any other format, or a
    /// multi-conversation export, fails with a message instead of silently ignoring it.
    /// </summary>
    public ExportTemplateId? TemplateId { get; set; }

    /// <summary>
    /// Not applied: no exporter includes branch conversations yet, so the export dialog does
    /// not offer the option. Kept for API compatibility.
    /// </summary>
    public bool IncludeBranches { get; set; } = true;

}

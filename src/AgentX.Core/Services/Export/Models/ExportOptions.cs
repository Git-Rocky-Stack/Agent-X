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
    /// When true, citation references and footnotes are included in the export.
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
    /// When true, the AI model identifier is shown for assistant messages.
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
    /// When set, the export is structured according to the specified template
    /// (e.g., Research Report, Executive Summary, Annotated Bibliography).
    /// Templates are only applicable to Markdown, HTML, and DOCX formats.
    /// </summary>
    public ExportTemplateId? TemplateId { get; set; }

    /// <summary>
    /// When true, conversation branch data is included in the export.
    /// </summary>
    public bool IncludeBranches { get; set; } = true;
}

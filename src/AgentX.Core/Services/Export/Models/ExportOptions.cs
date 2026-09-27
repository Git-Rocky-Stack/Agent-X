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
    /// When true, each message's stored sources (<c>MessageEntity.CitationsJson</c>, such as the
    /// web pages Research Mode gave an answer) are listed with that message, numbered as its [n]
    /// markers are. JSON exports carry the stored list as it is; CSV and PowerPoint omit it.
    /// </summary>
    public bool IncludeCitations { get; set; } = true;

    /// <summary>
    /// When true, additional metadata (conversation details, and the token count and generation
    /// time of each answer) is included.
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// When true, message timestamps are displayed alongside each message.
    /// </summary>
    public bool IncludeTimestamps { get; set; } = true;

    /// <summary>
    /// When true, the model that wrote each answer (<c>MessageEntity.ModelId</c>) is shown with
    /// it. Answers saved before chat recorded the model have none, so nothing is shown for them.
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

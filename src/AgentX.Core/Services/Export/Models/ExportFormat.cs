namespace AgentX.Core.Services.Export.Models;

/// <summary>
/// Specifies the output format for exported content.
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// Markdown (.md) format with headers, role labels, and optional metadata.
    /// </summary>
    Markdown,

    /// <summary>
    /// Styled HTML (.html) document with CSS for dark/light printing.
    /// </summary>
    Html,

    /// <summary>
    /// Professional PDF (.pdf) document generated via QuestPDF with Agent-X branding.
    /// </summary>
    Pdf,

    /// <summary>
    /// Structured JSON (.json) with full metadata, messages, and citations.
    /// </summary>
    Json,

    /// <summary>
    /// Plain text (.txt) with minimal formatting.
    /// </summary>
    PlainText,

    /// <summary>
    /// Comma-separated values (.csv) for spreadsheet import and data analysis.
    /// </summary>
    Csv,

    /// <summary>
    /// Word document (.docx) with formatted conversation content using OpenXML SDK.
    /// </summary>
    Docx,

    /// <summary>
    /// PowerPoint presentation (.pptx) with key insights as slides using OpenXML SDK.
    /// </summary>
    Pptx,
}

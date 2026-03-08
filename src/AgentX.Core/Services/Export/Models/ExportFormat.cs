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
}

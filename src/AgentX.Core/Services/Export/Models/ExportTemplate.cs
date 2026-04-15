namespace AgentX.Core.Services.Export.Models;

/// <summary>
/// Identifies a built-in export template that structures conversation
/// content into a specific document layout.
/// </summary>
public enum ExportTemplateId
{
    /// <summary>
    /// Full research report with Introduction, Methodology, Findings, Discussion, Conclusion, References.
    /// </summary>
    ResearchReport,

    /// <summary>
    /// Concise executive summary with Key Findings and Recommendations.
    /// </summary>
    ExecutiveSummary,

    /// <summary>
    /// Annotated bibliography grouped by source document with summaries.
    /// </summary>
    AnnotatedBibliography
}

/// <summary>
/// Describes a built-in export template with its section structure.
/// </summary>
public sealed class ExportTemplate
{
    public ExportTemplateId Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string[] Sections { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A lightweight message representation used by the template service
/// to structure conversation content for export templates.
/// </summary>
public sealed class TemplateMessage
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime? Timestamp { get; init; }
    public string? DocumentName { get; init; }
}
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export;

/// <summary>
/// Provides built-in export templates that structure conversation content
/// into professional document layouts (Research Report, Executive Summary,
/// Annotated Bibliography).
/// </summary>
public interface IExportTemplateService
{
    /// <summary>
    /// Returns all built-in export templates.
    /// </summary>
    IReadOnlyList<ExportTemplate> GetTemplates();

    /// <summary>
    /// Applies a template to the given messages, producing structured Markdown
    /// content organized by the template's section layout.
    /// </summary>
    Task<string> ApplyTemplateAsync(ExportTemplateId templateId, IReadOnlyList<TemplateMessage> messages, string title);
}

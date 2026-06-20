using System.Text;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export;

/// <summary>
/// Built-in export template service that structures conversation content
/// into professional document layouts: Research Report, Executive Summary,
/// and Annotated Bibliography.
/// </summary>
public sealed class ExportTemplateService : IExportTemplateService
{
    private static readonly ExportTemplate[] BuiltInTemplates =
    [
        new()
        {
            Id = ExportTemplateId.ResearchReport,
            Name = "Research Report",
            Description = "Full research report with Introduction, Methodology, Findings, Discussion, Conclusion, and References.",
            Sections = ["Introduction", "Methodology", "Findings", "Discussion", "Conclusion", "References"]
        },
        new()
        {
            Id = ExportTemplateId.ExecutiveSummary,
            Name = "Executive Summary",
            Description = "Concise executive summary with Key Findings and Recommendations.",
            Sections = ["Executive Summary", "Key Findings", "Recommendations"]
        },
        new()
        {
            Id = ExportTemplateId.AnnotatedBibliography,
            Name = "Annotated Bibliography",
            Description = "Annotated bibliography grouped by source document with summaries.",
            Sections = ["Overview", "Sources"]
        }
    ];

    /// <inheritdoc />
    public IReadOnlyList<ExportTemplate> GetTemplates() => BuiltInTemplates;

    /// <inheritdoc />
    public Task<string> ApplyTemplateAsync(
        ExportTemplateId templateId,
        IReadOnlyList<TemplateMessage> messages,
        string title)
    {
        var sb = new StringBuilder();

        switch (templateId)
        {
            case ExportTemplateId.ResearchReport:
                ApplyResearchReport(sb, messages, title);
                break;
            case ExportTemplateId.ExecutiveSummary:
                ApplyExecutiveSummary(sb, messages, title);
                break;
            case ExportTemplateId.AnnotatedBibliography:
                ApplyAnnotatedBibliography(sb, messages, title);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(templateId), templateId,
                    $"Unknown template ID: {templateId}");
        }

        return Task.FromResult(sb.ToString());
    }

    // ── Research Report ─────────────────────────────────────────────

    private static void ApplyResearchReport(
        StringBuilder sb, IReadOnlyList<TemplateMessage> messages, string title)
    {
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        var assistantMessages = messages
            .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var userMessages = messages
            .Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sourceDocuments = messages
            .Where(m => !string.IsNullOrEmpty(m.DocumentName))
            .Select(m => m.DocumentName!)
            .Distinct()
            .ToList();

        // Introduction — first assistant message
        sb.AppendLine("## Introduction");
        sb.AppendLine();
        if (assistantMessages.Count > 0)
        {
            sb.AppendLine(assistantMessages[0].Content.Trim());
        }
        else if (userMessages.Count > 0)
        {
            sb.AppendLine($"This report investigates: {userMessages[0].Content.Trim()}");
        }
        sb.AppendLine();

        // Methodology
        sb.AppendLine("## Methodology");
        sb.AppendLine();
        sb.AppendLine("Analysis based on AI-assisted research and document review.");
        if (sourceDocuments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Source documents reviewed:");
            foreach (var doc in sourceDocuments)
            {
                sb.AppendLine($"- {doc}");
            }
        }
        sb.AppendLine();

        // Findings — middle assistant messages
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (assistantMessages.Count > 2)
        {
            // Messages between first and last form the findings
            for (var i = 1; i < assistantMessages.Count - 1; i++)
            {
                sb.AppendLine(assistantMessages[i].Content.Trim());
                sb.AppendLine();
            }
        }
        else if (assistantMessages.Count > 0)
        {
            // Only one or two assistant messages — all go to findings except first
            for (var i = 1; i < assistantMessages.Count; i++)
            {
                sb.AppendLine(assistantMessages[i].Content.Trim());
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No findings recorded.");
            sb.AppendLine();
        }

        // Discussion
        sb.AppendLine("## Discussion");
        sb.AppendLine();
        if (userMessages.Count > 1)
        {
            sb.AppendLine("Key questions explored:");
            foreach (var q in userMessages.Skip(1))
            {
                sb.AppendLine($"- {q.Content.Trim()}");
            }
        }
        else
        {
            sb.AppendLine("The findings are discussed in context of the research questions posed.");
        }
        sb.AppendLine();

        // Conclusion — last assistant message
        sb.AppendLine("## Conclusion");
        sb.AppendLine();
        if (assistantMessages.Count > 1)
        {
            sb.AppendLine(assistantMessages[^1].Content.Trim());
        }
        else if (assistantMessages.Count == 1)
        {
            sb.AppendLine(assistantMessages[0].Content.Trim());
        }
        else
        {
            sb.AppendLine("No conclusion available.");
        }
        sb.AppendLine();

        // References — source documents
        sb.AppendLine("## References");
        sb.AppendLine();
        if (sourceDocuments.Count > 0)
        {
            for (var i = 0; i < sourceDocuments.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {sourceDocuments[i]}");
            }
        }
        else
        {
            sb.AppendLine("No source documents referenced.");
        }
    }

    // ── Executive Summary ───────────────────────────────────────────

    private static void ApplyExecutiveSummary(
        StringBuilder sb, IReadOnlyList<TemplateMessage> messages, string title)
    {
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        var assistantMessages = messages
            .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Executive Summary — first assistant message
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        if (assistantMessages.Count > 0)
        {
            sb.AppendLine(assistantMessages[0].Content.Trim());
        }
        else
        {
            sb.AppendLine("No summary available.");
        }
        sb.AppendLine();

        // Key Findings — bullet points from subsequent assistant messages
        sb.AppendLine("## Key Findings");
        sb.AppendLine();
        if (assistantMessages.Count > 1)
        {
            for (var i = 1; i < assistantMessages.Count; i++)
            {
                // Split content into sentences/paragraphs and render as bullet points
                var content = assistantMessages[i].Content.Trim();
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l));

                foreach (var line in lines)
                {
                    sb.AppendLine($"- {line}");
                }
            }
        }
        else
        {
            sb.AppendLine("- No additional findings.");
        }
        sb.AppendLine();

        // Recommendations — last assistant message or derived from findings
        sb.AppendLine("## Recommendations");
        sb.AppendLine();
        if (assistantMessages.Count > 1)
        {
            sb.AppendLine(assistantMessages[^1].Content.Trim());
        }
        else
        {
            sb.AppendLine("Based on the findings above, further investigation is recommended.");
        }
    }

    // ── Annotated Bibliography ───────────────────────────────────────

    private static void ApplyAnnotatedBibliography(
        StringBuilder sb, IReadOnlyList<TemplateMessage> messages, string title)
    {
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        // Overview
        sb.AppendLine("## Overview");
        sb.AppendLine();
        var totalSources = messages
            .Where(m => !string.IsNullOrEmpty(m.DocumentName))
            .Select(m => m.DocumentName)
            .Distinct()
            .Count();
        sb.AppendLine($"This bibliography covers {totalSources} source(s) referenced in the conversation.");
        sb.AppendLine();

        // Sources — grouped by DocumentName
        sb.AppendLine("## Sources");
        sb.AppendLine();

        var grouped = messages
            .Where(m => !string.IsNullOrEmpty(m.DocumentName))
            .GroupBy(m => m.DocumentName!);

        foreach (var group in grouped)
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();

            foreach (var msg in group)
            {
                if (msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(msg.Content.Trim());
                    sb.AppendLine();
                }
            }
        }

        // If no documents were referenced, include all assistant messages
        if (!grouped.Any())
        {
            var assistantMessages = messages
                .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var msg in assistantMessages)
            {
                sb.AppendLine($"### Conversation Insight");
                sb.AppendLine();
                sb.AppendLine(msg.Content.Trim());
                sb.AppendLine();
            }
        }
    }
}

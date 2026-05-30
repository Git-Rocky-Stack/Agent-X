using System.Text;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Export.Models;
using Markdig;

namespace AgentX.Core.Services.Export.Formats;

/// <summary>
/// HTML export format implementation using the strategy pattern.
/// Generates styled, responsive HTML documents with dark mode support.
/// </summary>
public sealed class HtmlExport : IExportFormat
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public ExportFormat Format => ExportFormat.Html;

    public string FileExtension => ".html";

    public bool Supports<T>() => typeof(T) == typeof(ConversationEntity)
                              || typeof(T) == typeof(IReadOnlyList<ConversationEntity>)
                              || typeof(T) == typeof(IReadOnlyList<SearchResultExportItem>);

    public Task<object> RenderAsync<T>(T data, ExportOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var html = data switch
        {
            ConversationEntity conversation => RenderConversation(conversation, options),
            IReadOnlyList<ConversationEntity> conversations => RenderConversations(conversations, options),
            IReadOnlyList<SearchResultExportItem> results => RenderSearchResults(results, options),
            _ => throw new NotSupportedException($"HtmlExport does not support type {typeof(T).Name}")
        };

        return Task.FromResult<object>(html);
    }

    private static string RenderConversation(ConversationEntity conversation, ExportOptions options)
    {
        var sb = new StringBuilder();
        sb.Append(GetDocumentHeader(conversation.Title));
        sb.Append(BuildHtmlBody(conversation, options));
        sb.Append(GetDocumentFooter());
        return sb.ToString();
    }

    private static string RenderConversations(IReadOnlyList<ConversationEntity> conversations, ExportOptions options)
    {
        var sb = new StringBuilder();
        var title = $"Agent-X Conversations Export ({conversations.Count})";
        sb.Append(GetDocumentHeader(title));

        for (var i = 0; i < conversations.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine("<hr class=\"section-divider\" />");
            }
            sb.Append(BuildHtmlBody(conversations[i], options));
        }

        sb.Append(GetDocumentFooter());
        return sb.ToString();
    }

    private static string RenderSearchResults(IReadOnlyList<SearchResultExportItem> results, ExportOptions options)
    {
        var sb = new StringBuilder();
        var title = options.Title ?? $"Search Results: {results.FirstOrDefault()?.Query ?? "Search"}";
        sb.Append(GetDocumentHeader(title));

        sb.AppendLine("<div class=\"search-results\">");
        sb.AppendLine($"  <h1>{HtmlEncode(title)}</h1>");
        sb.AppendLine("  <div class=\"metadata\">");
        sb.AppendLine($"    <span>Results: {results.Count}</span>");
        sb.AppendLine($"    <span>Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</span>");
        sb.AppendLine("  </div>");

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"  <div class=\"result\">");
            sb.AppendLine($"    <h2>Result {i + 1}: {HtmlEncode(result.DocumentName)}</h2>");

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"    <div class=\"relevance\">Relevance: {result.RelevanceScore:P1}</div>");
            }

            var contentHtml = Markdown.ToHtml(result.Content, MarkdownPipeline);
            sb.AppendLine($"    <div class=\"content\">{contentHtml}</div>");

            if (options.IncludeCitations && result.Citations.Count > 0)
            {
                sb.AppendLine("    <div class=\"citations\">");
                sb.AppendLine("      <strong>Sources:</strong>");
                sb.AppendLine("      <ul>");
                foreach (var citation in result.Citations)
                {
                    sb.AppendLine($"        <li>{HtmlEncode(citation)}</li>");
                }
                sb.AppendLine("      </ul>");
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("  </div>");
        }

        sb.AppendLine("</div>");
        sb.Append(GetDocumentFooter());
        return sb.ToString();
    }

    private static string BuildHtmlBody(ConversationEntity conversation, ExportOptions options)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<div class=\"conversation\">");
        sb.AppendLine($"  <h1>{HtmlEncode(conversation.Title)}</h1>");

        if (options.IncludeMetadata)
        {
            sb.AppendLine("  <div class=\"metadata\">");
            sb.AppendLine($"    <span>Created: {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</span>");
            sb.AppendLine($"    <span>Updated: {conversation.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC</span>");
            sb.AppendLine($"    <span>Messages: {conversation.MessageCount}</span>");
            sb.AppendLine($"    <span>Tokens: {conversation.TokensUsed:N0}</span>");
            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"    <span>Model: {HtmlEncode(conversation.ModelId)}</span>");
            }
            sb.AppendLine("  </div>");
        }

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("  <div class=\"system-prompt\">");
            sb.AppendLine("    <h2>System Prompt</h2>");
            sb.AppendLine($"    <blockquote>{HtmlEncode(conversation.SystemPrompt)}</blockquote>");
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("  <div class=\"messages\">");

        var messages = conversation.Messages.OrderBy(m => m.SortOrder).ToList();
        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role == "system") continue;

            var roleClass = message.Role == "user" ? "user" : "assistant";
            var roleLabel = GetRoleLabel(message.Role);

            sb.AppendLine($"    <div class=\"message {roleClass}\">");
            sb.AppendLine($"      <div class=\"role-label\">{HtmlEncode(roleLabel)}</div>");

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"      <div class=\"timestamp\">{message.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</div>");
            }

            var htmlContent = message.Role == "assistant"
                ? Markdown.ToHtml(message.Content, MarkdownPipeline)
                : $"<p>{HtmlEncode(message.Content)}</p>";

            sb.AppendLine($"      <div class=\"content\">{htmlContent}</div>");

            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                sb.AppendLine($"      <div class=\"model-info\">Model: {HtmlEncode(message.ModelId)}</div>");
            }

            if (options.IncludeMetadata && message.Role == "assistant")
            {
                var metaParts = new List<string>();
                if (message.TokenCount > 0)
                    metaParts.Add($"Tokens: {message.TokenCount:N0}");
                if (message.GenerationTimeMs.HasValue)
                    metaParts.Add($"Generation: {message.GenerationTimeMs.Value:F0}ms");
                if (metaParts.Count > 0)
                {
                    sb.AppendLine($"      <div class=\"generation-meta\">{string.Join(" &bull; ", metaParts)}</div>");
                }
            }

            sb.AppendLine("    </div>");

            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                var citations = TryParseCitations(message.CitationsJson);
                citationsList.AddRange(citations);
            }
        }

        sb.AppendLine("  </div>");

        if (citationsList.Count > 0)
        {
            sb.AppendLine("  <div class=\"citations\">");
            sb.AppendLine("    <h2>Citations</h2>");
            sb.AppendLine("    <ol>");
            foreach (var citation in citationsList)
            {
                sb.AppendLine($"      <li>{HtmlEncode(citation)}</li>");
            }
            sb.AppendLine("    </ol>");
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string GetDocumentHeader(string title)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <meta name=""generator"" content=""Agent-X Export"" />
  <title>{HtmlEncode(title)}</title>
  <style>
    :root {{
      --bg-primary: #ffffff;
      --bg-secondary: #f8f9fa;
      --bg-user: #e3f2fd;
      --bg-assistant: #f5f5f5;
      --text-primary: #212529;
      --text-secondary: #6c757d;
      --text-muted: #adb5bd;
      --border-color: #dee2e6;
      --accent-color: #0d6efd;
      --font-sans: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
      --font-mono: 'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace;
    }}
    @media (prefers-color-scheme: dark) {{
      :root {{
        --bg-primary: #1a1a2e;
        --bg-secondary: #16213e;
        --bg-user: #1a365d;
        --bg-assistant: #2d2d44;
        --text-primary: #e2e8f0;
        --text-secondary: #a0aec0;
        --text-muted: #718096;
        --border-color: #4a5568;
        --accent-color: #63b3ed;
      }}
    }}
    @media print {{
      :root {{
        --bg-primary: #ffffff;
        --bg-secondary: #f8f9fa;
        --bg-user: #e3f2fd;
        --bg-assistant: #f5f5f5;
        --text-primary: #212529;
        --text-secondary: #6c757d;
        --text-muted: #adb5bd;
        --border-color: #dee2e6;
        --accent-color: #0d6efd;
      }}
    }}
    * {{ margin: 0; padding: 0; box-sizing: border-box; }}
    body {{
      font-family: var(--font-sans);
      background-color: var(--bg-primary);
      color: var(--text-primary);
      line-height: 1.6;
      max-width: 900px;
      margin: 0 auto;
      padding: 2rem;
    }}
    h1 {{ font-size: 1.75rem; font-weight: 700; margin-bottom: 0.5rem; color: var(--text-primary); }}
    h2 {{ font-size: 1.25rem; font-weight: 600; margin: 1.5rem 0 0.75rem; color: var(--text-primary); }}
    .metadata {{
      display: flex; flex-wrap: wrap; gap: 1rem; padding: 0.75rem 1rem;
      background-color: var(--bg-secondary); border-radius: 8px;
      margin-bottom: 1.5rem; font-size: 0.875rem; color: var(--text-secondary);
      border: 1px solid var(--border-color);
    }}
    .system-prompt blockquote {{
      padding: 0.75rem 1rem; border-left: 4px solid var(--accent-color);
      background-color: var(--bg-secondary); border-radius: 0 8px 8px 0;
      font-style: italic; color: var(--text-secondary); white-space: pre-wrap;
    }}
    .messages {{ display: flex; flex-direction: column; gap: 1rem; }}
    .message {{ padding: 1rem 1.25rem; border-radius: 12px; border: 1px solid var(--border-color); }}
    .message.user {{ background-color: var(--bg-user); border-left: 4px solid var(--accent-color); }}
    .message.assistant {{ background-color: var(--bg-assistant); border-left: 4px solid var(--text-muted); }}
    .role-label {{ font-weight: 700; font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--accent-color); margin-bottom: 0.25rem; }}
    .message.assistant .role-label {{ color: var(--text-secondary); }}
    .timestamp {{ font-size: 0.75rem; color: var(--text-muted); margin-bottom: 0.5rem; }}
    .content {{ font-size: 0.9375rem; line-height: 1.6; }}
    .content p {{ margin-bottom: 0.5rem; }}
    .content pre {{
      background-color: var(--bg-secondary); border: 1px solid var(--border-color);
      border-radius: 6px; padding: 0.75rem 1rem; overflow-x: auto;
      font-family: var(--font-mono); font-size: 0.85rem; margin: 0.5rem 0;
    }}
    .content code {{ font-family: var(--font-mono); font-size: 0.85em; background-color: var(--bg-secondary); padding: 0.125rem 0.375rem; border-radius: 4px; }}
    .content pre code {{ background: none; padding: 0; }}
    .model-info, .generation-meta {{ font-size: 0.75rem; color: var(--text-muted); margin-top: 0.5rem; font-style: italic; }}
    .citations {{ margin-top: 2rem; padding-top: 1rem; border-top: 2px solid var(--border-color); }}
    .citations ol {{ padding-left: 1.5rem; }}
    .citations li {{ margin-bottom: 0.25rem; font-size: 0.875rem; color: var(--text-secondary); }}
    .result {{ padding: 1rem 1.25rem; border-radius: 12px; border: 1px solid var(--border-color); background-color: var(--bg-secondary); margin-bottom: 1rem; }}
    .relevance {{ font-size: 0.8rem; color: var(--accent-color); font-weight: 600; margin-bottom: 0.5rem; }}
    hr.section-divider {{ border: none; border-top: 2px solid var(--border-color); margin: 2rem 0; }}
    .footer {{ margin-top: 2rem; padding-top: 1rem; border-top: 1px solid var(--border-color); text-align: center; font-size: 0.8rem; color: var(--text-muted); }}
    @media print {{ body {{ max-width: none; padding: 1rem; }} .message {{ break-inside: avoid; }} }}
  </style>
</head>
<body>
";
    }

    private static string GetDocumentFooter()
    {
        return $@"
  <div class=""footer"">
    Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}
  </div>
</body>
</html>
";
    }

    private static string HtmlEncode(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                   .Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static string GetRoleLabel(string role) => role.ToLowerInvariant() switch
    {
        "user" => "User",
        "assistant" => "Assistant",
        "system" => "System",
        _ => role
    };

    private static List<string> TryParseCitations(string citationsJson)
    {
        var result = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(citationsJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return result;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var fileName = element.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "Unknown" : "Unknown";
                var pageNumber = element.TryGetProperty("pageNumber", out var pn) && pn.ValueKind == System.Text.Json.JsonValueKind.Number ? pn.GetInt32() : (int?)null;
                var excerpt = element.TryGetProperty("excerpt", out var ex) ? ex.GetString() : null;

                var description = pageNumber.HasValue ? $"{fileName}, page {pageNumber.Value}" : fileName;
                if (!string.IsNullOrWhiteSpace(excerpt))
                {
                    var shortExcerpt = excerpt.Length > 80 ? excerpt[..80] + "..." : excerpt;
                    description += $" - \"{shortExcerpt}\"";
                }
                result.Add(description);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Citation metadata is optional and decorative; malformed or partial
            // JSON must not fail the export. Return whatever parsed successfully.
        }
        return result;
    }
}

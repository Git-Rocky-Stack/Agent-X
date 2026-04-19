using System.Text;
using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as Markdown with headers, role labels, timestamps,
/// citations, and metadata. Matches the output produced by
/// <c>ExportService.BuildMarkdown</c> exactly.
/// </summary>
public sealed class MarkdownFormatter : IExportFormatter
{
    public ExportFormat Format => ExportFormat.Markdown;
    public string FileExtension => ".md";
    public string MimeType => "text/markdown";

    /// <inheritdoc />
    public Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var title = options.Title ?? conversation.Title;
        return Task.FromResult(BuildMarkdown(conversation, options, title));
    }

    /// <inheritdoc />
    public Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var title = options.Title ?? $"Agent-X Conversations Export ({conversations.Count})";
        var sb = new StringBuilder();

        sb.AppendLine($"# {EscapeMarkdown(title)}");
        sb.AppendLine();
        sb.AppendLine($"*Exported {conversations.Count} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        for (var i = 0; i < conversations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            sb.Append(BuildMarkdown(conversations[i], options, conversations[i].Title));
        }

        return Task.FromResult(sb.ToString());
    }

    // ────────────────────────────────────────────────────────────────
    //  Core formatting (extracted from ExportService.BuildMarkdown)
    // ────────────────────────────────────────────────────────────────

    private static string BuildMarkdown(
        ConversationEntity conversation,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {EscapeMarkdown(title)}");
        sb.AppendLine();

        if (options.IncludeMetadata)
        {
            sb.AppendLine($"**Created:** {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Updated:** {conversation.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Messages:** {conversation.MessageCount}");
            sb.AppendLine($"**Tokens Used:** {conversation.TokensUsed:N0}");

            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"**Model:** {conversation.ModelId}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("## System Prompt");
            sb.AppendLine();
            sb.AppendLine($"> {conversation.SystemPrompt.Replace("\n", "\n> ")}");
            sb.AppendLine();
        }

        sb.AppendLine("## Conversation");
        sb.AppendLine();

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            // Skip system messages from the export body (they are shown above)
            if (message.Role == "system")
            {
                continue;
            }

            var roleLabel = GetRoleLabel(message.Role);
            sb.AppendLine($"### {roleLabel}");

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"*{message.Timestamp:yyyy-MM-dd HH:mm:ss} UTC*");
            }

            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                sb.AppendLine($"*Model: {message.ModelId}*");
            }

            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();

            // Collect citations if present
            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                var citations = TryParseCitations(message.CitationsJson);
                citationsList.AddRange(citations);
            }

            if (options.IncludeMetadata && message.Role == "assistant")
            {
                var metaParts = new List<string>();
                if (message.TokenCount > 0)
                {
                    metaParts.Add($"Tokens: {message.TokenCount:N0}");
                }
                if (message.GenerationTimeMs.HasValue)
                {
                    metaParts.Add($"Generation: {message.GenerationTimeMs.Value:F0}ms");
                }
                if (metaParts.Count > 0)
                {
                    sb.AppendLine($"*{string.Join(" | ", metaParts)}*");
                    sb.AppendLine();
                }
            }
        }

        // Append citations as footnotes
        if (citationsList.Count > 0)
        {
            sb.AppendLine("## Citations");
            sb.AppendLine();
            for (var i = 0; i < citationsList.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {citationsList[i]}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────

    private static string GetRoleLabel(string role) =>
        role.ToLowerInvariant() switch
        {
            "user" => "User",
            "assistant" => "Assistant",
            "system" => "System",
            _ => role,
        };

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Escape characters that have special meaning in Markdown titles
        return text
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }

    private static List<string> TryParseCitations(string citationsJson)
    {
        var result = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(citationsJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var fileName = element.TryGetProperty("fileName", out var fn)
                    ? fn.GetString() ?? "Unknown"
                    : "Unknown";

                var pageNumber = element.TryGetProperty("pageNumber", out var pn)
                    && pn.ValueKind == JsonValueKind.Number
                    ? pn.GetInt32()
                    : (int?)null;

                var excerpt = element.TryGetProperty("excerpt", out var ex)
                    ? ex.GetString()
                    : null;

                var description = pageNumber.HasValue
                    ? $"{fileName}, page {pageNumber.Value}"
                    : fileName;

                if (!string.IsNullOrWhiteSpace(excerpt))
                {
                    var shortExcerpt = excerpt.Length > 80
                        ? excerpt[..80] + "..."
                        : excerpt;
                    description += $" - \"{shortExcerpt}\"";
                }

                result.Add(description);
            }
        }
        catch (JsonException)
        {
            // CitationsJson was not valid JSON; return empty list
        }

        return result;
    }
}

using System.Text;
using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;
using Markdig;

namespace AgentX.Core.Services.Export.Formats;

/// <summary>
/// Exports conversations and search results to Markdown format.
/// Includes frontmatter metadata, role labels, timestamps, and citations.
/// </summary>
public class MarkdownExport : IExportFormat
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public ExportFormat Format => ExportFormat.Markdown;
    public string FileExtension => ".md";

    public bool Supports<T>() =>
        typeof(T) == typeof(ConversationEntity) ||
        typeof(T) == typeof(IEnumerable<ConversationEntity>) ||
        typeof(T) == typeof(SearchResultExportItem) ||
        typeof(T) == typeof(IEnumerable<SearchResultExportItem>);

    public async Task<object> RenderAsync<T>(T data, ExportOptions options, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            return data switch
            {
                ConversationEntity conversation => RenderConversation(conversation, options),
                IEnumerable<ConversationEntity> conversations => RenderConversations(conversations, options),
                SearchResultExportItem result => RenderSearchResult(result, options),
                IEnumerable<SearchResultExportItem> results => RenderSearchResults(results, options),
                _ => throw new NotSupportedException($"Markdown export does not support type {typeof(T).Name}")
            };
        }, ct);
    }

    private static string RenderConversation(ConversationEntity conversation, ExportOptions options)
    {
        var sb = new StringBuilder();

        // Frontmatter metadata
        if (options.IncludeMetadata)
        {
            sb.AppendLine("---");
            sb.AppendLine($"title: \"{EscapeMarkdown(conversation.Title)}\"");
            sb.AppendLine($"created: {conversation.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}");
            sb.AppendLine($"updated: {conversation.UpdatedAt:yyyy-MM-ddTHH:mm:ssZ}");
            sb.AppendLine($"messages: {conversation.MessageCount}");
            sb.AppendLine($"tokens: {conversation.TokensUsed}");
            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"model: {conversation.ModelId}");
            }
            sb.AppendLine("exported_from: Agent-X");
            sb.AppendLine("---");
            sb.AppendLine();
        }

        // Title
        sb.AppendLine($"# {EscapeMarkdown(options.Title ?? conversation.Title)}");
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

        // System prompt
        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("## System Prompt");
            sb.AppendLine();
            sb.AppendLine($"> {conversation.SystemPrompt.Replace("\n", "\n> ")}");
            sb.AppendLine();
        }

        // Conversation body
        sb.AppendLine("## Conversation");
        sb.AppendLine();

        var messages = conversation.Messages.OrderBy(m => m.SortOrder).ToList();
        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                continue;

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

            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                var citations = TryParseCitations(message.CitationsJson);
                citationsList.AddRange(citations);
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
                    sb.AppendLine($"*{string.Join(" | ", metaParts)}*");
                    sb.AppendLine();
                }
            }
        }

        // Citations
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

    private static string RenderConversations(IEnumerable<ConversationEntity> conversations, ExportOptions options)
    {
        var sb = new StringBuilder();
        var list = conversations.ToList();
        var title = options.Title ?? $"Agent-X Conversations Export ({list.Count})";

        sb.AppendLine($"# {EscapeMarkdown(title)}");
        sb.AppendLine();
        sb.AppendLine($"*Exported {list.Count} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            sb.Append(RenderConversation(list[i], options));
        }

        return sb.ToString();
    }

    private static string RenderSearchResult(SearchResultExportItem result, ExportOptions options)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {EscapeMarkdown(result.DocumentName)}");
        sb.AppendLine();

        if (options.IncludeMetadata)
        {
            sb.AppendLine($"**Relevance:** {result.RelevanceScore:P1}");
            sb.AppendLine();
        }

        sb.AppendLine(result.Content);
        sb.AppendLine();

        if (options.IncludeCitations && result.Citations.Count > 0)
        {
            sb.AppendLine("**Sources:**");
            foreach (var citation in result.Citations)
            {
                sb.AppendLine($"- {citation}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    private static string RenderSearchResults(IEnumerable<SearchResultExportItem> results, ExportOptions options)
    {
        var sb = new StringBuilder();
        var list = results.ToList();

        sb.AppendLine($"# Search Results ({list.Count})");
        sb.AppendLine();
        sb.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        for (var i = 0; i < list.Count; i++)
        {
            var result = list[i];
            sb.AppendLine($"## Result {i + 1}: {EscapeMarkdown(result.DocumentName)}");
            sb.AppendLine();

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"**Relevance:** {result.RelevanceScore:P1}");
                sb.AppendLine();
            }

            sb.AppendLine(result.Content);
            sb.AppendLine();

            if (options.IncludeCitations && result.Citations.Count > 0)
            {
                sb.AppendLine("**Sources:**");
                foreach (var citation in result.Citations)
                {
                    sb.AppendLine($"- {citation}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    private static string GetRoleLabel(string role) =>
        role.ToLowerInvariant() switch
        {
            "user" => "User",
            "assistant" => "Assistant",
            "system" => "System",
            _ => role
        };

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("[", "\\[").Replace("]", "\\]");
    }

    private static List<string> TryParseCitations(string citationsJson)
    {
        var result = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(citationsJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

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
            // Return empty list on parse failure
        }

        return result;
    }
}

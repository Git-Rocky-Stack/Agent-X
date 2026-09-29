using System.Text;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as plain text with minimal formatting.
/// Matches the output produced by <c>ExportService.BuildPlainText</c> exactly.
/// </summary>
public sealed class PlainTextFormatter : IExportFormatter
{
    public ExportFormat Format => ExportFormat.PlainText;
    public string FileExtension => ".txt";
    public string MimeType => "text/plain";

    /// <inheritdoc />
    public Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var title = options.Title ?? conversation.Title;
        return Task.FromResult(BuildPlainText(conversation, options, title));
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

        sb.AppendLine(title);
        sb.AppendLine(new string('=', title.Length));
        sb.AppendLine($"Exported {conversations.Count} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        for (var i = 0; i < conversations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine(new string('-', 60));
                sb.AppendLine();
            }
            sb.Append(BuildPlainText(conversations[i], options, conversations[i].Title));
        }

        return Task.FromResult(sb.ToString());
    }

    // ----------------------------------------------------------------
    //  Core formatting (extracted from ExportService.BuildPlainText)
    // ----------------------------------------------------------------

    private static string BuildPlainText(
        ConversationEntity conversation,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine(title);
        sb.AppendLine(new string('=', Math.Min(title.Length, 80)));
        sb.AppendLine();

        if (options.IncludeMetadata)
        {
            sb.AppendLine($"Created:     {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Updated:     {conversation.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Messages:    {conversation.MessageCount}");
            sb.AppendLine($"Tokens Used: {conversation.TokensUsed:N0}");

            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"Model:       {conversation.ModelId}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("[System Prompt]");
            sb.AppendLine(conversation.SystemPrompt);
            sb.AppendLine();
        }

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                continue;
            }

            var roleLabel = GetRoleLabel(message.Role);
            sb.AppendLine($"[{roleLabel}]");

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"  {message.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            }

            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                sb.AppendLine($"  Model: {message.ModelId}");
            }

            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();

            if (options.IncludeCitations)
            {
                AppendCitations(sb, message.CitationsJson);
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
                    sb.AppendLine($"  ({string.Join(" | ", metaParts)})");
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }

    // ----------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------

    private static string GetRoleLabel(string role) =>
        role.ToLowerInvariant() switch
        {
            "user" => "User",
            "assistant" => "Assistant",
            "system" => "System",
            _ => role,
        };

    /// <summary>
    /// Lists a message's sources with it, numbered as the message's own [n] markers number
    /// them. Each answer numbers its own sources from 1, so one list for the whole export
    /// would not match the markers of any answer after the first.
    /// </summary>
    private static void AppendCitations(StringBuilder sb, string? citationsJson)
    {
        var citations = MessageCitations.Describe(citationsJson);
        if (citations.Count == 0)
        {
            return;
        }

        sb.AppendLine("  Citations:");
        for (var i = 0; i < citations.Count; i++)
        {
            sb.AppendLine($"    {i + 1}. {citations[i]}");
        }

        sb.AppendLine();
    }
}

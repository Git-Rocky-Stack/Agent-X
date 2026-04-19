using System.Text;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as CSV for spreadsheet import and data analysis.
/// Matches the output produced by <c>ExportService.BuildConversationCsv</c>
/// and the batch CSV export path exactly.
/// </summary>
public sealed class CsvFormatter : IExportFormatter
{
    public ExportFormat Format => ExportFormat.Csv;
    public string FileExtension => ".csv";
    public string MimeType => "text/csv";

    /// <inheritdoc />
    public Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(BuildConversationCsv(conversation));
    }

    /// <inheritdoc />
    public Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sb = new StringBuilder();

        // CSV header row — includes ConversationTitle for batch exports
        sb.AppendLine("ConversationTitle,Role,Content,Timestamp,Model,Tokens");

        foreach (var conv in conversations)
        {
            ct.ThrowIfCancellationRequested();
            var messages = conv.Messages
                .OrderBy(m => m.SortOrder)
                .ToList();

            foreach (var message in messages)
            {
                if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.Append(CsvEscape(conv.Title)).Append(',');
                sb.Append(CsvEscape(message.Role)).Append(',');
                sb.Append(CsvEscape(message.Content)).Append(',');
                sb.Append(CsvEscape(message.Timestamp.ToString("O"))).Append(',');
                sb.Append(CsvEscape(message.ModelId ?? "")).Append(',');
                sb.AppendLine(message.TokenCount.ToString());
            }
        }

        return Task.FromResult(sb.ToString());
    }

    // ────────────────────────────────────────────────────────────────
    //  Core formatting (extracted from ExportService.BuildConversationCsv)
    // ────────────────────────────────────────────────────────────────

    private static string BuildConversationCsv(ConversationEntity conversation)
    {
        var sb = new StringBuilder();

        // CSV header row — single conversation has no ConversationTitle column
        // to match the original ExportService.BuildConversationCsv output
        sb.AppendLine("Role,Content,Timestamp,Model,Tokens");

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        foreach (var message in messages)
        {
            // Skip system messages — they are internal directives, not user-facing content
            if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                continue;

            sb.Append(CsvEscape(message.Role)).Append(',');
            sb.Append(CsvEscape(message.Content)).Append(',');
            sb.Append(CsvEscape(message.Timestamp.ToString("O"))).Append(',');
            sb.Append(CsvEscape(message.ModelId ?? "")).Append(',');
            sb.AppendLine(message.TokenCount.ToString());
        }

        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}

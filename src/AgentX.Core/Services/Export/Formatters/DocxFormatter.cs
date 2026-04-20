using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AgentX.Core.Services.Export.Formatters;

/// <summary>
/// Formats conversations as Word documents (.docx) using the OpenXML SDK.
/// Produces a base64-encoded string of the binary DOCX output per the
/// <see cref="IExportFormatter"/> binary convention.
/// <para>
/// The DOCX structure includes a styled title, optional metadata, conversation
/// messages with role labels and timestamps, citations, and a footer — matching
/// the visual structure of Markdown/HTML/PDF exports.
/// </para>
/// </summary>
public sealed class DocxFormatter : IExportFormatter
{
    public ExportFormat Format => ExportFormat.Docx;
    public string FileExtension => ".docx";
    public string MimeType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <inheritdoc />
    public async Task<string> ExportConversationAsync(
        ConversationEntity conversation,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var title = options.Title ?? conversation.Title ?? "Conversation Export";

        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new W.Document();
                var body = mainPart.Document.AppendChild(new W.Body());

                AppendDocxConversation(body, conversation, options, title);

                mainPart.Document.Save();
            }

            return ms.ToArray();
        }, ct);

        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc />
    public async Task<string> ExportConversationsAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var title = options.Title ?? "Conversation Export";

        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new W.Document();
                var body = mainPart.Document.AppendChild(new W.Body());

                // Main title
                body.AppendChild(new W.Paragraph(
                    new W.ParagraphProperties(new W.SpacingBetweenLines { After = "200" }),
                    new W.Run(
                        new W.RunProperties(new W.Bold(), new W.FontSize { Val = "56" }),
                        new W.Text(title) { Space = SpaceProcessingModeValues.Preserve })));

                // Batch metadata
                body.AppendChild(new W.Paragraph(
                    new W.Run(
                        new W.RunProperties(new W.Color { Val = "6C757D" }, new W.FontSize { Val = "18" }),
                        new W.Text($"Exported {conversations.Count} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}"))));

                // Page break before first conversation
                body.AppendChild(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));

                for (var i = 0; i < conversations.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (i > 0)
                    {
                        body.AppendChild(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
                    }

                    AppendDocxConversation(body, conversations[i], options, conversations[i].Title);
                }

                mainPart.Document.Save();
            }

            return ms.ToArray();
        }, ct);

        return Convert.ToBase64String(bytes);
    }

    // ════════════════════════════════════════════════════════════════
    //  Private helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Appends a single conversation's content to a Word document body.
    /// Includes title, metadata, messages, citations, and a footer.
    /// </summary>
    private static void AppendDocxConversation(
        W.Body body,
        ConversationEntity conversation,
        ExportOptions options,
        string? title)
    {
        var displayTitle = title ?? "Conversation";

        // -- Title
        body.AppendChild(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "200" }),
            new W.Run(
                new W.RunProperties(new W.Bold(), new W.FontSize { Val = "56" }),
                new W.Text(displayTitle) { Space = SpaceProcessingModeValues.Preserve })));

        // -- Metadata
        if (options.IncludeMetadata)
        {
            body.AppendChild(new W.Paragraph(
                new W.Run(
                    new W.RunProperties(new W.Color { Val = "6C757D" }, new W.FontSize { Val = "18" }),
                    new W.Text($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")),
                new W.Break(),
                new W.Run(
                    new W.RunProperties(new W.Color { Val = "6C757D" }, new W.FontSize { Val = "18" }),
                    new W.Text($"Messages: {conversation.MessageCount}")),
                new W.Break(),
                new W.Run(
                    new W.RunProperties(new W.Color { Val = "6C757D" }, new W.FontSize { Val = "18" }),
                    new W.Text($"Model: {conversation.ModelId ?? "N/A"}"))));
        }

        // -- System prompt
        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            body.AppendChild(new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "120" }),
                new W.Run(
                    new W.RunProperties(new W.Bold(), new W.Italic(), new W.Color { Val = "6C757D" }, new W.FontSize { Val = "20" }),
                    new W.Text($"System Prompt: {conversation.SystemPrompt}"))));
        }

        // -- Messages
        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role == "system")
                continue;

            var isUser = message.Role == "user";
            var roleLabel = GetRoleLabel(message.Role);
            var roleColor = isUser ? "0D6EFD" : "6C757D";

            // Role label
            body.AppendChild(new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "360" }),
                new W.Run(
                    new W.RunProperties(new W.Bold(), new W.Color { Val = roleColor }, new W.FontSize { Val = "22" }),
                    new W.Text(roleLabel))));

            // Timestamp
            if (options.IncludeTimestamps)
            {
                body.AppendChild(new W.Paragraph(
                    new W.Run(
                        new W.RunProperties(new W.Italic(), new W.Color { Val = "ADB5BD" }, new W.FontSize { Val = "16" }),
                        new W.Text(message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss UTC")))));
            }

            // Model info
            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                body.AppendChild(new W.Paragraph(
                    new W.Run(
                        new W.RunProperties(new W.Italic(), new W.Color { Val = "ADB5BD" }, new W.FontSize { Val = "16" }),
                        new W.Text($"Model: {message.ModelId}"))));
            }

            // Content -- each line becomes a separate paragraph for accurate representation
            var lines = message.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                body.AppendChild(new W.Paragraph(
                    new W.Run(new W.Text(line) { Space = SpaceProcessingModeValues.Preserve })));
            }

            // Generation metadata for assistant messages
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
                    body.AppendChild(new W.Paragraph(
                        new W.Run(
                            new W.RunProperties(new W.Italic(), new W.Color { Val = "ADB5BD" }, new W.FontSize { Val = "16" }),
                            new W.Text(string.Join(" | ", metaParts)))));
                }
            }

            // Collect citations
            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                citationsList.AddRange(TryParseCitations(message.CitationsJson));
            }
        }

        // -- Citations
        if (citationsList.Count > 0)
        {
            body.AppendChild(new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "480" }),
                new W.Run(
                    new W.RunProperties(new W.Bold(), new W.FontSize { Val = "28" }),
                    new W.Text("Citations"))));

            for (var i = 0; i < citationsList.Count; i++)
            {
                body.AppendChild(new W.Paragraph(
                    new W.Run(
                        new W.RunProperties(new W.FontSize { Val = "18" }, new W.Color { Val = "6C757D" }),
                        new W.Text($"{i + 1}. {citationsList[i]}"))));
            }
        }

        // -- Footer
        body.AppendChild(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "480" }),
            new W.Run(
                new W.RunProperties(new W.Italic(), new W.Color { Val = "ADB5BD" }, new W.FontSize { Val = "16" }),
                new W.Text($"Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}"))));
    }

    /// <summary>
    /// Returns a human-readable label for a message role.
    /// </summary>
    private static string GetRoleLabel(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "user" => "User",
            "assistant" => "Assistant",
            "system" => "System",
            _ => role,
        };
    }

    /// <summary>
    /// Attempts to parse a CitationsJson string into a list of human-readable
    /// citation descriptions. Returns an empty list on parse failure.
    /// </summary>
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

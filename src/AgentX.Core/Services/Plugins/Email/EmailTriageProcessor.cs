using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Converts an <see cref="EmailMessage"/> into parameters suitable for
/// <see cref="IInboxService.TriageExternalAsync"/>, building a rich
/// searchable text representation for the knowledge vault.
/// </summary>
public sealed class EmailTriageProcessor
{
    private const string PluginId = "com.agentx.email";
    private const string SourceCategory = "email_message";
    private const string SourceType = "email-connector";

    private readonly ILogger _log;

    public EmailTriageProcessor(ILogger logger)
    {
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<EmailTriageProcessor>();
    }

    /// <summary>
    /// Converts an <see cref="EmailMessage"/> into the 10-parameter tuple
    /// expected by <see cref="IInboxService.TriageExternalAsync"/>.
    /// </summary>
    public (string FileName, string FileType, string SourceType, string? SourceUrl,
            string SourcePluginId, string? SourceCategory, string ExternalId,
            string? ContentPreview, string ContentText)
        ConvertToInboxParameters(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var fileName = $"Email: {message.Subject}";
        var fileType = "EmailMessage";
        var externalId = $"{message.SourceProvider}:{message.FolderId}:{message.Id}";
        var contentPreview = message.BodyPreview;
        var contentText = ExtractSearchableContent(message);

        return (fileName, fileType, SourceType, message.WebLink,
                PluginId, SourceCategory, externalId, contentPreview, contentText);
    }

    /// <summary>
    /// Builds a rich text representation of the email for full-text search.
    /// </summary>
    public string ExtractSearchableContent(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var parts = new List<string>();

        // Subject
        parts.Add($"Subject: {message.Subject}");

        // From
        parts.Add($"From: {FormatContact(message.From)}");

        // To
        if (message.To.Count > 0)
            parts.Add($"To: {string.Join(", ", message.To.Select(FormatContact))}");

        // Cc
        if (message.Cc.Count > 0)
            parts.Add($"Cc: {string.Join(", ", message.Cc.Select(FormatContact))}");

        // Date
        parts.Add($"Date: {message.ReceivedAt:yyyy-MM-dd HH:mm}");

        // Folder
        parts.Add($"Folder: {message.FolderName}");

        // Flags
        var flags = new List<string>();
        if (message.IsStarred) flags.Add("Starred");
        if (message.HasAttachments) flags.Add("HasAttachments");
        if (message.IsRead) flags.Add("Read");
        if (flags.Count > 0)
            parts.Add($"Flags: {string.Join(", ", flags)}");

        // Attachments
        if (message.AttachmentNames.Count > 0)
            parts.Add($"Attachments: {string.Join(", ", message.AttachmentNames)}");

        // Source provider
        parts.Add($"Source: {message.SourceProvider}");

        // Body text (preferred over HTML for search)
        if (!string.IsNullOrWhiteSpace(message.BodyText))
            parts.Add(message.BodyText);
        else if (!string.IsNullOrWhiteSpace(message.BodyHtml))
            parts.Add(StripHtmlTags(message.BodyHtml));

        return string.Join("\n\n", parts);
    }

    private static string FormatContact(EmailContact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.DisplayName))
            return contact.EmailAddress;
        return $"{contact.DisplayName} <{contact.EmailAddress}>";
    }

    /// <summary>
    /// Strips HTML tags for plain-text search indexing.
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        // Remove HTML tags
        var result = new System.Text.StringBuilder(html.Length);
        var inTag = false;

        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) result.Append(c);
        }

        // Decode common HTML entities
        return result.ToString()
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");
    }
}
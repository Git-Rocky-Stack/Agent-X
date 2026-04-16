namespace AgentX.Core.Services.Plugins.Email.Models;

/// <summary>
/// Unified email message DTO returned by all email providers.
/// Provider-specific JSON is normalized into this shape.
/// </summary>
public sealed class EmailMessage
{
    public string Id { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyPreview { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public string BodyText { get; init; } = string.Empty;
    public EmailContact From { get; init; } = new();
    public List<EmailContact> To { get; init; } = [];
    public List<EmailContact> Cc { get; init; } = [];
    public List<EmailContact> Bcc { get; init; } = [];
    public DateTime ReceivedAt { get; init; }
    public bool IsRead { get; init; }
    public bool IsStarred { get; init; }
    public bool HasAttachments { get; init; }
    public string FolderName { get; init; } = string.Empty;
    public string FolderId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public string SourceProvider { get; init; } = string.Empty;
    public List<string> AttachmentNames { get; init; } = [];
    public string? WebLink { get; init; }
}
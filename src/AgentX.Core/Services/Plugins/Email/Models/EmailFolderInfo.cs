namespace AgentX.Core.Services.Plugins.Email.Models;

/// <summary>
/// Metadata for a mail folder (Gmail label or Outlook mailFolder).
/// </summary>
public sealed class EmailFolderInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public int UnreadCount { get; init; }
    public string SourceProvider { get; init; } = string.Empty;
}

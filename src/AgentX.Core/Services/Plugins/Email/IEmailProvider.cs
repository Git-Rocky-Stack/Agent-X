using AgentX.Core.Services.Plugins.Email.Models;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Abstraction for an email provider (Gmail, Outlook).
/// Each provider handles API-specific pagination, auth, and normalization.
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g. "google", "microsoft").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Lists all mail folders/labels available to the authenticated user.
    /// </summary>
    Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches messages from a specific folder.
    /// Returns the messages and a delta token for incremental sync.
    /// </summary>
    /// <param name="folderId">The folder to fetch from.</param>
    /// <param name="maxResults">Maximum number of messages to return.</param>
    /// <param name="deltaToken">Previous delta token for incremental sync, or null for full sync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of messages and the next delta token (null if no more changes).</returns>
    Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(
        string folderId,
        int maxResults = 50,
        string? deltaToken = null,
        CancellationToken cancellationToken = default);
}
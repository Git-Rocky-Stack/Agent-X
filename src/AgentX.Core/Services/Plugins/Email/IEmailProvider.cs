using AgentX.Core.Services.Plugins.Email.Models;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Abstraction for an email provider (Gmail, Outlook).
/// Each provider handles API-specific pagination, auth, and normalization.
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// The folder id every provider uses for the account's inbox, whatever the provider's own
    /// identifier for it. Gmail's inbox label is literally "INBOX"; Outlook reports its inbox
    /// under this id too, so the default folder selection works for both.
    /// </summary>
    public const string InboxFolderId = "INBOX";

    /// <summary>
    /// Unique identifier for this provider (e.g. "google", "microsoft").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Lists all mail folders/labels available to the authenticated user. The inbox is
    /// reported with the id <see cref="InboxFolderId"/>.
    /// </summary>
    Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches messages from a specific folder.
    /// Returns the messages and a delta token for incremental sync.
    /// </summary>
    /// <param name="folderId">The folder to fetch from.</param>
    /// <param name="maxResults">Maximum number of messages to return.</param>
    /// <param name="deltaToken">Previous delta token for incremental sync, or null for full sync.</param>
    /// <param name="receivedAfterUtc">
    /// For a full sync (no delta token): only messages received after this instant are read.
    /// This is how the "sync days back" setting reaches the provider. An incremental sync
    /// returns whatever changed since the token and ignores it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of messages and the token to pass next time: the provider's sync position, or a
    /// continuation when <paramref name="maxResults"/> stopped the read early, so the next call
    /// resumes where this one stopped.
    /// </returns>
    Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(
        string folderId,
        int maxResults = 50,
        string? deltaToken = null,
        DateTime? receivedAfterUtc = null,
        CancellationToken cancellationToken = default);
}

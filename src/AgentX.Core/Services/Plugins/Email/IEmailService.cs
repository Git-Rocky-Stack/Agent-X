using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email.Models;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// High-level email service exposed by the EmailPlugin.
/// Delegates to registered IEmailProvider instances.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gets recent messages across all enabled folders and providers.
    /// </summary>
    Task<IReadOnlyList<EmailMessage>> GetRecentMessagesAsync(int count = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a sync cycle across all providers and folders.
    /// </summary>
    Task<SyncResult> SyncMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available mail folders from all connected providers.
    /// </summary>
    Task<IReadOnlyList<EmailFolderInfo>> ListAvailableFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sync settings.
    /// </summary>
    Task<EmailSyncSettings> GetSyncSettingsAsync();

    /// <summary>
    /// Updates and persists sync settings.
    /// </summary>
    Task UpdateSyncSettingsAsync(EmailSyncSettings settings);

    /// <summary>
    /// Returns true if at least one email provider is connected.
    /// </summary>
    Task<bool> IsConnectedAsync();
}
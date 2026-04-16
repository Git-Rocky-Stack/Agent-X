using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// IEmailService implementation that delegates to the EmailPlugin's
/// registered providers for all operations.
/// </summary>
public sealed class EmailService : IEmailService
{
    private readonly EmailPlugin _plugin;
    private readonly ILogger _log;

    public EmailService(EmailPlugin plugin, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<EmailService>();
    }

    public async Task<IReadOnlyList<EmailMessage>> GetRecentMessagesAsync(
        int count = 20, CancellationToken cancellationToken = default)
    {
        var results = new List<EmailMessage>();

        foreach (var provider in _plugin.Providers)
        {
            try
            {
                var (messages, _) = await provider.GetMessagesAsync(
                    "INBOX", maxResults: count, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                results.AddRange(messages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Failed to get recent messages from {ProviderId}", provider.ProviderId);
            }
        }

        return results
            .OrderByDescending(m => m.ReceivedAt)
            .Take(count)
            .ToList();
    }

    public async Task<SyncResult> SyncMessagesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSyncSettingsAsync().ConfigureAwait(false);

        // SyncService is accessed via the plugin's internal state.
        // For now, delegate to providers directly.
        var totalAdded = 0;
        var totalFailed = 0;
        var startedAt = DateTime.UtcNow;

        foreach (var provider in _plugin.Providers)
        {
            try
            {
                var folders = await provider.ListFoldersAsync(cancellationToken).ConfigureAwait(false);
                var enabledIds = settings.EnabledFolders
                    .Where(kv => kv.Value).Select(kv => kv.Key).ToHashSet();

                foreach (var folder in folders.Where(f => enabledIds.Contains(f.Id)))
                {
                    var (messages, _) = await provider.GetMessagesAsync(
                        folder.Id, settings.MaxMessagesPerSync,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    totalAdded += messages.Count;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                totalFailed++;
                _log.Error(ex, "Failed to sync messages from {ProviderId}", provider.ProviderId);
            }
        }

        return new SyncResult
        {
            ItemsAdded = totalAdded,
            ItemsFailed = totalFailed,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
        };
    }

    public async Task<IReadOnlyList<EmailFolderInfo>> ListAvailableFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<EmailFolderInfo>();

        foreach (var provider in _plugin.Providers)
        {
            try
            {
                var folders = await provider.ListFoldersAsync(cancellationToken).ConfigureAwait(false);
                results.AddRange(folders);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Failed to list folders from {ProviderId}", provider.ProviderId);
            }
        }

        return results;
    }

    public Task<EmailSyncSettings> GetSyncSettingsAsync()
    {
        return Task.FromResult(_plugin.GetSettings());
    }

    public Task UpdateSyncSettingsAsync(EmailSyncSettings settings)
    {
        _plugin.UpdateSettings(settings);
        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync()
    {
        return Task.FromResult(_plugin.Providers.Count > 0);
    }
}
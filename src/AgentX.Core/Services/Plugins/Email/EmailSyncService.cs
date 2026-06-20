using System.Text.Json;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Orchestrates email sync cycles: fetches messages from all registered providers,
/// converts them into inbox items via <see cref="EmailTriageProcessor"/>,
/// and pushes them into the Smart Inbox via <see cref="IInboxService.TriageExternalAsync"/>.
/// </summary>
public sealed class EmailSyncService
{
    private static readonly JsonSerializerOptions DeltaTokenJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string DeltaTokenFileName = "email-delta-tokens.json";

    private readonly IInboxService _inboxService;
    private readonly EmailTriageProcessor _processor;
    private readonly ILogger _log;
    private readonly string _pluginDataPath;

    public EmailSyncService(
        IInboxService inboxService,
        EmailTriageProcessor processor,
        ILogger logger,
        string pluginDataPath)
    {
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<EmailSyncService>();
        _pluginDataPath = pluginDataPath ?? throw new ArgumentNullException(nameof(pluginDataPath));
    }

    /// <summary>
    /// Runs a full sync cycle across the given providers and enabled folders.
    /// For each enabled folder on each provider, fetches messages, processes them
    /// through <see cref="EmailTriageProcessor"/>, and pushes them into the Smart Inbox.
    /// </summary>
    public async Task<SyncResult> SyncAsync(
        IReadOnlyList<IEmailProvider> providers,
        EmailSyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var totalAdded = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var totalFailed = 0;
        var deltaTokens = await LoadDeltaTokensAsync().ConfigureAwait(false);
        var updatedDeltaTokens = new Dictionary<string, string>();

        _log.Information(
            "Starting email sync across {ProviderCount} provider(s) with {EnabledFolderCount} enabled folder(s)",
            providers.Count,
            settings.EnabledFolders.Count(kv => kv.Value));

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var enabledFolderIds = settings.EnabledFolders
                    .Where(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();

                if (enabledFolderIds.Count == 0)
                {
                    _log.Debug("No enabled folders for provider {ProviderId} — skipping", provider.ProviderId);
                    continue;
                }

                var folders = await provider.ListFoldersAsync(cancellationToken).ConfigureAwait(false);

                foreach (var folder in folders)
                {
                    if (!enabledFolderIds.Contains(folder.Id))
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    var deltaKey = $"{provider.ProviderId}:{folder.Id}";
                    var existingDeltaToken = deltaTokens.GetValueOrDefault(deltaKey);

                    var (messages, newDeltaToken) = await provider.GetMessagesAsync(
                        folder.Id, settings.MaxMessagesPerSync,
                        deltaToken: existingDeltaToken,
                        cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (newDeltaToken is not null)
                        updatedDeltaTokens[deltaKey] = newDeltaToken;

                    foreach (var email in messages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var (fileName, fileType, sourceType, sourceUrl,
                                 sourcePluginId, sourceCategory, externalId,
                                 contentPreview, contentText) = _processor.ConvertToInboxParameters(email);

                            var inboxItem = await _inboxService.TriageExternalAsync(
                                fileName, fileType, sourceType, sourceUrl,
                                sourcePluginId, sourceCategory, externalId,
                                contentPreview, contentText).ConfigureAwait(false);

                            if (inboxItem.ProcessedAt == inboxItem.AddedAt || inboxItem.AddedAt < startedAt.AddSeconds(-1))
                            {
                                totalSkipped++;
                            }
                            else
                            {
                                totalAdded++;
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            totalFailed++;
                            _log.Error(ex,
                                "Failed to process email {EmailId} from {ProviderId}/{FolderId}",
                                email.Id, provider.ProviderId, folder.Id);
                        }
                    }

                    _log.Debug(
                        "Processed {MessageCount} emails from {ProviderId}/{FolderId}",
                        messages.Count, provider.ProviderId, folder.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                totalFailed++;
                _log.Error(ex,
                    "Failed to sync with email provider {ProviderId}",
                    provider.ProviderId);
            }
        }

        // Persist updated delta tokens.
        if (updatedDeltaTokens.Count > 0)
        {
            foreach (var kv in updatedDeltaTokens)
                deltaTokens[kv.Key] = kv.Value;

            await SaveDeltaTokensAsync(deltaTokens).ConfigureAwait(false);
        }

        var completedAt = DateTime.UtcNow;

        var result = new SyncResult
        {
            ItemsAdded = totalAdded,
            ItemsUpdated = totalUpdated,
            ItemsSkipped = totalSkipped,
            ItemsFailed = totalFailed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };

        _log.Information(
            "Email sync complete. Added={Added} Updated={Updated} Skipped={Skipped} Failed={Failed} Duration={Duration}",
            result.ItemsAdded, result.ItemsUpdated, result.ItemsSkipped,
            result.ItemsFailed, result.Duration);

        return result;
    }

    // ── Private: delta token persistence ────────────────────────────────────────

    private async Task<Dictionary<string, string>> LoadDeltaTokensAsync()
    {
        var path = Path.Combine(_pluginDataPath, DeltaTokenFileName);

        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(json, DeltaTokenJsonOptions);
            return tokens ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load email delta tokens from {Path} — starting fresh", path);
            return new Dictionary<string, string>();
        }
    }

    private async Task SaveDeltaTokensAsync(Dictionary<string, string> tokens)
    {
        var path = Path.Combine(_pluginDataPath, DeltaTokenFileName);

        try
        {
            var json = JsonSerializer.Serialize(tokens, DeltaTokenJsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            _log.Debug("Saved {Count} email delta tokens to {Path}", tokens.Count, path);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save email delta tokens to {Path}", path);
        }
    }
}

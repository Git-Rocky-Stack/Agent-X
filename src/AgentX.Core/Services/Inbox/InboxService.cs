using System.Security.Cryptography;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Intelligence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Inbox;

/// <summary>
/// EF Core-backed implementation of <see cref="IInboxService"/>.
/// Provides file triage, AI-powered 2-3 sentence previews with collection and tag
/// suggestions, and batch accept/reject operations. Accepting an item copies its file
/// into app storage and imports it into the knowledge vault through
/// <see cref="IDocumentService.ImportFileAsync"/>.
/// </summary>
public sealed class InboxService : IInboxService
{
    /// <summary>Folder under the app data root that holds every file the inbox owns.</summary>
    internal const string InboxStoreFolderName = "Inbox";

    /// <summary>Subfolder of the inbox store for connector (calendar, email) content.</summary>
    internal const string ExternalStoreFolderName = "External";

    /// <summary>Subfolder of the inbox store for accepted watch-folder files and web clips.</summary>
    internal const string AcceptedStoreFolderName = "Accepted";

    private readonly AgentXDbContext _db;
    private readonly ISummaryService _summaryService;
    private readonly ICollectionService _collectionService;
    private readonly IAiService _aiService;
    private readonly IDocumentService? _documentService;
    private readonly IAppPathService _appPaths;

    /// <summary>
    /// Maximum characters read from a file for AI preview generation.
    /// Keeps the prompt well within typical context window limits.
    /// </summary>
    private const int PreviewReadChars = 2000;

    /// <summary>
    /// Inference options tuned for factual, low-temperature triage outputs.
    /// </summary>
    private static readonly ChatOptions TriageChatOptions = new()
    {
        Temperature = 0.2,
        MaxTokens = 512,
    };

    public InboxService(
        AgentXDbContext dbContext,
        ISummaryService summaryService,
        ICollectionService collectionService,
        IAiService aiService,
        IDocumentService? documentService = null,
        IAppPathService? appPaths = null)
    {
        _db = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _collectionService = collectionService ?? throw new ArgumentNullException(nameof(collectionService));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _documentService = documentService;
        _appPaths = appPaths ?? new AppPathService();
    }

    // ── Ingestion ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InboxItemEntity> AddToInboxAsync(
        string filePath,
        long? watchFolderId = null,
        string? sourceType = null,
        string? sourceUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);

        // Return an existing pending item if one already exists for this path
        // to avoid duplicate inbox rows from rapid watcher events.
        var existing = await _db.InboxItems
            .FirstOrDefaultAsync(i => i.FilePath == normalizedPath && i.Status == "pending")
            .ConfigureAwait(false);

        if (existing is not null)
        {
            Log.Debug(
                "InboxService: File already pending in inbox, skipping duplicate: {FilePath}",
                normalizedPath);
            return existing;
        }

        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException(
                $"Cannot add file to inbox — file does not exist: {normalizedPath}",
                normalizedPath);
        }

        var fileInfo = new FileInfo(normalizedPath);
        var extension = fileInfo.Extension;

        var item = new InboxItemEntity
        {
            FilePath = normalizedPath,
            FileName = fileInfo.Name,
            FileType = FileTypeHelper.GetFileCategory(extension),
            FileSizeBytes = fileInfo.Length,
            Status = "pending",
            AddedAt = DateTime.UtcNow,
            WatchFolderId = watchFolderId,
            SourceType = sourceType,
            SourceUrl = sourceUrl,
        };

        _db.InboxItems.Add(item);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        Log.Information(
            "InboxService: Added {FileName} to inbox (ID {ItemId}, size {SizeBytes} bytes, source folder {WatchFolderId})",
            item.FileName, item.Id, item.FileSizeBytes, watchFolderId?.ToString() ?? "none");

        return item;
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxItemEntity>> GetPendingItemsAsync()
    {
        try
        {
            var items = await _db.InboxItems
                .Where(i => i.Status == "pending")
                .OrderBy(i => i.AddedAt)
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);

            Log.Debug("InboxService: Retrieved {Count} pending inbox items", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to retrieve pending inbox items");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxItemEntity>> GetAllItemsAsync(
        string? statusFilter = null,
        int skip = 0,
        int take = 50)
    {
        try
        {
            var query = _db.InboxItems.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                query = query.Where(i => i.Status == statusFilter);
            }

            var items = await query
                .OrderByDescending(i => i.AddedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync()
                .ConfigureAwait(false);

            Log.Debug(
                "InboxService: Retrieved {Count} inbox items (filter={Filter}, skip={Skip}, take={Take})",
                items.Count, statusFilter ?? "all", skip, take);

            return items;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to retrieve inbox items");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetPendingCountAsync()
    {
        try
        {
            return await _db.InboxItems
                .CountAsync(i => i.Status == "pending")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to get pending inbox count");
            throw;
        }
    }

    // ── Single-item triage ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InboxAcceptResult> AcceptItemAsync(long itemId, long? collectionId = null)
    {
        try
        {
            var item = await RequireItemAsync(itemId).ConfigureAwait(false);

            // A caller-supplied collectionId overrides the AI suggestion.
            string? overrideName = null;
            if (collectionId.HasValue)
            {
                // Refresh the denormalized name if the override differs.
                var collection = await _collectionService
                    .GetCollectionAsync(collectionId.Value)
                    .ConfigureAwait(false);
                overrideName = collection?.Name;
            }

            var result = await AcceptIntoVaultAsync(item, collectionId, overrideName).ConfigureAwait(false);

            Log.Information(
                "InboxService: Accepted inbox item {ItemId} '{FileName}' ({Outcome}, document {DocumentId}, collection {CollectionId})",
                item.Id, item.FileName, result.Outcome, result.DocumentId?.ToString() ?? "none",
                item.SuggestedCollectionId?.ToString() ?? "none");

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to accept inbox item {ItemId}", itemId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<InboxBatchAcceptResult> AcceptAllPendingAsync()
    {
        try
        {
            var pending = await _db.InboxItems
                .Where(i => i.Status == "pending")
                .OrderBy(i => i.AddedAt)
                .ToListAsync()
                .ConfigureAwait(false);

            if (pending.Count == 0)
            {
                Log.Debug("InboxService: AcceptAllPending - no pending items found");
                return InboxBatchAcceptResult.Empty;
            }

            var result = await AcceptBatchAsync(pending, collectionId: null, overrideName: null).ConfigureAwait(false);

            Log.Information(
                "InboxService: Accept-all finished. Imported={Imported} AlreadyInVault={Linked} Failed={Failed}",
                result.Imported, result.AlreadyInVault, result.Failed);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to accept all pending inbox items");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RejectItemAsync(long itemId)
    {
        try
        {
            var item = await RequireItemAsync(itemId).ConfigureAwait(false);

            item.Status = "rejected";
            item.ProcessedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Rejected inbox item {ItemId} '{FileName}'",
                item.Id, item.FileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to reject inbox item {ItemId}", itemId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeferItemAsync(long itemId)
    {
        try
        {
            var item = await RequireItemAsync(itemId).ConfigureAwait(false);

            item.Status = "deferred";
            item.ProcessedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Deferred inbox item {ItemId} '{FileName}'",
                item.Id, item.FileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to defer inbox item {ItemId}", itemId);
            throw;
        }
    }

    // ── Batch triage ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InboxBatchAcceptResult> AcceptSelectedAsync(
        IEnumerable<long> itemIds,
        long? collectionId = null)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var idList = itemIds.Distinct().ToList();
        if (idList.Count == 0) return InboxBatchAcceptResult.Empty;

        try
        {
            // Resolve the override collection name once rather than per-row.
            string? overrideName = null;
            if (collectionId.HasValue)
            {
                var col = await _collectionService
                    .GetCollectionAsync(collectionId.Value)
                    .ConfigureAwait(false);
                overrideName = col?.Name;
            }

            var items = await _db.InboxItems
                .Where(i => idList.Contains(i.Id))
                .ToListAsync()
                .ConfigureAwait(false);

            var result = await AcceptBatchAsync(items, collectionId, overrideName).ConfigureAwait(false);

            Log.Information(
                "InboxService: Batch-accepted {Accepted}/{Requested} inbox items (collection {CollectionId}, failed {Failed})",
                result.Accepted, idList.Count, collectionId?.ToString() ?? "per-item", result.Failed);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to batch-accept inbox items");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RejectSelectedAsync(IEnumerable<long> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var idList = itemIds.Distinct().ToList();
        if (idList.Count == 0) return;

        try
        {
            var items = await _db.InboxItems
                .Where(i => idList.Contains(i.Id))
                .ToListAsync()
                .ConfigureAwait(false);

            var now = DateTime.UtcNow;
            foreach (var item in items)
            {
                item.Status = "rejected";
                item.ProcessedAt = now;
            }

            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Batch-rejected {Count}/{Requested} inbox items",
                items.Count, idList.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to batch-reject inbox items");
            throw;
        }
    }

    // ── AI preview generation ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task GeneratePreviewAsync(long itemId, CancellationToken ct = default)
    {
        InboxItemEntity item;
        try
        {
            item = await RequireItemAsync(itemId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: GeneratePreview — could not load item {ItemId}", itemId);
            throw;
        }

        Log.Information(
            "InboxService: Generating AI preview for inbox item {ItemId} '{FileName}'",
            item.Id, item.FileName);

        // Read up to PreviewReadChars characters from the file.
        var snippet = await ReadFileSnippetAsync(item.FilePath, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(snippet))
        {
            Log.Warning(
                "InboxService: File is empty or unreadable for item {ItemId} '{FileName}' — skipping preview",
                item.Id, item.FileName);
            return;
        }

        // Fetch the current collection list for the suggestion prompt.
        var collections = await _collectionService
            .GetAllCollectionsAsync()
            .ConfigureAwait(false);

        var collectionNames = collections.Count > 0
            ? string.Join(", ", collections.Select(c => c.Name))
            : "none available";

        // Single AI call that returns preview, collection suggestion, and tags in a
        // structured format so we can parse them with simple string splitting.
        var prompt = BuildTriagePrompt(item.FileName, snippet, collectionNames);

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        string aiResponse;
        try
        {
            var sb = new StringBuilder(512);
            await foreach (var token in _aiService
                               .StreamChatAsync(messages, options: TriageChatOptions, ct: ct)
                               .WithCancellation(ct)
                               .ConfigureAwait(false))
            {
                sb.Append(token);
            }

            aiResponse = sb.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            Log.Warning(
                "InboxService: Preview generation cancelled for item {ItemId} '{FileName}'",
                item.Id, item.FileName);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "InboxService: AI call failed during preview generation for item {ItemId} '{FileName}'",
                item.Id, item.FileName);
            throw;
        }

        // Parse the structured AI response.
        ParseTriageResponse(
            aiResponse,
            out var preview,
            out var suggestedCollectionName,
            out var suggestedTags);

        item.Preview = preview;
        item.SuggestedTags = suggestedTags;

        // Resolve the suggested collection name to an ID if possible.
        if (!string.IsNullOrWhiteSpace(suggestedCollectionName))
        {
            var matched = collections.FirstOrDefault(c =>
                string.Equals(c.Name, suggestedCollectionName, StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                item.SuggestedCollectionId = matched.Id;
                item.SuggestedCollectionName = matched.Name;
            }
            else
            {
                // Store the name even if it does not match an existing collection;
                // the UI can show it as an informational suggestion.
                item.SuggestedCollectionName = suggestedCollectionName;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        Log.Information(
            "InboxService: Preview generated for item {ItemId} '{FileName}' " +
            "(collection: '{CollectionName}', tags: '{Tags}')",
            item.Id, item.FileName,
            item.SuggestedCollectionName ?? "none",
            item.SuggestedTags ?? "none");
    }

    /// <inheritdoc />
    public async Task GenerateAllPreviewsAsync(CancellationToken ct = default)
    {
        try
        {
            // Only target pending items that have not yet had a preview generated.
            var items = await _db.InboxItems
                .Where(i => i.Status == "pending" && i.Preview == null)
                .OrderBy(i => i.AddedAt)
                .AsNoTracking()
                .Select(i => i.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            Log.Information(
                "InboxService: GenerateAllPreviews — {Count} items require preview generation",
                items.Count);

            var succeeded = 0;
            var failed = 0;

            foreach (var id in items)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await GeneratePreviewAsync(id, ct).ConfigureAwait(false);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Log and continue — a single bad file should not abort the batch.
                    Log.Warning(
                        ex,
                        "InboxService: Preview generation failed for item {ItemId}, continuing batch",
                        id);
                    failed++;
                }
            }

            Log.Information(
                "InboxService: GenerateAllPreviews complete — {Succeeded} succeeded, {Failed} failed",
                succeeded, failed);
        }
        catch (OperationCanceledException)
        {
            Log.Information("InboxService: GenerateAllPreviews was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: GenerateAllPreviews failed unexpectedly");
            throw;
        }
    }

    // ── Maintenance ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task DeleteProcessedItemsAsync()
    {
        try
        {
            var processed = await _db.InboxItems
                .Where(i => i.Status == "accepted"
                         || i.Status == "rejected"
                         || i.Status == "deferred")
                .ToListAsync()
                .ConfigureAwait(false);

            if (processed.Count == 0)
            {
                Log.Debug("InboxService: DeleteProcessedItems — no processed items to remove");
                return;
            }

            _db.InboxItems.RemoveRange(processed);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Deleted {Count} processed inbox items",
                processed.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to delete processed inbox items");
            throw;
        }
    }

    // ── External (plugin-sourced) items ────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InboxItemEntity> TriageExternalAsync(
        string fileName,
        string fileType,
        string sourceType,
        string? sourceUrl,
        string sourcePluginId,
        string? sourceCategory,
        string externalId,
        string? contentPreview,
        string contentText)
    {
        var result = await UpsertExternalAsync(
            fileName, fileType, sourceType, sourceUrl, sourcePluginId,
            sourceCategory, externalId, contentPreview, contentText).ConfigureAwait(false);
        return result.Item;
    }

    /// <inheritdoc />
    public async Task<ExternalTriageResult> UpsertExternalAsync(
        string fileName,
        string fileType,
        string sourceType,
        string? sourceUrl,
        string sourcePluginId,
        string? sourceCategory,
        string externalId,
        string? contentPreview,
        string contentText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        contentText ??= string.Empty;

        // The provider item is identified by (plugin, external ID). The external ID is kept
        // verbatim on the row for dedupe; only its hash ever reaches the file system.
        var contentPath = BuildExternalContentPath(GetInboxStoreRoot(), sourcePluginId, externalId);

        var existing = await _db.InboxItems
            .FirstOrDefaultAsync(i =>
                i.ExternalId == externalId &&
                i.SourcePluginId == sourcePluginId)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var created = await CreateExternalItemAsync(
                contentPath, fileName, fileType, sourceType, sourceUrl, sourcePluginId,
                sourceCategory, externalId, contentPreview, contentText).ConfigureAwait(false);
            return new ExternalTriageResult(created, ExternalTriageOutcome.Created);
        }

        return await UpdateExternalItemAsync(
            existing, contentPath, fileName, fileType, sourceType, sourceUrl,
            sourceCategory, contentPreview, contentText).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the content file for a new external item, creates its accepted row, and
    /// bridges it into the document library when <see cref="IDocumentService"/> is available.
    /// </summary>
    private async Task<InboxItemEntity> CreateExternalItemAsync(
        string contentPath,
        string fileName,
        string fileType,
        string sourceType,
        string? sourceUrl,
        string sourcePluginId,
        string? sourceCategory,
        string externalId,
        string? contentPreview,
        string contentText)
    {
        await WriteContentFileAsync(contentPath, contentText).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        var item = new InboxItemEntity
        {
            FilePath = contentPath,
            FileName = fileName,
            FileType = fileType,
            FileSizeBytes = new FileInfo(contentPath).Length,
            Status = "accepted", // Auto-accept external items
            Preview = contentPreview,
            AddedAt = now,
            ProcessedAt = now,
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            SourcePluginId = sourcePluginId,
            SourceCategory = sourceCategory,
            ExternalId = externalId,
        };

        _db.InboxItems.Add(item);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        await BridgeExternalItemAsync(item).ConfigureAwait(false);

        Log.Information(
            "InboxService: TriageExternal - added '{FileName}' (Plugin={PluginId}, ExternalId={ExternalId})",
            fileName, sourcePluginId, externalId);

        return item;
    }

    /// <summary>
    /// Refreshes an existing external row when the provider item changed. The content file
    /// is compared with the new text; metadata (title, preview, link, category) is compared
    /// field by field. Nothing is written when both match.
    /// </summary>
    private async Task<ExternalTriageResult> UpdateExternalItemAsync(
        InboxItemEntity existing,
        string contentPath,
        string fileName,
        string fileType,
        string sourceType,
        string? sourceUrl,
        string? sourceCategory,
        string? contentPreview,
        string contentText)
    {
        var contentChanged = !await ContentFileMatchesAsync(existing.FilePath, contentText).ConfigureAwait(false);
        var nameChanged = !string.Equals(existing.FileName, fileName, StringComparison.Ordinal);
        var metadataChanged = nameChanged
            || !string.Equals(existing.FileType, fileType, StringComparison.Ordinal)
            || !string.Equals(existing.SourceType, sourceType, StringComparison.Ordinal)
            || !string.Equals(existing.SourceUrl, sourceUrl, StringComparison.Ordinal)
            || !string.Equals(existing.SourceCategory, sourceCategory, StringComparison.Ordinal)
            || !string.Equals(existing.Preview, contentPreview, StringComparison.Ordinal);

        if (!contentChanged && !metadataChanged)
        {
            Log.Debug(
                "InboxService: External item unchanged (ExternalId={ExternalId}, Plugin={PluginId})",
                existing.ExternalId, existing.SourcePluginId);
            return new ExternalTriageResult(existing, ExternalTriageOutcome.Unchanged);
        }

        if (contentChanged)
        {
            // Rewrite at the stable hashed path; a row created by an older build may still
            // point at a temp-folder file, which is migrated here.
            await WriteContentFileAsync(contentPath, contentText).ConfigureAwait(false);
            existing.FilePath = contentPath;
            existing.FileSizeBytes = new FileInfo(contentPath).Length;
        }

        existing.FileName = fileName;
        existing.FileType = fileType;
        existing.SourceType = sourceType;
        existing.SourceUrl = sourceUrl;
        existing.SourceCategory = sourceCategory;
        existing.Preview = contentPreview;
        existing.ProcessedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync().ConfigureAwait(false);

        // Only an accepted row feeds the vault; a row the user rejected or deferred keeps its
        // decision and is not re-imported just because the source changed.
        if (string.Equals(existing.Status, "accepted", StringComparison.Ordinal))
        {
            await RefreshExternalDocumentAsync(existing, contentChanged, nameChanged).ConfigureAwait(false);
        }

        Log.Information(
            "InboxService: TriageExternal - updated '{FileName}' (Plugin={PluginId}, ExternalId={ExternalId}, ContentChanged={ContentChanged})",
            fileName, existing.SourcePluginId, existing.ExternalId, contentChanged);

        return new ExternalTriageResult(existing, ExternalTriageOutcome.Updated);
    }

    /// <summary>
    /// Imports an external item's content file into the document library, preserving the
    /// semantic file type (e.g. "CalendarEvent"), and links the row to the new document.
    /// Best effort: a failure is logged and the inbox row stays valid.
    /// </summary>
    private async Task BridgeExternalItemAsync(InboxItemEntity item)
    {
        if (_documentService is null)
            return;

        try
        {
            var document = await _documentService.ImportExternalContentAsync(
                item.FilePath,
                item.FileType,
                displayName: item.FileName,
                sourceUrl: item.SourceUrl,
                ct: default).ConfigureAwait(false);

            item.DocumentId = document.Id;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Debug(
                "InboxService: TriageExternal - linked inbox item {ItemId} to document {DocumentId}",
                item.Id, document.Id);
        }
        catch (Exception ex)
        {
            // Non-fatal: the inbox item is still valid; search indexing is best-effort.
            Log.Warning(ex,
                "InboxService: TriageExternal - failed to import '{FileName}' into document library (non-fatal)",
                item.FileName);
        }
    }

    /// <summary>
    /// Brings the vault copy of a changed external item up to date: renames and re-points the
    /// linked document and re-indexes it when the content changed, or imports the content when
    /// the row has no (surviving) document yet.
    /// </summary>
    private async Task RefreshExternalDocumentAsync(InboxItemEntity item, bool contentChanged, bool nameChanged)
    {
        if (_documentService is null)
            return;

        DocumentEntity? document = null;
        if (item.DocumentId is { } documentId)
        {
            document = await _db.Documents.FindAsync(documentId).ConfigureAwait(false);
        }

        if (document is null)
        {
            // Never bridged, or the user deleted the document; import the current content.
            await BridgeExternalItemAsync(item).ConfigureAwait(false);
            return;
        }

        if (!contentChanged && !nameChanged)
            return;

        try
        {
            document.FileName = item.FileName;
            document.FilePath = Path.GetFullPath(item.FilePath);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            if (contentChanged)
            {
                await _documentService.ReindexDocumentAsync(document.Id).ConfigureAwait(false);

                // Re-extraction derives a title from the text; keep the connector's display
                // name, as the original import did.
                document.ExtractedTitle = item.FileName;
                await _db.SaveChangesAsync().ConfigureAwait(false);
            }

            Log.Debug(
                "InboxService: TriageExternal - refreshed document {DocumentId} for inbox item {ItemId}",
                document.Id, item.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "InboxService: TriageExternal - failed to refresh document {DocumentId} for '{FileName}' (non-fatal)",
                document.Id, item.FileName);
        }
    }

    /// <summary>
    /// Builds the stable content-file path for an external item:
    /// <c>{storeRoot}/External/{plugin}/{hash}.txt</c>. The plugin segment is reduced to
    /// <c>[A-Za-z0-9._-]</c> and the file name is a hash of the plugin and external IDs, so
    /// provider IDs containing ':' (an NTFS alternate-data-stream separator), '/' or other
    /// reserved characters never reach the file system, and the same item always maps to
    /// the same file.
    /// </summary>
    internal static string BuildExternalContentPath(string storeRoot, string sourcePluginId, string externalId)
    {
        var hashInput = Encoding.UTF8.GetBytes(sourcePluginId + "\n" + externalId);
        var hash = Convert.ToHexString(SHA256.HashData(hashInput))[..32].ToLowerInvariant();
        return Path.Combine(
            storeRoot,
            ExternalStoreFolderName,
            ToSafePathSegment(sourcePluginId, "connector"),
            hash + ".txt");
    }

    /// <summary>
    /// Reduces <paramref name="value"/> to a single safe directory or file-name segment:
    /// characters outside <c>[A-Za-z0-9._-]</c> become '_', leading and trailing dots are
    /// trimmed, and an empty result falls back to <paramref name="fallback"/>. The rule is
    /// the same on every OS, unlike <see cref="Path.GetInvalidFileNameChars"/>.
    /// </summary>
    internal static string ToSafePathSegment(string value, string fallback, int maxLength = 64)
    {
        var chars = value
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')
            .ToArray();
        var segment = new string(chars).Trim('.');
        if (segment.Length > maxLength)
            segment = segment[..maxLength].Trim('.');
        return segment.Length == 0 ? fallback : segment;
    }

    /// <summary>The root of the inbox's own storage: <c>{AppData}/Inbox</c>.</summary>
    private string GetInboxStoreRoot() => Path.Combine(_appPaths.GetAppDataPath(), InboxStoreFolderName);

    private static async Task WriteContentFileAsync(string path, string contentText)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contentText).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the file at <paramref name="path"/> exists and holds exactly
    /// <paramref name="contentText"/>. A missing or unreadable file counts as changed so it
    /// is rewritten.
    /// </summary>
    private static async Task<bool> ContentFileMatchesAsync(string path, string contentText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var current = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return string.Equals(current, contentText, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // -- Accept into the vault -------------------------------------------------

    /// <summary>
    /// Accepts each item in turn. One failure does not stop the batch: the failed item keeps
    /// its status and the reason is collected for the caller.
    /// </summary>
    private async Task<InboxBatchAcceptResult> AcceptBatchAsync(
        IReadOnlyList<InboxItemEntity> items,
        long? collectionId,
        string? overrideName)
    {
        int imported = 0, linked = 0, alreadyAccepted = 0, failed = 0;
        var errors = new List<string>();

        foreach (var item in items)
        {
            try
            {
                var result = await AcceptIntoVaultAsync(item, collectionId, overrideName).ConfigureAwait(false);
                switch (result.Outcome)
                {
                    case InboxAcceptOutcome.Imported: imported++; break;
                    case InboxAcceptOutcome.AlreadyInVault: linked++; break;
                    default: alreadyAccepted++; break;
                }
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                failed++;
                errors.Add($"{item.FileName}: {ex.Message}");
                Log.Warning(ex, "InboxService: Could not accept inbox item {ItemId} '{FileName}' - left as {Status}",
                    item.Id, item.FileName, item.Status);
            }
        }

        return new InboxBatchAcceptResult(imported, linked, alreadyAccepted, failed, errors);
    }

    /// <summary>
    /// Moves one inbox item into the knowledge vault and marks it accepted. The row is only
    /// updated after the document exists, so any failure leaves the item as it was.
    /// </summary>
    private async Task<InboxAcceptResult> AcceptIntoVaultAsync(
        InboxItemEntity item,
        long? collectionId,
        string? overrideName)
    {
        var effectiveCollectionId = collectionId ?? item.SuggestedCollectionId;

        // Already accepted and linked (e.g. connector content, which is imported at triage).
        if (item.DocumentId is { } linkedId && string.Equals(item.Status, "accepted", StringComparison.Ordinal))
        {
            if (collectionId.HasValue)
            {
                ApplyCollectionOverride(item, collectionId, overrideName);
                await _db.SaveChangesAsync().ConfigureAwait(false);
            }

            return new InboxAcceptResult(item.Id, InboxAcceptOutcome.AlreadyAccepted, linkedId);
        }

        if (_documentService is null)
        {
            throw new InvalidOperationException(
                $"Cannot accept '{item.FileName}': document import is not available, so the item was left {item.Status}.");
        }

        long documentId;
        InboxAcceptOutcome outcome;

        if (item.DocumentId is { } existingDocumentId)
        {
            // Linked earlier (connector content that was rejected or deferred): accepting
            // again only restores the decision; the document is already in the vault.
            documentId = existingDocumentId;
            outcome = InboxAcceptOutcome.AlreadyInVault;
        }
        else
        {
            if (!File.Exists(item.FilePath))
            {
                throw new FileNotFoundException(
                    $"The file for '{item.FileName}' no longer exists at '{item.FilePath}'. " +
                    "It may have been moved or removed by temp-folder cleanup; reject the item or add the file again.",
                    item.FilePath);
            }

            var duplicate = await _documentService.CheckForDuplicateAsync(item.FilePath).ConfigureAwait(false);
            if (duplicate.IsDuplicate && duplicate.ExistingDocumentId is { } duplicateId)
            {
                documentId = duplicateId;
                outcome = InboxAcceptOutcome.AlreadyInVault;

                if (effectiveCollectionId.HasValue)
                {
                    await _documentService
                        .BulkAssignToCollectionAsync(new[] { duplicateId }, effectiveCollectionId.Value)
                        .ConfigureAwait(false);
                }

                Log.Information(
                    "InboxService: '{FileName}' is already in the vault as document {DocumentId} ({ExistingName}); linking instead of importing",
                    item.FileName, duplicateId, duplicate.ExistingFileName);
            }
            else
            {
                documentId = await ImportIntoVaultAsync(item, effectiveCollectionId).ConfigureAwait(false);
                outcome = InboxAcceptOutcome.Imported;
            }
        }

        item.DocumentId = documentId;
        item.Status = "accepted";
        item.ProcessedAt = DateTime.UtcNow;
        ApplyCollectionOverride(item, collectionId, overrideName);

        await _db.SaveChangesAsync().ConfigureAwait(false);

        return new InboxAcceptResult(item.Id, outcome, documentId);
    }

    /// <summary>
    /// Copies the item's file to <c>{AppData}/Inbox/Accepted/{itemId}/{fileName}</c> (the file
    /// name is kept because the vault shows it) and imports the copy. The copy is removed
    /// again if the import fails.
    /// </summary>
    private async Task<long> ImportIntoVaultAsync(InboxItemEntity item, long? collectionId)
    {
        var sourceName = Path.GetFileName(item.FilePath);
        var fileName = PathHelper.SanitizeFileName(string.IsNullOrWhiteSpace(sourceName) ? item.FileName : sourceName);
        var itemFolder = Path.Combine(
            GetInboxStoreRoot(),
            AcceptedStoreFolderName,
            item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var storedPath = Path.Combine(itemFolder, fileName);

        Directory.CreateDirectory(itemFolder);
        File.Copy(item.FilePath, storedPath, overwrite: true);

        try
        {
            var document = await _documentService!
                .ImportFileAsync(storedPath, collectionId)
                .ConfigureAwait(false);
            return document.Id;
        }
        catch
        {
            TryDeleteDirectory(itemFolder);
            throw;
        }
    }

    private static void ApplyCollectionOverride(InboxItemEntity item, long? collectionId, string? overrideName)
    {
        if (!collectionId.HasValue)
            return;

        item.SuggestedCollectionId = collectionId.Value;
        item.SuggestedCollectionName = overrideName;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "InboxService: Could not remove '{Path}' after a failed import", path);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads a tracked <see cref="InboxItemEntity"/> by ID or throws
    /// <see cref="InvalidOperationException"/> if it does not exist.
    /// </summary>
    private async Task<InboxItemEntity> RequireItemAsync(long itemId)
    {
        var item = await _db.InboxItems.FindAsync(itemId).ConfigureAwait(false);

        if (item is null)
        {
            throw new InvalidOperationException(
                $"Inbox item with ID {itemId} was not found.");
        }

        return item;
    }

    /// <summary>
    /// Reads up to <see cref="PreviewReadChars"/> characters from a text-like file.
    /// Returns an empty string for binary files or files that cannot be read.
    /// </summary>
    private static async Task<string> ReadFileSnippetAsync(
        string filePath,
        CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            Log.Warning("InboxService: File no longer exists at path: {FilePath}", filePath);
            return string.Empty;
        }

        try
        {
            // Use a small buffer — we only need the first PreviewReadChars chars.
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[PreviewReadChars];
            var charsRead = await reader.ReadAsync(buffer, ct).ConfigureAwait(false);

            return new string(buffer, 0, charsRead);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "InboxService: Could not read snippet from file '{FilePath}' — file may be binary or locked",
                filePath);
            return string.Empty;
        }
    }

    /// <summary>
    /// Constructs the AI prompt that asks for a triage response in a parseable format.
    /// The format is deliberately simple (labelled sections) so parsing is robust
    /// even when the model includes extra whitespace or minor formatting variation.
    /// </summary>
    private static string BuildTriagePrompt(
        string fileName,
        string snippet,
        string availableCollections)
    {
        return $"""
            You are a document triage assistant. Given the file name and a short snippet of its content, respond using EXACTLY this format (do not add extra sections):

            PREVIEW: <2-3 sentence plain-text summary of what the document is about>
            COLLECTION: <name of the most relevant collection from the list, or "none" if none fit>
            TAGS: <5 or fewer lowercase comma-separated tags that describe the content>

            FILE NAME: {fileName}
            AVAILABLE COLLECTIONS: {availableCollections}

            CONTENT SNIPPET:
            {snippet}
            """;
    }

    /// <summary>
    /// Parses the structured AI triage response into its three components.
    /// Each section is introduced by a labelled prefix on its own line.
    /// Missing or malformed sections result in null for that field.
    /// </summary>
    private static void ParseTriageResponse(
        string response,
        out string? preview,
        out string? suggestedCollectionName,
        out string? suggestedTags)
    {
        preview = null;
        suggestedCollectionName = null;
        suggestedTags = null;

        if (string.IsNullOrWhiteSpace(response))
            return;

        foreach (var rawLine in response.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("PREVIEW:", StringComparison.OrdinalIgnoreCase))
            {
                preview = rawLine["PREVIEW:".Length..].Trim();
                if (string.IsNullOrWhiteSpace(preview))
                    preview = null;
            }
            else if (rawLine.StartsWith("COLLECTION:", StringComparison.OrdinalIgnoreCase))
            {
                var value = rawLine["COLLECTION:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    suggestedCollectionName = value;
                }
            }
            else if (rawLine.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase))
            {
                var value = rawLine["TAGS:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    // Normalise: lowercase, trim each tag, remove empties.
                    var tags = value
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.ToLowerInvariant())
                        .Where(t => t.Length > 0)
                        .Take(5);

                    var joined = string.Join(",", tags);
                    if (!string.IsNullOrWhiteSpace(joined))
                        suggestedTags = joined;
                }
            }
        }
    }
}

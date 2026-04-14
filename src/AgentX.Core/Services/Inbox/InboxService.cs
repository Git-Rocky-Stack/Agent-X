using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Intelligence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Inbox;

/// <summary>
/// EF Core-backed implementation of <see cref="IInboxService"/>.
/// Provides file triage, AI-powered 2–3 sentence previews with collection and tag
/// suggestions, and batch accept/reject operations. Accepted items are stamped so
/// that the downstream indexing pipeline can pick them up by status.
/// </summary>
public sealed class InboxService : IInboxService
{
    private readonly AgentXDbContext _db;
    private readonly ISummaryService _summaryService;
    private readonly ICollectionService _collectionService;
    private readonly IAiService _aiService;

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
        IAiService aiService)
    {
        _db = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _collectionService = collectionService ?? throw new ArgumentNullException(nameof(collectionService));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
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
    public async Task AcceptItemAsync(long itemId, long? collectionId = null)
    {
        try
        {
            var item = await RequireItemAsync(itemId).ConfigureAwait(false);

            item.Status = "accepted";
            item.ProcessedAt = DateTime.UtcNow;

            // A caller-supplied collectionId overrides the AI suggestion.
            if (collectionId.HasValue)
            {
                item.SuggestedCollectionId = collectionId.Value;

                // Refresh the denormalized name if the override differs.
                var collection = await _collectionService
                    .GetCollectionAsync(collectionId.Value)
                    .ConfigureAwait(false);

                item.SuggestedCollectionName = collection?.Name;
            }

            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Accepted inbox item {ItemId} '{FileName}' (collection {CollectionId})",
                item.Id, item.FileName, item.SuggestedCollectionId?.ToString() ?? "none");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InboxService: Failed to accept inbox item {ItemId}", itemId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AcceptAllPendingAsync()
    {
        try
        {
            var pending = await _db.InboxItems
                .Where(i => i.Status == "pending")
                .ToListAsync()
                .ConfigureAwait(false);

            if (pending.Count == 0)
            {
                Log.Debug("InboxService: AcceptAllPending — no pending items found");
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var item in pending)
            {
                item.Status = "accepted";
                item.ProcessedAt = now;
            }

            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Accepted all {Count} pending inbox items",
                pending.Count);
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
    public async Task AcceptSelectedAsync(
        IEnumerable<long> itemIds,
        long? collectionId = null)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var idList = itemIds.Distinct().ToList();
        if (idList.Count == 0) return;

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

            var now = DateTime.UtcNow;
            var accepted = 0;

            foreach (var item in items)
            {
                item.Status = "accepted";
                item.ProcessedAt = now;

                if (collectionId.HasValue)
                {
                    item.SuggestedCollectionId = collectionId.Value;
                    item.SuggestedCollectionName = overrideName;
                }

                accepted++;
            }

            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "InboxService: Batch-accepted {Accepted}/{Requested} inbox items (collection {CollectionId})",
                accepted, idList.Count, collectionId?.ToString() ?? "per-item");
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

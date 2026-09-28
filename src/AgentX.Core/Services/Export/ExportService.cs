using System.Text;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Export;

/// <summary>
/// Thin orchestrator implementation of <see cref="IExportService"/>.
/// Delegates format-specific rendering to registered <see cref="IExportFormatter"/>
/// implementations and <see cref="ExportContentBuilder"/> for search results
/// and collections. Handles file I/O and orchestration. Every export format is
/// unconditionally available to every user.
/// </summary>
public class ExportService : IExportService
{
    private readonly IConversationService _conversationService;
    private readonly IDocumentService _documentService;
    private readonly ICollectionService _collectionService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _log;
    private readonly IReadOnlyDictionary<ExportFormat, IExportFormatter> _formatters;
    private readonly AgentXDbContext? _db;
    private readonly IExportTemplateService _templateService;

    private static readonly HashSet<ExportFormat> BinaryFormats =
        [ExportFormat.Pdf, ExportFormat.Docx, ExportFormat.Pptx];

    /// <param name="conversationService">Conversation reads when no database context is supplied.</param>
    /// <param name="documentService">Document service.</param>
    /// <param name="collectionService">Collection reads when no database context is supplied.</param>
    /// <param name="settingsService">Settings (default export directory).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="formatters">One formatter per export format.</param>
    /// <param name="db">
    /// When supplied, conversations, and the collections and documents of a collection export,
    /// are read with no change tracking: an export only reads, and tracked entities would stay
    /// in the shared context (an "export all" pins every conversation and message), making
    /// every later SaveChanges scan them.
    /// </param>
    /// <param name="templateService">Built-in templates for <see cref="ExportOptions.TemplateId"/>.</param>
    public ExportService(
        IConversationService conversationService,
        IDocumentService documentService,
        ICollectionService collectionService,
        ISettingsService settingsService,
        ILogger logger,
        IEnumerable<IExportFormatter> formatters,
        AgentXDbContext? db = null,
        IExportTemplateService? templateService = null)
    {
        _db = db;
        _templateService = templateService ?? new ExportTemplateService();

        _conversationService = conversationService
            ?? throw new ArgumentNullException(nameof(conversationService));
        _documentService = documentService
            ?? throw new ArgumentNullException(nameof(documentService));
        _collectionService = collectionService
            ?? throw new ArgumentNullException(nameof(collectionService));
        _settingsService = settingsService
            ?? throw new ArgumentNullException(nameof(settingsService));
        _log = logger?.ForContext<ExportService>()
               ?? throw new ArgumentNullException(nameof(logger));

        _formatters = formatters?.ToDictionary(f => f.Format)
            ?? throw new ArgumentNullException(nameof(formatters));
    }

    // IExportService -- Single conversation

    /// <inheritdoc />
    public async Task<ExportResult> ExportConversationAsync(
        long conversationId,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var conversation = await LoadConversationAsync(conversationId, ct);
            if (conversation is null)
            {
                _log.Warning("Export failed: conversation {ConversationId} not found", conversationId);
                return ExportResult.Fail($"Conversation {conversationId} not found.");
            }

            if (options.TemplateId is not null && options.Format != ExportFormat.Markdown)
                return ExportResult.Fail("Export templates produce Markdown, so they apply to Markdown exports only.");

            var outputPath = await ExportPathUtility.ResolveOutputPathAsync(options, conversation.Title, options.Format, _settingsService);
            ExportPathUtility.EnsureDirectoryExists(outputPath);

            var formatter = ResolveFormatter(options.Format);
            var title = options.Title ?? conversation.Title;
            if (options.Title != title) options.Title = title;
            var content = options.TemplateId is { } templateId
                ? await ApplyTemplateAsync(conversation, templateId, title)
                : await formatter.ExportConversationAsync(conversation, options, ct);

            var writeError = await WriteOutputAsync(outputPath, content, options.Format, ct);
            if (writeError is not null) return writeError;

            var fileInfo = new FileInfo(outputPath);
            _log.Information(
                "Exported conversation {ConversationId} to {Format} at '{Path}' ({Size} bytes)",
                conversationId, options.Format, outputPath, fileInfo.Length);

            return ExportResult.Ok(outputPath, fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Export of conversation {ConversationId} was cancelled", conversationId);
            return ExportResult.Fail("Export was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export conversation {ConversationId}", conversationId);
            return ExportResult.Fail($"Export failed: {ex.Message}");
        }
    }

    // IExportService -- Multiple conversations

    /// <inheritdoc />
    public async Task<ExportResult> ExportConversationsAsync(
        IReadOnlyList<long> conversationIds,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (conversationIds is null || conversationIds.Count == 0)
                return ExportResult.Fail("No conversation IDs provided.");

            if (options.TemplateId is not null)
                return ExportResult.Fail("Export templates structure a single conversation; export one conversation to use a template.");

            var conversations = await FetchConversationsAsync(conversationIds, ct);
            if (conversations.Count == 0)
                return ExportResult.Fail("None of the specified conversations were found.");

            var title = options.Title ?? $"Agent-X Conversations Export ({conversations.Count})";
            var outputPath = await ExportPathUtility.ResolveOutputPathAsync(options, title, options.Format, _settingsService);
            ExportPathUtility.EnsureDirectoryExists(outputPath);

            var formatter = ResolveFormatter(options.Format);
            if (options.Title != title) options.Title = title;
            var content = await formatter.ExportConversationsAsync(conversations, options, ct);

            var writeError = await WriteOutputAsync(outputPath, content, options.Format, ct);
            if (writeError is not null) return writeError;

            var fileInfo = new FileInfo(outputPath);
            _log.Information(
                "Exported {Count} conversations to {Format} at '{Path}' ({Size} bytes)",
                conversations.Count, options.Format, outputPath, fileInfo.Length);

            return ExportResult.Ok(outputPath, fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Batch export was cancelled");
            return ExportResult.Fail("Export was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export {Count} conversations", conversationIds?.Count ?? 0);
            return ExportResult.Fail($"Export failed: {ex.Message}");
        }
    }

    // IExportService -- Search results (delegated to ExportContentBuilder)

    /// <inheritdoc />
    public async Task<ExportResult> ExportSearchResultsAsync(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(query))
                return ExportResult.Fail("Search query must not be empty.");

            if (results is null || results.Count == 0)
                return ExportResult.Fail("No search results to export.");

            var title = options.Title ?? $"Search Results: {query}";
            var outputPath = await ExportPathUtility.ResolveOutputPathAsync(options, title, options.Format, _settingsService);
            ExportPathUtility.EnsureDirectoryExists(outputPath);

            // Search results use ExportContentBuilder - formatters only handle ConversationEntity
            var content = ExportContentBuilder.BuildSearchResultsContent(query, results, options, title);
            if (content is null)
                return ExportResult.Fail($"Unsupported export format: {options.Format}");

            await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);

            var fileInfo = new FileInfo(outputPath);
            _log.Information(
                "Exported {Count} search results for '{Query}' to {Format} at '{Path}' ({Size} bytes)",
                results.Count, query, options.Format, outputPath, fileInfo.Length);

            return ExportResult.Ok(outputPath, fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Search results export was cancelled");
            return ExportResult.Fail("Export was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export search results for '{Query}'", query);
            return ExportResult.Fail($"Export failed: {ex.Message}");
        }
    }

    // IExportService -- Collection export (delegated to ExportContentBuilder)

    /// <inheritdoc />
    public async Task<ExportResult> ExportCollectionAsync(
        long collectionId,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var collection = await LoadCollectionAsync(collectionId, ct);
            if (collection is null)
            {
                _log.Warning("Export failed: collection {CollectionId} not found", collectionId);
                return ExportResult.Fail($"Collection {collectionId} not found.");
            }

            var documents = await LoadCollectionDocumentsAsync(collectionId, ct);
            var title = options.Title ?? collection.Name;

            // CSV format gets a dedicated flat-file export instead of the default ZIP
            if (options.Format == ExportFormat.Csv)
            {
                var csvOutputPath = options.OutputPath
                    ?? Path.Combine(
                        await ExportPathUtility.GetExportDirectoryAsync(_settingsService),
                        ExportPathUtility.SanitizeFileName($"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"));

                ExportPathUtility.EnsureDirectoryExists(csvOutputPath);

                var csvContent = ExportContentBuilder.BuildCollectionCsv(collection, documents);
                await File.WriteAllTextAsync(csvOutputPath, csvContent, Encoding.UTF8, ct);

                var csvFileInfo = new FileInfo(csvOutputPath);
                _log.Information(
                    "Exported collection {CollectionId} '{Name}' ({DocumentCount} documents) as CSV to '{Path}' ({Size} bytes)",
                    collectionId, collection.Name, documents.Count, csvOutputPath, csvFileInfo.Length);

                return ExportResult.Ok(csvOutputPath, csvFileInfo.Length);
            }

            var outputPath = options.OutputPath
                ?? Path.Combine(
                    await ExportPathUtility.GetExportDirectoryAsync(_settingsService),
                    ExportPathUtility.SanitizeFileName($"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"));

            ExportPathUtility.EnsureDirectoryExists(outputPath);

            await ExportContentBuilder.WriteCollectionZipAsync(collection, documents, outputPath, ct);

            var fileInfo = new FileInfo(outputPath);
            _log.Information(
                "Exported collection {CollectionId} '{Name}' ({DocumentCount} documents) to '{Path}' ({Size} bytes)",
                collectionId, collection.Name, documents.Count, outputPath, fileInfo.Length);

            return ExportResult.Ok(outputPath, fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Collection export was cancelled");
            return ExportResult.Fail("Export was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export collection {CollectionId}", collectionId);
            return ExportResult.Fail($"Export failed: {ex.Message}");
        }
    }

    // IExportService -- Standalone text artifact export

    /// <inheritdoc />
    public async Task<ExportResult> ExportTextArtifactAsync(
        TextArtifactExportItem artifact,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (artifact is null)
                return ExportResult.Fail("No export artifact provided.");

            if (string.IsNullOrWhiteSpace(artifact.Title))
                return ExportResult.Fail("Export artifact title must not be empty.");

            if (string.IsNullOrWhiteSpace(artifact.Content))
                return ExportResult.Fail("Export artifact content must not be empty.");

            var title = options.Title ?? artifact.Title;
            var outputPath = await ExportPathUtility.ResolveOutputPathAsync(options, title, options.Format, _settingsService);
            ExportPathUtility.EnsureDirectoryExists(outputPath);

            var exportArtifact = new TextArtifactExportItem
            {
                Title = title,
                Content = artifact.Content,
                Metadata = artifact.Metadata
            };

            var content = ExportContentBuilder.BuildTextArtifactContent(exportArtifact, options);
            if (content is null)
                return ExportResult.Fail($"Unsupported export format for text artifact: {options.Format}");

            await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);

            var fileInfo = new FileInfo(outputPath);
            _log.Information(
                "Exported text artifact '{Title}' to {Format} at '{Path}' ({Size} bytes)",
                title, options.Format, outputPath, fileInfo.Length);

            return ExportResult.Ok(outputPath, fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Text artifact export was cancelled");
            return ExportResult.Fail("Export was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export text artifact '{Title}'", artifact?.Title);
            return ExportResult.Fail($"Export failed: {ex.Message}");
        }
    }

    // IExportService -- In-memory formatting helpers

    /// <inheritdoc />
    public Task<string> FormatConversationAsMarkdownAsync(long conversationId, bool includeMeta) =>
        FormatConversationAsync(conversationId, includeMeta, ExportFormat.Markdown, "Markdown");

    /// <inheritdoc />
    public Task<string> FormatConversationAsHtmlAsync(long conversationId, bool includeMeta) =>
        FormatConversationAsync(conversationId, includeMeta, ExportFormat.Html, "HTML");

    private async Task<string> FormatConversationAsync(
        long conversationId, bool includeMeta, ExportFormat format, string formatLabel)
    {
        try
        {
            var conversation = await LoadConversationAsync(conversationId, CancellationToken.None);
            if (conversation is null)
            {
                _log.Warning("Cannot format as {Format}: conversation {ConversationId} not found",
                    formatLabel, conversationId);
                return string.Empty;
            }

            var options = new ExportOptions
            {
                IncludeMetadata = includeMeta,
                IncludeTimestamps = includeMeta,
                IncludeCitations = true,
                IncludeModelInfo = includeMeta,
            };

            var formatter = ResolveFormatter(format);
            return await formatter.ExportConversationAsync(conversation, options);
        }
        catch (Exception ex)
        {
            // Rethrown: an empty string here made "Copy as Markdown" report success after a
            // failure, with nothing (or the previous text) on the clipboard.
            _log.Error(ex, "Failed to format conversation {ConversationId} as {Format}",
                conversationId, formatLabel);
            throw;
        }
    }

    /// <summary>
    /// Renders <paramref name="conversation"/> through a built-in template (Markdown).
    /// </summary>
    private Task<string> ApplyTemplateAsync(ConversationEntity conversation, ExportTemplateId templateId, string title)
    {
        var messages = conversation.Messages
            .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.SortOrder)
            .Select(m => new TemplateMessage { Role = m.Role, Content = m.Content, Timestamp = m.Timestamp })
            .ToList();

        return _templateService.ApplyTemplateAsync(templateId, messages, title);
    }

    /// <summary>
    /// Loads a conversation with its messages for export: untracked when a context is
    /// available (see the constructor), through the conversation service otherwise.
    /// </summary>
    private async Task<ConversationEntity?> LoadConversationAsync(long conversationId, CancellationToken ct)
    {
        if (_db is null)
            return await _conversationService.GetConversationAsync(conversationId);

        return await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.SortOrder))
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
    }

    /// <summary>
    /// Loads the collection an export describes: untracked when a context is available (see the
    /// constructor), through the collection service otherwise. Only the collection's own fields
    /// are exported; the document count comes from the exported documents.
    /// </summary>
    private async Task<CollectionEntity?> LoadCollectionAsync(long collectionId, CancellationToken ct)
    {
        if (_db is null)
            return await _collectionService.GetCollectionAsync(collectionId);

        return await _db.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == collectionId, ct);
    }

    /// <summary>
    /// Loads the documents of an exported collection by file name: untracked when a context is
    /// available, through the collection service otherwise.
    /// </summary>
    private async Task<IReadOnlyList<DocumentEntity>> LoadCollectionDocumentsAsync(long collectionId, CancellationToken ct)
    {
        if (_db is null)
            return await _collectionService.GetDocumentsInCollectionAsync(collectionId);

        return await _db.DocumentCollections
            .AsNoTracking()
            .Where(dc => dc.CollectionId == collectionId)
            .Select(dc => dc.Document)
            .OrderBy(d => d.FileName)
            .ToListAsync(ct);
    }

    // Formatter resolution & output writing

    private IExportFormatter ResolveFormatter(ExportFormat format)
    {
        if (_formatters.TryGetValue(format, out var formatter))
            return formatter;

        throw new NotSupportedException($"Unsupported export format: {format}");
    }

    private async Task<ExportResult?> WriteOutputAsync(
        string outputPath, string content, ExportFormat format, CancellationToken ct)
    {
        if (BinaryFormats.Contains(format))
        {
            try
            {
                var bytes = Convert.FromBase64String(content);
                await File.WriteAllBytesAsync(outputPath, bytes, ct);
            }
            catch (FormatException ex)
            {
                _log.Warning(ex,
                    "Binary formatter returned invalid Base64 content for {Format} format", format);
                return ExportResult.Fail($"Binary formatter returned invalid content for {format} format");
            }
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
        }

        return null; // success - no error
    }

    // Conversation fetching

    private async Task<List<ConversationEntity>> FetchConversationsAsync(
        IReadOnlyList<long> ids, CancellationToken ct)
    {
        var conversations = new List<ConversationEntity>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var conversation = await LoadConversationAsync(id, ct);
            if (conversation is not null)

            {
                conversations.Add(conversation);
            }
            else
            {
                _log.Warning("Skipping missing conversation {ConversationId} during batch export", id);
            }
        }

        return conversations;
    }
}

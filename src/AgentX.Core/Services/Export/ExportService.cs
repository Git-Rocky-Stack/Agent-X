using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.License;
using AgentX.Core.Services.Settings;
using Markdig;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Serilog;

namespace AgentX.Core.Services.Export;

/// <summary>
/// Production implementation of <see cref="IExportService"/>.
/// Exports conversations, search results, and document collections to
/// Markdown, HTML, PDF, JSON, PlainText, and CSV formats.
/// </summary>
public class ExportService : IExportService
{
    private readonly IConversationService _conversationService;
    private readonly IDocumentService _documentService;
    private readonly ICollectionService _collectionService;
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;
    private readonly ILogger _log;
    private readonly HtmlExport _htmlExport;
    private readonly PdfExport _pdfExport;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public ExportService(
        IConversationService conversationService,
        IDocumentService documentService,
        ICollectionService collectionService,
        ISettingsService settingsService,
        ILicenseService licenseService,
        ILogger logger)
    {
        _conversationService = conversationService
            ?? throw new ArgumentNullException(nameof(conversationService));
        _documentService = documentService
            ?? throw new ArgumentNullException(nameof(documentService));
        _collectionService = collectionService
            ?? throw new ArgumentNullException(nameof(collectionService));
        _settingsService = settingsService
            ?? throw new ArgumentNullException(nameof(settingsService));
        _licenseService = licenseService
            ?? throw new ArgumentNullException(nameof(licenseService));
        _log = logger?.ForContext<ExportService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _htmlExport = new HtmlExport();
        _pdfExport = new PdfExport(logger);
    }

    // ────────────────────────────────────────────────────────────────
    // IExportService — Single conversation
    // ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ExportResult> ExportConversationAsync(
        long conversationId,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // License gating for PDF/Markdown/HTML formats (requires Professional or Ultimate)
            if (options.Format is ExportFormat.Pdf or ExportFormat.Markdown or ExportFormat.Html)
            {
                var license = await _licenseService.GetCurrentLicenseAsync();
                if (license.Tier < LicenseTier.Professional)
                {
                    _log.Warning("Export blocked: {Format} requires Professional or Ultimate license, current tier is {Tier}", options.Format, license.Tier);
                    return ExportResult.Fail("PDF/Markdown/HTML export requires Professional or Ultimate license.");
                }
            }

            var conversation = await _conversationService.GetConversationAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning("Export failed: conversation {ConversationId} not found", conversationId);
                return ExportResult.Fail($"Conversation {conversationId} not found.");
            }

            var outputPath = await ResolveOutputPathAsync(
                options, conversation.Title, options.Format);

            EnsureDirectoryExists(outputPath);

            var title = options.Title ?? conversation.Title;

            switch (options.Format)
            {
                case ExportFormat.Markdown:
                {
                    var content = BuildMarkdown(conversation, options, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Html:
                {
                    var content = (string)await _htmlExport.RenderAsync(conversation, options, ct);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Pdf:
                {
                    await _pdfExport.RenderToFileAsync(conversation, options, outputPath, ct);
                    break;
                }
                case ExportFormat.Json:
                {
                    var content = BuildJson(new[] { conversation }, options, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.PlainText:
                {
                    var content = BuildPlainText(conversation, options, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Csv:
                {
                    var content = BuildConversationCsv(conversation, options);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                default:
                    return ExportResult.Fail($"Unsupported export format: {options.Format}");
            }

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

    // ────────────────────────────────────────────────────────────────
    // IExportService — Multiple conversations
    // ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ExportResult> ExportConversationsAsync(
        IReadOnlyList<long> conversationIds,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // License gating for PDF/Markdown/HTML formats (requires Professional or Ultimate)
            if (options.Format is ExportFormat.Pdf or ExportFormat.Markdown or ExportFormat.Html)
            {
                var license = await _licenseService.GetCurrentLicenseAsync();
                if (license.Tier < LicenseTier.Professional)
                {
                    _log.Warning("Export blocked: {Format} requires Professional or Ultimate license, current tier is {Tier}", options.Format, license.Tier);
                    return ExportResult.Fail("PDF/Markdown/HTML export requires Professional or Ultimate license.");
                }
            }

            if (conversationIds is null || conversationIds.Count == 0)
            {
                return ExportResult.Fail("No conversation IDs provided.");
            }

            var conversations = new List<ConversationEntity>();
            foreach (var id in conversationIds)
            {
                ct.ThrowIfCancellationRequested();
                var conversation = await _conversationService.GetConversationAsync(id);
                if (conversation is not null)
                {
                    conversations.Add(conversation);
                }
                else
                {
                    _log.Warning("Skipping missing conversation {ConversationId} during batch export", id);
                }
            }

            if (conversations.Count == 0)
            {
                return ExportResult.Fail("None of the specified conversations were found.");
            }

            var title = options.Title ?? $"Agent-X Conversations Export ({conversations.Count})";
            var outputPath = await ResolveOutputPathAsync(options, title, options.Format);
            EnsureDirectoryExists(outputPath);

            switch (options.Format)
            {
                case ExportFormat.Markdown:
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"# {title}");
                    sb.AppendLine();
                    sb.AppendLine($"*Exported {conversations.Count} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();

                    for (var i = 0; i < conversations.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (i > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine("---");
                            sb.AppendLine();
                        }
                        sb.Append(BuildMarkdown(conversations[i], options, conversations[i].Title));
                    }

                    await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Html:
                {
                    var content = (string)await _htmlExport.RenderAsync(conversations, options, ct);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Pdf:
                {
                    await _pdfExport.RenderToFileAsync(conversations, options, outputPath, ct);
                    break;
                }
                case ExportFormat.Json:
                {
                    var content = BuildJson(conversations, options, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.PlainText:
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(title);
                    sb.AppendLine(new string('=', title.Length));
                    sb.AppendLine($"Exported {conversations.Count} conversations on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();

                    for (var i = 0; i < conversations.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (i > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine(new string('-', 60));
                            sb.AppendLine();
                        }
                        sb.Append(BuildPlainText(conversations[i], options, conversations[i].Title));
                    }

                    await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Csv:
                {
                    var csvSb = new StringBuilder();
                    csvSb.AppendLine("ConversationTitle,Role,Content,Timestamp,Model,Tokens");

                    foreach (var conv in conversations)
                    {
                        ct.ThrowIfCancellationRequested();
                        var messages = conv.Messages
                            .OrderBy(m => m.SortOrder)
                            .ToList();

                        foreach (var message in messages)
                        {
                            if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                                continue;

                            csvSb.Append(CsvEscape(conv.Title)).Append(',');
                            csvSb.Append(CsvEscape(message.Role)).Append(',');
                            csvSb.Append(CsvEscape(message.Content)).Append(',');
                            csvSb.Append(CsvEscape(message.Timestamp.ToString("O"))).Append(',');
                            csvSb.Append(CsvEscape(message.ModelId ?? "")).Append(',');
                            csvSb.AppendLine(message.TokenCount.ToString());
                        }
                    }

                    await File.WriteAllTextAsync(outputPath, csvSb.ToString(), Encoding.UTF8, ct);
                    break;
                }
                default:
                    return ExportResult.Fail($"Unsupported export format: {options.Format}");
            }

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

    // ────────────────────────────────────────────────────────────────
    // IExportService — Search results
    // ────────────────────────────────────────────────────────────────

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

            // License gating for PDF/Markdown/HTML formats (requires Professional or Ultimate)
            if (options.Format is ExportFormat.Pdf or ExportFormat.Markdown or ExportFormat.Html)
            {
                var license = await _licenseService.GetCurrentLicenseAsync();
                if (license.Tier < LicenseTier.Professional)
                {
                    _log.Warning("Export blocked: {Format} requires Professional or Ultimate license, current tier is {Tier}", options.Format, license.Tier);
                    return ExportResult.Fail("PDF/Markdown/HTML export requires Professional or Ultimate license.");
                }
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return ExportResult.Fail("Search query must not be empty.");
            }

            if (results is null || results.Count == 0)
            {
                return ExportResult.Fail("No search results to export.");
            }

            var title = options.Title ?? $"Search Results: {query}";
            var outputPath = await ResolveOutputPathAsync(options, title, options.Format);
            EnsureDirectoryExists(outputPath);

            switch (options.Format)
            {
                case ExportFormat.Markdown:
                {
                    var content = BuildSearchResultsMarkdown(query, results, options, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Html:
                {
                    var content = (string)await _htmlExport.RenderAsync(results, options, ct);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Pdf:
                {
                    await _pdfExport.RenderToFileAsync(results, options, outputPath, ct);
                    break;
                }
                case ExportFormat.Json:
                {
                    var content = BuildSearchResultsJson(query, results, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.PlainText:
                {
                    var content = BuildSearchResultsPlainText(query, results, options, title);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                case ExportFormat.Csv:
                {
                    var content = BuildSearchResultsCsv(query, results);
                    await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);
                    break;
                }
                default:
                    return ExportResult.Fail($"Unsupported export format: {options.Format}");
            }

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

    // ────────────────────────────────────────────────────────────────
    // IExportService — Collection export (ZIP)
    // ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ExportResult> ExportCollectionAsync(
        long collectionId,
        ExportOptions options,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var collection = await _collectionService.GetCollectionAsync(collectionId);
            if (collection is null)
            {
                _log.Warning("Export failed: collection {CollectionId} not found", collectionId);
                return ExportResult.Fail($"Collection {collectionId} not found.");
            }

            var documents = await _collectionService.GetDocumentsInCollectionAsync(collectionId);

            var title = options.Title ?? collection.Name;

            // CSV format gets a dedicated flat-file export instead of the default ZIP
            if (options.Format == ExportFormat.Csv)
            {
                var csvOutputPath = options.OutputPath
                    ?? Path.Combine(
                        await GetExportDirectoryAsync(),
                        SanitizeFileName($"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"));

                EnsureDirectoryExists(csvOutputPath);

                var csvContent = BuildCollectionCsv(collection, documents);
                await File.WriteAllTextAsync(csvOutputPath, csvContent, Encoding.UTF8, ct);

                var csvFileInfo = new FileInfo(csvOutputPath);
                _log.Information(
                    "Exported collection {CollectionId} '{Name}' ({DocumentCount} documents) as CSV to '{Path}' ({Size} bytes)",
                    collectionId, collection.Name, documents.Count, csvOutputPath, csvFileInfo.Length);

                return ExportResult.Ok(csvOutputPath, csvFileInfo.Length);
            }

            var outputPath = options.OutputPath
                ?? Path.Combine(
                    await GetExportDirectoryAsync(),
                    SanitizeFileName($"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"));

            EnsureDirectoryExists(outputPath);

            // Build a JSON manifest describing the collection and its documents
            var manifest = new
            {
                collection = new
                {
                    id = collection.Id,
                    name = collection.Name,
                    description = collection.Description,
                    createdAt = collection.CreatedAt,
                    updatedAt = collection.UpdatedAt,
                    documentCount = documents.Count,
                },
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                documents = documents.Select(d => new
                {
                    id = d.Id,
                    fileName = d.FileName,
                    filePath = d.FilePath,
                    fileType = d.FileType,
                    mimeType = d.MimeType,
                    fileSizeBytes = d.FileSizeBytes,
                    importedAt = d.ImportedAt,
                    pageCount = d.PageCount,
                    wordCount = d.WordCount,
                    summary = d.Summary,
                    language = d.Language,
                    indexingStatus = d.IndexingStatus,
                    contentHash = d.ContentHash,
                }).ToArray(),
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);

            // Create a ZIP containing the manifest and a README
            using (var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                ct.ThrowIfCancellationRequested();

                // Add manifest
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
                {
                    await writer.WriteAsync(manifestJson);
                }

                // Add a human-readable README
                var readmeContent = BuildCollectionReadme(collection, documents);
                var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
                {
                    await writer.WriteAsync(readmeContent);
                }
            }

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

    // ────────────────────────────────────────────────────────────────
    // IExportService — In-memory formatting helpers
    // ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> FormatConversationAsMarkdown(long conversationId, bool includeMeta)
    {
        try
        {
            var conversation = await _conversationService.GetConversationAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot format as Markdown: conversation {ConversationId} not found",
                    conversationId);
                return string.Empty;
            }

            var options = new ExportOptions
            {
                IncludeMetadata = includeMeta,
                IncludeTimestamps = includeMeta,
                IncludeCitations = true,
                IncludeModelInfo = includeMeta,
            };

            return BuildMarkdown(conversation, options, conversation.Title);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to format conversation {ConversationId} as Markdown",
                conversationId);
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public async Task<string> FormatConversationAsHtml(long conversationId, bool includeMeta)
    {
        try
        {
            var conversation = await _conversationService.GetConversationAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot format as HTML: conversation {ConversationId} not found",
                    conversationId);
                return string.Empty;
            }

            var options = new ExportOptions
            {
                IncludeMetadata = includeMeta,
                IncludeTimestamps = includeMeta,
                IncludeCitations = true,
                IncludeModelInfo = includeMeta,
            };

            return (string)await _htmlExport.RenderAsync(conversation, options);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to format conversation {ConversationId} as HTML",
                conversationId);
            return string.Empty;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Markdown builders
    // ════════════════════════════════════════════════════════════════

    private static string BuildMarkdown(
        ConversationEntity conversation,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {EscapeMarkdown(title)}");
        sb.AppendLine();

        if (options.IncludeMetadata)
        {
            sb.AppendLine($"**Created:** {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Updated:** {conversation.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Messages:** {conversation.MessageCount}");
            sb.AppendLine($"**Tokens Used:** {conversation.TokensUsed:N0}");

            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"**Model:** {conversation.ModelId}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("## System Prompt");
            sb.AppendLine();
            sb.AppendLine($"> {conversation.SystemPrompt.Replace("\n", "\n> ")}");
            sb.AppendLine();
        }

        sb.AppendLine("## Conversation");
        sb.AppendLine();

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            // Skip system messages from the export body (they are shown above)
            if (message.Role == "system")
            {
                continue;
            }

            var roleLabel = GetRoleLabel(message.Role);
            sb.AppendLine($"### {roleLabel}");

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"*{message.Timestamp:yyyy-MM-dd HH:mm:ss} UTC*");
            }

            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                sb.AppendLine($"*Model: {message.ModelId}*");
            }

            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();

            // Collect citations if present
            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                var citations = TryParseCitations(message.CitationsJson);
                citationsList.AddRange(citations);
            }

            if (options.IncludeMetadata && message.Role == "assistant")
            {
                var metaParts = new List<string>();
                if (message.TokenCount > 0)
                {
                    metaParts.Add($"Tokens: {message.TokenCount:N0}");
                }
                if (message.GenerationTimeMs.HasValue)
                {
                    metaParts.Add($"Generation: {message.GenerationTimeMs.Value:F0}ms");
                }
                if (metaParts.Count > 0)
                {
                    sb.AppendLine($"*{string.Join(" | ", metaParts)}*");
                    sb.AppendLine();
                }
            }
        }

        // Append citations as footnotes
        if (citationsList.Count > 0)
        {
            sb.AppendLine("## Citations");
            sb.AppendLine();
            for (var i = 0; i < citationsList.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {citationsList[i]}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    private static string BuildSearchResultsMarkdown(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {EscapeMarkdown(title)}");
        sb.AppendLine();
        sb.AppendLine($"**Query:** {EscapeMarkdown(query)}");
        sb.AppendLine($"**Results:** {results.Count}");
        sb.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"## Result {i + 1}: {EscapeMarkdown(result.DocumentName)}");
            sb.AppendLine();

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"**Relevance:** {result.RelevanceScore:P1}");
                sb.AppendLine();
            }

            sb.AppendLine(result.Content);
            sb.AppendLine();

            if (options.IncludeCitations && result.Citations.Count > 0)
            {
                sb.AppendLine("**Sources:**");
                foreach (var citation in result.Citations)
                {
                    sb.AppendLine($"- {citation}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    //  HTML builders
    // ════════════════════════════════════════════════════════════════

    private static string BuildHtml(
        ConversationEntity conversation,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();
        sb.Append(GetHtmlDocumentHeader(title));
        sb.Append(BuildHtmlBody(conversation, options, title));
        sb.Append(GetHtmlDocumentFooter());
        return sb.ToString();
    }

    private static string BuildHtmlBody(
        ConversationEntity conversation,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"<div class=\"conversation\">");
        sb.AppendLine($"  <h1>{HtmlEncode(title)}</h1>");

        if (options.IncludeMetadata)
        {
            sb.AppendLine("  <div class=\"metadata\">");
            sb.AppendLine($"    <span>Created: {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</span>");
            sb.AppendLine($"    <span>Updated: {conversation.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC</span>");
            sb.AppendLine($"    <span>Messages: {conversation.MessageCount}</span>");
            sb.AppendLine($"    <span>Tokens: {conversation.TokensUsed:N0}</span>");
            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"    <span>Model: {HtmlEncode(conversation.ModelId)}</span>");
            }
            sb.AppendLine("  </div>");
        }

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("  <div class=\"system-prompt\">");
            sb.AppendLine("    <h2>System Prompt</h2>");
            sb.AppendLine($"    <blockquote>{HtmlEncode(conversation.SystemPrompt)}</blockquote>");
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("  <div class=\"messages\">");

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                continue;
            }

            var roleClass = message.Role == "user" ? "user" : "assistant";
            var roleLabel = GetRoleLabel(message.Role);

            sb.AppendLine($"    <div class=\"message {roleClass}\">");
            sb.AppendLine($"      <div class=\"role-label\">{HtmlEncode(roleLabel)}</div>");

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"      <div class=\"timestamp\">{message.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</div>");
            }

            // Convert markdown content to HTML for assistant messages
            var htmlContent = message.Role == "assistant"
                ? Markdig.Markdown.ToHtml(message.Content, MarkdownPipeline)
                : $"<p>{HtmlEncode(message.Content)}</p>";

            sb.AppendLine($"      <div class=\"content\">{htmlContent}</div>");

            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                sb.AppendLine($"      <div class=\"model-info\">Model: {HtmlEncode(message.ModelId)}</div>");
            }

            if (options.IncludeMetadata && message.Role == "assistant")
            {
                var metaParts = new List<string>();
                if (message.TokenCount > 0)
                {
                    metaParts.Add($"Tokens: {message.TokenCount:N0}");
                }
                if (message.GenerationTimeMs.HasValue)
                {
                    metaParts.Add($"Generation: {message.GenerationTimeMs.Value:F0}ms");
                }
                if (metaParts.Count > 0)
                {
                    sb.AppendLine($"      <div class=\"generation-meta\">{string.Join(" &bull; ", metaParts)}</div>");
                }
            }

            sb.AppendLine("    </div>");

            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                var citations = TryParseCitations(message.CitationsJson);
                citationsList.AddRange(citations);
            }
        }

        sb.AppendLine("  </div>"); // .messages

        if (citationsList.Count > 0)
        {
            sb.AppendLine("  <div class=\"citations\">");
            sb.AppendLine("    <h2>Citations</h2>");
            sb.AppendLine("    <ol>");
            foreach (var citation in citationsList)
            {
                sb.AppendLine($"      <li>{HtmlEncode(citation)}</li>");
            }
            sb.AppendLine("    </ol>");
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("</div>"); // .conversation

        return sb.ToString();
    }

    private static string BuildSearchResultsHtml(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();
        sb.Append(GetHtmlDocumentHeader(title));

        sb.AppendLine("<div class=\"search-results\">");
        sb.AppendLine($"  <h1>{HtmlEncode(title)}</h1>");
        sb.AppendLine("  <div class=\"metadata\">");
        sb.AppendLine($"    <span>Query: {HtmlEncode(query)}</span>");
        sb.AppendLine($"    <span>Results: {results.Count}</span>");
        sb.AppendLine($"    <span>Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</span>");
        sb.AppendLine("  </div>");

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"  <div class=\"result\">");
            sb.AppendLine($"    <h2>Result {i + 1}: {HtmlEncode(result.DocumentName)}</h2>");

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"    <div class=\"relevance\">Relevance: {result.RelevanceScore:P1}</div>");
            }

            var contentHtml = Markdig.Markdown.ToHtml(result.Content, MarkdownPipeline);
            sb.AppendLine($"    <div class=\"content\">{contentHtml}</div>");

            if (options.IncludeCitations && result.Citations.Count > 0)
            {
                sb.AppendLine("    <div class=\"citations\">");
                sb.AppendLine("      <strong>Sources:</strong>");
                sb.AppendLine("      <ul>");
                foreach (var citation in result.Citations)
                {
                    sb.AppendLine($"        <li>{HtmlEncode(citation)}</li>");
                }
                sb.AppendLine("      </ul>");
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("  </div>"); // .result
        }

        sb.AppendLine("</div>"); // .search-results
        sb.Append(GetHtmlDocumentFooter());

        return sb.ToString();
    }

    private static string GetHtmlDocumentHeader(string title)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <meta name=""generator"" content=""Agent-X Export"" />
  <title>{HtmlEncode(title)}</title>
  <style>
    :root {{
      --bg-primary: #ffffff;
      --bg-secondary: #f8f9fa;
      --bg-user: #e3f2fd;
      --bg-assistant: #f5f5f5;
      --text-primary: #212529;
      --text-secondary: #6c757d;
      --text-muted: #adb5bd;
      --border-color: #dee2e6;
      --accent-color: #0d6efd;
      --accent-dark: #0a58ca;
      --font-sans: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
      --font-mono: 'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace;
    }}

    @media (prefers-color-scheme: dark) {{
      :root {{
        --bg-primary: #1a1a2e;
        --bg-secondary: #16213e;
        --bg-user: #1a365d;
        --bg-assistant: #2d2d44;
        --text-primary: #e2e8f0;
        --text-secondary: #a0aec0;
        --text-muted: #718096;
        --border-color: #4a5568;
        --accent-color: #63b3ed;
        --accent-dark: #4299e1;
      }}
    }}

    @media print {{
      :root {{
        --bg-primary: #ffffff;
        --bg-secondary: #f8f9fa;
        --bg-user: #e3f2fd;
        --bg-assistant: #f5f5f5;
        --text-primary: #212529;
        --text-secondary: #6c757d;
        --text-muted: #adb5bd;
        --border-color: #dee2e6;
        --accent-color: #0d6efd;
      }}
    }}

    * {{
      margin: 0;
      padding: 0;
      box-sizing: border-box;
    }}

    body {{
      font-family: var(--font-sans);
      background-color: var(--bg-primary);
      color: var(--text-primary);
      line-height: 1.6;
      max-width: 900px;
      margin: 0 auto;
      padding: 2rem;
    }}

    h1 {{
      font-size: 1.75rem;
      font-weight: 700;
      margin-bottom: 0.5rem;
      color: var(--text-primary);
    }}

    h2 {{
      font-size: 1.25rem;
      font-weight: 600;
      margin: 1.5rem 0 0.75rem;
      color: var(--text-primary);
    }}

    .metadata {{
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
      padding: 0.75rem 1rem;
      background-color: var(--bg-secondary);
      border-radius: 8px;
      margin-bottom: 1.5rem;
      font-size: 0.875rem;
      color: var(--text-secondary);
      border: 1px solid var(--border-color);
    }}

    .system-prompt {{
      margin-bottom: 1.5rem;
    }}

    .system-prompt blockquote {{
      padding: 0.75rem 1rem;
      border-left: 4px solid var(--accent-color);
      background-color: var(--bg-secondary);
      border-radius: 0 8px 8px 0;
      font-style: italic;
      color: var(--text-secondary);
      white-space: pre-wrap;
    }}

    .messages {{
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }}

    .message {{
      padding: 1rem 1.25rem;
      border-radius: 12px;
      border: 1px solid var(--border-color);
    }}

    .message.user {{
      background-color: var(--bg-user);
      border-left: 4px solid var(--accent-color);
    }}

    .message.assistant {{
      background-color: var(--bg-assistant);
      border-left: 4px solid var(--text-muted);
    }}

    .role-label {{
      font-weight: 700;
      font-size: 0.8rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--accent-color);
      margin-bottom: 0.25rem;
    }}

    .message.assistant .role-label {{
      color: var(--text-secondary);
    }}

    .timestamp {{
      font-size: 0.75rem;
      color: var(--text-muted);
      margin-bottom: 0.5rem;
    }}

    .content {{
      font-size: 0.9375rem;
      line-height: 1.6;
    }}

    .content p {{
      margin-bottom: 0.5rem;
    }}

    .content pre {{
      background-color: var(--bg-secondary);
      border: 1px solid var(--border-color);
      border-radius: 6px;
      padding: 0.75rem 1rem;
      overflow-x: auto;
      font-family: var(--font-mono);
      font-size: 0.85rem;
      margin: 0.5rem 0;
    }}

    .content code {{
      font-family: var(--font-mono);
      font-size: 0.85em;
      background-color: var(--bg-secondary);
      padding: 0.125rem 0.375rem;
      border-radius: 4px;
    }}

    .content pre code {{
      background: none;
      padding: 0;
    }}

    .model-info,
    .generation-meta {{
      font-size: 0.75rem;
      color: var(--text-muted);
      margin-top: 0.5rem;
      font-style: italic;
    }}

    .citations {{
      margin-top: 2rem;
      padding-top: 1rem;
      border-top: 2px solid var(--border-color);
    }}

    .citations ol {{
      padding-left: 1.5rem;
    }}

    .citations li {{
      margin-bottom: 0.25rem;
      font-size: 0.875rem;
      color: var(--text-secondary);
    }}

    .result {{
      padding: 1rem 1.25rem;
      border-radius: 12px;
      border: 1px solid var(--border-color);
      background-color: var(--bg-secondary);
      margin-bottom: 1rem;
    }}

    .relevance {{
      font-size: 0.8rem;
      color: var(--accent-color);
      font-weight: 600;
      margin-bottom: 0.5rem;
    }}

    hr.section-divider {{
      border: none;
      border-top: 2px solid var(--border-color);
      margin: 2rem 0;
    }}

    .footer {{
      margin-top: 2rem;
      padding-top: 1rem;
      border-top: 1px solid var(--border-color);
      text-align: center;
      font-size: 0.8rem;
      color: var(--text-muted);
    }}

    @media print {{
      body {{
        max-width: none;
        padding: 1rem;
      }}

      .message {{
        break-inside: avoid;
      }}
    }}
  </style>
</head>
<body>
";
    }

    private static string GetHtmlDocumentFooter()
    {
        return $@"
  <div class=""footer"">
    Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}
  </div>
</body>
</html>
";
    }

    // ════════════════════════════════════════════════════════════════
    //  JSON builders
    // ════════════════════════════════════════════════════════════════

    private static string BuildJson(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        string title)
    {
        var export = new
        {
            exportMetadata = new
            {
                title,
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                format = "json",
                conversationCount = conversations.Count,
            },
            conversations = conversations.Select(c => new
            {
                id = c.Id,
                title = c.Title,
                systemPrompt = c.SystemPrompt,
                modelId = c.ModelId,
                createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt,
                isPinned = c.IsPinned,
                isArchived = c.IsArchived,
                messageCount = c.MessageCount,
                tokensUsed = c.TokensUsed,
                messages = c.Messages
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new
                    {
                        id = m.Id,
                        role = m.Role,
                        content = m.Content,
                        timestamp = m.Timestamp,
                        tokenCount = options.IncludeMetadata ? m.TokenCount : (int?)null,
                        generationTimeMs = options.IncludeMetadata ? m.GenerationTimeMs : null,
                        modelId = options.IncludeModelInfo ? m.ModelId : null,
                        citations = options.IncludeCitations
                            ? m.CitationsJson
                            : null,
                        sortOrder = m.SortOrder,
                    }).ToArray(),
            }).ToArray(),
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    private static string BuildSearchResultsJson(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        string title)
    {
        var export = new
        {
            exportMetadata = new
            {
                title,
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                format = "json",
                resultCount = results.Count,
            },
            query,
            results = results.Select((r, i) => new
            {
                index = i + 1,
                documentName = r.DocumentName,
                relevanceScore = r.RelevanceScore,
                content = r.Content,
                citations = r.Citations,
            }).ToArray(),
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    // ════════════════════════════════════════════════════════════════
    //  Plain text builders
    // ════════════════════════════════════════════════════════════════

    private static string BuildPlainText(
        ConversationEntity conversation,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine(title);
        sb.AppendLine(new string('=', Math.Min(title.Length, 80)));
        sb.AppendLine();

        if (options.IncludeMetadata)
        {
            sb.AppendLine($"Created:     {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Updated:     {conversation.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Messages:    {conversation.MessageCount}");
            sb.AppendLine($"Tokens Used: {conversation.TokensUsed:N0}");

            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            {
                sb.AppendLine($"Model:       {conversation.ModelId}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            sb.AppendLine("[System Prompt]");
            sb.AppendLine(conversation.SystemPrompt);
            sb.AppendLine();
        }

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        var citationsList = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                continue;
            }

            var roleLabel = GetRoleLabel(message.Role);
            sb.AppendLine($"[{roleLabel}]");

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"  {message.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            }

            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
            {
                sb.AppendLine($"  Model: {message.ModelId}");
            }

            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();

            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
            {
                var citations = TryParseCitations(message.CitationsJson);
                citationsList.AddRange(citations);
            }

            if (options.IncludeMetadata && message.Role == "assistant")
            {
                var metaParts = new List<string>();
                if (message.TokenCount > 0)
                {
                    metaParts.Add($"Tokens: {message.TokenCount:N0}");
                }
                if (message.GenerationTimeMs.HasValue)
                {
                    metaParts.Add($"Generation: {message.GenerationTimeMs.Value:F0}ms");
                }
                if (metaParts.Count > 0)
                {
                    sb.AppendLine($"  ({string.Join(" | ", metaParts)})");
                    sb.AppendLine();
                }
            }
        }

        if (citationsList.Count > 0)
        {
            sb.AppendLine(new string('-', 40));
            sb.AppendLine("Citations:");
            for (var i = 0; i < citationsList.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {citationsList[i]}");
            }
            sb.AppendLine();
        }

        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }

    private static string BuildSearchResultsPlainText(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        string title)
    {
        var sb = new StringBuilder();

        sb.AppendLine(title);
        sb.AppendLine(new string('=', Math.Min(title.Length, 80)));
        sb.AppendLine();
        sb.AppendLine($"Query:    {query}");
        sb.AppendLine($"Results:  {results.Count}");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];

            sb.AppendLine($"--- Result {i + 1}: {result.DocumentName} ---");

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"Relevance: {result.RelevanceScore:P1}");
            }

            sb.AppendLine();
            sb.AppendLine(result.Content);
            sb.AppendLine();

            if (options.IncludeCitations && result.Citations.Count > 0)
            {
                sb.AppendLine("Sources:");
                foreach (var citation in result.Citations)
                {
                    sb.AppendLine($"  - {citation}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    //  PDF generation (QuestPDF)
    // ════════════════════════════════════════════════════════════════

    private async Task GeneratePdfAsync(
        ConversationEntity conversation,
        ExportOptions options,
        string title,
        string outputPath,
        CancellationToken ct)
    {
        await GenerateMultiConversationPdfAsync(
            new[] { conversation }, options, title, outputPath, ct);
    }

    private Task GenerateMultiConversationPdfAsync(
        IReadOnlyList<ConversationEntity> conversations,
        ExportOptions options,
        string title,
        string outputPath,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // QuestPDF Community license configuration
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.MarginTop(1.5f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.MarginBottom(1.5f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.MarginHorizontal(2f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor("#212529"));

                    // ── Header ──────────────────────────────────
                    page.Header().Column(headerCol =>
                    {
                        headerCol.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Agent-X")
                                .Bold().FontSize(16).FontColor("#0d6efd");

                            row.ConstantItem(160).AlignRight().Text(text =>
                            {
                                text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).FontSize(8)
                                    .FontColor("#6c757d");
                            });
                        });

                        headerCol.Item().PaddingTop(4).LineHorizontal(1).LineColor("#dee2e6");
                        headerCol.Item().PaddingBottom(8);
                    });

                    // ── Content ─────────────────────────────────
                    page.Content().Column(contentCol =>
                    {
                        // Document title
                        contentCol.Item().Text(title)
                            .Bold().FontSize(14).FontColor("#212529");

                        contentCol.Item().PaddingBottom(12);

                        for (var ci = 0; ci < conversations.Count; ci++)
                        {
                            var conversation = conversations[ci];

                            // Section divider between conversations
                            if (ci > 0)
                            {
                                contentCol.Item().PaddingVertical(10)
                                    .LineHorizontal(1).LineColor("#adb5bd");

                                contentCol.Item().PaddingBottom(6).Text(conversation.Title)
                                    .Bold().FontSize(12).FontColor("#212529");
                            }

                            // Metadata block
                            if (options.IncludeMetadata)
                            {
                                contentCol.Item().Background("#f8f9fa")
                                    .Border(1).BorderColor("#dee2e6")
                                    .Padding(8).Column(metaCol =>
                                    {
                                        metaCol.Item().Text($"Created: {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC")
                                            .FontSize(8).FontColor("#6c757d");
                                        metaCol.Item().Text($"Messages: {conversation.MessageCount}  |  Tokens: {conversation.TokensUsed:N0}")
                                            .FontSize(8).FontColor("#6c757d");

                                        if (!string.IsNullOrWhiteSpace(conversation.ModelId))
                                        {
                                            metaCol.Item().Text($"Model: {conversation.ModelId}")
                                                .FontSize(8).FontColor("#6c757d");
                                        }
                                    });

                                contentCol.Item().PaddingBottom(10);
                            }

                            // System prompt
                            if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
                            {
                                contentCol.Item().Text("System Prompt")
                                    .Bold().FontSize(10).FontColor("#6c757d");

                                contentCol.Item().PaddingLeft(8)
                                    .BorderLeft(3).BorderColor("#0d6efd")
                                    .PaddingLeft(8).PaddingVertical(4)
                                    .Text(conversation.SystemPrompt)
                                    .FontSize(9).Italic().FontColor("#6c757d");

                                contentCol.Item().PaddingBottom(10);
                            }

                            // Messages
                            var messages = conversation.Messages
                                .OrderBy(m => m.SortOrder)
                                .ToList();

                            var citationsList = new List<string>();

                            foreach (var message in messages)
                            {
                                if (message.Role == "system")
                                {
                                    continue;
                                }

                                var isUser = message.Role == "user";
                                var bgColor = isUser ? "#e3f2fd" : "#f5f5f5";
                                var accentColor = isUser ? "#0d6efd" : "#adb5bd";
                                var roleLabel = GetRoleLabel(message.Role);

                                contentCol.Item().PaddingBottom(6)
                                    .BorderLeft(3).BorderColor(accentColor)
                                    .Background(bgColor)
                                    .Padding(8).Column(msgCol =>
                                    {
                                        // Role label row
                                        msgCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text(roleLabel)
                                                .Bold().FontSize(8).FontColor(accentColor);

                                            if (options.IncludeTimestamps)
                                            {
                                                row.ConstantItem(120).AlignRight()
                                                    .Text(message.Timestamp.ToString("yyyy-MM-dd HH:mm"))
                                                    .FontSize(7).FontColor("#adb5bd");
                                            }
                                        });

                                        msgCol.Item().PaddingTop(4)
                                            .Text(message.Content).FontSize(9.5f);

                                        // Model info
                                        if (options.IncludeModelInfo
                                            && !string.IsNullOrWhiteSpace(message.ModelId))
                                        {
                                            msgCol.Item().PaddingTop(4)
                                                .Text($"Model: {message.ModelId}")
                                                .FontSize(7).Italic().FontColor("#adb5bd");
                                        }

                                        // Generation metadata
                                        if (options.IncludeMetadata && message.Role == "assistant")
                                        {
                                            var metaParts = new List<string>();
                                            if (message.TokenCount > 0)
                                            {
                                                metaParts.Add($"Tokens: {message.TokenCount:N0}");
                                            }
                                            if (message.GenerationTimeMs.HasValue)
                                            {
                                                metaParts.Add($"{message.GenerationTimeMs.Value:F0}ms");
                                            }
                                            if (metaParts.Count > 0)
                                            {
                                                msgCol.Item().PaddingTop(2)
                                                    .Text(string.Join("  |  ", metaParts))
                                                    .FontSize(7).FontColor("#adb5bd");
                                            }
                                        }
                                    });

                                // Collect citations
                                if (options.IncludeCitations
                                    && !string.IsNullOrWhiteSpace(message.CitationsJson))
                                {
                                    var citations = TryParseCitations(message.CitationsJson);
                                    citationsList.AddRange(citations);
                                }
                            }

                            // Citations section
                            if (citationsList.Count > 0)
                            {
                                contentCol.Item().PaddingTop(12);
                                contentCol.Item().Text("Citations")
                                    .Bold().FontSize(10).FontColor("#6c757d");

                                contentCol.Item().PaddingTop(4).Column(citCol =>
                                {
                                    for (var ci2 = 0; ci2 < citationsList.Count; ci2++)
                                    {
                                        citCol.Item().Text($"{ci2 + 1}. {citationsList[ci2]}")
                                            .FontSize(8).FontColor("#6c757d");
                                    }
                                });
                            }
                        }
                    });

                    // ── Footer ──────────────────────────────────
                    page.Footer().Column(footerCol =>
                    {
                        footerCol.Item().LineHorizontal(1).LineColor("#dee2e6");
                        footerCol.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text("Exported from Agent-X")
                                .FontSize(7).FontColor("#adb5bd");

                            row.ConstantItem(80).AlignRight().Text(text =>
                            {
                                text.Span("Page ").FontSize(7).FontColor("#adb5bd");
                                text.CurrentPageNumber().FontSize(7).FontColor("#adb5bd");
                                text.Span(" / ").FontSize(7).FontColor("#adb5bd");
                                text.TotalPages().FontSize(7).FontColor("#adb5bd");
                            });
                        });
                    });
                });
            });

            document.GeneratePdf(outputPath);

            _log.Debug("PDF generated successfully at '{Path}'", outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "QuestPDF generation failed, falling back to HTML export");

            // Graceful fallback: generate an HTML file instead of PDF
            var fallbackPath = Path.ChangeExtension(outputPath, ".html");

            var sb = new StringBuilder();
            sb.Append(GetHtmlDocumentHeader(title));

            foreach (var conversation in conversations)
            {
                sb.Append(BuildHtmlBody(conversation, options, conversation.Title));
            }

            sb.Append(GetHtmlDocumentFooter());

            File.WriteAllText(fallbackPath, sb.ToString(), Encoding.UTF8);

            _log.Warning(
                "PDF fallback: saved HTML to '{FallbackPath}' instead of PDF", fallbackPath);

            // Update the output path so the caller gets the correct file reference
            // This is handled by re-throwing with context; the caller's catch block
            // will return the error. We throw a specific exception to signal this.
            throw new InvalidOperationException(
                $"PDF generation failed. An HTML fallback was saved to: {fallbackPath}", ex);
        }

        return Task.CompletedTask;
    }

    private Task GenerateSearchResultsPdfAsync(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        string title,
        string outputPath,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.MarginTop(1.5f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.MarginBottom(1.5f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.MarginHorizontal(2f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor("#212529"));

                    // ── Header ──────────────────────────────────
                    page.Header().Column(headerCol =>
                    {
                        headerCol.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Agent-X")
                                .Bold().FontSize(16).FontColor("#0d6efd");

                            row.ConstantItem(160).AlignRight().Text(text =>
                            {
                                text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
                                    .FontSize(8).FontColor("#6c757d");
                            });
                        });

                        headerCol.Item().PaddingTop(4).LineHorizontal(1).LineColor("#dee2e6");
                        headerCol.Item().PaddingBottom(8);
                    });

                    // ── Content ─────────────────────────────────
                    page.Content().Column(contentCol =>
                    {
                        contentCol.Item().Text(title)
                            .Bold().FontSize(14).FontColor("#212529");

                        contentCol.Item().PaddingTop(4).Text($"Query: {query}")
                            .FontSize(10).FontColor("#6c757d");

                        contentCol.Item().Text($"Results: {results.Count}")
                            .FontSize(9).FontColor("#adb5bd");

                        contentCol.Item().PaddingBottom(12);

                        for (var i = 0; i < results.Count; i++)
                        {
                            var result = results[i];

                            contentCol.Item().PaddingBottom(8)
                                .Background("#f8f9fa")
                                .Border(1).BorderColor("#dee2e6")
                                .Padding(10).Column(resultCol =>
                                {
                                    resultCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text($"Result {i + 1}: {result.DocumentName}")
                                            .Bold().FontSize(10).FontColor("#212529");

                                        if (options.IncludeMetadata)
                                        {
                                            row.ConstantItem(80).AlignRight()
                                                .Text($"{result.RelevanceScore:P1}")
                                                .FontSize(9).FontColor("#0d6efd");
                                        }
                                    });

                                    resultCol.Item().PaddingTop(6)
                                        .Text(result.Content).FontSize(9.5f);

                                    if (options.IncludeCitations && result.Citations.Count > 0)
                                    {
                                        resultCol.Item().PaddingTop(6).Column(citCol =>
                                        {
                                            citCol.Item().Text("Sources:")
                                                .Bold().FontSize(8).FontColor("#6c757d");

                                            foreach (var citation in result.Citations)
                                            {
                                                citCol.Item().PaddingLeft(8)
                                                    .Text($"- {citation}")
                                                    .FontSize(8).FontColor("#6c757d");
                                            }
                                        });
                                    }
                                });
                        }
                    });

                    // ── Footer ──────────────────────────────────
                    page.Footer().Column(footerCol =>
                    {
                        footerCol.Item().LineHorizontal(1).LineColor("#dee2e6");
                        footerCol.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text("Exported from Agent-X")
                                .FontSize(7).FontColor("#adb5bd");

                            row.ConstantItem(80).AlignRight().Text(text =>
                            {
                                text.Span("Page ").FontSize(7).FontColor("#adb5bd");
                                text.CurrentPageNumber().FontSize(7).FontColor("#adb5bd");
                                text.Span(" / ").FontSize(7).FontColor("#adb5bd");
                                text.TotalPages().FontSize(7).FontColor("#adb5bd");
                            });
                        });
                    });
                });
            });

            document.GeneratePdf(outputPath);

            _log.Debug("Search results PDF generated successfully at '{Path}'", outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "QuestPDF generation failed for search results, falling back to HTML");

            var fallbackPath = Path.ChangeExtension(outputPath, ".html");
            var htmlContent = BuildSearchResultsHtml(query, results, options, title);
            File.WriteAllText(fallbackPath, htmlContent, Encoding.UTF8);

            _log.Warning(
                "PDF fallback: saved HTML to '{FallbackPath}' instead of PDF", fallbackPath);

            throw new InvalidOperationException(
                $"PDF generation failed. An HTML fallback was saved to: {fallbackPath}", ex);
        }

        return Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════════
    //  Collection README builder
    // ════════════════════════════════════════════════════════════════

    private static string BuildCollectionReadme(
        CollectionEntity collection,
        IReadOnlyList<DocumentEntity> documents)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Collection: {collection.Name}");
        sb.AppendLine(new string('=', Math.Min(collection.Name.Length + 12, 80)));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(collection.Description))
        {
            sb.AppendLine(collection.Description);
            sb.AppendLine();
        }

        sb.AppendLine($"Created:   {collection.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Updated:   {collection.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Documents: {documents.Count}");
        sb.AppendLine();

        if (documents.Count > 0)
        {
            sb.AppendLine("Document Manifest");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();

            var totalSizeBytes = 0L;
            foreach (var doc in documents)
            {
                sb.AppendLine($"  {doc.FileName}");
                sb.AppendLine($"    Type:      {doc.FileType}");
                sb.AppendLine($"    Size:      {FormatFileSize(doc.FileSizeBytes)}");
                sb.AppendLine($"    Pages:     {doc.PageCount}");
                sb.AppendLine($"    Words:     {doc.WordCount:N0}");
                sb.AppendLine($"    Imported:  {doc.ImportedAt:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine($"    Status:    {doc.IndexingStatus}");
                sb.AppendLine($"    Path:      {doc.FilePath}");

                if (!string.IsNullOrWhiteSpace(doc.Summary))
                {
                    var summaryPreview = doc.Summary.Length > 200
                        ? doc.Summary[..200] + "..."
                        : doc.Summary;
                    sb.AppendLine($"    Summary:   {summaryPreview}");
                }

                sb.AppendLine();
                totalSizeBytes += doc.FileSizeBytes;
            }

            sb.AppendLine($"Total Size: {FormatFileSize(totalSizeBytes)}");
        }
        else
        {
            sb.AppendLine("(No documents in this collection)");
        }

        sb.AppendLine();
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    //  CSV builders
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a CSV representation of a single conversation's messages.
    /// Columns: Role, Content, Timestamp, Model, Tokens.
    /// System messages are excluded from the output.
    /// </summary>
    private static string BuildConversationCsv(
        ConversationEntity conversation,
        ExportOptions options)
    {
        var sb = new StringBuilder();

        // CSV header row
        sb.AppendLine("Role,Content,Timestamp,Model,Tokens");

        var messages = conversation.Messages
            .OrderBy(m => m.SortOrder)
            .ToList();

        foreach (var message in messages)
        {
            // Skip system messages — they are internal directives, not user-facing content
            if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                continue;

            sb.Append(CsvEscape(message.Role)).Append(',');
            sb.Append(CsvEscape(message.Content)).Append(',');
            sb.Append(CsvEscape(message.Timestamp.ToString("O"))).Append(',');
            sb.Append(CsvEscape(message.ModelId ?? "")).Append(',');
            sb.AppendLine(message.TokenCount.ToString());
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a CSV representation of search results.
    /// Columns: Query, DocumentName, Excerpt, Score, Citations.
    /// </summary>
    private static string BuildSearchResultsCsv(
        string query,
        IReadOnlyList<SearchResultExportItem> results)
    {
        var sb = new StringBuilder();

        // CSV header row
        sb.AppendLine("Query,DocumentName,Excerpt,Score,Citations");

        foreach (var result in results)
        {
            sb.Append(CsvEscape(query)).Append(',');
            sb.Append(CsvEscape(result.DocumentName)).Append(',');
            sb.Append(CsvEscape(result.Content)).Append(',');
            sb.Append(CsvEscape(result.RelevanceScore.ToString("F4"))).Append(',');

            var citations = result.Citations.Count > 0
                ? string.Join("; ", result.Citations)
                : "";
            sb.AppendLine(CsvEscape(citations));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a CSV representation of a document collection's contents.
    /// Columns: FileName, FilePath, FileType, FileSize, ImportedAt, IndexingStatus, PageCount, WordCount.
    /// </summary>
    private static string BuildCollectionCsv(
        CollectionEntity collection,
        IReadOnlyList<DocumentEntity> documents)
    {
        var sb = new StringBuilder();

        // CSV header row
        sb.AppendLine("FileName,FilePath,FileType,FileSize,ImportedAt,IndexingStatus,PageCount,WordCount");

        foreach (var doc in documents)
        {
            sb.Append(CsvEscape(doc.FileName)).Append(',');
            sb.Append(CsvEscape(doc.FilePath)).Append(',');
            sb.Append(CsvEscape(doc.FileType)).Append(',');
            sb.Append(doc.FileSizeBytes.ToString()).Append(',');
            sb.Append(CsvEscape(doc.ImportedAt.ToString("O"))).Append(',');
            sb.Append(CsvEscape(doc.IndexingStatus)).Append(',');
            sb.Append(doc.PageCount.ToString()).Append(',');
            sb.AppendLine(doc.WordCount.ToString());
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a value for safe inclusion in a CSV field.
    /// Wraps the value in double quotes if it contains commas, double quotes,
    /// newlines, or carriage returns. Internal double quotes are escaped by doubling them.
    /// Empty or null values are represented as an empty quoted string.
    /// </summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    // ════════════════════════════════════════════════════════════════
    //  Utility methods
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the final output file path based on export options and format.
    /// Falls back to the app storage export directory if no path is specified.
    /// </summary>
    private async Task<string> ResolveOutputPathAsync(
        ExportOptions options,
        string title,
        ExportFormat format)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return options.OutputPath;
        }

        var exportDir = await GetExportDirectoryAsync();
        var extension = GetFileExtension(format);
        var sanitizedTitle = SanitizeFileName(title);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        return Path.Combine(exportDir, $"{sanitizedTitle}_{timestamp}{extension}");
    }

    /// <summary>
    /// Returns the default export directory within the app storage path.
    /// Creates the directory if it does not exist.
    /// </summary>
    private async Task<string> GetExportDirectoryAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        var exportDir = Path.Combine(settings.StoragePath, "Exports");
        Directory.CreateDirectory(exportDir);
        return exportDir;
    }

    /// <summary>
    /// Ensures the parent directory for the given file path exists.
    /// </summary>
    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Returns the file extension (including the dot) for the given format.
    /// </summary>
    private static string GetFileExtension(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Markdown => ".md",
            ExportFormat.Html => ".html",
            ExportFormat.Pdf => ".pdf",
            ExportFormat.Json => ".json",
            ExportFormat.PlainText => ".txt",
            ExportFormat.Csv => ".csv",
            _ => ".txt",
        };
    }

    /// <summary>
    /// Sanitizes a string for use as a file name by replacing invalid characters.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "export";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            sanitized.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
        }

        // Trim to a reasonable file name length
        var result = sanitized.ToString().Trim();
        if (result.Length > 100)
        {
            result = result[..100];
        }

        return string.IsNullOrWhiteSpace(result) ? "export" : result;
    }

    /// <summary>
    /// Returns a human-readable label for a message role.
    /// </summary>
    private static string GetRoleLabel(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "user" => "User",
            "assistant" => "Assistant",
            "system" => "System",
            _ => role,
        };
    }

    /// <summary>
    /// HTML-encodes a string to prevent XSS and rendering issues.
    /// </summary>
    private static string HtmlEncode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// Escapes special Markdown characters in a string.
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Escape characters that have special meaning in Markdown titles
        return text
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }

    /// <summary>
    /// Attempts to parse a CitationsJson string into a list of human-readable
    /// citation descriptions. Returns an empty list on parse failure.
    /// </summary>
    private static List<string> TryParseCitations(string citationsJson)
    {
        var result = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(citationsJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var fileName = element.TryGetProperty("fileName", out var fn)
                    ? fn.GetString() ?? "Unknown"
                    : "Unknown";

                var pageNumber = element.TryGetProperty("pageNumber", out var pn)
                    && pn.ValueKind == JsonValueKind.Number
                    ? pn.GetInt32()
                    : (int?)null;

                var excerpt = element.TryGetProperty("excerpt", out var ex)
                    ? ex.GetString()
                    : null;

                var description = pageNumber.HasValue
                    ? $"{fileName}, page {pageNumber.Value}"
                    : fileName;

                if (!string.IsNullOrWhiteSpace(excerpt))
                {
                    var shortExcerpt = excerpt.Length > 80
                        ? excerpt[..80] + "..."
                        : excerpt;
                    description += $" - \"{shortExcerpt}\"";
                }

                result.Add(description);
            }
        }
        catch (JsonException)
        {
            // CitationsJson was not valid JSON; return empty list
        }

        return result;
    }

    /// <summary>
    /// Formats a byte count into a human-readable file size string.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        var size = (double)bytes;

        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:F1} {suffixes[order]}";
    }
}

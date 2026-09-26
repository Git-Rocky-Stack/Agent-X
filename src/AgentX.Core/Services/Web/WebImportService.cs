using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Web.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// High-level service that bridges web content scraping with the document import pipeline.
/// Extracts content from URLs and creates <see cref="DocumentEntity"/> records that feed
/// into the normal indexing pipeline for chunking and embedding.
/// </summary>
public interface IWebImportService
{
    /// <summary>
    /// Imports a single URL: scrapes the web page, saves the extracted content to a
    /// temporary Markdown file, and creates a <see cref="DocumentEntity"/> with status "pending".
    /// When a collection is given, the document and its collection link are saved together,
    /// so the call either imports the page into that collection or imports nothing.
    /// </summary>
    /// <param name="url">The absolute HTTP or HTTPS URL to import.</param>
    /// <param name="collectionId">Optional collection to associate the imported document with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="DocumentEntity"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the URL is invalid, content extraction fails, the content is a duplicate,
    /// or the requested collection does not exist.
    /// </exception>
    Task<DocumentEntity> ImportFromUrlAsync(string url, long? collectionId = null, CancellationToken ct = default);

    /// <summary>
    /// Imports multiple URLs sequentially, reporting progress after each URL.
    /// Individual failures do not abort the batch; each is reported in its own result.
    /// </summary>
    /// <param name="urls">The list of URLs to import.</param>
    /// <param name="collectionId">Optional collection to associate all imported documents with.</param>
    /// <param name="progress">Optional progress reporter (number of URLs completed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One <see cref="WebImportResult"/> per requested URL, in request order.</returns>
    Task<IReadOnlyList<WebImportResult>> ImportFromUrlsAsync(
        IReadOnlyList<string> urls,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Imports URLs read from a feed or sitemap. Works like <see cref="ImportFromUrlsAsync"/>,
    /// except that these URLs come from remote content: a URL that points to this computer or
    /// a private network address is not fetched, and its result says why, unless
    /// <paramref name="sourceUrl"/> (the feed or sitemap itself) is on such a network too.
    /// This keeps a public feed or sitemap from making Agent-X call local services.
    /// </summary>
    /// <param name="sourceUrl">The feed or sitemap URL the user entered.</param>
    /// <param name="urls">The URLs listed by that feed or sitemap.</param>
    /// <param name="collectionId">Optional collection to associate all imported documents with.</param>
    /// <param name="progress">Optional progress reporter (number of URLs completed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One <see cref="WebImportResult"/> per requested URL, in request order.</returns>
    Task<IReadOnlyList<WebImportResult>> ImportDiscoveredUrlsAsync(
        string sourceUrl,
        IReadOnlyList<string> urls,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of <see cref="IWebImportService"/> that coordinates web scraping,
/// file persistence, and database record creation.
/// <para>
/// The import flow:
/// <list type="number">
///   <item>Extract content from the URL using <see cref="IWebScraperService"/>.</item>
///   <item>Save the extracted text to a Markdown (.md) file in the application storage path.</item>
///   <item>Create a <see cref="DocumentEntity"/> with the file path, content hash, and metadata.</item>
///   <item>Link the document to the requested collection in the same save; a missing collection fails the import.</item>
///   <item>The document is left in "pending" status for the indexing pipeline.</item>
/// </list>
/// </para>
/// </summary>
public class WebImportService : IWebImportService
{
    private readonly IWebScraperService _webScraper;
    private readonly AgentXDbContext _db;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _log;

    /// <summary>
    /// JSON serializer options used for writing metadata JSON to the DocumentEntity.
    /// </summary>
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Subdirectory under the application storage path where web-imported files are stored.
    /// </summary>
    private const string WebImportFolderName = "WebImports";

    /// <summary>
    /// Initializes a new instance of <see cref="WebImportService"/>.
    /// </summary>
    /// <param name="webScraper">The web scraper service for content extraction.</param>
    /// <param name="db">The EF Core database context.</param>
    /// <param name="settingsService">The settings service for resolving the storage path.</param>
    /// <param name="logger">The Serilog logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public WebImportService(
        IWebScraperService webScraper,
        AgentXDbContext db,
        ISettingsService settingsService,
        ILogger logger)
    {
        _webScraper = webScraper ?? throw new ArgumentNullException(nameof(webScraper));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _log = logger?.ForContext<WebImportService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DocumentEntity> ImportFromUrlAsync(
        string url,
        long? collectionId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL must not be empty.", nameof(url));
        }

        if (!_webScraper.IsValidUrl(url))
        {
            throw new InvalidOperationException(
                $"Invalid URL: '{url}'. Only HTTP and HTTPS URLs are supported.");
        }

        // Resolve the target collection before fetching anything: a missing collection fails
        // the import up front instead of leaving an imported document outside it.
        CollectionEntity? collection = null;
        if (collectionId.HasValue)
        {
            collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId.Value, ct)
                ?? throw new InvalidOperationException(
                    $"Collection {collectionId.Value} was not found, so '{url}' was not imported.");
        }

        _log.Information("Importing web content from: {Url}", url);

        // Step 1: Extract content from the URL
        var webContent = await _webScraper.ExtractContentAsync(url, ct);

        if (!webContent.Success)
        {
            throw new InvalidOperationException(
                $"Failed to extract content from '{url}': {webContent.ErrorMessage}");
        }

        if (string.IsNullOrWhiteSpace(webContent.Content))
        {
            throw new InvalidOperationException(
                $"No content could be extracted from '{url}'.");
        }

        // Step 2: Build the Markdown content with metadata header
        var markdownContent = BuildMarkdownContent(webContent);

        // Step 3: Compute content hash for duplicate detection
        var contentHash = HashHelper.ComputeStringHash(markdownContent);

        // Check for existing document with same content
        var existingDoc = await _db.Documents
            .FirstOrDefaultAsync(d => d.ContentHash == contentHash, ct);

        if (existingDoc is not null)
        {
            _log.Information(
                "Duplicate detected: URL {Url} matches existing document {DocumentId} ({FileName})",
                url, existingDoc.Id, existingDoc.FileName);

            throw new InvalidOperationException(
                $"A document with identical content already exists: '{existingDoc.FileName}' (ID {existingDoc.Id}).");
        }

        // Step 4: Save content to a Markdown file
        var settings = await _settingsService.GetSettingsAsync();
        var webImportDir = GetWebImportDirectory(settings.StoragePath);

        var sanitizedTitle = SanitizeForFileName(webContent.Title);
        var fileName = $"{sanitizedTitle}.md";
        var filePath = Path.Combine(webImportDir, fileName);

        // Ensure unique file name to avoid overwriting existing files
        filePath = EnsureUniqueFilePath(filePath);
        fileName = Path.GetFileName(filePath);

        await File.WriteAllTextAsync(filePath, markdownContent, ct);

        _log.Debug("Saved web content to file: {FilePath}", filePath);

        // Step 5: Create DocumentEntity
        var fileInfo = new FileInfo(filePath);
        var metadataJson = BuildMetadataJson(webContent, url);

        var entity = new DocumentEntity
        {
            FileName = fileName,
            FilePath = Path.GetFullPath(filePath),
            FileType = "web",
            MimeType = "text/markdown",
            FileSizeBytes = fileInfo.Length,
            ContentHash = contentHash,
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = fileInfo.LastWriteTimeUtc,
            IndexingStatus = "pending",
            PageCount = 1,
            WordCount = webContent.WordCount,
            ExtractedTitle = webContent.Title,
            Language = webContent.Language,
            MetadataJson = metadataJson,
        };

        _db.Documents.Add(entity);

        // Step 6: Link to the collection in the same save, so the document is never imported
        // without the collection membership the caller asked for.
        DocumentCollectionEntity? link = null;
        if (collection is not null)
        {
            link = new DocumentCollectionEntity
            {
                Document = entity,
                CollectionId = collection.Id,
                AddedAt = DateTime.UtcNow,
            };
            _db.DocumentCollections.Add(link);

            // Keep the denormalized document count in step with the new membership
            collection.DocumentCount += 1;
            collection.UpdatedAt = DateTime.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            UndoUnsavedImport(entity, link, collection);
            TryDeleteImportFile(filePath);
            throw;
        }

        _log.Information(
            "Imported web document: {FileName} (ID {DocumentId}, {WordCount} words) from {Url}, collection {CollectionId}",
            entity.FileName, entity.Id, entity.WordCount, url, collection?.Id);

        return entity;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WebImportResult>> ImportFromUrlsAsync(
        IReadOnlyList<string> urls,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default) =>
        ImportBatchAsync(urls, collectionId, progress, screen: null, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<WebImportResult>> ImportDiscoveredUrlsAsync(
        string sourceUrl,
        IReadOnlyList<string> urls,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        // Looked up once, and only if some listed URL turns out to be private
        Task<bool>? sourceIsPrivate = null;

        return ImportBatchAsync(urls, collectionId, progress, async (url, token) =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target)
                || !await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(target, token))
            {
                return null;
            }

            sourceIsPrivate ??= Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
                ? PrivateNetworkGuard.IsPrivateOrLocalHostAsync(source, token)
                : Task.FromResult(false);

            return await sourceIsPrivate
                ? null
                : $"Skipped: '{url}' points to this computer or a private network address, which a public feed or sitemap may not do.";
        }, ct);
    }

    /// <summary>
    /// Imports each URL in turn. <paramref name="screen"/>, when given, may refuse a URL before
    /// it is fetched by returning the reason, which becomes that URL's failed result.
    /// </summary>
    private async Task<IReadOnlyList<WebImportResult>> ImportBatchAsync(
        IReadOnlyList<string> urls,
        long? collectionId,
        IProgress<int>? progress,
        Func<string, CancellationToken, Task<string?>>? screen,
        CancellationToken ct)
    {
        if (urls is null || urls.Count == 0)
        {
            return Array.Empty<WebImportResult>();
        }

        _log.Information("Starting batch web import of {Count} URLs", urls.Count);

        var results = new List<WebImportResult>(urls.Count);

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var refusal = screen is null ? null : await screen(url, ct);
                if (refusal is not null)
                {
                    _log.Warning("Did not import {Url}: {Reason}", url, refusal);
                    results.Add(new WebImportResult { Url = url, ErrorMessage = refusal });
                }
                else
                {
                    var entity = await ImportFromUrlAsync(url, collectionId, ct);
                    results.Add(new WebImportResult { Url = url, Document = entity });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The failure is reported in this URL's own result; the batch continues.
                _log.Warning(ex, "Failed to import URL: {Url}", url);
                results.Add(new WebImportResult { Url = url, ErrorMessage = ex.Message });
            }

            progress?.Report(results.Count);
        }

        _log.Information(
            "Batch web import completed: {Imported}/{Total} URLs imported",
            results.Count(r => r.Success), urls.Count);

        return results.AsReadOnly();
    }

    // ─── Private Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Markdown-formatted string from the extracted web content.
    /// Includes a YAML-style metadata header with source URL, author, publish date,
    /// and site name.
    /// </summary>
    private static string BuildMarkdownContent(WebContent webContent)
    {
        var sb = new System.Text.StringBuilder();

        // YAML frontmatter with source metadata
        sb.AppendLine("---");
        sb.AppendLine($"source: {webContent.Url}");

        if (!string.IsNullOrEmpty(webContent.Author))
        {
            sb.AppendLine($"author: {webContent.Author}");
        }

        if (webContent.PublishDate.HasValue)
        {
            sb.AppendLine($"date: {webContent.PublishDate.Value:yyyy-MM-dd}");
        }

        if (!string.IsNullOrEmpty(webContent.SiteName))
        {
            sb.AppendLine($"site: {webContent.SiteName}");
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // Title as H1
        if (!string.IsNullOrEmpty(webContent.Title))
        {
            sb.AppendLine($"# {webContent.Title}");
            sb.AppendLine();
        }

        // Description as introductory paragraph if available
        if (!string.IsNullOrEmpty(webContent.Description))
        {
            sb.AppendLine($"*{webContent.Description}*");
            sb.AppendLine();
        }

        // Main content
        sb.AppendLine(webContent.Content);

        return sb.ToString();
    }

    /// <summary>
    /// Serializes web-specific metadata into a JSON string for the DocumentEntity.MetadataJson field.
    /// Includes the source URL, author, publish date, site name, description, and featured image URL.
    /// </summary>
    private static string BuildMetadataJson(WebContent webContent, string url)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["sourceUrl"] = url,
            ["sourceType"] = "web",
        };

        if (!string.IsNullOrEmpty(webContent.Author))
        {
            metadata["author"] = webContent.Author;
        }

        if (webContent.PublishDate.HasValue)
        {
            metadata["publishDate"] = webContent.PublishDate.Value.ToString("O");
        }

        if (!string.IsNullOrEmpty(webContent.SiteName))
        {
            metadata["siteName"] = webContent.SiteName;
        }

        if (!string.IsNullOrEmpty(webContent.Description))
        {
            metadata["description"] = webContent.Description;
        }

        if (!string.IsNullOrEmpty(webContent.FeaturedImageUrl))
        {
            metadata["featuredImageUrl"] = webContent.FeaturedImageUrl;
        }

        if (!string.IsNullOrEmpty(webContent.Language))
        {
            metadata["language"] = webContent.Language;
        }

        return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }

    /// <summary>
    /// Resolves and ensures the web import directory exists under the application storage path.
    /// Creates the directory if it does not exist.
    /// </summary>
    /// <param name="storagePath">The root application storage path from settings.</param>
    /// <returns>The absolute path to the web import directory.</returns>
    private static string GetWebImportDirectory(string storagePath)
    {
        var webImportDir = Path.Combine(storagePath, WebImportFolderName);
        return PathHelper.EnsureDirectoryExists(webImportDir);
    }

    /// <summary>
    /// Sanitizes a page title for use as a file name. Removes invalid characters,
    /// limits length, and ensures the result is a valid Windows file name.
    /// </summary>
    /// <param name="title">The raw page title to sanitize.</param>
    /// <returns>A sanitized string safe for use as a file name (without extension).</returns>
    private static string SanitizeForFileName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return $"web-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        // Use PathHelper's sanitizer for basic invalid character removal
        var sanitized = PathHelper.SanitizeFileName(title);

        // Additionally trim to a reasonable length for file names
        // (leave room for extension and uniqueness suffix)
        const int maxBaseNameLength = 120;
        if (sanitized.Length > maxBaseNameLength)
        {
            sanitized = sanitized[..maxBaseNameLength].TrimEnd();
        }

        // Remove trailing dots and spaces that Windows doesn't allow
        sanitized = sanitized.TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return $"web-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        }

        return sanitized;
    }

    /// <summary>
    /// Ensures the file path is unique by appending a numeric suffix if a file
    /// with the same name already exists.
    /// </summary>
    /// <param name="filePath">The desired file path.</param>
    /// <returns>A unique file path (original if no conflict, or with suffix appended).</returns>
    private static string EnsureUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var counter = 1;
        string candidatePath;

        do
        {
            candidatePath = Path.Combine(directory, $"{fileNameWithoutExt} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(candidatePath));

        return candidatePath;
    }

    /// <summary>
    /// Takes the unsaved document, its collection link and the collection count change back
    /// off the shared context after a failed save, so a later unrelated save does not retry
    /// (or trip over) them.
    /// </summary>
    private void UndoUnsavedImport(
        DocumentEntity document,
        DocumentCollectionEntity? link,
        CollectionEntity? collection)
    {
        if (link is not null)
        {
            _db.Entry(link).State = EntityState.Detached;
        }

        _db.Entry(document).State = EntityState.Detached;

        if (collection is not null)
        {
            var entry = _db.Entry(collection);
            if (entry.State == EntityState.Modified)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }

    /// <summary>
    /// Removes the Markdown file written for an import whose database save failed, so no
    /// orphaned file is left in the web import folder.
    /// </summary>
    private void TryDeleteImportFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "Could not delete web import file {FilePath} after a failed save", filePath);
        }
    }
}

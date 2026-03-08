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
    /// </summary>
    /// <param name="url">The absolute HTTP or HTTPS URL to import.</param>
    /// <param name="collectionId">Optional collection to associate the imported document with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="DocumentEntity"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the URL is invalid or content extraction fails.</exception>
    Task<DocumentEntity> ImportFromUrlAsync(string url, long? collectionId = null, CancellationToken ct = default);

    /// <summary>
    /// Imports multiple URLs sequentially, reporting progress after each URL.
    /// Individual failures are logged but do not abort the batch.
    /// </summary>
    /// <param name="urls">The list of URLs to import.</param>
    /// <param name="collectionId">Optional collection to associate all imported documents with.</param>
    /// <param name="progress">Optional progress reporter (number of URLs completed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of successfully created <see cref="DocumentEntity"/> records.</returns>
    Task<IReadOnlyList<DocumentEntity>> ImportFromUrlsAsync(
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
///   <item>Associate the document with a collection if specified.</item>
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
        await _db.SaveChangesAsync(ct);

        _log.Information(
            "Imported web document: {FileName} (ID {DocumentId}, {WordCount} words) from {Url}",
            entity.FileName, entity.Id, entity.WordCount, url);

        // Step 6: Associate with collection if specified
        if (collectionId.HasValue)
        {
            await AssociateWithCollectionAsync(entity.Id, collectionId.Value, ct);
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> ImportFromUrlsAsync(
        IReadOnlyList<string> urls,
        long? collectionId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (urls is null || urls.Count == 0)
        {
            return Array.Empty<DocumentEntity>();
        }

        _log.Information("Starting batch web import of {Count} URLs", urls.Count);

        var results = new List<DocumentEntity>(urls.Count);
        var completed = 0;

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var entity = await ImportFromUrlAsync(url, collectionId, ct);
                results.Add(entity);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log and continue with remaining URLs rather than aborting the batch
                _log.Warning(ex, "Failed to import URL: {Url}", url);
            }

            completed++;
            progress?.Report(completed);
        }

        _log.Information(
            "Batch web import completed: {Imported}/{Total} URLs imported",
            results.Count, urls.Count);

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
    /// Associates a document with a collection if the collection exists.
    /// Logs a warning if the collection is not found but does not throw.
    /// </summary>
    private async Task AssociateWithCollectionAsync(
        long documentId,
        long collectionId,
        CancellationToken ct)
    {
        try
        {
            var collectionExists = await _db.Collections
                .AnyAsync(c => c.Id == collectionId, ct);

            if (!collectionExists)
            {
                _log.Warning(
                    "Collection {CollectionId} not found; skipping collection association for document {DocumentId}",
                    collectionId, documentId);
                return;
            }

            // Check for duplicate association
            var alreadyAssociated = await _db.DocumentCollections
                .AnyAsync(dc => dc.DocumentId == documentId && dc.CollectionId == collectionId, ct);

            if (alreadyAssociated)
            {
                _log.Debug(
                    "Document {DocumentId} is already in collection {CollectionId}, skipping",
                    documentId, collectionId);
                return;
            }

            var docCollection = new DocumentCollectionEntity
            {
                DocumentId = documentId,
                CollectionId = collectionId,
                AddedAt = DateTime.UtcNow,
            };

            _db.DocumentCollections.Add(docCollection);

            // Update the denormalized document count on the collection
            var collection = await _db.Collections.FindAsync(new object[] { collectionId }, ct);
            if (collection is not null)
            {
                collection.DocumentCount += 1;
                collection.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            _log.Debug(
                "Associated web document {DocumentId} with collection {CollectionId}",
                documentId, collectionId);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to associate document {DocumentId} with collection {CollectionId}",
                documentId, collectionId);
        }
    }
}

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export;

/// <summary>
/// Static helper that builds export content for search results and document
/// collections. Extracted from <see cref="ExportService"/> to keep the service
/// a thin orchestrator while moving format-specific rendering here.
/// </summary>
internal static class ExportContentBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ════════════════════════════════════════════════════════════════
    //  Search results — format dispatcher
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dispatches search-result rendering to the correct format builder.
    /// Returns <c>null</c> when the format is not supported for search results.
    /// </summary>
    internal static string? BuildSearchResultsContent(
        string query,
        IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options,
        string title)
    {
        return options.Format switch
        {
            ExportFormat.Markdown => BuildSearchResultsMarkdown(query, results, options, title),
            ExportFormat.Json => BuildSearchResultsJson(query, results, title),
            ExportFormat.PlainText => BuildSearchResultsPlainText(query, results, options, title),
            ExportFormat.Csv => BuildSearchResultsCsv(query, results),
            _ => null
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  Search results — format builders
    // ════════════════════════════════════════════════════════════════

    internal static string BuildSearchResultsMarkdown(
        string query, IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options, string title)
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
                    sb.AppendLine($"- {citation}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        return sb.ToString();
    }

    internal static string BuildSearchResultsJson(
        string query, IReadOnlyList<SearchResultExportItem> results, string title)
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

    internal static string BuildSearchResultsPlainText(
        string query, IReadOnlyList<SearchResultExportItem> results,
        ExportOptions options, string title)
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
                sb.AppendLine($"Relevance: {result.RelevanceScore:P1}");

            sb.AppendLine();
            sb.AppendLine(result.Content);
            sb.AppendLine();

            if (options.IncludeCitations && result.Citations.Count > 0)
            {
                sb.AppendLine("Sources:");
                foreach (var citation in result.Citations)
                    sb.AppendLine($"  - {citation}");
                sb.AppendLine();
            }
        }

        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"Exported from Agent-X on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    internal static string BuildSearchResultsCsv(
        string query, IReadOnlyList<SearchResultExportItem> results)
    {
        var sb = new StringBuilder();
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

    // ════════════════════════════════════════════════════════════════
    //  Collection export — ZIP generation
    // ════════════════════════════════════════════════════════════════

    internal static async Task WriteCollectionZipAsync(
        CollectionEntity collection,
        IReadOnlyList<DocumentEntity> documents,
        string outputPath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

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

        ct.ThrowIfCancellationRequested();

        using var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);
        ct.ThrowIfCancellationRequested();

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            await writer.WriteAsync(manifestJson.AsMemory(), ct);

        ct.ThrowIfCancellationRequested();

        var readmeContent = BuildCollectionReadme(collection, documents);
        var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
            await writer.WriteAsync(readmeContent.AsMemory(), ct);
    }

    // ════════════════════════════════════════════════════════════════
    //  Collection export — README and CSV builders
    // ════════════════════════════════════════════════════════════════

    internal static string BuildCollectionReadme(
        CollectionEntity collection, IReadOnlyList<DocumentEntity> documents)
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

    internal static string BuildCollectionCsv(
        CollectionEntity collection, IReadOnlyList<DocumentEntity> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FileName,FilePath,FileType,FileSize,ImportedAt,IndexingStatus,PageCount,WordCount");

        foreach (var doc in documents)
        {
            sb.Append(CsvEscape(doc.FileName)).Append(',');
            sb.Append(CsvEscape(doc.FilePath)).Append(',');
            sb.Append(CsvEscape(doc.FileType)).Append(',');
            sb.Append(CsvEscape(doc.FileSizeBytes.ToString())).Append(',');
            sb.Append(CsvEscape(doc.ImportedAt.ToString("O"))).Append(',');
            sb.Append(CsvEscape(doc.IndexingStatus)).Append(',');
            sb.Append(CsvEscape(doc.PageCount.ToString())).Append(',');
            sb.AppendLine(CsvEscape(doc.WordCount.ToString()));
        }

        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    //  Shared utility methods
    // ════════════════════════════════════════════════════════════════

    internal static string FormatFileSize(long bytes)
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

    internal static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("[", "\\[").Replace("]", "\\]");
    }
}

using System.Text;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Extracts text content from Word documents (.docx, .doc) using DocumentFormat.OpenXml 3.2.0.
/// <para>
/// Walks all <see cref="Paragraph"/> elements in the document body, extracting InnerText.
/// Detects heading styles (Heading1, Heading2, etc.) to identify section structure.
/// Note: The .doc (legacy binary) format is listed for convenience but is not natively
/// supported by the OpenXml SDK — only .docx (Open XML) files can be fully parsed.
/// </para>
/// </summary>
public class DocxProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<DocxProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".doc" };

    /// <summary>
    /// Heading style IDs recognized when walking paragraphs to detect section boundaries.
    /// Covers both "HeadingN" and "heading N" conventions used by different Word templates.
    /// </summary>
    private static readonly HashSet<string> HeadingStylePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6",
        "heading 1", "heading 2", "heading 3", "heading 4", "heading 5", "heading 6",
        "Title",
    };

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public bool CanProcess(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && Extensions.Contains(ext);
    }

    /// <inheritdoc />
    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        Log.Debug("Processing Word document: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Word document not found.", filePath);

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            FileSizeBytes = fileInfo.Length,
        };

        try
        {
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            // OpenXml SDK requires synchronous stream access; offload to thread pool
            var (text, title, metadata) = await Task.Run(() => ExtractDocxContent(filePath), ct);

            document.ContentHash = await hashTask;
            document.ExtractedText = text;
            document.ExtractedTitle = title;
            document.WordCount = CountWords(text);
            document.PageCount = EstimatePageCount(document.WordCount);
            document.Metadata = metadata;

            Log.Information(
                "Successfully processed Word document: {FileName} (~{PageCount} pages, {WordCount} words)",
                document.FileName, document.PageCount, document.WordCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process Word document: {FilePath}", filePath);
            document.ExtractedText = string.Empty;
            document.Metadata.Custom["error"] = ex.Message;
        }

        return document;
    }

    /// <summary>
    /// Opens the .docx file and extracts body text, heading structure, and package metadata.
    /// </summary>
    private static (string Text, string? Title, DocumentMetadata Metadata) ExtractDocxContent(string filePath)
    {
        using var wordDoc = WordprocessingDocument.Open(filePath, false);
        var metadata = new DocumentMetadata();

        // Extract package-level metadata
        ExtractPackageMetadata(wordDoc, metadata);

        var body = wordDoc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            Log.Warning("Word document has no body content: {FilePath}", filePath);
            return (string.Empty, null, metadata);
        }

        var fullText = new StringBuilder();
        string? documentTitle = null;
        string? currentSection = null;

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var innerText = paragraph.InnerText;
            if (string.IsNullOrWhiteSpace(innerText))
            {
                // Preserve paragraph breaks for readability
                if (fullText.Length > 0 && fullText[fullText.Length - 1] != '\n')
                    fullText.AppendLine();
                continue;
            }

            // Check if this paragraph is a heading
            var styleId = GetParagraphStyleId(paragraph);
            var isHeading = IsHeadingStyle(styleId);

            if (isHeading)
            {
                currentSection = innerText.Trim();

                // The first heading (especially Title or Heading1) becomes the document title
                if (documentTitle is null && IsTitleOrH1(styleId))
                {
                    documentTitle = currentSection;
                }

                // Add heading with visual separation
                if (fullText.Length > 0)
                    fullText.AppendLine();

                fullText.AppendLine(innerText.Trim());
            }
            else
            {
                fullText.AppendLine(innerText.Trim());
            }
        }

        // If no heading-based title was found, try the package Title property
        if (documentTitle is null && metadata.Custom.TryGetValue("Title", out var packageTitle)
            && !string.IsNullOrWhiteSpace(packageTitle))
        {
            documentTitle = packageTitle;
        }

        return (fullText.ToString().Trim(), documentTitle, metadata);
    }

    /// <summary>
    /// Extracts metadata from the Open XML package properties (core, extended).
    /// </summary>
    private static void ExtractPackageMetadata(WordprocessingDocument wordDoc, DocumentMetadata metadata)
    {
        try
        {
            var props = wordDoc.PackageProperties;
            if (props is null) return;

            if (!string.IsNullOrWhiteSpace(props.Creator))
            {
                metadata.Author = props.Creator;
                metadata.Custom["Author"] = props.Creator;
            }

            if (!string.IsNullOrWhiteSpace(props.Subject))
            {
                metadata.Subject = props.Subject;
                metadata.Custom["Subject"] = props.Subject;
            }

            if (!string.IsNullOrWhiteSpace(props.Title))
            {
                metadata.Custom["Title"] = props.Title;
            }

            if (!string.IsNullOrWhiteSpace(props.Description))
            {
                metadata.Custom["Description"] = props.Description;
            }

            if (!string.IsNullOrWhiteSpace(props.Category))
            {
                metadata.Custom["Category"] = props.Category;
            }

            if (!string.IsNullOrWhiteSpace(props.Keywords))
            {
                metadata.Custom["Keywords"] = props.Keywords;
            }

            if (!string.IsNullOrWhiteSpace(props.LastModifiedBy))
            {
                metadata.Custom["LastModifiedBy"] = props.LastModifiedBy;
            }

            if (props.Created.HasValue)
            {
                metadata.CreatedDate = props.Created.Value;
            }

            if (props.Modified.HasValue)
            {
                metadata.ModifiedDate = props.Modified.Value;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract Word document package metadata");
        }
    }

    /// <summary>
    /// Retrieves the style ID from a paragraph's properties, if present.
    /// </summary>
    private static string? GetParagraphStyleId(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    }

    /// <summary>
    /// Determines whether a style ID represents any heading level or the Title style.
    /// </summary>
    private static bool IsHeadingStyle(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;

        // Check exact matches first
        if (HeadingStylePrefixes.Contains(styleId))
            return true;

        // Also match patterns like "Heading1", "heading 1", etc. that may vary by locale
        return styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
               || styleId.StartsWith("heading", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the style ID is a Title or Heading1 (top-level heading).
    /// </summary>
    private static bool IsTitleOrH1(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;

        return styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)
               || styleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase)
               || styleId.Equals("heading 1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Estimates page count based on word count, using the standard approximation
    /// of ~250 words per page for typical document formatting.
    /// </summary>
    private static int EstimatePageCount(long wordCount)
    {
        if (wordCount <= 0)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(wordCount / 250.0));
    }

    /// <summary>
    /// Counts words by splitting on whitespace, filtering out empty entries.
    /// </summary>
    private static long CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).LongLength;
    }
}

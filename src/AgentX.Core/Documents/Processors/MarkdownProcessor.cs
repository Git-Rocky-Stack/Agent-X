using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using Markdig;
using Markdig.Syntax;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Processes Markdown files (.md, .mdx, .markdown) using Markdig 0.37.0.
/// <para>
/// Uses Markdig's <see cref="Markdown.ToPlainText"/> to strip all formatting and produce
/// clean plain text for indexing. Parses the document AST via <see cref="Markdown.Parse"/>
/// to extract <see cref="HeadingBlock"/> nodes for section structure. The first H1 heading
/// (if found) is used as the document title.
/// </para>
/// </summary>
public class MarkdownProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MarkdownProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".mdx", ".markdown"
    };

    /// <summary>
    /// Markdig pipeline configuration used for parsing. Includes common extensions for
    /// maximum compatibility with real-world Markdown files.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

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
        Log.Debug("Processing Markdown file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Markdown file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = ext.TrimStart('.'),
            FileSizeBytes = fileInfo.Length,
            PageCount = 1,
        };

        try
        {
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            var rawMarkdown = await File.ReadAllTextAsync(filePath, ct);

            // Strip frontmatter (YAML between --- delimiters) if present, to avoid
            // polluting the extracted text with metadata keys
            var markdownContent = StripFrontmatter(rawMarkdown);

            // Convert Markdown to plain text (strips all formatting)
            var plainText = Markdown.ToPlainText(markdownContent, Pipeline);

            // Parse the AST to extract heading structure
            var ast = Markdown.Parse(markdownContent, Pipeline);
            var headings = ExtractHeadings(ast);
            var title = FindFirstH1(headings);

            document.ContentHash = await hashTask;
            document.ExtractedText = plainText.Trim();
            document.ExtractedTitle = title;
            document.WordCount = CountWords(plainText);

            // Store heading structure in metadata for downstream chunking/navigation
            if (headings.Count > 0)
            {
                document.Metadata.Custom["headingCount"] = headings.Count.ToString();
                document.Metadata.Custom["headings"] = string.Join(" | ", headings.Select(h => h.Title));
            }

            // File timestamps
            document.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            Log.Information(
                "Successfully processed Markdown: {FileName} ({WordCount} words, {HeadingCount} headings)",
                document.FileName, document.WordCount, headings.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process Markdown file: {FilePath}", filePath);
            document.ExtractedText = string.Empty;
            document.Metadata.Custom["error"] = ex.Message;
        }

        return document;
    }

    /// <summary>
    /// Strips YAML frontmatter (content between leading "---" markers) from the Markdown string.
    /// This is common in static site generators (Jekyll, Hugo, Astro, etc.).
    /// </summary>
    private static string StripFrontmatter(string markdown)
    {
        if (!markdown.StartsWith("---"))
            return markdown;

        // Find the closing "---" delimiter (must be on its own line)
        var closingIndex = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (closingIndex < 0)
            return markdown;

        // Skip past the closing delimiter and its newline
        var contentStart = closingIndex + 4; // "\n---".Length
        if (contentStart < markdown.Length && markdown[contentStart] == '\n')
            contentStart++;
        if (contentStart < markdown.Length && markdown[contentStart] == '\r')
            contentStart++;

        return contentStart < markdown.Length
            ? markdown[contentStart..]
            : string.Empty;
    }

    /// <summary>
    /// Walks the Markdig AST and extracts all <see cref="HeadingBlock"/> nodes,
    /// returning their level and text content.
    /// </summary>
    private static List<(int Level, string Title)> ExtractHeadings(MarkdownDocument ast)
    {
        var headings = new List<(int Level, string Title)>();

        foreach (var block in ast.Descendants<HeadingBlock>())
        {
            // Extract the inline text content of the heading
            var headingText = block.Inline is not null
                ? string.Join("", block.Inline.Select(inline => inline.ToString()))
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(headingText))
            {
                headings.Add((block.Level, headingText.Trim()));
            }
        }

        return headings;
    }

    /// <summary>
    /// Returns the text of the first H1 heading found, or null if no H1 exists.
    /// </summary>
    private static string? FindFirstH1(List<(int Level, string Title)> headings)
    {
        var h1 = headings.FirstOrDefault(h => h.Level == 1);
        return h1 == default ? null : h1.Title;
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

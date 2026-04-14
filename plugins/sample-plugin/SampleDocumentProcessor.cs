using Serilog;

namespace AgentX.Plugins.Sample;

/// <summary>
/// Result of processing a document through the sample plugin.
/// Contains the file content, computed statistics, and optional frontmatter metadata.
/// </summary>
/// <param name="FilePath">Absolute path to the source file that was processed.</param>
/// <param name="Content">Full text content of the file (with frontmatter stripped for Markdown files).</param>
/// <param name="WordCount">Number of words in the content, split on whitespace boundaries.</param>
/// <param name="LineCount">Number of lines in the content (newline-delimited).</param>
/// <param name="CharacterCount">Total number of characters in the content, including whitespace.</param>
/// <param name="Frontmatter">Key-value pairs extracted from YAML frontmatter delimiters, if present. Empty for non-Markdown files or files without frontmatter.</param>
/// <param name="ProcessedAt">UTC timestamp when the processing completed.</param>
public sealed record ProcessedDocument(
    string FilePath,
    string Content,
    int WordCount,
    int LineCount,
    int CharacterCount,
    Dictionary<string, string> Frontmatter,
    DateTime ProcessedAt);

/// <summary>
/// Document processor that handles plain-text (.txt) and Markdown (.md) files.
/// Reads file content, computes word/line/character counts, and extracts YAML
/// frontmatter from Markdown files bounded by <c>---</c> delimiters.
/// </summary>
/// <remarks>
/// This processor is designed for demonstration purposes. It shows how a
/// <see cref="PluginType.DocumentProcessor"/> plugin can extend AgentX with
/// custom file-format handling. The processing pipeline is deliberately
/// simple to keep the sample focused on plugin lifecycle and API usage.
/// </remarks>
public sealed class SampleDocumentProcessor
{
    private readonly Serilog.ILogger _logger;

    /// <summary>
    /// File extensions this processor can handle, with the leading dot.
    /// </summary>
    private static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt",
            ".md"
        };

    /// <summary>
    /// Initializes a new instance of <see cref="SampleDocumentProcessor"/>.
    /// </summary>
    /// <param name="logger">
    /// A Serilog logger, typically the one provided by <see cref="IPluginContext.Logger"/>,
    /// pre-enriched with plugin metadata.
    /// </param>
    public SampleDocumentProcessor(Serilog.ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the file at <paramref name="filePath"/> and returns a
    /// <see cref="ProcessedDocument"/> containing the content, statistics,
    /// and any extracted frontmatter.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to process.</param>
    /// <returns>A <see cref="ProcessedDocument"/> with computed metrics and metadata.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="filePath"/> is null, empty, or has an unsupported extension.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the specified file does not exist on disk.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the file cannot be read due to an I/O error.
    /// </exception>
    public async Task<ProcessedDocument> ProcessDocumentAsync(string filePath)
    {
        ValidateFilePath(filePath);

        var extension = Path.GetExtension(filePath);
        _logger.Information(
            "Processing document: {FilePath} (extension: {Extension})",
            filePath, extension);

        var rawContent = await ReadFileContentAsync(filePath).ConfigureAwait(false);
        _logger.Debug("Read {ByteCount} characters from {FilePath}", rawContent.Length, filePath);

        var frontmatter = new Dictionary<string, string>();
        var content = rawContent;

        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            (frontmatter, content) = ExtractFrontmatter(rawContent);
            if (frontmatter.Count > 0)
            {
                _logger.Information(
                    "Extracted {FrontmatterCount} frontmatter keys from {FilePath}",
                    frontmatter.Count, filePath);
            }
            else
            {
                _logger.Debug("No frontmatter found in {FilePath}", filePath);
            }
        }

        var wordCount = CountWords(content);
        var lineCount = CountLines(content);
        var characterCount = content.Length;

        _logger.Information(
            "Document processed: {FilePath} — {WordCount} words, {LineCount} lines, {CharacterCount} characters",
            filePath, wordCount, lineCount, characterCount);

        return new ProcessedDocument(
            FilePath: filePath,
            Content: content,
            WordCount: wordCount,
            LineCount: lineCount,
            CharacterCount: characterCount,
            Frontmatter: frontmatter,
            ProcessedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// Validates that the file path is usable and references a supported format.
    /// </summary>
    private void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be null or whitespace.", nameof(filePath));
        }

        var extension = Path.GetExtension(filePath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new ArgumentException(
                $"Unsupported file extension '{extension}'. This processor supports: {string.Join(", ", SupportedExtensions)}.",
                nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"The file '{filePath}' was not found.", filePath);
        }
    }

    /// <summary>
    /// Reads all text from the specified file asynchronously.
    /// </summary>
    private static async Task<string> ReadFileContentAsync(string filePath)
    {
        return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts YAML-style frontmatter from Markdown content.
    /// Frontmatter is delimited by <c>---</c> lines at the very start of the file.
    /// Only the first frontmatter block is processed. Key-value pairs are parsed
    /// from simple <c>key: value</c> lines (no nested structures or lists).
    /// </summary>
    /// <param name="rawContent">The full raw Markdown content.</param>
    /// <returns>
    /// A tuple containing the extracted frontmatter dictionary and the
    /// content with the frontmatter block stripped.
    /// </returns>
    private (Dictionary<string, string> Frontmatter, string Content) ExtractFrontmatter(string rawContent)
    {
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!rawContent.StartsWith("---", StringComparison.Ordinal))
        {
            return (frontmatter, rawContent);
        }

        // Find the closing delimiter. Start searching after the opening "---\n".
        var closingIndex = rawContent.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            _logger.Debug("Opening frontmatter delimiter found but no closing delimiter — treating as regular content.");
            return (frontmatter, rawContent);
        }

        var frontmatterBlock = rawContent.Substring(3, closingIndex - 3).Trim();
        var contentStart = closingIndex + 4; // skip past "\n---"
        var content = rawContent[contentStart..].TrimStart('\r', '\n');

        foreach (var line in frontmatterBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0)
            {
                continue;
            }

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (!string.IsNullOrEmpty(key))
            {
                frontmatter[key] = value;
            }
        }

        return (frontmatter, content);
    }

    /// <summary>
    /// Counts the number of words in the content, splitting on whitespace.
    /// Empty or whitespace-only content returns zero.
    /// </summary>
    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var count = 0;
        var inWord = false;

        foreach (var c in content)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts the number of lines in the content.
    /// A single line with no trailing newline returns 1. Empty content returns 0.
    /// </summary>
    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var count = 1;
        foreach (var c in content)
        {
            if (c == '\n')
            {
                count++;
            }
        }

        // If the content ends with a newline, the final "\n" does not constitute an additional line.
        if (content.EndsWith('\n'))
        {
            count--;
        }

        return count;
    }
}
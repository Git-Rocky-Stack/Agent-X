using System.Text;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Processes plain text and structured text files (.txt, .csv, .log, .json, .xml, .yaml, etc.).
/// <para>
/// Reads file content as-is without format-specific parsing. Structured formats like
/// JSON, XML, YAML, and CSV are treated as raw text — no schema-aware parsing is performed.
/// Encoding detection tries UTF-8 first, then falls back to the system default encoding.
/// </para>
/// </summary>
public class TextProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<TextProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".log", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg"
    };

    /// <summary>
    /// Maps file extensions to human-readable file type identifiers for the <see cref="ProcessedDocument.FileType"/> field.
    /// </summary>
    private static readonly Dictionary<string, string> FileTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "txt",
        [".csv"] = "csv",
        [".log"] = "log",
        [".json"] = "json",
        [".xml"] = "xml",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".toml"] = "toml",
        [".ini"] = "ini",
        [".cfg"] = "cfg",
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
        Log.Debug("Processing text file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Text file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = FileTypeNames.GetValueOrDefault(ext, ext.TrimStart('.')),
            FileSizeBytes = fileInfo.Length,
            PageCount = 1,
        };

        try
        {
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            // Detect encoding and read file content
            var (text, encodingName) = await ReadFileWithEncodingDetectionAsync(filePath, ct);

            document.ContentHash = await hashTask;
            document.ExtractedText = text;
            document.WordCount = CountWords(text);
            document.Metadata.Custom["encoding"] = encodingName;
            document.Metadata.Custom["lineCount"] = CountLines(text).ToString();

            // Use the file name (without extension) as a rudimentary title
            document.ExtractedTitle = Path.GetFileNameWithoutExtension(filePath);

            // Set file timestamps as metadata
            document.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            Log.Information(
                "Successfully processed text file: {FileName} ({WordCount} words, {Encoding})",
                document.FileName, document.WordCount, encodingName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process text file: {FilePath}", filePath);
            document.ExtractedText = string.Empty;
            document.Metadata.Custom["error"] = ex.Message;
        }

        return document;
    }

    /// <summary>
    /// Reads file content with encoding detection. Tries UTF-8 first (with BOM detection),
    /// and falls back to the system default encoding if the file contains invalid UTF-8 sequences.
    /// </summary>
    private static async Task<(string Text, string EncodingName)> ReadFileWithEncodingDetectionAsync(
        string filePath, CancellationToken ct)
    {
        // First attempt: UTF-8 with BOM detection
        // StreamReader with detectEncodingFromByteOrderMarks will check for UTF-8/16/32 BOMs
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);

            // Check for BOM markers
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 (BOM)");
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
            }

            // No BOM — try UTF-8 first with strict validation
            try
            {
                var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                var text = strictUtf8.GetString(bytes);
                return (text, "UTF-8");
            }
            catch (DecoderFallbackException)
            {
                // Not valid UTF-8; fall back to system default
                var fallbackEncoding = Encoding.Default;
                var text = fallbackEncoding.GetString(bytes);
                return (text, fallbackEncoding.EncodingName);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Encoding detection failed, using UTF-8 fallback for: {FilePath}", filePath);
            var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
            return (text, "UTF-8 (fallback)");
        }
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

    /// <summary>
    /// Counts the number of lines in the text.
    /// </summary>
    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var lineCount = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lineCount++;
        }

        return lineCount;
    }
}

using System.Text.RegularExpressions;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Processes source code files across a wide range of programming languages.
/// <para>
/// Detects the programming language from the file extension, counts lines of code,
/// and attempts to extract a "title" from the first class, function, module, or
/// namespace declaration found in the file using regular expressions.
/// </para>
/// </summary>
public class CodeFileProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<CodeFileProcessor>();

    /// <summary>
    /// Uses the canonical set from <see cref="SupportedFileTypes.Code"/> to ensure
    /// consistency with the rest of the application.
    /// </summary>
    private static readonly HashSet<string> Extensions =
        new(SupportedFileTypes.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps file extensions to their human-readable programming language names.
    /// </summary>
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".js"] = "JavaScript",
        [".ts"] = "TypeScript",
        [".py"] = "Python",
        [".java"] = "Java",
        [".cpp"] = "C++",
        [".c"] = "C",
        [".h"] = "C/C++ Header",
        [".go"] = "Go",
        [".rs"] = "Rust",
        [".swift"] = "Swift",
        [".kt"] = "Kotlin",
        [".rb"] = "Ruby",
        [".php"] = "PHP",
        [".html"] = "HTML",
        [".css"] = "CSS",
        [".scss"] = "SCSS",
        [".sql"] = "SQL",
        [".sh"] = "Shell",
        [".yaml"] = "YAML",
        [".yml"] = "YAML",
        [".toml"] = "TOML",
        [".ini"] = "INI",
        [".cfg"] = "Configuration",
        [".xaml"] = "XAML",
    };

    /// <summary>
    /// Regular expressions used to detect the first class, function, module, or namespace
    /// declaration in source code. Ordered by specificity — the first match wins.
    /// Each pattern captures a named group "name" containing the identifier.
    /// </summary>
    private static readonly List<(string Language, Regex Pattern)> DeclarationPatterns = new()
    {
        // C#: class, interface, struct, record, enum, namespace
        ("C#",       new Regex(@"(?:public|internal|private|protected)?\s*(?:static\s+)?(?:partial\s+)?(?:class|interface|struct|record|enum)\s+(?<name>\w+)", RegexOptions.Compiled)),
        ("C#",       new Regex(@"namespace\s+(?<name>[\w.]+)", RegexOptions.Compiled)),

        // Java/Kotlin: class, interface
        ("Java",     new Regex(@"(?:public|private|protected)?\s*(?:abstract\s+)?(?:class|interface)\s+(?<name>\w+)", RegexOptions.Compiled)),
        ("Kotlin",   new Regex(@"(?:class|object|interface)\s+(?<name>\w+)", RegexOptions.Compiled)),

        // Python: class, def
        ("Python",   new Regex(@"^class\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.Multiline)),
        ("Python",   new Regex(@"^def\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.Multiline)),

        // JavaScript/TypeScript: class, function, export default
        ("JavaScript", new Regex(@"(?:export\s+)?(?:default\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
        ("TypeScript", new Regex(@"(?:export\s+)?(?:default\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
        ("JavaScript", new Regex(@"(?:export\s+)?(?:async\s+)?function\s+(?<name>\w+)", RegexOptions.Compiled)),
        ("TypeScript", new Regex(@"(?:export\s+)?(?:async\s+)?function\s+(?<name>\w+)", RegexOptions.Compiled)),

        // Go: package, func, type struct
        ("Go",       new Regex(@"^package\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.Multiline)),
        ("Go",       new Regex(@"^func\s+(?:\(\w+\s+\*?\w+\)\s+)?(?<name>\w+)", RegexOptions.Compiled | RegexOptions.Multiline)),
        ("Go",       new Regex(@"^type\s+(?<name>\w+)\s+struct", RegexOptions.Compiled | RegexOptions.Multiline)),

        // Rust: fn, struct, mod, impl
        ("Rust",     new Regex(@"(?:pub\s+)?(?:mod|struct|enum|trait)\s+(?<name>\w+)", RegexOptions.Compiled)),
        ("Rust",     new Regex(@"(?:pub\s+)?(?:async\s+)?fn\s+(?<name>\w+)", RegexOptions.Compiled)),

        // Swift: class, struct, protocol, func
        ("Swift",    new Regex(@"(?:public\s+|private\s+|internal\s+)?(?:class|struct|protocol|enum)\s+(?<name>\w+)", RegexOptions.Compiled)),

        // Ruby: class, module, def
        ("Ruby",     new Regex(@"^(?:class|module)\s+(?<name>[\w:]+)", RegexOptions.Compiled | RegexOptions.Multiline)),

        // PHP: class, function
        ("PHP",      new Regex(@"(?:class|interface|trait)\s+(?<name>\w+)", RegexOptions.Compiled)),

        // C/C++: class, struct, namespace
        ("C++",      new Regex(@"(?:class|struct|namespace)\s+(?<name>\w+)", RegexOptions.Compiled)),

        // HTML: <title>
        ("HTML",     new Regex(@"<title>(?<name>[^<]+)</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase)),

        // SQL: CREATE TABLE, CREATE PROCEDURE
        ("SQL",      new Regex(@"CREATE\s+(?:TABLE|PROCEDURE|FUNCTION|VIEW)\s+(?:\[?\w+\]?\.)?(?<name>\[?\w+\]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),

        // Shell: first function or script name in shebang comment
        ("Shell",    new Regex(@"^(?:#!.+\n)?#\s*(?<name>.+)", RegexOptions.Compiled | RegexOptions.Multiline)),

        // XAML: x:Class
        ("XAML",     new Regex(@"x:Class=""(?<name>[^""]+)""", RegexOptions.Compiled)),
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
        Log.Debug("Processing code file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Code file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var language = LanguageMap.GetValueOrDefault(ext, ext.TrimStart('.').ToUpperInvariant());

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = ext.TrimStart('.'),
            FileSizeBytes = fileInfo.Length,
            PageCount = 1,
            Language = language,
        };

        try
        {
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            var text = await File.ReadAllTextAsync(filePath, ct);

            document.ContentHash = await hashTask;
            document.ExtractedText = text;

            // Count lines (total and non-empty)
            var (totalLines, nonEmptyLines) = CountLines(text);
            document.WordCount = CountWordsFromNonEmptyLines(text);
            document.Metadata.Custom["language"] = language;
            document.Metadata.Custom["lines"] = totalLines.ToString();
            document.Metadata.Custom["nonEmptyLines"] = nonEmptyLines.ToString();

            // Attempt to extract a title from the first declaration
            document.ExtractedTitle = TryExtractTitle(text, language);

            // File timestamps
            document.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            Log.Information(
                "Successfully processed code file: {FileName} ({Language}, {Lines} lines, {WordCount} words)",
                document.FileName, language, totalLines, document.WordCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process code file: {FilePath}", filePath);
            document.ExtractedText = string.Empty;
            document.Metadata.Custom["error"] = ex.Message;
        }

        return document;
    }

    /// <summary>
    /// Attempts to extract a meaningful "title" from the source code by matching
    /// the first class, function, module, or namespace declaration using language-specific
    /// regular expressions.
    /// </summary>
    private static string? TryExtractTitle(string text, string language)
    {
        try
        {
            // Try language-specific patterns first
            foreach (var (patternLanguage, pattern) in DeclarationPatterns)
            {
                if (!patternLanguage.Equals(language, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = pattern.Match(text);
                if (match.Success && match.Groups["name"].Success)
                {
                    var name = match.Groups["name"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }

            // Fallback: try all patterns regardless of language
            foreach (var (_, pattern) in DeclarationPatterns)
            {
                var match = pattern.Match(text);
                if (match.Success && match.Groups["name"].Success)
                {
                    var name = match.Groups["name"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract title from code file");
        }

        return null;
    }

    /// <summary>
    /// Counts total lines and non-empty lines in the text.
    /// </summary>
    private static (int TotalLines, int NonEmptyLines) CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);

        var totalLines = 1;
        var nonEmptyLines = 0;
        var lineHasContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\n')
            {
                totalLines++;
                if (lineHasContent)
                    nonEmptyLines++;
                lineHasContent = false;
            }
            else if (!char.IsWhiteSpace(c))
            {
                lineHasContent = true;
            }
        }

        // Account for the last line if it has content
        if (lineHasContent)
            nonEmptyLines++;

        return (totalLines, nonEmptyLines);
    }

    /// <summary>
    /// Counts words from non-empty lines by splitting each line on whitespace.
    /// This avoids inflating word count with blank lines.
    /// </summary>
    private static long CountWordsFromNonEmptyLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        long wordCount = 0;
        using var reader = new StringReader(text);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            wordCount += line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).Length;
        }

        return wordCount;
    }
}

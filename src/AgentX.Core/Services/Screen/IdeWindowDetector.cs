using System;

namespace AgentX.Core.Services.Screen;

/// <summary>
/// Pure static utility that detects IDE context from a window title string.
/// Parses structured information (IDE name, active file, project, language)
/// from the title bars of common development environments.
/// No external dependencies — entirely string-based detection.
/// </summary>
public static class IdeWindowDetector
{
    /// <summary>
    /// IDE identifier suffixes found at the end of window titles.
    /// Each tuple maps a title suffix to the canonical IDE name.
    /// Order matters: longer/more-specific suffixes should come before shorter ones
    /// to avoid premature partial matches (e.g., "Microsoft Visual Studio" before "Visual Studio Code").
    /// </summary>
    private static readonly (string Suffix, string IdeName)[] IdeSuffixes =
    [
        // Longer/more-specific suffixes must come before shorter ones
        // to avoid premature partial matches (e.g., "Microsoft Visual Studio 2022"
        // before "Microsoft Visual Studio").
        ("- Visual Studio Code", "VS Code"),
        ("- Microsoft Visual Studio 2022", "Visual Studio"),
        ("- Microsoft Visual Studio 2019", "Visual Studio"),
        ("- Microsoft Visual Studio 2017", "Visual Studio"),
        ("- Microsoft Visual Studio", "Visual Studio"),
        ("\u2013 JetBrains Rider", "JetBrains Rider"),         // en-dash (U+2013)
        ("\u2013 IntelliJ IDEA", "IntelliJ IDEA"),             // en-dash (U+2013)
        ("- Cursor", "Cursor"),
        ("\u2014 Zed", "Zed"),                                  // em-dash (U+2014)
    ];

    /// <summary>
    /// Maps file extensions (with leading dot, lowercase) to their
    /// canonical programming language names.
    /// Covers common web, systems, scripting, and configuration languages.
    /// </summary>
    private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET languages
        { ".cs", "C#" },
        { ".fs", "F#" },
        { ".vb", "Visual Basic" },
        { ".razor", "Blazor" },
        { ".xaml", "XAML" },

        // Web frontend
        { ".html", "HTML" },
        { ".css", "CSS" },
        { ".scss", "CSS" },
        { ".js", "JavaScript" },
        { ".mjs", "JavaScript" },
        { ".jsx", "React JSX" },
        { ".ts", "TypeScript" },
        { ".tsx", "TypeScript" },

        // Systems languages
        { ".rs", "Rust" },
        { ".go", "Go" },
        { ".cpp", "C++" },
        { ".cc", "C++" },
        { ".cxx", "C++" },
        { ".c", "C" },
        { ".h", "C/C++ Header" },
        { ".hpp", "C/C++ Header" },

        // JVM languages
        { ".java", "Java" },
        { ".kt", "Kotlin" },
        { ".scala", "Scala" },

        // Scripting languages
        { ".py", "Python" },
        { ".rb", "Ruby" },
        { ".php", "PHP" },
        { ".swift", "Swift" },

        // Shell / scripting
        { ".sh", "Shell" },
        { ".bash", "Shell" },
        { ".ps1", "PowerShell" },

        // Data / configuration
        { ".json", "JSON" },
        { ".yaml", "YAML" },
        { ".yml", "YAML" },
        { ".xml", "XML" },
        { ".sql", "SQL" },
        { ".md", "Markdown" },

        // Container / infra
        { ".dockerfile", "Docker" },
    };

    /// <summary>
    /// Detects IDE context from the given window title.
    /// Returns <c>null</c> if the title is empty or does not match any known IDE pattern.
    /// </summary>
    /// <param name="windowTitle">The raw window title to analyze.</param>
    /// <returns>
    /// An <see cref="IdeDetection"/> with structured IDE context, or <c>null</c>
    /// if no IDE was detected.
    /// </returns>
    public static IdeDetection? Detect(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return null;

        // Find the first matching IDE suffix
        foreach (var (suffix, ideName) in IdeSuffixes)
        {
            if (windowTitle.EndsWith(suffix, StringComparison.Ordinal))
            {
                return ParseDetection(windowTitle, suffix, ideName);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a window title into an <see cref="IdeDetection"/> after the IDE suffix
    /// has been identified. Handles dash-style separators (hyphen, en-dash, em-dash)
    /// and VS Code remote title patterns.
    /// </summary>
    private static IdeDetection ParseDetection(string windowTitle, string suffix, string ideName)
    {
        // Strip the IDE suffix to get the content portion
        var content = windowTitle[..^suffix.Length].TrimEnd();

        // Split the content portion on separator patterns
        var segments = SplitOnSeparators(content);

        string activeFileName;
        string projectName;

        if (ideName == "VS Code" && segments.Length > 1)
        {
            // VS Code remote titles: "filename - folder [SSH: user@host] - Visual Studio Code"
            // The folder segment may contain the remote suffix like "[SSH: ...]"
            // We want just the folder name, not the remote suffix
            projectName = ExtractVsCodeProjectName(segments[1]);
            activeFileName = segments[0].Trim();
        }
        else if (segments.Length >= 2)
        {
            activeFileName = segments[0].Trim();
            projectName = segments[1].Trim();
        }
        else if (segments.Length == 1)
        {
            activeFileName = segments[0].Trim();
            projectName = string.Empty;
        }
        else
        {
            activeFileName = content.Trim();
            projectName = string.Empty;
        }

        var language = InferLanguage(activeFileName);

        return new IdeDetection
        {
            IdeName = ideName,
            ActiveFileName = activeFileName,
            ProjectName = projectName,
            Language = language,
            RawTitle = windowTitle,
        };
    }

    /// <summary>
    /// Splits a string on separator patterns: hyphen+space, en-dash+space, em-dash+space.
    /// This handles the varied dash conventions used by different IDEs in their title bars.
    /// </summary>
    private static string[] SplitOnSeparators(string content)
    {
        // The three separator patterns used across IDEs
        // Order by longest first to avoid partial splitting of em-dash/en-dash
        var separators = new[] { " \u2014 ", " \u2013 ", " - " };

        // Replace all separator variants with a canonical separator, then split
        var normalized = content;
        foreach (var sep in separators)
        {
            normalized = normalized.Replace(sep, "\u0000");
        }

        return normalized.Split('\u0000', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Extracts the project/workspace name from a VS Code title segment.
    /// Handles remote development patterns like "folder [SSH: user@host]"
    /// by stripping the remote suffix and returning just the folder name.
    /// </summary>
    private static string ExtractVsCodeProjectName(string projectSegment)
    {
        var name = projectSegment.Trim();

        // VS Code remote titles use brackets for the remote indicator, e.g.:
        // "my-project [SSH: user@host]" or "my-project [Dev Container: ...]"
        var bracketIndex = name.IndexOf('[');
        if (bracketIndex > 0)
        {
            name = name[..bracketIndex].TrimEnd();
        }

        return name;
    }

    /// <summary>
    /// Infers the programming language from a file name's extension.
    /// Returns the canonical language name if the extension is recognized,
    /// or <see cref="string.Empty"/> if it is not in the mapping.
    /// </summary>
    private static string InferLanguage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        // Special case: "Dockerfile" (no extension) and "Dockerfile.dev"
        // Must be checked before extension extraction, since "Dockerfile"
        // has no dot and would otherwise fall through the early return.
        if (fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase))
        {
            return "Docker";
        }

        var dotIndex = fileName.LastIndexOf('.');
        if (dotIndex < 0 || dotIndex == fileName.Length - 1)
            return string.Empty;

        var extension = fileName[dotIndex..];

        return ExtensionToLanguage.TryGetValue(extension, out var language)
            ? language
            : string.Empty;
    }
}
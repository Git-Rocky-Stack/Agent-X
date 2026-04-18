using System.Text.RegularExpressions;

namespace LocaleAudit;

public sealed record CodeKeyReference(string Key, string SourceFile, int LineNumber);

public static class CSharpGetStringExtractor
{
    // Match: (anything).GetString("<literal>") possibly followed by , ...extra args... )
    // Captures the literal key only. Non-literal args (identifiers, interpolated strings,
    // concatenations) are intentionally excluded because the audit cannot verify them.
    private static readonly Regex GetStringRegex = new(
        @"\.GetString\s*\(\s*""([^""\\]*(?:\\.[^""\\]*)*)""\s*(?:,\s*[^)]*)?\)",
        RegexOptions.Compiled);

    // Strip C# single-line comments before matching — block comments are rare enough
    // in Agent-X to defer; can be added if Spike 0 later finds a false-positive.
    private static readonly Regex SingleLineCommentRegex = new(
        @"//[^\n]*",
        RegexOptions.Compiled);

    public static IReadOnlyList<CodeKeyReference> ExtractAll(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException($"C# root not found: {rootDirectory}");

        var results = new List<CodeKeyReference>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories))
        {
            // Skip auto-generated files — they pollute results and typically don't call GetString.
            if (path.Contains("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (path.Contains("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (path.EndsWith(".g.cs", StringComparison.Ordinal)) continue;
            if (path.EndsWith(".g.i.cs", StringComparison.Ordinal)) continue;

            results.AddRange(ExtractFromFile(path));
        }
        return results;
    }

    public static IReadOnlyList<CodeKeyReference> ExtractFromFile(string path)
    {
        var raw = File.ReadAllText(path);
        var stripped = SingleLineCommentRegex.Replace(raw, string.Empty);

        var results = new List<CodeKeyReference>();
        foreach (Match m in GetStringRegex.Matches(stripped))
        {
            var key = m.Groups[1].Value;
            // Unescape any `\"` inside the key literal.
            key = key.Replace("\\\"", "\"");
            var line = stripped.Substring(0, m.Index).Count(c => c == '\n') + 1;
            results.Add(new CodeKeyReference(key, path, line));
        }
        return results;
    }
}

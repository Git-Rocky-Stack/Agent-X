using System.Text.RegularExpressions;

namespace LocaleAudit;

public sealed record CodeKeyReference(string Key, string SourceFile, int LineNumber);

public static class CSharpGetStringExtractor
{
    // Match: (anything).GetString("<literal>") possibly followed by , ...extra args... )
    // Captures the literal key only. Non-literal args (identifiers, interpolated strings,
    // concatenations) are intentionally excluded because the audit cannot verify them.
    // The extra-args group allows one level of nested parens so method-call args like
    // `GetString("Key", GetCount())` or `GetString("Key", Math.Max(a, b))` match correctly.
    private static readonly Regex GetStringRegex = new(
        @"\.GetString\s*\(\s*""([^""\\]*(?:\\.[^""\\]*)*)""\s*(?:,\s*[^()]*(?:\([^)]*\)[^()]*)*)?\)",
        RegexOptions.Compiled);

    // Strip C# single-line comments before matching — block comments are rare enough
    // in Agent-X to defer; can be added if Spike 0 later finds a false-positive.
    //
    // LIMITATION: this regex does not distinguish `//` inside a string literal (e.g.,
    // "https://..."). If a same-line pattern of `var url = "//..."; _l.GetString("Key");`
    // appears in a .cs file, the GetString call AFTER the URL will be silently dropped.
    // Agent-X has no such pattern today. If one is introduced, either:
    //   (a) split onto separate lines (preferred — improves readability anyway), or
    //   (b) upgrade this to a proper tokenizer that respects string-literal boundaries.
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
            // Skip empty keys — GetString("") has no locale value to check.
            // Matches XamlUidExtractor's exclusion of x:Uid="".
            if (string.IsNullOrEmpty(key)) continue;
            var line = stripped.Substring(0, m.Index).Count(c => c == '\n') + 1;
            results.Add(new CodeKeyReference(key, path, line));
        }
        return results;
    }
}

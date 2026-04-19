using System.Text.RegularExpressions;

namespace LocaleAudit;

public sealed record UidReference(string Uid, string SourceFile, int LineNumber);

public static class XamlUidExtractor
{
    // Captures x:Uid="SomeValue"; tolerates single or double quotes.
    // NOTE: Requires at least one character (`+`) inside the quotes — empty `x:Uid=""`
    // attributes are intentionally excluded since they have no locale value to check.
    private static readonly Regex UidRegex = new(
        @"x:Uid\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled);

    // Strip XAML comments before matching so commented-out x:Uid is not counted.
    private static readonly Regex XamlCommentRegex = new(
        @"<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static IReadOnlyList<UidReference> ExtractAll(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException($"XAML root not found: {rootDirectory}");

        var results = new List<UidReference>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            results.AddRange(ExtractFromFile(path));
        }
        return results;
    }

    public static IReadOnlyList<UidReference> ExtractFromFile(string path)
    {
        var raw = File.ReadAllText(path);
        var stripped = XamlCommentRegex.Replace(raw, string.Empty);

        var results = new List<UidReference>();
        foreach (Match m in UidRegex.Matches(stripped))
        {
            var uid = m.Groups[1].Value;
            // Approximate line number by counting newlines up to the match in the stripped text.
            var line = stripped.Substring(0, m.Index).Count(c => c == '\n') + 1;
            results.Add(new UidReference(uid, path, line));
        }
        return results;
    }
}

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the "finished but unreachable" defect class for services the container
/// resolves as a collection.
/// <para>
/// A service like <c>IDocumentProcessor</c> is never referenced by name at a call
/// site. <c>DocumentService</c> takes <c>IEnumerable&lt;IDocumentProcessor&gt;</c> and
/// picks a processor by extension, so the ONLY thing that makes an implementation real
/// is its <c>AddSingleton</c> line in composition root. An implementation that compiles,
/// has unit tests, and reports as covered is still completely absent from the product
/// when that one line is missing, and no other test in the suite notices.
/// </para>
/// <para>
/// This is not hypothetical: <c>AudioProcessor</c> and <c>WebProcessor</c> both shipped
/// fully implemented and unregistered, so importing an .mp3 or a .url file silently fell
/// through to "unsupported format" while the README advertised the capability.
/// </para>
/// </summary>
public sealed class EveryCollectionServiceIsRegisteredTests
{
    /// <summary>
    /// Interfaces the composition root registers many times and resolves as
    /// <c>IEnumerable&lt;T&gt;</c>. Every concrete implementation of these must appear
    /// in a registration line.
    /// </summary>
    public static TheoryData<string> CollectionInterfaces => new()
    {
        "IDocumentProcessor",
        "IExportFormatter",
    };

    [Theory]
    [MemberData(nameof(CollectionInterfaces))]
    public void EveryImplementation_IsRegisteredInTheCompositionRoot(string collectionInterface)
    {
        var sourceRoot = ResolveSourceRoot();
        var compositionRoot = ReadExecutableLines(
            Path.Combine(sourceRoot, "AgentX.App", "App.xaml.cs"));

        var implementations = FindImplementations(
            Path.Combine(sourceRoot, "AgentX.Core"), collectionInterface);

        implementations.Should().NotBeEmpty(
            $"the scan for {collectionInterface} implementations must find something, " +
            "otherwise this guard silently passes forever");

        var unregistered = implementations
            .Where(type => !Regex.IsMatch(compositionRoot, $@"\b{Regex.Escape(type)}\s*>\s*\("))
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();

        unregistered.Should().BeEmpty(
            $"every {collectionInterface} is resolved only as IEnumerable<{collectionInterface}>, " +
            "so an unregistered implementation is invisible to the user no matter how well it is " +
            "written or tested. Unregistered:\n  " + string.Join("\n  ", unregistered));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the file's text with single-line comments stripped.
    /// <para>
    /// Matching against the raw file would accept a commented-out registration, since the
    /// type name survives inside the comment. That is a realistic way for a registration to
    /// disappear (someone disables one while debugging), so the guard must not see it.
    /// </para>
    /// </summary>
    private static string ReadExecutableLines(string path) =>
        string.Join(
            '\n',
            File.ReadAllLines(path)
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>
    /// Returns the concrete (non-abstract) class names under <paramref name="coreRoot"/>
    /// that declare <paramref name="collectionInterface"/> in their base list.
    /// </summary>
    private static List<string> FindImplementations(string coreRoot, string collectionInterface)
    {
        var declaration = new Regex(
            @"(?<modifiers>(?:public|internal)(?:\s+(?:sealed|partial))*)\s+class\s+(?<type>\w+)\s*:\s*(?<bases>[^{]+)",
            RegexOptions.Compiled);

        var implementations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (Match match in declaration.Matches(File.ReadAllText(path)))
            {
                if (match.Groups["modifiers"].Value.Contains("abstract", StringComparison.Ordinal))
                {
                    continue;
                }

                var bases = match.Groups["bases"].Value;
                if (Regex.IsMatch(bases, $@"\b{Regex.Escape(collectionInterface)}\b"))
                {
                    implementations.Add(match.Groups["type"].Value);
                }
            }
        }

        return implementations;
    }

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Agent-X source root from test output directory.");
    }
}

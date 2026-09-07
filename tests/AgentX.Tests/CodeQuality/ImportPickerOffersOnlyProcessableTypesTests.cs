using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the import file picker against offering a format nothing can read.
/// <para>
/// The Knowledge Vault picker advertises a fixed extension list. Every entry on it is a
/// promise: the user is shown a file, allowed to select it, and expects it in their vault.
/// If no <c>IDocumentProcessor</c> claims that extension the import falls through to
/// "unsupported format" after the user has already chosen the file, which reads as a bug in
/// the app rather than a limit of it.
/// </para>
/// <para>
/// This is the same defect class as an unregistered processor, approached from the other
/// end: there the capability existed with no route to it, here the route exists with no
/// capability behind it. Both look correct in isolation and only disagree when compared.
/// </para>
/// </summary>
public sealed class ImportPickerOffersOnlyProcessableTypesTests
{
    /// <summary>The picker also offers "*", which means "any file" and claims nothing.</summary>
    private const string WildcardFilter = "*";

    [Fact]
    public void EveryExtensionTheImportPickerOffers_HasAProcessor()
    {
        var offered = ReadPickerExtensions();
        var handled = ReadProcessorExtensions();

        offered.Should().NotBeEmpty("the picker scan must find the filter list, or this guard is vacuous");
        handled.Should().NotBeEmpty("the processor scan must find extensions, or this guard is vacuous");

        var unbacked = offered
            .Where(ext => !handled.Contains(ext))
            .OrderBy(ext => ext, StringComparer.Ordinal)
            .ToList();

        unbacked.Should().BeEmpty(
            "the picker offers these but no IDocumentProcessor declares them, so selecting one "
            + "fails after the user has already committed to the file. Either add the extension "
            + "to the processor that should own it, or stop offering it. Unbacked:\n  "
            + string.Join("\n  ", unbacked));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extensions the Knowledge Vault import picker advertises, read from the source so the
    /// guard cannot drift from the list a user actually sees.
    /// </summary>
    private static HashSet<string> ReadPickerExtensions()
    {
        var source = File.ReadAllText(Path.Combine(
            ResolveSourceRoot(), "AgentX.App", "Views", "KnowledgeVaultPage.xaml.cs"));

        return Regex.Matches(source, @"FileTypeFilter\.Add\(""([^""]+)""\)")
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(ext => ext != WildcardFilter)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every extension declared by any concrete document processor. Read as text rather than
    /// by reflection so a processor that exists but is unregistered still counts here: this
    /// guard is about the picker's promise, and a separate guard covers registration.
    /// </summary>
    private static HashSet<string> ReadProcessorExtensions()
    {
        var directory = Path.Combine(
            ResolveSourceRoot(), "AgentX.Core", "Documents", "Processors");

        var extensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"""(\.[A-Za-z0-9]+)"""))
            {
                extensions.Add(match.Groups[1].Value.ToLowerInvariant());
            }
        }
        return extensions;
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

        throw new DirectoryNotFoundException(
            $"Could not locate the source root from {AppContext.BaseDirectory}.");
    }
}

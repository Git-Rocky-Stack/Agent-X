using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the model facts the Hardware Advisor shows. The advisor told users that
/// <c>llama3.2:latest</c> was an 8B, 4.7 GB model (the tag is the 3B, 2.0 GB model) and
/// recommended <c>phi3:medium</c>, a 7.9 GB download, to machines with 4-8 GB of memory.
/// The view model lives in the WinUI project, which this test project cannot compile, so the
/// recommendation table is checked on its source.
/// </summary>
public sealed class HardwareAdvisorModelFactsTests
{
    private static readonly Regex EntryPattern = new(
        @"Name = ""(?<name>[^""]+)"",\s*Description = ""(?<description>[^""]+)"",\s*Size = ""(?<size>[^""]+)""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Llama_3_2_tags_are_described_as_the_3B_model()
    {
        var entries = ReadEntries(ReadSource());

        var llama32 = entries.Where(e => e.Name.StartsWith("llama3.2", StringComparison.Ordinal)).ToList();
        llama32.Should().NotBeEmpty();
        llama32.Should().OnlyContain(e => e.Size == "2.0 GB" && e.Description.Contains("(3B)"),
            "llama3.2 and llama3.2:latest are the 3B model in the Ollama library");
    }

    [Fact]
    public void Phi3_medium_is_not_recommended_below_8_GB()
    {
        var source = ReadSource();
        var lightTier = Between(source, "else if (effectiveMemoryGb < 8)", "else if (effectiveMemoryGb < 16)");

        ReadEntries(lightTier).Should().NotContain(e => e.Name == "phi3:medium");
        ReadEntries(source).Where(e => e.Name == "phi3:medium")
            .Should().OnlyContain(e => e.Size == "7.9 GB");
    }

    private sealed record Entry(string Name, string Description, string Size);

    private static List<Entry> ReadEntries(string source) =>
        EntryPattern.Matches(source)
            .Select(m => new Entry(m.Groups["name"].Value, m.Groups["description"].Value, m.Groups["size"].Value))
            .ToList();

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string ReadSource() =>
        File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "ViewModels", "HardwareAdvisorViewModel.cs"));

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

using System.Text.RegularExpressions;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Processors;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Documents;

public sealed class DocumentFileTypeFilterTests
{
    [Fact]
    public void Resolve_Code_IsEveryExtensionTheCodeProcessorReads()
    {
        var expected = new CodeFileProcessor().SupportedExtensions.Select(e => e.TrimStart('.').ToLowerInvariant());

        DocumentFileTypeFilter.Resolve(DocumentFileTypeFilter.Code).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Resolve_Image_IsEveryExtensionTheImageProcessorReads()
    {
        var expected = new ImageProcessor().SupportedExtensions.Select(e => e.TrimStart('.').ToLowerInvariant());

        DocumentFileTypeFilter.Resolve(DocumentFileTypeFilter.Image).Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("pdf", "pdf")]
    [InlineData(".PDF", "pdf")]
    [InlineData(" Docx ", "docx")]
    public void Resolve_ASingleType_MatchesThatTypeOnly(string filter, string expected)
    {
        DocumentFileTypeFilter.Resolve(filter).Should().Equal(expected);
    }

    [Fact]
    public void VaultTypeChips_UseTheCategoriesTheFilterUnderstands()
    {
        // The chips carry their filter in Tag; the category names must stay in step with the
        // filter or the chips silently match nothing again.
        var xaml = File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Views", "KnowledgeVaultPage.xaml"));
        var tags = Regex.Matches(xaml, @"Click=""OnFilterTypeClick""\s+Tag=""(?<tag>[^""]*)""")
            .Select(match => match.Groups["tag"].Value)
            .ToList();

        tags.Should().Contain(DocumentFileTypeFilter.Code).And.Contain(DocumentFileTypeFilter.Image);
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

        throw new DirectoryNotFoundException("Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}

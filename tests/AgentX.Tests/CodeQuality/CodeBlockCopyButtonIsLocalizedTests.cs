using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// The Copy button on chat code blocks, and the "Copied!" it shows afterwards, were English
/// literals whatever the user's language. MarkdownMessageControl lives in the WinUI project, which
/// this test project cannot compile, so the rule is checked on its source: the code block reads
/// both texts from the localization service and uses English only when there is none.
/// </summary>
public sealed class CodeBlockCopyButtonIsLocalizedTests
{
    [Fact]
    public void The_code_block_reads_its_copy_texts_from_the_localization_service()
    {
        var source = File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Controls", "MarkdownMessageControl.xaml.cs"));

        source.Should().Contain("GetString(\"Chat_CodeCopy\")");
        source.Should().Contain("GetString(\"Chat_CodeCopied\")");
        source.Should().NotContain("Text = \"Copy\"", "the label comes from the resources");
        source.Should().NotContain("tb.Text = \"Copied!\"", "the feedback comes from the resources");
    }

    [Fact]
    public void The_English_fallback_matches_the_English_resources()
    {
        var english = ReswLocalization.For("en-US");

        english.GetString("Chat_CodeCopy").Should().Be("Copy");
        english.GetString("Chat_CodeCopied").Should().Be("Copied!");
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

        throw new DirectoryNotFoundException("Could not locate the Agent-X source root from the test output directory.");
    }
}

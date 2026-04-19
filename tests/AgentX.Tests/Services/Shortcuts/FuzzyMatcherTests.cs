using System.Linq;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class FuzzyMatcherTests
{
    [Fact]
    public void Score_exact_match_is_highest()
    {
        FuzzyMatcher.Score("import document", "import document")
            .Should().BeGreaterThan(FuzzyMatcher.Score("import document", "import"));
    }

    [Fact]
    public void Score_prefix_beats_suffix()
    {
        var prefix = FuzzyMatcher.Score("import document", "import");
        var suffix = FuzzyMatcher.Score("import document", "document");
        prefix.Should().BeGreaterThan(suffix);
    }

    [Fact]
    public void Score_word_boundary_beats_mid_word()
    {
        // "id" matches "import document" word boundaries (two capital-word starts).
        // "id" in "bridge" is mid-word only.
        FuzzyMatcher.Score("import document", "id")
            .Should().BeGreaterThan(FuzzyMatcher.Score("bridge", "id"));
    }

    [Fact]
    public void Score_is_zero_for_non_matching_query()
    {
        FuzzyMatcher.Score("import document", "xyz").Should().Be(0);
    }

    [Fact]
    public void Score_case_insensitive()
    {
        FuzzyMatcher.Score("Import Document", "IMPORT")
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rank_orders_best_match_first_and_filters_non_matches()
    {
        var items = new[] { "Settings", "Import Document", "Export Document", "Chat" };
        var ranked = FuzzyMatcher.Rank(items, x => x, "doc")
                                  .Select(r => r.Item)
                                  .ToList();

        ranked.Should().NotContain("Settings");
        ranked.Should().NotContain("Chat");
        ranked[0].Should().BeOneOf("Import Document", "Export Document");
        ranked.Should().HaveCount(2);
    }

    [Fact]
    public void Rank_excludes_zero_scores()
    {
        var items = new[] { "Alpha", "Beta", "Gamma" };
        var ranked = FuzzyMatcher.Rank(items, x => x, "xyz").ToList();
        ranked.Should().BeEmpty();
    }
}

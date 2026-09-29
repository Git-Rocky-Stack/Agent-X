using AgentX.Core.Services.Annotations;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Annotations;

/// <summary>
/// Placing a viewer's selection on the passage it came from, so a highlight is saved with
/// offsets into the passage text.
/// </summary>
public sealed class AnnotationSelectionTests
{
    [Fact]
    public void Locate_ExactSelection_ReturnsItsOffsets()
    {
        var range = AnnotationSelection.Locate("Latency budgets matter.", "budgets", positionHint: 8);

        range.Should().Be(new AnnotationRange(8, 15, "budgets"));
    }

    [Fact]
    public void Locate_SurroundingWhiteSpace_IsNotPartOfTheHighlight()
    {
        var range = AnnotationSelection.Locate("Latency budgets matter.", "  budgets \r\n", positionHint: -1);

        range.Should().Be(new AnnotationRange(8, 15, "budgets"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(20, 16)]
    [InlineData(40, 32)]
    public void Locate_ARepeatedSelection_TakesTheOccurrenceNearestTheHint(int hint, int expectedStart)
    {
        // "cost" occurs at 0, 16 and 32.
        const string passage = "cost and value, cost and value, cost";

        var range = AnnotationSelection.Locate(passage, "cost", hint);

        range.Should().NotBeNull();
        range!.Value.Start.Should().Be(expectedStart);
        range.Value.End.Should().Be(expectedStart + 4);
    }

    [Fact]
    public void Locate_DifferentLineBreaks_StillFindTheSelection_AndKeepThePassageText()
    {
        const string passage = "First line.\nSecond  line, with detail.";

        var range = AnnotationSelection.Locate(passage, "line.\r\nSecond line", positionHint: 6);

        range.Should().Be(new AnnotationRange(6, 24, "line.\nSecond  line"));
        passage[6..24].Should().Be("line.\nSecond  line");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not in the passage")]
    public void Locate_NothingUsable_ReturnsNull(string? selected)
    {
        AnnotationSelection.Locate("Latency budgets matter.", selected, positionHint: 0).Should().BeNull();
    }

    [Fact]
    public void Locate_WithoutAPassage_ReturnsNull()
    {
        AnnotationSelection.Locate(null, "budgets", positionHint: 0).Should().BeNull();
        AnnotationSelection.Locate(string.Empty, "budgets", positionHint: 0).Should().BeNull();
    }
}

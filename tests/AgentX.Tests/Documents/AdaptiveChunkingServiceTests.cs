using AgentX.Core.Configuration;
using AgentX.Core.Documents;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Documents;

/// <summary>
/// Covers the content analyzer that tells <see cref="ChunkingService"/> when prose-sized
/// chunks are the wrong shape. Its verdict silently rewrites the caller's chunk size for
/// code and tables, so a misclassification changes how a whole document is retrieved
/// without surfacing anywhere.
/// </summary>
public sealed class AdaptiveChunkingServiceTests
{
    private const int DefaultChunkSize = 512;
    private const int MinChunkSize = 128;
    private const int MaxChunkSize = 2048;

    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();

    private static AdaptiveChunkingService Service(
        int defaultSize = DefaultChunkSize,
        int minSize = MinChunkSize,
        int maxSize = MaxChunkSize)
    {
        var config = new Mock<IRagConfiguration>();
        config.SetupGet(c => c.DefaultChunkSize).Returns(defaultSize);
        config.SetupGet(c => c.MinChunkSize).Returns(minSize);
        config.SetupGet(c => c.MaxChunkSize).Returns(maxSize);
        return new AdaptiveChunkingService(config.Object, Silent);
    }

    // ── Constructor guards ───────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new AdaptiveChunkingService(null!, Silent);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AdaptiveChunkingService(Mock.Of<IRagConfiguration>(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── AnalyzeContent: empty input ──────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnalyzeContent_BlankText_ReturnsTheProseDefault(string? text)
    {
        var info = Service().AnalyzeContent(text!);

        info.ContentType.Should().Be(ContentType.Prose);
        info.LineCount.Should().Be(0);
        info.AverageLineLength.Should().Be(0);
        info.RecommendedChunkSize.Should().Be(0, "an empty analysis recommends nothing");
    }

    // ── AnalyzeContent: classification by file extension ─────────────────────

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("app.js")]
    [InlineData("app.ts")]
    [InlineData("script.py")]
    [InlineData("Main.java")]
    [InlineData("engine.cpp")]
    [InlineData("engine.h")]
    public void AnalyzeContent_CodeExtension_ClassifiesAsCodeWithoutReadingTheBody(string fileName)
    {
        // The body is unmistakable prose; the extension still wins, which is the point.
        var info = Service().AnalyzeContent("A quiet afternoon by the river, and nothing else.", fileName);

        info.ContentType.Should().Be(ContentType.Code);
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("notes.txt")]
    public void AnalyzeContent_ProseExtension_ClassifiesAsProse(string fileName)
    {
        var info = Service().AnalyzeContent("public class Foo { if (x) { } }", fileName);

        info.ContentType.Should().Be(ContentType.Prose);
    }

    [Fact]
    public void AnalyzeContent_UnknownExtension_FallsThroughToBodyAnalysis()
    {
        var info = Service().AnalyzeContent("public class A\nprivate int b\nif (c)\nfor (d)", "thing.bin");

        info.ContentType.Should().Be(ContentType.Code);
    }

    // ── AnalyzeContent: classification by body ───────────────────────────────

    [Fact]
    public void AnalyzeContent_CodeKeywords_ClassifyAsCode()
    {
        var text = "function alpha()\nclass Bravo\ndef charlie\nif (delta)\nwhile (echo)";

        Service().AnalyzeContent(text).ContentType.Should().Be(ContentType.Code);
    }

    [Fact]
    public void AnalyzeContent_PipeDelimitedRows_ClassifyAsTable()
    {
        var text = "|a|b|\n|c|d|\n|e|f|\n|g|h|";

        Service().AnalyzeContent(text).ContentType.Should().Be(ContentType.Table);
    }

    [Theory]
    [InlineData("- one\n- two\n- three\n- four")]
    [InlineData("* one\n* two\n* three\n* four")]
    [InlineData("1. one\n2. two\n3. three\n4. four")]
    [InlineData("1) one\n2) two\n3) three\n4) four")]
    public void AnalyzeContent_BulletsAndNumbering_ClassifyAsList(string text)
    {
        Service().AnalyzeContent(text).ContentType.Should().Be(ContentType.List);
    }

    [Fact]
    public void AnalyzeContent_LongSentences_ClassifyAsProse()
    {
        var text = string.Join("\n", Enumerable.Repeat(
            "This line is comfortably longer than twenty characters of ordinary narrative.", 6));

        Service().AnalyzeContent(text).ContentType.Should().Be(ContentType.Prose);
    }

    [Fact]
    public void AnalyzeContent_TwoWeakSignals_ClassifyAsMixed()
    {
        // One code line and one table line: neither clears the "more than 2" bar for a
        // dominant type, but two distinct types are present.
        var text = "class Alpha\n|a|b|\nshort\nshort";

        Service().AnalyzeContent(text).ContentType.Should().Be(ContentType.Mixed);
    }

    [Fact]
    public void AnalyzeContent_SamplesOnlyTheFirstTwentyLines()
    {
        // Twenty table rows, then a hundred code lines. Classification must not see them.
        var text = string.Join("\n", Enumerable.Repeat("|a|b|", 20))
                 + "\n" + string.Join("\n", Enumerable.Repeat("function x()", 100));

        Service().AnalyzeContent(text).ContentType.Should().Be(ContentType.Table);
    }

    // ── AnalyzeContent: measured fields ──────────────────────────────────────

    [Fact]
    public void AnalyzeContent_CountsNonEmptyLinesOnly()
    {
        var info = Service().AnalyzeContent("alpha\n\n\nbravo\n\ncharlie");

        info.LineCount.Should().Be(3);
    }

    [Fact]
    public void AnalyzeContent_AveragesLineLength()
    {
        // 2, 4 and 6 characters -> 4.
        var info = Service().AnalyzeContent("ab\nabcd\nabcdef");

        info.AverageLineLength.Should().Be(4);
    }

    [Theory]
    [InlineData("# Heading\nbody text follows here")]
    [InlineData("```\ncode\n```")]
    [InlineData("---\nbody text follows here")]
    [InlineData("___\nbody text follows here")]
    [InlineData("- a list item\nbody text follows here")]
    public void AnalyzeContent_StructuralMarkers_SetHasStructure(string text)
    {
        Service().AnalyzeContent(text).HasStructure.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeContent_PlainProse_HasNoStructure()
    {
        var text = "A quiet afternoon by the river.\nNothing here marks a section boundary.";

        Service().AnalyzeContent(text).HasStructure.Should().BeFalse();
    }

    // ── GetOptimalChunkSize ──────────────────────────────────────────────────

    [Theory]
    [InlineData(ContentType.Code, 256)]
    [InlineData(ContentType.Table, 1024)]
    [InlineData(ContentType.List, 384)]
    [InlineData(ContentType.Prose, DefaultChunkSize)]
    public void GetOptimalChunkSize_ReturnsThePerTypeSize(ContentType type, int expected)
    {
        Service().GetOptimalChunkSize(type, averageLineLength: 80).Should().Be(expected);
    }

    [Fact]
    public void GetOptimalChunkSize_Mixed_IsTwentyPercentOverTheDefault()
    {
        Service().GetOptimalChunkSize(ContentType.Mixed, 80)
            .Should().Be((int)(DefaultChunkSize * 1.2));
    }

    [Fact]
    public void GetOptimalChunkSize_UnknownType_FallsBackToTheDefault()
    {
        Service().GetOptimalChunkSize((ContentType)999, 80).Should().Be(DefaultChunkSize);
    }

    // ── RecommendedChunkSize (CalculateOptimalChunkSize) ─────────────────────

    [Fact]
    public void RecommendedChunkSize_Code_IsThreeQuartersOfTheDefault()
    {
        // Lines are 12-13 chars, i.e. under 50, so the short-line bonus applies on top.
        var info = Service().AnalyzeContent("function a()\nclass Bravo\ndef charlie\nif (delta)");

        var expected = Math.Min(DefaultChunkSize * 3 / 4 * 6 / 5, MaxChunkSize);
        info.RecommendedChunkSize.Should().Be(expected);
    }

    [Fact]
    public void RecommendedChunkSize_ShortLines_GetTwentyPercentMore()
    {
        var shortLines = string.Join("\n", Enumerable.Repeat("tiny line", 8));
        var mediumLines = string.Join("\n", Enumerable.Repeat(new string('x', 100), 8));

        var shorter = Service().AnalyzeContent(shortLines).RecommendedChunkSize;
        var medium = Service().AnalyzeContent(mediumLines).RecommendedChunkSize;

        shorter.Should().Be(DefaultChunkSize * 6 / 5);
        medium.Should().Be(DefaultChunkSize, "lines between 50 and 150 characters get no adjustment");
    }

    [Fact]
    public void RecommendedChunkSize_DenseLines_GetTwentyPercentLess()
    {
        var dense = string.Join("\n", Enumerable.Repeat(new string('x', 200), 8));

        Service().AnalyzeContent(dense).RecommendedChunkSize
            .Should().Be(DefaultChunkSize * 4 / 5);
    }

    [Fact]
    public void RecommendedChunkSize_NeverExceedsTheConfiguredMaximum()
    {
        var shortLines = string.Join("\n", Enumerable.Repeat("tiny", 8));

        Service(defaultSize: 500, minSize: 10, maxSize: 300)
            .AnalyzeContent(shortLines).RecommendedChunkSize
            .Should().BeLessThanOrEqualTo(300);
    }

    [Fact]
    public void RecommendedChunkSize_NeverFallsBelowTheConfiguredMinimum()
    {
        var dense = string.Join("\n", Enumerable.Repeat(new string('x', 200), 8));

        Service(defaultSize: 100, minSize: 400, maxSize: 2048)
            .AnalyzeContent(dense).RecommendedChunkSize
            .Should().BeGreaterThanOrEqualTo(400);
    }

    // ── DetectNaturalBoundaries ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DetectNaturalBoundaries_BlankText_ReturnsNothing(string? text)
    {
        Service().DetectNaturalBoundaries(text!, 100).Should().BeEmpty();
    }

    [Fact]
    public void DetectNaturalBoundaries_HeadingAfterEnoughText_IsABoundary()
    {
        var body = new string('x', 80);
        var text = $"{body}\n# Section two\n{body}";

        Service().DetectNaturalBoundaries(text, targetChunkSize: 100).Should().Contain(1);
    }

    [Fact]
    public void DetectNaturalBoundaries_HeadingTooEarly_IsNotABoundary()
    {
        // The heading arrives well under half the target, so splitting there would emit a
        // chunk barely larger than its own title.
        var text = "tiny\n# Section two\n" + new string('x', 400);

        Service().DetectNaturalBoundaries(text, targetChunkSize: 1000).Should().NotContain(1);
    }

    [Fact]
    public void DetectNaturalBoundaries_NoStructure_FallsBackToLengthAlone()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('x', 50), 10));

        var boundaries = Service().DetectNaturalBoundaries(text, targetChunkSize: 100);

        boundaries.Should().NotBeEmpty();
        boundaries.Should().BeInAscendingOrder();
    }

    [Fact]
    public void DetectNaturalBoundaries_ShortText_ProducesNone()
    {
        Service().DetectNaturalBoundaries("one line only", targetChunkSize: 10_000)
            .Should().BeEmpty();
    }
}

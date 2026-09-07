using AgentX.Core.AI;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Documents;

/// <summary>
/// Covers the recursive splitter that turns a document into the chunks everything
/// downstream embeds and retrieves. Its outputs are the unit of RAG: a chunk that loses
/// its character offsets breaks citation highlighting, and a chunk that quietly exceeds
/// the size budget gets truncated by the embedding model instead of rejected.
/// </summary>
public sealed class ChunkingServiceTests
{
    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();

    private static ChunkingService Service() => new(Silent);

    // ── Parameter validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChunkText_NonPositiveChunkSize_Throws(int chunkSize)
    {
        var act = () => Service().ChunkText("some text here", chunkSize, 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("chunkSize");
    }

    [Fact]
    public void ChunkText_NegativeOverlap_Throws()
    {
        var act = () => Service().ChunkText("some text here", 100, -1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("chunkOverlap");
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void ChunkText_OverlapAtOrAboveChunkSize_Throws(int chunkSize, int overlap)
    {
        // Overlap >= size cannot make forward progress; without the guard the grouping
        // loop re-emits the same tail forever.
        var act = () => Service().ChunkText("some text here", chunkSize, overlap);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("chunkOverlap");
    }

    // ── Empty and trivial input ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t ")]
    public void ChunkText_BlankInput_ReturnsNoChunks(string text)
    {
        Service().ChunkText(text).Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_ShortText_ProducesASingleChunk()
    {
        var chunks = Service().ChunkText("A short paragraph of text.", 512, 50);

        chunks.Should().ContainSingle();
        chunks[0].Index.Should().Be(0);
        chunks[0].Content.Should().Be("A short paragraph of text.");
        chunks[0].TokenCount.Should().Be(5);
    }

    [Fact]
    public void ChunkText_AttachesSectionTitleAndPageNumberToEveryChunk()
    {
        var chunks = Service().ChunkText(
            Words(60), chunkSize: 20, chunkOverlap: 0,
            sectionTitle: "Chapter 2", pageNumber: 7);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => c.SectionTitle == "Chapter 2" && c.PageNumber == 7);
    }

    [Fact]
    public void ChunkText_IndexesChunksConsecutivelyFromZero()
    {
        var chunks = Service().ChunkText(Words(120), chunkSize: 20, chunkOverlap: 0);

        chunks.Select(c => c.Index).Should().Equal(Enumerable.Range(0, chunks.Count));
    }

    // ── Paragraph / sentence / word splitting ────────────────────────────────

    [Fact]
    public void ChunkText_SplitsOnParagraphBoundariesBeforeAnythingElse()
    {
        var text = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";

        var chunks = Service().ChunkText(text, chunkSize: 2, chunkOverlap: 0);

        chunks.Should().HaveCount(3);
        chunks[0].Content.Should().Be("First paragraph.");
        chunks[2].Content.Should().Be("Third paragraph.");
    }

    [Fact]
    public void ChunkText_DropsWhitespaceOnlyParagraphs()
    {
        var text = "Real content here.\n\n   \n\nMore real content.";

        var chunks = Service().ChunkText(text, chunkSize: 3, chunkOverlap: 0);

        chunks.Should().HaveCount(2);
        chunks.Should().OnlyContain(c => c.Content.Trim().Length > 0);
    }

    [Theory]
    [InlineData(". ")]
    [InlineData("! ")]
    [InlineData("? ")]
    public void ChunkText_OversizedParagraph_SplitsAtSentenceBoundaries(string separator)
    {
        // One paragraph of three sentences, each 5 words, with a 6-token budget: too big
        // for one chunk, so the sentence splitter has to run.
        var text = $"one two three four five{separator}six seven eight nine ten{separator}"
                 + "eleven twelve thirteen fourteen fifteen.";

        var chunks = Service().ChunkText(text, chunkSize: 6, chunkOverlap: 0);

        chunks.Should().HaveCount(3);
        chunks[0].Content.Should().StartWith("one two three");
        chunks[2].Content.Should().Contain("fifteen");
    }

    [Fact]
    public void ChunkText_SentenceEndingBeforeNewline_IsABoundaryToo()
    {
        var text = "one two three four five.\nsix seven eight nine ten.\neleven twelve thirteen fourteen.";

        var chunks = Service().ChunkText(text, chunkSize: 6, chunkOverlap: 0);

        chunks.Should().HaveCount(3);
    }

    [Fact]
    public void ChunkText_SentenceLongerThanTheBudget_SplitsAtWordBoundaries()
    {
        // A single 30-word sentence with no internal punctuation: paragraphs cannot help,
        // sentences cannot help, so the word splitter is the only path that fits.
        var chunks = Service().ChunkText(Words(30), chunkSize: 10, chunkOverlap: 0);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => c.TokenCount <= 10);
    }

    [Fact]
    public void ChunkText_ReassemblesEveryWordOfTheSource()
    {
        var source = Words(75);

        var rebuilt = string.Join(" ", Service().ChunkText(source, chunkSize: 12, chunkOverlap: 0)
            .Select(c => c.Content));

        rebuilt.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Equal(source.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ── Offsets ──────────────────────────────────────────────────────────────

    [Fact]
    public void ChunkText_FirstChunkStartsAtTheStartOfTheSource()
    {
        var chunks = Service().ChunkText("First paragraph.\n\nSecond paragraph.", 3, 0);

        chunks[0].StartCharOffset.Should().Be(0);
    }

    [Fact]
    public void ChunkText_OffsetsAdvanceAndStayInsideTheSource()
    {
        var source = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";

        var chunks = Service().ChunkText(source, 3, 0);

        chunks.Should().BeInAscendingOrder(c => c.StartCharOffset);
        chunks.Should().OnlyContain(c => c.EndCharOffset <= source.Length);
        chunks.Should().OnlyContain(c => c.StartCharOffset < c.EndCharOffset);
    }

    // ── Overlap ──────────────────────────────────────────────────────────────

    [Fact]
    public void ChunkText_WithOverlap_RepeatsTheTailOfThePreviousChunk()
    {
        var text = "alpha bravo.\n\ncharlie delta.\n\necho foxtrot.\n\ngolf hotel.";

        var overlapped = Service().ChunkText(text, chunkSize: 4, chunkOverlap: 2);
        var plain = Service().ChunkText(text, chunkSize: 4, chunkOverlap: 0);

        overlapped.Count.Should().BeGreaterThan(plain.Count,
            "carrying tokens forward means fewer new tokens fit per chunk");

        // Overlap carries whole trailing segments, not a token slice: the walk back stops
        // as soon as it has collected chunkOverlap tokens, so chunk 1 opens with the last
        // segment of chunk 0 verbatim.
        overlapped[0].Content.Should().EndWith("charlie delta.");
        overlapped[1].Content.Should().StartWith("charlie delta.");
    }

    [Fact]
    public void ChunkText_ZeroOverlap_RepeatsNothing()
    {
        var text = "alpha bravo.\n\ncharlie delta.\n\necho foxtrot.";

        var chunks = Service().ChunkText(text, chunkSize: 4, chunkOverlap: 0);

        chunks[1].Content.Should().NotContain("bravo");
    }

    // ── Token counting ───────────────────────────────────────────────────────

    [Fact]
    public void ChunkText_WithoutATokenCounter_ApproximatesTokensAsWords()
    {
        var chunks = Service().ChunkText("one two three four", 512, 0);

        chunks[0].TokenCount.Should().Be(4);
    }

    [Fact]
    public void ChunkText_WithATokenCounter_UsesItInsteadOfTheWordApproximation()
    {
        var counter = new Mock<ITokenCounter>();
        counter.Setup(c => c.CountTokens(It.IsAny<string>(), It.IsAny<string?>()))
               .Returns((string t, string? _) => t.Length);   // deliberately not word count

        var chunks = new ChunkingService(counter.Object, Silent).ChunkText("one two three four", 512, 0);

        chunks[0].TokenCount.Should().Be("one two three four".Length);
        counter.Verify(c => c.CountTokens(It.IsAny<string>(), It.IsAny<string?>()), Times.AtLeastOnce);
    }

    // ── ChunkDocument ────────────────────────────────────────────────────────

    [Fact]
    public void ChunkDocument_NullDocument_Throws()
    {
        var act = () => Service().ChunkDocument(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ChunkDocument_NoExtractedText_ReturnsNoChunks(string text)
    {
        Service().ChunkDocument(Doc(text)).Should().BeEmpty();
    }

    [Fact]
    public void ChunkDocument_SinglePage_ChunksAsOneStreamWithNoPageNumber()
    {
        var chunks = Service().ChunkDocument(Doc("alpha bravo charlie.", pageCount: 1), 512, 50);

        chunks.Should().ContainSingle();
        chunks[0].PageNumber.Should().BeNull();
    }

    [Fact]
    public void ChunkDocument_FormFeedsWithMultiplePages_TagsChunksWithOneBasedPageNumbers()
    {
        var doc = Doc("page one text.\fpage two text.\fpage three text.", pageCount: 3);

        var chunks = Service().ChunkDocument(doc, chunkSize: 4, chunkOverlap: 0);

        chunks.Select(c => c.PageNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ChunkDocument_PagedChunks_CarryAGlobalIndexAndGlobalOffsets()
    {
        var doc = Doc("page one text.\fpage two text.\fpage three text.", pageCount: 3);

        var chunks = Service().ChunkDocument(doc, chunkSize: 4, chunkOverlap: 0);

        chunks.Select(c => c.Index).Should().Equal(0, 1, 2);
        chunks.Should().BeInAscendingOrder(c => c.StartCharOffset);
        chunks[1].StartCharOffset.Should().BeGreaterThan(chunks[0].EndCharOffset - 1,
            "page two's offsets are relative to the whole document, not to page two");
    }

    [Fact]
    public void ChunkDocument_BlankPage_IsSkippedButStillAdvancesTheOffset()
    {
        var doc = Doc("page one text.\f   \fpage three text.", pageCount: 3);

        var chunks = Service().ChunkDocument(doc, chunkSize: 4, chunkOverlap: 0);

        chunks.Select(c => c.PageNumber).Should().Equal(1, 3);
        chunks[1].StartCharOffset.Should().BeGreaterThan("page one text.\f   \f".Length - 1);
    }

    [Fact]
    public void ChunkDocument_FormFeedsButOnlyOnePage_StaysOnTheSingleStreamPath()
    {
        var doc = Doc("page one text.\fpage two text.", pageCount: 1);

        Service().ChunkDocument(doc, 512, 50).Should().OnlyContain(c => c.PageNumber == null);
    }

    // ── Adaptive override ────────────────────────────────────────────────────

    [Theory]
    [InlineData(ContentType.Code)]
    [InlineData(ContentType.Table)]
    public void ChunkDocument_AdaptiveAnalyzer_OverridesTheCallerForCodeAndTables(ContentType type)
    {
        var adaptive = Analyzer(type, recommended: 4);

        var chunks = new ChunkingService(null, adaptive.Object, Silent)
            .ChunkDocument(Doc(Words(40)), chunkSize: 40, chunkOverlap: 0);

        chunks.Should().HaveCountGreaterThan(1,
            "a recommended size of 4 must beat the caller's 40 for code and table content");
        chunks.Should().OnlyContain(c => c.TokenCount <= 4);
    }

    [Theory]
    [InlineData(ContentType.Prose)]
    [InlineData(ContentType.List)]
    [InlineData(ContentType.Mixed)]
    public void ChunkDocument_AdaptiveAnalyzer_DefersToTheCallerForEverythingElse(ContentType type)
    {
        var adaptive = Analyzer(type, recommended: 4);

        var chunks = new ChunkingService(null, adaptive.Object, Silent)
            .ChunkDocument(Doc(Words(40)), chunkSize: 40, chunkOverlap: 0);

        chunks.Should().ContainSingle("the caller's chunkSize is the user-tuned setting");
    }

    [Fact]
    public void ChunkDocument_AdaptiveRecommendationMatchesTheCaller_ChangesNothing()
    {
        var adaptive = Analyzer(ContentType.Code, recommended: 40);

        var chunks = new ChunkingService(null, adaptive.Object, Silent)
            .ChunkDocument(Doc(Words(40)), chunkSize: 40, chunkOverlap: 0);

        chunks.Should().ContainSingle();
    }

    [Fact]
    public void ChunkDocument_AdaptiveAnalyzerThrows_FallsBackToTheCallerInsteadOfFailing()
    {
        var adaptive = new Mock<IAdaptiveChunkingService>();
        adaptive.Setup(a => a.AnalyzeContent(It.IsAny<string>(), It.IsAny<string?>()))
                .Throws(new InvalidOperationException("analyzer exploded"));

        var chunks = new ChunkingService(null, adaptive.Object, Silent)
            .ChunkDocument(Doc(Words(40)), chunkSize: 40, chunkOverlap: 0);

        chunks.Should().ContainSingle("a broken analyzer must not lose the document");
    }

    // ── Constructors ─────────────────────────────────────────────────────────

    [Fact]
    public void ParameterlessConstructor_Works()
    {
        new ChunkingService().ChunkText("alpha bravo charlie").Should().ContainSingle();
    }

    [Fact]
    public void NullLogger_FallsBackToTheStaticLoggerInsteadOfThrowing()
    {
        new ChunkingService(null!).ChunkText("alpha bravo").Should().ContainSingle();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Mock<IAdaptiveChunkingService> Analyzer(ContentType type, int recommended)
    {
        var mock = new Mock<IAdaptiveChunkingService>();
        mock.Setup(a => a.AnalyzeContent(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new AdaptiveChunkInfo { ContentType = type, RecommendedChunkSize = recommended });
        return mock;
    }

    private static ProcessedDocument Doc(string text, int pageCount = 1) => new()
    {
        FileName = "sample.txt",
        FilePath = @"C:\docs\sample.txt",
        FileType = ".txt",
        ExtractedText = text,
        PageCount = pageCount,
        WordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
    };

    /// <summary>A single sentence of <paramref name="count"/> distinct words, no punctuation.</summary>
    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"w{i}"));
}

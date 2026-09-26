using System.Text;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Processors;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Documents.Processors;

/// <summary>
/// Tests for <see cref="PdfProcessor"/> against small PDFs written byte by byte, so no font
/// or rendering support is needed to produce them. Covers the page markers the chunker
/// depends on and the failure reporting that replaced "import as empty success".
/// </summary>
public sealed class PdfProcessorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PdfProcessor _processor = new();

    public PdfProcessorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "agentx-pdfprocessor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch (IOException) { }
    }

    // ── Page markers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_MultiPagePdf_SeparatesPagesWithFormFeeds()
    {
        // The chunker only records page numbers when pages are separated by '\f'; the
        // processor used to join pages with a newline, so PageNumber was never populated.
        var path = WritePdf("two-pages.pdf", ShowText("Hello page one"), ShowText("Hello page two"));

        var document = await _processor.ProcessAsync(path);

        document.PageCount.Should().Be(2);
        document.ExtractedText.Should().Be("Hello page one\fHello page two");
    }

    [Fact]
    public async Task ProcessAsync_ChunksCarryTheirPageNumbers_IncludingAfterAnEmptyPage()
    {
        var path = WritePdf("gap.pdf", ShowText("First page words"), string.Empty, ShowText("Third page words"));

        var document = await _processor.ProcessAsync(path);
        var chunks = new ChunkingService(new LoggerConfiguration().CreateLogger()).ChunkDocument(document, 512, 50);

        chunks.Select(c => c.PageNumber).Should().Equal(1, 3);
    }

    // ── Failures are reported, not imported as empty documents ───────────────

    [Fact]
    public async Task ProcessAsync_CorruptFile_ThrowsAnExtractionFailure()
    {
        var path = Path.Combine(_tempDirectory, "corrupt.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4\nthis is not really a pdf");

        var act = () => _processor.ProcessAsync(path);

        (await act.Should().ThrowAsync<DocumentExtractionException>())
            .Which.Message.Should().Contain("corrupt.pdf");
    }

    [Fact]
    public async Task ProcessAsync_PdfWithoutATextLayer_ThrowsAnExtractionFailure()
    {
        var path = WritePdf("scan.pdf", string.Empty, string.Empty);

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("*no extractable text layer*");
    }

    [Fact]
    public async Task ProcessAsync_TextThatCannotBeDecoded_ThrowsInsteadOfIndexingGarbage()
    {
        // Two-byte glyph ids shown with a hex string come out as control characters when the
        // font's ToUnicode map is not applied.
        var path = WritePdf("cid.pdf", "BT /F1 12 Tf 72 720 Td <000100020003000400050006000700080009> Tj ET");

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("*cannot decode*");
    }

    [Theory]
    [InlineData("Plain readable sentence, with punctuation!", false)]
    [InlineData("\u0000\u0001\u0000\u0002\u0000\u0003\u0000\u0004", true)]
    [InlineData("Mostly fine text\u0001", false)]
    [InlineData(" ab", true)]
    public void LooksUndecodable_FlagsControlAndPrivateUseHeavyText(string text, bool expected)
    {
        PdfProcessor.LooksUndecodable(text).Should().Be(expected);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ShowText(string text) => $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";

    /// <summary>Writes a minimal PDF with one content stream per page and a valid xref table.</summary>
    private string WritePdf(string name, params string[] pageStreams)
    {
        var bodies = new Dictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R >>",
            [3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        var nextObject = 4;
        var pageObjects = new List<int>();
        foreach (var stream in pageStreams)
        {
            var pageObject = nextObject++;
            var contentObject = nextObject++;
            pageObjects.Add(pageObject);
            bodies[pageObject] =
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 3 0 R >> >> /Contents {contentObject} 0 R >>";
            bodies[contentObject] =
                $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream";
        }

        bodies[2] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjects.Select(n => $"{n} 0 R"))}] /Count {pageObjects.Count} >>";

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[nextObject];
        for (var number = 1; number < nextObject; number++)
        {
            offsets[number] = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append($"{number} 0 obj\n{bodies[number]}\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {nextObject}\n");
        pdf.Append("0000000000 65535 f \n");
        for (var number = 1; number < nextObject; number++)
        {
            pdf.Append($"{offsets[number]:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {nextObject} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(pdf.ToString()));
        return path;
    }
}

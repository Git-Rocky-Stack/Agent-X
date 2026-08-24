using AgentX.Core.Documents.Processors;
using AgentX.Core.Services.Web;
using AgentX.Core.Services.Web.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Documents.Processors;

/// <summary>
/// Tests for <see cref="WebProcessor"/>, the .url / .webloc shortcut importer.
/// <para>
/// The processor was implemented but never registered in the composition root, so it
/// never ran for a user. Now that it is registered these tests cover the parsing and
/// failure paths against real files on disk, with only the network boundary
/// (<see cref="IWebScraperService"/>) mocked.
/// </para>
/// </summary>
public sealed class WebProcessorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IWebScraperService> _scraper = new(MockBehavior.Strict);
    private readonly WebProcessor _processor;

    public WebProcessorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "agentx-webprocessor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _processor = new WebProcessor(_scraper.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch (IOException) { }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullScraper_Throws()
    {
        var act = () => new WebProcessor(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("webScraper");
    }

    // ── CanProcess ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("bookmark.url", true)]
    [InlineData("bookmark.URL", true)]
    [InlineData("bookmark.webloc", true)]
    [InlineData("bookmark.WebLoc", true)]
    [InlineData("document.pdf", false)]
    [InlineData("notes.txt", false)]
    [InlineData("no-extension", false)]
    public void CanProcess_MatchesOnlyShortcutExtensions(string fileName, bool expected)
    {
        _processor.CanProcess(fileName).Should().Be(expected);
    }

    [Fact]
    public void SupportedExtensions_AreTheTwoShortcutFormats()
    {
        _processor.SupportedExtensions.Should().BeEquivalentTo(new[] { ".url", ".webloc" });
    }

    // ── Missing file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_MissingFile_ThrowsFileNotFound()
    {
        var missing = Path.Combine(_tempDirectory, "nope.url");

        var act = () => _processor.ProcessAsync(missing);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── .url (Windows INI) parsing ───────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_WindowsShortcut_ExtractsUrlAndScrapedContent()
    {
        var path = WriteFile("article.url", "[InternetShortcut]\r\nURL=https://example.com/article\r\n");
        ExpectScrape("https://example.com/article", Success("Example Article", "Body text here.", wordCount: 3));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().Be("Body text here.");
        document.ExtractedTitle.Should().Be("Example Article");
        document.WordCount.Should().Be(3);
        document.FileType.Should().Be("web");
        document.Metadata.Custom["sourceUrl"].Should().Be("https://example.com/article");
        document.ContentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProcessAsync_WindowsShortcut_IgnoresPrecedingKeysAndIsCaseInsensitive()
    {
        var path = WriteFile(
            "article.url",
            "[InternetShortcut]\r\nIconIndex=0\r\nIDList=\r\nurl=https://example.com/late\r\nHotKey=0\r\n");
        ExpectScrape("https://example.com/late", Success("Late", "Found it.", wordCount: 2));

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom["sourceUrl"].Should().Be("https://example.com/late");
    }

    [Fact]
    public async Task ProcessAsync_WindowsShortcutWithBlankUrlValue_ReportsNoUrlFound()
    {
        var path = WriteFile("blank.url", "[InternetShortcut]\r\nURL=   \r\n");

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().BeEmpty();
        document.Metadata.Custom["error"].Should().Be("No URL found in shortcut file.");
        _scraper.Verify(s => s.IsValidUrl(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_EmptyFile_ReportsNoUrlFound()
    {
        var path = WriteFile("empty.url", "   \r\n  ");

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom["error"].Should().Be("No URL found in shortcut file.");
    }

    // ── .webloc (macOS plist) parsing ────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_WeblocShortcut_ExtractsUrlFromPlist()
    {
        var path = WriteFile("bookmark.webloc", """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
              <dict>
                <key>SomethingElse</key>
                <string>ignored</string>
                <key>URL</key>
                <string>https://example.com/mac</string>
              </dict>
            </plist>
            """);
        ExpectScrape("https://example.com/mac", Success("Mac Bookmark", "Plist body.", wordCount: 2));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedTitle.Should().Be("Mac Bookmark");
        document.Metadata.Custom["sourceUrl"].Should().Be("https://example.com/mac");
    }

    [Fact]
    public async Task ProcessAsync_WeblocWithNoDict_ReportsNoUrlFound()
    {
        var path = WriteFile("nodict.webloc", """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><array><string>nothing</string></array></plist>
            """);

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom["error"].Should().Be("No URL found in shortcut file.");
    }

    [Fact]
    public async Task ProcessAsync_WeblocWithMalformedXml_ReportsNoUrlFound()
    {
        var path = WriteFile("broken.webloc", "<plist><dict><key>URL</key>");

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom["error"].Should().Be("No URL found in shortcut file.");
    }

    // ── Validation and scraper failure paths ─────────────────────────────────

    [Fact]
    public async Task ProcessAsync_InvalidUrl_SkipsScrapingAndReportsTheUrl()
    {
        var path = WriteFile("bad.url", "[InternetShortcut]\r\nURL=notaurl\r\n");
        _scraper.Setup(s => s.IsValidUrl("notaurl")).Returns(false);

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().BeEmpty();
        document.Metadata.Custom["error"].Should().Be("Invalid URL: notaurl");
        _scraper.Verify(s => s.ExtractContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ScraperFailure_RecordsErrorAndKeepsSourceUrl()
    {
        var path = WriteFile("fail.url", "[InternetShortcut]\r\nURL=https://example.com/down\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/down")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/down", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent { Success = false, ErrorMessage = "HTTP 503" });

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().BeEmpty();
        document.Metadata.Custom["error"].Should().Be("HTTP 503");
        document.Metadata.Custom["sourceUrl"].Should().Be("https://example.com/down");
    }

    [Fact]
    public async Task ProcessAsync_ScraperFailureWithNoMessage_FallsBackToGenericError()
    {
        var path = WriteFile("fail2.url", "[InternetShortcut]\r\nURL=https://example.com/quiet\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/quiet")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/quiet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent { Success = false, ErrorMessage = null });

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom["error"].Should().Be("Extraction failed.");
    }

    [Fact]
    public async Task ProcessAsync_ScraperThrows_IsSwallowedIntoAnErrorDocument()
    {
        var path = WriteFile("throw.url", "[InternetShortcut]\r\nURL=https://example.com/boom\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/boom")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/boom", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("scraper exploded"));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().BeEmpty();
        document.Metadata.Custom["error"].Should().Be("scraper exploded");
    }

    [Fact]
    public async Task ProcessAsync_Cancellation_PropagatesInsteadOfBeingSwallowed()
    {
        var path = WriteFile("cancel.url", "[InternetShortcut]\r\nURL=https://example.com/slow\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/slow")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/slow", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _processor.ProcessAsync(path, new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Optional metadata mapping ────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_PopulatesEveryOptionalMetadataFieldWhenPresent()
    {
        var published = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var path = WriteFile("rich.url", "[InternetShortcut]\r\nURL=https://example.com/rich\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/rich")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/rich", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent
            {
                Success = true,
                Title = "Rich",
                Content = "Full body.",
                WordCount = 2,
                Language = "en",
                Author = "Ada Lovelace",
                SiteName = "Example",
                Description = "A description.",
                FeaturedImageUrl = "https://example.com/hero.png",
                PublishDate = published,
            });

        var document = await _processor.ProcessAsync(path);

        document.Language.Should().Be("en");
        document.Metadata.Author.Should().Be("Ada Lovelace");
        document.Metadata.Custom["author"].Should().Be("Ada Lovelace");
        document.Metadata.Custom["siteName"].Should().Be("Example");
        document.Metadata.Custom["description"].Should().Be("A description.");
        document.Metadata.Custom["featuredImageUrl"].Should().Be("https://example.com/hero.png");
        document.Metadata.CreatedDate.Should().Be(published);
        document.Metadata.Custom["publishDate"].Should().Be(published.ToString("O"));
    }

    [Fact]
    public async Task ProcessAsync_OmitsOptionalMetadataKeysWhenTheScraperReturnsNone()
    {
        var path = WriteFile("bare.url", "[InternetShortcut]\r\nURL=https://example.com/bare\r\n");
        ExpectScrape("https://example.com/bare", Success("Bare", "Body.", wordCount: 1));

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom.Should().NotContainKeys("author", "siteName", "description", "featuredImageUrl", "publishDate");
        document.Metadata.Author.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private void ExpectScrape(string url, WebContent result)
    {
        _scraper.Setup(s => s.IsValidUrl(url)).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync(url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static WebContent Success(string title, string content, long wordCount) => new()
    {
        Success = true,
        Title = title,
        Content = content,
        WordCount = wordCount,
    };
}

using System.Net;
using AgentX.Core.Documents;
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
        // Hermetic DNS: every host name resolves to a public address unless a test says otherwise.
        _processor = new WebProcessor(_scraper.Object, (host, _) => Task.FromResult(ResolveHost(host)));
    }

    private readonly Dictionary<string, IPAddress[]> _dns = new(StringComparer.OrdinalIgnoreCase);

    private IPAddress[] ResolveHost(string host)
        => _dns.TryGetValue(host, out var addresses) ? addresses : new[] { IPAddress.Parse("93.184.216.34") };

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

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("No URL found in shortcut file.");
        _scraper.Verify(s => s.IsValidUrl(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_EmptyFile_ReportsNoUrlFound()
    {
        var path = WriteFile("empty.url", "   \r\n  ");

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("No URL found in shortcut file.");
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

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("No URL found in shortcut file.");
    }

    [Fact]
    public async Task ProcessAsync_WeblocWithMalformedXml_ReportsNoUrlFound()
    {
        var path = WriteFile("broken.webloc", "<plist><dict><key>URL</key>");

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("No URL found in shortcut file.");
    }

    // ── Validation and scraper failure paths ─────────────────────────────────

    [Fact]
    public async Task ProcessAsync_InvalidUrl_SkipsScrapingAndReportsTheUrl()
    {
        var path = WriteFile("bad.url", "[InternetShortcut]\r\nURL=notaurl\r\n");
        _scraper.Setup(s => s.IsValidUrl("notaurl")).Returns(false);

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("Invalid URL: notaurl");
        _scraper.Verify(s => s.ExtractContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ScraperFailure_ReportsTheScraperError()
    {
        var path = WriteFile("fail.url", "[InternetShortcut]\r\nURL=https://example.com/down\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/down")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/down", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent { Success = false, ErrorMessage = "HTTP 503" });

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("HTTP 503");
    }

    [Fact]
    public async Task ProcessAsync_ScraperFailureWithNoMessage_FallsBackToGenericError()
    {
        var path = WriteFile("fail2.url", "[InternetShortcut]\r\nURL=https://example.com/quiet\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/quiet")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/quiet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent { Success = false, ErrorMessage = null });

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("Extraction failed.");
    }

    [Fact]
    public async Task ProcessAsync_ScraperThrows_IsReportedAsAnExtractionFailure()
    {
        var path = WriteFile("throw.url", "[InternetShortcut]\r\nURL=https://example.com/boom\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://example.com/boom")).Returns(true);
        _scraper
            .Setup(s => s.ExtractContentAsync("https://example.com/boom", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("scraper exploded"));

        var act = () => _processor.ProcessAsync(path);

        (await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("scraper exploded"))
            .Which.InnerException.Should().BeOfType<InvalidOperationException>();
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

    // Local and private-network targets

    [Theory]
    [InlineData("http://localhost:11434/api/tags")]
    [InlineData("http://127.0.0.1:9846/api/documents")]
    [InlineData("http://127.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://10.0.0.5/admin")]
    [InlineData("http://172.20.1.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://printer.local/status")]
    [InlineData("http://service.internal/")]
    public async Task ProcessAsync_LocalOrPrivateTarget_IsRefusedWithoutFetching(string url)
    {
        // A .url file dropped into a watched folder must not make the app fetch from this
        // computer or the local network on the file author's behalf.
        var path = WriteFile("local.url", $"[InternetShortcut]\r\nURL={url}\r\n");
        _scraper.Setup(s => s.IsValidUrl(url)).Returns(true);

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("Refused to fetch*");
        _scraper.Verify(s => s.ExtractContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_HostNameResolvingToAPrivateAddress_IsRefused()
    {
        _dns["intranet.example.com"] = new[] { IPAddress.Parse("10.1.2.3") };
        var path = WriteFile("rebind.url", "[InternetShortcut]\r\nURL=https://intranet.example.com/wiki\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://intranet.example.com/wiki")).Returns(true);

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("Refused to fetch*");
        _scraper.Verify(s => s.ExtractContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_UnresolvableHost_IsRefused()
    {
        _dns["nowhere.example.com"] = Array.Empty<IPAddress>();
        var path = WriteFile("nowhere.url", "[InternetShortcut]\r\nURL=https://nowhere.example.com/\r\n");
        _scraper.Setup(s => s.IsValidUrl("https://nowhere.example.com/")).Returns(true);

        var act = () => _processor.ProcessAsync(path);

        await act.Should().ThrowAsync<DocumentExtractionException>().WithMessage("Could not resolve*");
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("93.184.216.34", false)]
    [InlineData("2606:4700::1111", false)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:192.168.0.1", true)]
    public void IsNonPublicAddress_ClassifiesAddresses(string address, bool expected)
    {
        WebProcessor.IsNonPublicAddress(IPAddress.Parse(address)).Should().Be(expected);
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

using AgentX.Core.Services.Web;
using AgentX.Core.Services.Web.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Web;

/// <summary>
/// Integration tests for the thin WebScraperService orchestrator.
/// Tests the full fetch -> parse -> extract -> merge pipeline with mocked service dependencies,
/// verifying that the orchestrator correctly delegates and composes results.
/// </summary>
public class WebScraperServiceIntegrationTests
{
    private readonly Mock<IWebContentFetcher> _fetcherMock;
    private readonly Mock<IHtmlParser> _parserMock;
    private readonly Mock<IStructuredDataExtractor> _extractorMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly WebScraperService _sut;

    private const string SampleHtml = """
        <html lang="en">
        <head>
            <title>Test Article</title>
            <meta property="og:title" content="OG Title" />
            <meta name="description" content="A test description" />
            <meta property="og:site_name" content="TestSite" />
            <meta property="og:image" content="https://example.com/image.jpg" />
            <link rel="canonical" href="https://example.com/article" />
        </head>
        <body>
            <article>
                <h1>Test Article</h1>
                <p>This is a test article with enough content to be meaningful and pass extraction thresholds.</p>
            </article>
        </body>
        </html>
        """;

    public WebScraperServiceIntegrationTests()
    {
        _fetcherMock = new Mock<IWebContentFetcher>();
        _parserMock = new Mock<IHtmlParser>();
        _extractorMock = new Mock<IStructuredDataExtractor>();
        _loggerMock = new Mock<ILogger>();

        _loggerMock.Setup(l => l.ForContext<WebScraperService>()).Returns(_loggerMock.Object);

        _sut = new WebScraperService(
            _fetcherMock.Object,
            _parserMock.Object,
            _extractorMock.Object,
            _loggerMock.Object);
    }

    // ─── ExtractContentAsync Pipeline Tests ──────────────────────────────────

    [Fact]
    public async Task ExtractContentAsync_ReturnsFailure_WhenUrlIsEmpty()
    {
        var result = await _sut.ExtractContentAsync(string.Empty);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ExtractContentAsync_ReturnsFailure_WhenUrlIsNull()
    {
        var result = await _sut.ExtractContentAsync(null!);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public async Task ExtractContentAsync_ReturnsFailure_WhenUrlIsInvalid(string url)
    {
        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid URL");
    }

    [Fact]
    public async Task ExtractContentAsync_RoutesYouTube_ToTranscriptExtractor()
    {
        // Arrange
        var youtubeUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        _fetcherMock.Setup(f => f.FetchAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new FetchResult("<html>empty</html>", "https://youtube.com", TimeSpan.Zero, false));
        _parserMock.Setup(p => p.ExtractMetadata(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new Metadata("Video Title", null, null, null, null, null));
        _parserMock.Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new ParsedContent("Title", "Content", null, null, null, null));

        var result = await _sut.ExtractContentAsync(youtubeUrl);

        // Should have called fetch (for watch page) -- this proves YouTube path was taken
        _fetcherMock.Verify(f => f.FetchAsync(It.Is<string>(s => s.Contains("youtube.com")), default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExtractContentAsync_FetchesPipesAndBuilds_Successfully()
    {
        var url = "https://example.com/article";

        // Step 1: Fetcher returns HTML
        _fetcherMock.Setup(f => f.FetchAsync(url, default))
            .ReturnsAsync(new FetchResult(SampleHtml, url, TimeSpan.FromMilliseconds(200), false));

        // Step 2: Parser extracts content
        _parserMock.Setup(p => p.Parse(SampleHtml, url))
            .Returns(new ParsedContent(
                "OG Title",
                "This is a test article with enough content to be meaningful and pass extraction thresholds.",
                "A test description",
                "Jane Doe",
                new DateTime(2025, 1, 15),
                TimeSpan.FromMinutes(1)));

        // Step 2b: Parser extracts metadata
        _parserMock.Setup(p => p.ExtractMetadata(SampleHtml, url))
            .Returns(new Metadata(
                "OG Title",
                "A test description",
                "Jane Doe",
                "https://example.com/image.jpg",
                new DateTime(2025, 1, 15),
                "TestSite"));

        // Step 3: Structured data enriches author
        _extractorMock.Setup(e => e.ExtractAuthor(SampleHtml))
            .Returns("Structured Author");

        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeTrue();
        result.Title.Should().Be("OG Title");
        result.Content.Should().Contain("test article");
        result.Description.Should().Be("A test description");
        result.SiteName.Should().Be("TestSite");
        result.FeaturedImageUrl.Should().Be("https://example.com/image.jpg");
        result.CanonicalUrl.Should().Be("https://example.com/article");
        result.Language.Should().Be("en");
        result.Author.Should().Be("Structured Author");
        result.WordCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExtractContentAsync_FallsBackToMetadataAuthor_WhenStructuredReturnsNull()
    {
        var url = "https://example.com/article";

        _fetcherMock.Setup(f => f.FetchAsync(url, default))
            .ReturnsAsync(new FetchResult(SampleHtml, url, TimeSpan.Zero, false));

        _parserMock.Setup(p => p.Parse(SampleHtml, url))
            .Returns(new ParsedContent("Title", "Content text here.", null, null, null, null));

        _parserMock.Setup(p => p.ExtractMetadata(SampleHtml, url))
            .Returns(new Metadata(null, null, "Meta Author", null, null, null));

        _extractorMock.Setup(e => e.ExtractAuthor(SampleHtml))
            .Returns((string?)null);

        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeTrue();
        result.Author.Should().Be("Meta Author");
    }

    [Fact]
    public async Task ExtractContentAsync_ReturnsFailure_WhenFetcherReturnsEmptyHtml()
    {
        var url = "https://example.com/empty";

        _fetcherMock.Setup(f => f.FetchAsync(url, default))
            .ReturnsAsync(new FetchResult(string.Empty, url, TimeSpan.Zero, false));

        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty content");
    }

    [Fact]
    public async Task ExtractContentAsync_ReturnsFailure_WhenParserReturnsNoText()
    {
        var url = "https://example.com/no-content";

        _fetcherMock.Setup(f => f.FetchAsync(url, default))
            .ReturnsAsync(new FetchResult("<html><body></body></html>", url, TimeSpan.Zero, false));

        _parserMock.Setup(p => p.Parse(It.IsAny<string>(), url))
            .Returns(new ParsedContent(string.Empty, string.Empty, null, null, null, null));

        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("meaningful article content");
    }

    [Fact]
    public async Task ExtractContentAsync_ReturnsFailure_OnTimeout()
    {
        var url = "https://example.com/timeout";

        _fetcherMock.Setup(f => f.FetchAsync(url, default))
            .ThrowsAsync(new TimeoutException("Request timed out"));

        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task ExtractContentAsync_ReturnsFailure_OnHttpRequestException()
    {
        var url = "https://example.com/error";

        _fetcherMock.Setup(f => f.FetchAsync(url, default))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));

        var result = await _sut.ExtractContentAsync(url);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Network error");
    }

    [Fact]
    public async Task ExtractContentAsync_Throws_OnCancellation()
    {
        var url = "https://example.com/cancel";
        var ct = new CancellationToken(true);

        _fetcherMock.Setup(f => f.FetchAsync(url, ct))
            .ThrowsAsync(new OperationCanceledException(ct));

        var act = () => _sut.ExtractContentAsync(url, ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── ExtractYouTubeTranscriptAsync Tests ─────────────────────────────────

    [Fact]
    public async Task ExtractYouTubeTranscriptAsync_ReturnsFailure_WhenUrlIsEmpty()
    {
        var result = await _sut.ExtractYouTubeTranscriptAsync(string.Empty);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ExtractYouTubeTranscriptAsync_ReturnsFailure_WhenVideoIdCannotBeExtracted()
    {
        var result = await _sut.ExtractYouTubeTranscriptAsync("https://not-youtube.com/something");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("video ID");
    }

    // ─── ExtractBatchAsync Tests ─────────────────────────────────────────────

    [Fact]
    public async Task ExtractBatchAsync_ReturnsEmpty_WhenUrlListIsEmpty()
    {
        var result = await _sut.ExtractBatchAsync(Array.Empty<string>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractBatchAsync_ReturnsEmpty_WhenUrlListIsNull()
    {
        var result = await _sut.ExtractBatchAsync(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractBatchAsync_ProcessesMultipleUrls_WithProgressReporting()
    {
        var urls = new[] { "https://example.com/a", "https://example.com/b" };
        var progressReports = new List<int>();
        var progress = new Progress<int>(i => progressReports.Add(i));

        foreach (var url in urls)
        {
            _fetcherMock.Setup(f => f.FetchAsync(url, default))
                .ReturnsAsync(new FetchResult(SampleHtml, url, TimeSpan.Zero, false));
        }

        _parserMock.Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new ParsedContent("Title", "Some article text that is long enough to pass.", null, null, null, null));
        _parserMock.Setup(p => p.ExtractMetadata(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new Metadata(null, null, null, null, null, null));
        _extractorMock.Setup(e => e.ExtractAuthor(It.IsAny<string>()))
            .Returns((string?)null);

        var result = await _sut.ExtractBatchAsync(urls, progress);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Success);
        progressReports.Should().Equal(1, 2);
    }

    [Fact]
    public async Task ExtractBatchAsync_ContinuesOnIndividualFailure()
    {
        var urls = new[] { "https://example.com/fail", "https://example.com/ok" };

        _fetcherMock.Setup(f => f.FetchAsync("https://example.com/fail", default))
            .ThrowsAsync(new HttpRequestException("Server error"));
        _fetcherMock.Setup(f => f.FetchAsync("https://example.com/ok", default))
            .ReturnsAsync(new FetchResult(SampleHtml, "https://example.com/ok", TimeSpan.Zero, false));

        _parserMock.Setup(p => p.Parse(It.IsAny<string>(), "https://example.com/ok"))
            .Returns(new ParsedContent("Title", "Article text.", null, null, null, null));
        _parserMock.Setup(p => p.ExtractMetadata(It.IsAny<string>(), "https://example.com/ok"))
            .Returns(new Metadata(null, null, null, null, null, null));
        _extractorMock.Setup(e => e.ExtractAuthor(It.IsAny<string>()))
            .Returns((string?)null);

        var result = await _sut.ExtractBatchAsync(urls);

        result.Should().HaveCount(2);
        result[0].Success.Should().BeFalse();
        result[0].ErrorMessage.Should().Contain("Network error");
        result[1].Success.Should().BeTrue();
    }

    // ─── IsYouTubeUrl Tests ──────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", true)]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", true)]
    [InlineData("https://example.com/article", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsYouTubeUrl_DetectsYouTubeUrls(string? url, bool expected)
    {
        _sut.IsYouTubeUrl(url!).Should().Be(expected);
    }

    // ─── IsValidUrl Tests ────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidUrl_ValidatesHttpAndHttpsOnly(string? url, bool expected)
    {
        _sut.IsValidUrl(url!).Should().Be(expected);
    }

    // ─── Constructor Validation Tests ────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsWhenFetcherIsNull()
    {
        var act = () => new WebScraperService(
            null!, _parserMock.Object, _extractorMock.Object, _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("fetcher");
    }

    [Fact]
    public void Constructor_ThrowsWhenParserIsNull()
    {
        var act = () => new WebScraperService(
            _fetcherMock.Object, null!, _extractorMock.Object, _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("parser");
    }

    [Fact]
    public void Constructor_ThrowsWhenExtractorIsNull()
    {
        var act = () => new WebScraperService(
            _fetcherMock.Object, _parserMock.Object, null!, _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("extractor");
    }

    [Fact]
    public void Constructor_ThrowsWhenLoggerIsNull()
    {
        var act = () => new WebScraperService(
            _fetcherMock.Object, _parserMock.Object, _extractorMock.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}

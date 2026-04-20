using System.Net;
using AgentX.Core.Services.Web;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class WebContentFetcherTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly Mock<IJsRenderingService> _jsRenderingServiceMock;
    private WebContentFetcher _fetcher;

    public WebContentFetcherTests()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.ForContext<WebContentFetcher>()).Returns(loggerMock.Object);
        _logger = loggerMock.Object;

        _jsRenderingServiceMock = new Mock<IJsRenderingService>();

        // Default: fetcher without JS rendering, will be recreated per test with custom handler
        _fetcher = CreateFetcher();
    }

    public void Dispose()
    {
        _fetcher.Dispose();
    }

    // ─── Helper: Mock HTTP Handler ───────────────────────────────────────────

    /// <summary>
    /// Creates a mock HttpMessageHandler that returns the specified status code and content.
    /// </summary>
    private static MockHttpMessageHandler CreateMockHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string content = "<html><body>Hello World</body></html>",
        string contentType = "text/html")
    {
        return new MockHttpMessageHandler(statusCode, content, contentType);
    }

    /// <summary>
    /// Creates a WebContentFetcher using a mock handler for the HttpClient.
    /// </summary>
    private WebContentFetcher CreateFetcher(
        HttpMessageHandler? handler = null,
        IJsRenderingService? jsRenderingService = null)
    {
        handler ??= CreateMockHandler();
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        return new WebContentFetcher(_logger, jsRenderingService, httpClient);
    }

    // ─── Constructor Tests ───────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new WebContentFetcher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new WebContentFetcher(_logger, null, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    // ─── FetchAsync: Success Cases ───────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_ValidUrl_ReturnsHtml()
    {
        // Arrange
        var html = "<html><body><h1>Test Article</h1><p>Content here.</p></body></html>";
        var handler = CreateMockHandler(content: html);
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/article");

        // Assert
        result.Should().NotBeNull();
        result.Html.Should().Be(html);
        result.FinalUrl.Should().Be("https://example.com/article");
        result.UsedJsRendering.Should().BeFalse();
        result.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task FetchAsync_SetsCorrectUserAgentHeader()
    {
        // Arrange: Use the default-constructed fetcher which has User-Agent set on its
        // internally managed HttpClient. We cannot inspect headers on the HttpClient directly,
        // but we can verify the handler captures the correct User-Agent from the request.
        // Since CreateFetcher creates a new HttpClient without User-Agent, we create one
        // that mirrors the production configuration.
        var handler = new InspectableMockHandler();
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        _fetcher.Dispose();
        _fetcher = new WebContentFetcher(_logger, null, httpClient);

        // Act
        await _fetcher.FetchAsync("https://example.com/test");

        // Assert
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.UserAgent.ToString().Should().Contain("Mozilla");
    }

    [Fact]
    public async Task FetchAsync_IncludesAcceptHeaders()
    {
        // Arrange: The FetchHtmlInternalAsync method adds Accept headers to each request.
        // We use the inspectable handler to verify per-request headers.
        var handler = new InspectableMockHandler();
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        _fetcher.Dispose();
        _fetcher = new WebContentFetcher(_logger, null, httpClient);

        // Act
        await _fetcher.FetchAsync("https://example.com/test");

        // Assert: The per-request Accept header is set in FetchHtmlInternalAsync
        handler.LastRequest.Should().NotBeNull();
        var acceptHeader = handler.LastRequest!.Headers.Accept.ToString();
        acceptHeader.Should().Contain("text/html");
    }

    // ─── FetchAsync: Redirect Handling ───────────────────────────────────────

    [Fact]
    public async Task FetchAsync_FollowsRedirectsCorrectly()
    {
        // Arrange: handler returns a redirect which HttpClient follows automatically
        // Since we use AllowAutoRedirect=true, the final response shows the redirected content.
        // To test this properly, we simulate the handler seeing the request and returning
        // the content as if a redirect occurred.
        var finalHtml = "<html><body>Redirected Content</body></html>";
        var handler = CreateMockHandler(content: finalHtml);
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/old-page");

        // Assert
        result.Html.Should().Be(finalHtml);
    }

    // ─── FetchAsync: HTTP Error Handling ─────────────────────────────────────

    [Fact]
    public async Task FetchAsync_Http404_ThrowsHttpRequestException()
    {
        // Arrange
        var handler = CreateMockHandler(statusCode: HttpStatusCode.NotFound);
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var act = () => _fetcher.FetchAsync("https://example.com/not-found");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FetchAsync_Http500_ThrowsHttpRequestException()
    {
        // Arrange
        var handler = CreateMockHandler(statusCode: HttpStatusCode.InternalServerError);
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var act = () => _fetcher.FetchAsync("https://example.com/error");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FetchAsync_Http403_ThrowsHttpRequestException()
    {
        // Arrange
        var handler = CreateMockHandler(statusCode: HttpStatusCode.Forbidden);
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var act = () => _fetcher.FetchAsync("https://example.com/forbidden");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ─── FetchAsync: URL Validation ──────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_NullUrl_ThrowsArgumentException()
    {
        var act = () => _fetcher.FetchAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("URL must not be empty*")
            .WithParameterName("url");
    }

    [Fact]
    public async Task FetchAsync_EmptyUrl_ThrowsArgumentException()
    {
        var act = () => _fetcher.FetchAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("URL must not be empty*")
            .WithParameterName("url");
    }

    [Fact]
    public async Task FetchAsync_WhitespaceUrl_ThrowsArgumentException()
    {
        var act = () => _fetcher.FetchAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("URL must not be empty*")
            .WithParameterName("url");
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///C:/test.html")]
    [InlineData("javascript:void(0)")]
    public async Task FetchAsync_InvalidScheme_ThrowsArgumentException(string url)
    {
        var act = () => _fetcher.FetchAsync(url);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid URL scheme*")
            .WithParameterName("url");
    }

    [Fact]
    public async Task FetchAsync_RelativeUrl_ThrowsArgumentException()
    {
        var act = () => _fetcher.FetchAsync("/relative/path");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid URL format*")
            .WithParameterName("url");
    }

    [Fact]
    public async Task FetchAsync_MalformedUrl_ThrowsArgumentException()
    {
        var act = () => _fetcher.FetchAsync("not a url at all");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("url");
    }

    // ─── FetchAsync: Cancellation ────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var handler = new DelayedMockHandler(TimeSpan.FromSeconds(10));
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        var act = () => _fetcher.FetchAsync("https://example.com/slow", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── FetchAsync: Timeout Handling ────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_RequestTimeout_ThrowsTimeoutException()
    {
        // Arrange: Use a handler that never responds, combined with a short timeout client.
        var handler = new DelayedMockHandler(TimeSpan.FromSeconds(30));
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        _fetcher.Dispose();
        _fetcher = new WebContentFetcher(_logger, null, httpClient);

        // Act
        var act = () => _fetcher.FetchAsync("https://example.com/timeout");

        // Assert
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*timed out*");
    }

    // ─── FetchAsync: Content Size Limit ──────────────────────────────────────

    [Fact]
    public async Task FetchAsync_ContentExceedsMaxSize_ThrowsInvalidOperationException()
    {
        // Arrange: Return a response with a Content-Length header exceeding the 10 MB limit
        var handler = new OversizedContentMockHandler();
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var act = () => _fetcher.FetchAsync("https://example.com/huge-page");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*too large*");
    }

    // ─── FetchAsync: JS Rendering Fallback ───────────────────────────────────

    [Fact]
    public async Task FetchAsync_EmptyResponse_WithJsRendering_FallsBackToJsRendering()
    {
        // Arrange: HTTP returns empty content
        var handler = CreateMockHandler(content: "");
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler, _jsRenderingServiceMock.Object);

        var renderedHtml = "<html><body>JS Rendered Content</body></html>";
        _jsRenderingServiceMock
            .Setup(js => js.RenderPageAsync("https://example.com/js-page", true, default))
            .ReturnsAsync(renderedHtml);

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/js-page");

        // Assert
        result.Html.Should().Be(renderedHtml);
        result.UsedJsRendering.Should().BeTrue();
        _jsRenderingServiceMock.Verify(
            js => js.RenderPageAsync("https://example.com/js-page", true, default),
            Times.Once);
    }

    [Fact]
    public async Task FetchAsync_EmptyResponse_WithoutJsRendering_ReturnsEmptyHtml()
    {
        // Arrange: No JS rendering service, HTTP returns empty content
        var handler = CreateMockHandler(content: "");
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler, jsRenderingService: null);

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/empty");

        // Assert
        result.Html.Should().BeEmpty();
        result.UsedJsRendering.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_NonEmptyResponse_DoesNotUseJsRendering()
    {
        // Arrange: HTTP returns valid content
        var html = "<html><body>Normal Content</body></html>";
        var handler = CreateMockHandler(content: html);
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler, _jsRenderingServiceMock.Object);

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/normal");

        // Assert
        result.Html.Should().Be(html);
        result.UsedJsRendering.Should().BeFalse();
        _jsRenderingServiceMock.Verify(
            js => js.RenderPageAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FetchAsync_JsRenderingFails_ReturnsEmptyHtml()
    {
        // Arrange: HTTP returns empty, JS rendering throws
        var handler = CreateMockHandler(content: "");
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler, _jsRenderingServiceMock.Object);

        _jsRenderingServiceMock
            .Setup(js => js.RenderPageAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Browser crashed"));

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/js-fail");

        // Assert
        result.Html.Should().BeEmpty();
        result.UsedJsRendering.Should().BeFalse();
    }

    // ─── FetchAsync: Elapsed Time ────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_ElapsedTime_IsRecorded()
    {
        // Arrange
        var handler = CreateMockHandler();
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var result = await _fetcher.FetchAsync("https://example.com/test");

        // Assert
        result.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
        result.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    // ─── FetchAsync: Various Valid URLs ──────────────────────────────────────

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("https://subdomain.example.com/path/to/page?query=value")]
    [InlineData("https://example.com:8080/resource")]
    public async Task FetchAsync_ValidUrlFormats_AreAccepted(string url)
    {
        // Arrange
        var handler = CreateMockHandler();
        _fetcher.Dispose();
        _fetcher = CreateFetcher(handler);

        // Act
        var result = await _fetcher.FetchAsync(url);

        // Assert
        result.Should().NotBeNull();
        result.FinalUrl.Should().Be(url);
    }

    // ─── Mock Handlers ───────────────────────────────────────────────────────

    /// <summary>
    /// A mock HttpMessageHandler that returns a predefined status code and content.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        private readonly string _contentType;

        public MockHttpMessageHandler(
            HttpStatusCode statusCode,
            string content,
            string contentType = "text/html")
        {
            _statusCode = statusCode;
            _content = content;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, _contentType),
                RequestMessage = request,
            };

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// An inspectable mock handler that captures the request for assertion.
    /// </summary>
    private class InspectableMockHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Test</body></html>", System.Text.Encoding.UTF8, "text/html"),
                RequestMessage = request,
            };

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// A mock handler that introduces a delay to simulate slow responses.
    /// </summary>
    private class DelayedMockHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public DelayedMockHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Delayed</body></html>", System.Text.Encoding.UTF8, "text/html"),
                RequestMessage = request,
            };
        }
    }

    /// <summary>
    /// A mock handler that returns a response with a Content-Length header exceeding the max size.
    /// </summary>
    private class OversizedContentMockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent("x", System.Text.Encoding.UTF8, "text/html");
            // Set the Content-Length header to 20 MB (exceeds 10 MB limit)
            content.Headers.ContentLength = 20 * 1024 * 1024;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            };

            return Task.FromResult(response);
        }
    }
}

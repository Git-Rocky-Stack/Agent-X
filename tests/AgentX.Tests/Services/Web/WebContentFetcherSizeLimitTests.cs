using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AgentX.Core.Services.Web;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Web;

/// <summary>
/// Tests for <see cref="WebContentFetcher"/>'s content size cap, ensuring the 10 MB
/// limit is enforced even when the server omits or misreports <c>Content-Length</c>.
/// </summary>
public sealed class WebContentFetcherSizeLimitTests
{
    private const string TestUrl = "https://example.com/page";

    private static WebContentFetcher CreateFetcher(HttpResponseMessage response)
    {
        var handler = new StubHandler(_ => response);
        var client = new HttpClient(handler);
        // jsRenderingService is null so an empty/oversize fetch never falls back.
        return new WebContentFetcher(new LoggerConfiguration().CreateLogger(), null, client);
    }

    [Fact]
    public async Task FetchAsync_RejectsOversizeBody_WhenContentLengthMissing()
    {
        // Arrange: body is over the cap, and the server advertises NO Content-Length.
        var payload = new byte[WebContentFetcher.MaxContentLengthBytes + 1];
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new SizedContent(payload, advertiseLength: false),
        };
        var fetcher = CreateFetcher(response);

        // Act
        Func<Task> act = () => fetcher.FetchAsync(TestUrl);

        // Assert: the streaming guard must trip even without a Content-Length header.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*maximum allowed size*");
    }

    [Fact]
    public async Task FetchAsync_RejectsOversize_WhenContentLengthDeclared()
    {
        // Arrange: a small body but a declared Content-Length above the cap.
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new SizedContent(
                new byte[16],
                advertiseLength: true,
                declaredLength: WebContentFetcher.MaxContentLengthBytes + 1),
        };
        var fetcher = CreateFetcher(response);

        // Act
        Func<Task> act = () => fetcher.FetchAsync(TestUrl);

        // Assert: rejected up-front, before the body is read.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*too large*");
    }

    [Fact]
    public async Task FetchAsync_ReturnsContent_WhenUnderCap_WithContentLength()
    {
        // Arrange
        var html = "<html><body>hello world</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new SizedContent(bytes, advertiseLength: true),
        };
        var fetcher = CreateFetcher(response);

        // Act
        var result = await fetcher.FetchAsync(TestUrl);

        // Assert
        result.Html.Should().Be(html);
    }

    [Fact]
    public async Task FetchAsync_ReturnsContent_WhenUnderCap_WithoutContentLength()
    {
        // Arrange: chunked-style response (no Content-Length) that is within the cap.
        var html = "<html><body>streamed under cap</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new SizedContent(bytes, advertiseLength: false),
        };
        var fetcher = CreateFetcher(response);

        // Act
        var result = await fetcher.FetchAsync(TestUrl);

        // Assert
        result.Html.Should().Be(html);
    }

    // ─── Test doubles ────────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    /// <summary>
    /// HttpContent that writes a fixed payload but can independently control whether a
    /// <c>Content-Length</c> header is advertised and what value it reports — letting us
    /// simulate honest, missing, and dishonest length headers.
    /// </summary>
    private sealed class SizedContent : HttpContent
    {
        private readonly byte[] _payload;
        private readonly bool _advertiseLength;
        private readonly long? _declaredLength;

        public SizedContent(byte[] payload, bool advertiseLength, long? declaredLength = null)
        {
            _payload = payload;
            _advertiseLength = advertiseLength;
            _declaredLength = declaredLength;
            Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_payload, 0, _payload.Length);

        protected override bool TryComputeLength(out long length)
        {
            if (_advertiseLength)
            {
                length = _declaredLength ?? _payload.Length;
                return true;
            }

            length = 0;
            return false;
        }
    }
}

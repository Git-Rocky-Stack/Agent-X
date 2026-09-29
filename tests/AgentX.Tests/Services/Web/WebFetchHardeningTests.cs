using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AgentX.Core.Services.Web;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services.Web;

/// <summary>
/// Redirect handling and final URLs, charset detection, the JS-rendering trigger, and the
/// size, cycle and cancellation limits of the feed and sitemap readers.
/// </summary>
public sealed class WebFetchHardeningTests
{
    static WebFetchHardeningTests()
    {
        // Lets the tests encode their fixtures in legacy code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ---- Redirects and the final URL ----

    [Fact]
    public async Task FetchAsync_reports_the_url_a_redirect_ended_at()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/old" => Redirect(HttpStatusCode.MovedPermanently, "https://example.com/new"),
            "/new" => Html("<html><body>New home</body></html>"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        var result = await fetcher.FetchAsync("https://example.com/old");

        result.FinalUrl.Should().Be("https://example.com/new");
        result.Html.Should().Contain("New home");
    }

    [Fact]
    public async Task FetchAsync_resolves_a_relative_redirect_against_the_current_url()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/a/start" => Redirect(HttpStatusCode.Found, "../moved?x=1"),
            "/moved" => Html("<html><body>Moved</body></html>"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        var result = await fetcher.FetchAsync("https://example.com/a/start");

        result.FinalUrl.Should().Be("https://example.com/moved?x=1");
    }

    [Fact]
    public async Task FetchAsync_gives_up_after_too_many_redirects()
    {
        var hops = 0;
        var handler = new RoutingHandler(_ => Redirect(HttpStatusCode.Found, $"https://example.com/hop{++hops}"));
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        var act = () => fetcher.FetchAsync("https://example.com/start");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*Too many redirects*");
        handler.Requests.Should().HaveCount(WebHttp.MaxRedirects + 1);
    }

    [Fact]
    public async Task FetchAsync_refuses_a_redirect_to_a_non_http_scheme()
    {
        var handler = new RoutingHandler(_ => Redirect(HttpStatusCode.Found, "file:///etc/passwd"));
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        var act = () => fetcher.FetchAsync("https://example.com/start");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*only HTTP and HTTPS*");
    }

    // ---- Charset detection ----

    [Fact]
    public async Task FetchAsync_decodes_with_the_charset_of_the_content_type_header()
    {
        var html = "<html><body><p>\u041F\u0440\u0438\u0432\u0435\u0442, \u043C\u0438\u0440</p></body></html>";
        var handler = new RoutingHandler(_ => Bytes(Encoding.GetEncoding(1251).GetBytes(html), "text/html; charset=windows-1251"));
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        (await fetcher.FetchAsync("https://example.com/ru")).Html.Should().Contain("\u041F\u0440\u0438\u0432\u0435\u0442, \u043C\u0438\u0440");
    }

    [Fact]
    public async Task FetchAsync_decodes_with_a_meta_charset_when_the_header_names_none()
    {
        var html = "<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=shift_jis\"></head><body><p>\u3053\u3093\u306B\u3061\u306F</p></body></html>";
        var handler = new RoutingHandler(_ => Bytes(Encoding.GetEncoding("shift_jis").GetBytes(html), "text/html"));
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        (await fetcher.FetchAsync("https://example.com/jp")).Html.Should().Contain("\u3053\u3093\u306B\u3061\u306F");
    }

    [Fact]
    public void DecodeText_prefers_the_byte_order_mark_and_drops_it_from_the_text()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("<p>\u00E9</p>")).ToArray();

        WebHttp.DecodeText(bytes, "windows-1252").Should().Be("<p>\u00E9</p>");
    }

    [Fact]
    public void DecodeText_reads_a_latin1_label_as_windows_1252()
    {
        // 0x93/0x94 are curly quotes in windows-1252 and control characters in strict Latin-1.
        var bytes = new byte[] { 0x93, (byte)'h', (byte)'i', 0x94 };

        WebHttp.DecodeText(bytes, "ISO-8859-1").Should().Be("\u201Chi\u201D");
    }

    [Fact]
    public void DecodeText_uses_the_encoding_of_an_xml_declaration()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><rss><channel><title>Caf\u00E9 \u2013 news</title></channel></rss>";

        WebHttp.DecodeText(Encoding.GetEncoding(1252).GetBytes(xml), null).Should().Be(xml);
    }

    // ---- When to render with JavaScript ----

    [Theory]
    [InlineData("", true)]
    [InlineData("<html><head><script src=\"/app.js\"></script></head><body><div id=\"root\"></div><noscript>You need to enable JavaScript to run this app.</noscript></body></html>", true)]
    [InlineData("<html><body><p>Static page with no scripts at all.</p></body></html>", false)]
    public void NeedsJsRendering_detects_empty_pages_and_script_shells(string html, bool expected)
    {
        WebContentFetcher.NeedsJsRendering(html).Should().Be(expected);
    }

    [Fact]
    public void NeedsJsRendering_is_false_for_a_script_page_that_already_has_its_text()
    {
        var html = "<html><body><script>track()</script><article>" + string.Join(" ", Enumerable.Repeat("Readable words.", 40)) + "</article></body></html>";

        WebContentFetcher.NeedsJsRendering(html).Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_renders_a_script_shell_with_the_browser()
    {
        const string shell = "<html><head><script src=\"/bundle.js\"></script></head><body><div id=\"app\"></div></body></html>";
        var handler = new RoutingHandler(_ => Html(shell));
        var renderer = new Mock<IJsRenderingService>();
        renderer.Setup(r => r.RenderPageAsync("https://example.com/spa", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div id=\"app\">Rendered text</div></body></html>");
        using var fetcher = new WebContentFetcher(Logger.None, renderer.Object, new HttpClient(handler));

        var result = await fetcher.FetchAsync("https://example.com/spa");

        result.UsedJsRendering.Should().BeTrue();
        result.Html.Should().Contain("Rendered text");
    }

    [Fact]
    public async Task RenderPageAsync_honors_a_cancelled_token_before_starting_a_browser()
    {
        using var service = new JsRenderingService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.RenderPageAsync("https://example.com/", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- Feed reader limits ----

    [Fact]
    public async Task ParseFeedAsync_rejects_a_feed_larger_than_the_cap_even_without_content_length()
    {
        var handler = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new EndlessContent(FeedService.MaxFeedBytes + 1),
        });
        var service = new FeedService(Logger.None, new HttpClient(handler));

        var act = () => service.ParseFeedAsync("https://example.com/feed.xml");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*maximum allowed size*");
    }

    [Fact]
    public async Task ParseFeedAsync_decodes_a_feed_in_its_declared_legacy_encoding()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><rss version=\"2.0\"><channel><title>Caf\u00E9 \u2013 news</title><link>https://example.com/</link></channel></rss>";
        var handler = new RoutingHandler(_ => Bytes(Encoding.GetEncoding(1252).GetBytes(xml), "application/rss+xml"));
        var service = new FeedService(Logger.None, new HttpClient(handler));

        var feed = await service.ParseFeedAsync("https://example.com/feed.xml");

        feed.Title.Should().Be("Caf\u00E9 \u2013 news");
    }

    // ---- Sitemap cycles and cancellation ----

    [Fact]
    public async Task ParseSitemapAsync_reads_each_sitemap_once_when_indexes_list_each_other()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/index-a.xml" => Xml(Index("https://example.com/index-a.xml", "https://example.com/index-b.xml", "https://example.com/pages.xml")),
            "/index-b.xml" => Xml(Index("https://example.com/index-a.xml", "https://example.com/pages.xml")),
            "/pages.xml" => Xml(Urlset("https://example.com/1", "https://example.com/2")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var parser = new SitemapParser(Logger.None, new HttpClient(handler));

        var urls = await parser.ParseSitemapAsync("https://example.com/index-a.xml");

        urls.Should().Equal("https://example.com/1", "https://example.com/2");
        handler.Requests.GroupBy(u => u).Should().OnlyContain(g => g.Count() == 1,
            "a sitemap listed again by another index must not be fetched again");
    }

    [Fact]
    public async Task ParseSitemapAsync_propagates_cancellation_instead_of_returning_partial_results()
    {
        using var cts = new CancellationTokenSource();
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/index.xml")
            {
                return Xml(Index("https://example.com/first.xml", "https://example.com/second.xml"));
            }

            if (request.RequestUri.AbsolutePath == "/first.xml")
            {
                return Xml(Urlset("https://example.com/1"));
            }

            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var parser = new SitemapParser(Logger.None, new HttpClient(handler));

        var act = () => parser.ParseSitemapAsync("https://example.com/index.xml", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- Helpers ----

    private static string Index(params string[] locations) =>
        "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + string.Concat(locations.Select(l => $"<sitemap><loc>{l}</loc></sitemap>"))
        + "</sitemapindex>";

    private static string Urlset(params string[] locations) =>
        "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + string.Concat(locations.Select(l => $"<url><loc>{l}</loc></url>"))
        + "</urlset>";

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    private static HttpResponseMessage Bytes(byte[] body, string contentType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>Answers each request from a routing function and records the URLs requested.</summary>
    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            var response = route(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Streams the given number of bytes without announcing a Content-Length.</summary>
    private sealed class EndlessContent(long length) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)' ');
            for (long written = 0; written < length; written += chunk.Length)
            {
                await stream.WriteAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, length - written)));
            }
        }

        protected override bool TryComputeLength(out long computed)
        {
            computed = 0;
            return false;
        }
    }
}

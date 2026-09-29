using System.Net;
using System.Net.Sockets;
using System.Text;
using AgentX.Core.Services.Web;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services.Web;

/// <summary>
/// The server-side request forgery guard: which addresses count as private or local, and how
/// redirects, sitemap children and fetches apply the "same network zone as the source" rule.
/// Hosts are IP literals or localhost so no test depends on DNS.
/// </summary>
public sealed class PrivateNetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.251")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:c0a8:101::1")]
    public void IsPrivateOrLocal_flags_local_and_private_addresses(string address)
    {
        PrivateNetworkGuard.IsPrivateOrLocal(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    public void IsPrivateOrLocal_allows_public_addresses(string address)
    {
        PrivateNetworkGuard.IsPrivateOrLocal(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://localhost:8080/", true)]
    [InlineData("http://LOCALHOST./", true)]
    [InlineData("http://api.localhost/", true)]
    [InlineData("http://printer.local/", true)]
    [InlineData("http://[::1]/", true)]
    [InlineData("http://2130706433/", true)]
    [InlineData("https://93.184.216.34/", false)]
    public async Task IsPrivateOrLocalHostAsync_decides_names_and_literals_without_dns(string url, bool expected)
    {
        var resolved = false;

        var result = await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(
            new Uri(url),
            (_, _) => { resolved = true; return Task.FromResult(Array.Empty<IPAddress>()); },
            CancellationToken.None);

        result.Should().Be(expected);
        resolved.Should().BeFalse();
    }

    [Fact]
    public async Task IsPrivateOrLocalHostAsync_flags_a_name_when_any_resolved_address_is_private()
    {
        var result = await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(
            new Uri("https://rebind.example/"),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.7") }),
            CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPrivateOrLocalHostAsync_does_not_flag_a_name_that_does_not_resolve()
    {
        var result = await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(
            new Uri("https://no-such-host.example/"),
            (_, _) => Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)),
            CancellationToken.None);

        result.Should().BeFalse();
    }

    // ---- Redirects ----

    [Fact]
    public async Task FetchAsync_blocks_a_public_page_redirecting_to_a_local_address()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "93.184.216.34"
            ? Redirect("http://127.0.0.1:8080/admin")
            : Html("<html><body>local admin</body></html>"));
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        var act = () => fetcher.FetchAsync("https://93.184.216.34/article");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*Blocked a redirect*");
        handler.Requests.Should().NotContain(u => u.Contains("127.0.0.1"), "the local address must never be requested");
    }

    [Fact]
    public async Task FetchAsync_lets_an_intranet_page_redirect_within_the_private_network()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "localhost"
            ? Redirect("http://192.168.1.10/wiki/home")
            : Html("<html><body>intranet wiki</body></html>"));
        using var fetcher = new WebContentFetcher(Logger.None, null, new HttpClient(handler));

        var result = await fetcher.FetchAsync("http://localhost/wiki");

        result.FinalUrl.Should().Be("http://192.168.1.10/wiki/home");
        result.Html.Should().Contain("intranet wiki");
    }

    [Fact]
    public async Task ParseFeedAsync_blocks_a_public_feed_redirecting_to_a_local_address()
    {
        var handler = new RecordingHandler(_ => Redirect("http://10.0.0.1/feed"));
        var service = new FeedService(Logger.None, new HttpClient(handler));

        var act = () => service.ParseFeedAsync("https://93.184.216.34/feed.xml");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*Blocked a redirect*");
        handler.Requests.Should().ContainSingle();
    }

    // ---- Sitemap children ----

    [Fact]
    public async Task ParseSitemapAsync_skips_child_sitemaps_on_local_addresses_listed_by_a_public_sitemap()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/sitemap.xml" => Xml(
                "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "<sitemap><loc>http://127.0.0.1/internal.xml</loc></sitemap>"
                + "<sitemap><loc>https://93.184.216.34/pages.xml</loc></sitemap>"
                + "</sitemapindex>"),
            "/pages.xml" => Xml(
                "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://93.184.216.34/a</loc></url></urlset>"),
            _ => Xml("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>http://127.0.0.1/secret</loc></url></urlset>"),
        });
        var parser = new SitemapParser(Logger.None, new HttpClient(handler));

        var urls = await parser.ParseSitemapAsync("https://93.184.216.34/sitemap.xml");

        urls.Should().Equal("https://93.184.216.34/a");
        handler.Requests.Should().NotContain(u => u.Contains("127.0.0.1"));
    }

    [Fact]
    public async Task ParseSitemapAsync_reads_private_child_sitemaps_of_an_intranet_sitemap()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/sitemap.xml"
            ? Xml("<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><sitemap><loc>http://192.168.1.20/pages.xml</loc></sitemap></sitemapindex>")
            : Xml("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>http://192.168.1.20/doc</loc></url></urlset>"));
        var parser = new SitemapParser(Logger.None, new HttpClient(handler));

        var urls = await parser.ParseSitemapAsync("http://localhost/sitemap.xml");

        urls.Should().Equal("http://192.168.1.20/doc");
    }

    // ---- Helpers ----

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
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
}

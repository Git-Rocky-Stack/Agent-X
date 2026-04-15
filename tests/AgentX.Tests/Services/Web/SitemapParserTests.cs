using AgentX.Core.Services.Web;
using FluentAssertions;
using Moq;
using Serilog;
using System.Xml.Linq;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class SitemapParserTests
{
    private readonly ILogger _logger;

    public SitemapParserTests()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.ForContext<SitemapParser>()).Returns(loggerMock.Object);
        _logger = loggerMock.Object;
    }

    // ─── Sample Sitemap XML ─────────────────────────────────────────────────

    private const string SampleRegularSitemapXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url>
    <loc>https://example.com/</loc>
  </url>
  <url>
    <loc>https://example.com/about</loc>
  </url>
  <url>
    <loc>https://example.com/contact</loc>
  </url>
  <url>
    <loc>https://example.com/blog</loc>
  </url>
</urlset>";

    private const string SampleSitemapIndexXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <sitemap>
    <loc>https://example.com/sitemap-posts.xml</loc>
  </sitemap>
  <sitemap>
    <loc>https://example.com/sitemap-pages.xml</loc>
  </sitemap>
  <sitemap>
    <loc>https://example.com/sitemap-images.xml</loc>
  </sitemap>
</sitemapindex>";

    private const string SampleSitemapWithoutNamespace = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset>
  <url>
    <loc>https://example.com/</loc>
  </url>
  <url>
    <loc>https://example.com/about</loc>
  </url>
</urlset>";

    private const string SampleSitemapIndexWithoutNamespace = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex>
  <sitemap>
    <loc>https://example.com/sitemap-posts.xml</loc>
  </sitemap>
  <sitemap>
    <loc>https://example.com/sitemap-pages.xml</loc>
  </sitemap>
</sitemapindex>";

    private const string EmptySitemapXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
</urlset>";

    private const string EmptySitemapIndexXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
</sitemapindex>";

    private const string MalformedXml = @"<html><body>Not a sitemap</body></html>";

    private const string SitemapWithEmptyLocs = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url>
    <loc>https://example.com/</loc>
  </url>
  <url>
    <loc>   </loc>
  </url>
  <url>
  </url>
  <url>
    <loc>https://example.com/contact</loc>
  </url>
</urlset>";

    private const string SitemapWithExtraElements = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url>
    <loc>https://example.com/post-1</loc>
    <lastmod>2024-01-15</lastmod>
    <changefreq>weekly</changefreq>
    <priority>0.8</priority>
  </url>
  <url>
    <loc>https://example.com/post-2</loc>
    <lastmod>2024-01-10</lastmod>
    <changefreq>monthly</changefreq>
    <priority>0.5</priority>
  </url>
</urlset>";

    // ─── Constructor / Interface Tests ───────────────────────────────────────

    [Fact]
    public void SitemapParser_Implements_ISitemapParser()
    {
        var parser = new SitemapParser(_logger);
        parser.Should().BeAssignableTo<ISitemapParser>();
    }

    [Fact]
    public void SitemapParser_CanBeInstantiated()
    {
        var parser = new SitemapParser(_logger);
        parser.Should().NotBeNull();
    }

    [Fact]
    public void SitemapParser_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new SitemapParser(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ─── ParseFromXml - Regular Sitemap Tests ────────────────────────────────

    [Fact]
    public void ParseFromXml_RegularSitemap_ReturnsUrls()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SampleRegularSitemapXml);

        var urls = parser.ParseFromXml(doc);

        urls.Should().HaveCount(4);
        urls.Should().Contain("https://example.com/");
        urls.Should().Contain("https://example.com/about");
        urls.Should().Contain("https://example.com/contact");
        urls.Should().Contain("https://example.com/blog");
    }

    [Fact]
    public void ParseFromXml_RegularSitemapWithoutNamespace_ReturnsUrls()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SampleSitemapWithoutNamespace);

        var urls = parser.ParseFromXml(doc);

        urls.Should().HaveCount(2);
        urls.Should().Contain("https://example.com/");
        urls.Should().Contain("https://example.com/about");
    }

    [Fact]
    public void ParseFromXml_RegularSitemapWithExtraElements_ExtractsOnlyLocs()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SitemapWithExtraElements);

        var urls = parser.ParseFromXml(doc);

        urls.Should().HaveCount(2);
        urls.Should().Contain("https://example.com/post-1");
        urls.Should().Contain("https://example.com/post-2");
    }

    // ─── ParseFromXml - Sitemap Index Tests ─────────────────────────────────

    [Fact]
    public void ParseFromXml_SitemapIndex_ReturnsChildSitemapUrls()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SampleSitemapIndexXml);

        var urls = parser.ParseFromXml(doc);

        urls.Should().HaveCount(3);
        urls.Should().Contain("https://example.com/sitemap-posts.xml");
        urls.Should().Contain("https://example.com/sitemap-pages.xml");
        urls.Should().Contain("https://example.com/sitemap-images.xml");
    }

    [Fact]
    public void ParseFromXml_SitemapIndexWithoutNamespace_ReturnsChildSitemapUrls()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SampleSitemapIndexWithoutNamespace);

        var urls = parser.ParseFromXml(doc);

        urls.Should().HaveCount(2);
        urls.Should().Contain("https://example.com/sitemap-posts.xml");
        urls.Should().Contain("https://example.com/sitemap-pages.xml");
    }

    // ─── ParseUrlset Tests ──────────────────────────────────────────────────

    [Fact]
    public void ParseUrlset_ValidSitemap_ReturnsAllLocs()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SampleRegularSitemapXml);

        var urls = parser.ParseUrlset(doc);

        urls.Should().HaveCount(4);
        urls.Should().Contain("https://example.com/");
        urls.Should().Contain("https://example.com/about");
        urls.Should().Contain("https://example.com/contact");
        urls.Should().Contain("https://example.com/blog");
    }

    [Fact]
    public void ParseUrlset_EmptySitemap_ReturnsEmptyList()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(EmptySitemapXml);

        var urls = parser.ParseUrlset(doc);

        urls.Should().BeEmpty();
    }

    [Fact]
    public void ParseUrlset_SkipsEmptyLocs()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SitemapWithEmptyLocs);

        var urls = parser.ParseUrlset(doc);

        urls.Should().HaveCount(2);
        urls.Should().Contain("https://example.com/");
        urls.Should().Contain("https://example.com/contact");
    }

    // ─── ParseSitemapIndex Tests ─────────────────────────────────────────────

    [Fact]
    public void ParseSitemapIndex_ValidIndex_ReturnsChildSitemapUrls()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SampleSitemapIndexXml);

        var urls = parser.ParseSitemapIndex(doc);

        urls.Should().HaveCount(3);
        urls.Should().Contain("https://example.com/sitemap-posts.xml");
        urls.Should().Contain("https://example.com/sitemap-pages.xml");
        urls.Should().Contain("https://example.com/sitemap-images.xml");
    }

    [Fact]
    public void ParseSitemapIndex_EmptyIndex_ReturnsEmptyList()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(EmptySitemapIndexXml);

        var urls = parser.ParseSitemapIndex(doc);

        urls.Should().BeEmpty();
    }

    // ─── ParseFromXml Edge Case Tests ────────────────────────────────────────

    [Fact]
    public void ParseFromXml_MalformedXml_ReturnsEmptyList()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(MalformedXml);

        var urls = parser.ParseFromXml(doc);

        // Unrecognized root element should return empty
        urls.Should().BeEmpty();
    }

    [Fact]
    public void ParseFromXml_EmptyUrlset_ReturnsEmptyList()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(EmptySitemapXml);

        var urls = parser.ParseFromXml(doc);

        urls.Should().BeEmpty();
    }

    [Fact]
    public void ParseFromXml_EmptySitemapIndex_ReturnsEmptyList()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(EmptySitemapIndexXml);

        var urls = parser.ParseFromXml(doc);

        urls.Should().BeEmpty();
    }

    [Fact]
    public void ParseFromXml_UrlElementsWithMissingLoc_AreSkipped()
    {
        var parser = CreateParser();
        var doc = XDocument.Parse(SitemapWithEmptyLocs);

        var urls = parser.ParseFromXml(doc);

        // Only the URLs with non-empty <loc> values should be included
        urls.Should().HaveCount(2);
        urls.Should().Contain("https://example.com/");
        urls.Should().Contain("https://example.com/contact");
    }

    // ─── Depth Limiting Tests ────────────────────────────────────────────────

    [Fact]
    public void MaxDepth_Is10()
    {
        SitemapParser.MaxDepth.Should().Be(10);
    }

    [Fact]
    public void MaxChildSitemapsPerIndex_Is100()
    {
        SitemapParser.MaxChildSitemapsPerIndex.Should().Be(100);
    }

    // ─── Namespace Handling Tests ─────────────────────────────────────────────

    [Fact]
    public void ParseUrlset_WithStandardNamespace_ExtractsUrls()
    {
        var parser = CreateParser();
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url><loc>https://example.com/page1</loc></url>
  <url><loc>https://example.com/page2</loc></url>
</urlset>";

        var doc = XDocument.Parse(xml);
        var urls = parser.ParseUrlset(doc);

        urls.Should().HaveCount(2);
    }

    [Fact]
    public void ParseSitemapIndex_WithStandardNamespace_ExtractsUrls()
    {
        var parser = CreateParser();
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <sitemap><loc>https://example.com/sitemap1.xml</loc></sitemap>
  <sitemap><loc>https://example.com/sitemap2.xml</loc></sitemap>
</sitemapindex>";

        var doc = XDocument.Parse(xml);
        var urls = parser.ParseSitemapIndex(doc);

        urls.Should().HaveCount(2);
    }

    // ─── URL Validation Tests ────────────────────────────────────────────────

    [Fact]
    public void ParseUrlset_TrimsWhitespaceFromLocs()
    {
        var parser = CreateParser();
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url><loc>   https://example.com/page1   </loc></url>
</urlset>";

        var doc = XDocument.Parse(xml);
        var urls = parser.ParseUrlset(doc);

        urls.Should().Contain("https://example.com/page1");
    }

    [Fact]
    public void ParseFromXml_SingleUrlSitemap_ReturnsOneUrl()
    {
        var parser = CreateParser();
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url><loc>https://example.com/only-page</loc></url>
</urlset>";

        var doc = XDocument.Parse(xml);
        var urls = parser.ParseFromXml(doc);

        urls.Should().HaveCount(1);
        urls[0].Should().Be("https://example.com/only-page");
    }

    // ─── Helper ─────────────────────────────────────────────────────────────

    private SitemapParser CreateParser()
    {
        return new SitemapParser(_logger);
    }
}
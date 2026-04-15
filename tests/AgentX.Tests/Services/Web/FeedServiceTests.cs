using AgentX.Core.Services.Web;
using AgentX.Core.Services.Web.Models;
using FluentAssertions;
using Moq;
using Serilog;
using System.Xml.Linq;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class FeedServiceTests
{
    private readonly ILogger _logger;

    public FeedServiceTests()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.ForContext<FeedService>()).Returns(loggerMock.Object);
        _logger = loggerMock.Object;
    }
    // ─── Sample RSS 2.0 XML ────────────────────────────────────────────────

    private const string SampleRssXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:content=""http://purl.org/rss/1.0/modules/content/""
     xmlns:dc=""http://purl.org/dc/elements/1.1/"">
  <channel>
    <title>Test Blog</title>
    <link>https://example.com/blog</link>
    <description>A test blog about technology</description>
    <lastBuildDate>Mon, 15 Jan 2024 10:00:00 GMT</lastBuildDate>
    <item>
      <title>First Post</title>
      <link>https://example.com/blog/first-post</link>
      <description>This is the first post description.</description>
      <content:encoded><![CDATA[<p>This is the full content of the first post.</p>]]></content:encoded>
      <dc:creator>Jane Doe</dc:creator>
      <pubDate>Mon, 15 Jan 2024 09:00:00 GMT</pubDate>
      <category>Technology</category>
    </item>
    <item>
      <title>Second Post</title>
      <link>https://example.com/blog/second-post</link>
      <description>This is the second post description.</description>
      <author>john@example.com (John Smith)</author>
      <pubDate>Sun, 14 Jan 2024 12:00:00 GMT</pubDate>
      <category>Science</category>
    </item>
    <item>
      <title>Old Post</title>
      <link>https://example.com/blog/old-post</link>
      <description>An old post from 2023.</description>
      <pubDate>Fri, 01 Dec 2023 08:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>";

    // ─── Sample Atom 1.0 XML ────────────────────────────────────────────────

    private const string SampleAtomXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <title>Test Atom Feed</title>
  <link href=""https://example.com/atom"" rel=""alternate""/>
  <link href=""https://example.com/atom.xml"" rel=""self""/>
  <subtitle>A test Atom feed about science</subtitle>
  <updated>2024-01-15T10:00:00Z</updated>
  <entry>
    <title>Atom First Entry</title>
    <link href=""https://example.com/atom/first-entry"" rel=""alternate""/>
    <id>urn:uuid:first-entry</id>
    <updated>2024-01-15T09:00:00Z</updated>
    <published>2024-01-15T09:00:00Z</published>
    <author><name>Alice Author</name></author>
    <summary>Summary of the first Atom entry.</summary>
    <content type=""html""><p>Full content of the first Atom entry.</p></content>
    <category term=""Science""/>
  </entry>
  <entry>
    <title>Atom Second Entry</title>
    <link href=""https://example.com/atom/second-entry"" rel=""alternate""/>
    <id>urn:uuid:second-entry</id>
    <updated>2024-01-14T12:00:00Z</updated>
    <published>2024-01-14T12:00:00Z</published>
    <author><name>Bob Writer</name></author>
    <summary>Summary of the second Atom entry.</summary>
    <category term=""Technology"" label=""Tech""/>
  </entry>
  <entry>
    <title>Old Atom Entry</title>
    <link href=""https://example.com/atom/old-entry"" rel=""alternate""/>
    <id>urn:uuid:old-entry</id>
    <updated>2023-12-01T08:00:00Z</updated>
    <published>2023-12-01T08:00:00Z</published>
    <summary>An old entry from 2023.</summary>
  </entry>
</feed>";

    // ─── Sample RSS 1.0 (RDF) XML ──────────────────────────────────────────

    private const string SampleRdfXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#""
         xmlns=""http://purl.org/rss/1.0/""
         xmlns:dc=""http://purl.org/dc/elements/1.1/""
         xmlns:content=""http://purl.org/rss/1.0/modules/content/"">
  <channel rdf:about=""https://example.com/rdf"">
    <title>Test RDF Feed</title>
    <link>https://example.com/rdf</link>
    <description>A test RDF feed</description>
    <dc:date>2024-01-15T10:00:00Z</dc:date>
  </channel>
  <item rdf:about=""https://example.com/rdf/first"">
    <title>RDF First Post</title>
    <link>https://example.com/rdf/first</link>
    <description>First RDF post description.</description>
    <dc:creator>RDF Author</dc:creator>
    <dc:date>2024-01-15T09:00:00Z</dc:date>
  </item>
</rdf:RDF>";

    // ─── Constructor / Interface Tests ───────────────────────────────────────

    [Fact]
    public void FeedService_Implements_IFeedService()
    {
        var service = new FeedService(_logger);
        service.Should().BeAssignableTo<IFeedService>();
    }

    [Fact]
    public void FeedService_CanBeInstantiated()
    {
        var service = new FeedService(_logger);
        service.Should().NotBeNull();
    }

    // ─── RSS 2.0 Parsing Tests ──────────────────────────────────────────────

    [Fact]
    public void ParseRssFeed_ValidXml_ReturnsFeedInfo()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");

        result.Should().NotBeNull();
        result.Title.Should().Be("Test Blog");
        result.Url.Should().Be("https://example.com/blog");
        result.Description.Should().Be("A test blog about technology");
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public void ParseRssFeed_FirstItem_HasCorrectTitleAndUrl()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");
        var firstItem = result.Items[0];

        firstItem.Title.Should().Be("First Post");
        firstItem.Url.Should().Be("https://example.com/blog/first-post");
    }

    [Fact]
    public void ParseRssFeed_ItemWithContentEncoded_UsesContentEncoded()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");
        var firstItem = result.Items[0];

        // content:encoded should be used as Content
        firstItem.Content.Should().Contain("full content of the first post");
        // description should still be set separately
        firstItem.Description.Should().Be("This is the first post description.");
    }

    [Fact]
    public void ParseRssFeed_ItemWithDcCreator_ExtractsAuthor()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");
        var firstItem = result.Items[0];

        firstItem.Author.Should().Be("Jane Doe");
    }

    [Fact]
    public void ParseRssFeed_ItemWithAuthor_FallsBackToAuthorElement()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");
        var secondItem = result.Items[1];

        // Second item has <author> instead of <dc:creator>
        secondItem.Author.Should().Contain("John Smith");
    }

    [Fact]
    public void ParseRssFeed_ItemWithPubDate_ParsesDate()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");
        var firstItem = result.Items[0];

        firstItem.PublishedDate.Should().HaveValue();
        firstItem.PublishedDate!.Value.Year.Should().Be(2024);
        firstItem.PublishedDate.Value.Month.Should().Be(1);
        firstItem.PublishedDate.Value.Day.Should().Be(15);
    }

    [Fact]
    public void ParseRssFeed_ItemWithCategory_ExtractsCategory()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");

        result.Items[0].Category.Should().Be("Technology");
        result.Items[1].Category.Should().Be("Science");
    }

    [Fact]
    public void ParseRssFeed_ItemWithoutContentEncoded_FallsBackToDescription()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");
        // Third item has no content:encoded, should fall back to description
        var thirdItem = result.Items[2];

        thirdItem.Content.Should().Be("An old post from 2023.");
    }

    // ─── Atom 1.0 Parsing Tests ──────────────────────────────────────────────

    [Fact]
    public void ParseAtomFeed_ValidXml_ReturnsFeedInfo()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");

        result.Should().NotBeNull();
        result.Title.Should().Be("Test Atom Feed");
        result.Url.Should().Be("https://example.com/atom");
        result.Description.Should().Be("A test Atom feed about science");
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public void ParseAtomFeed_EntryWithContent_UsesContent()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");
        var firstEntry = result.Items[0];

        firstEntry.Content.Should().Contain("Full content of the first Atom entry");
        firstEntry.Description.Should().Be("Summary of the first Atom entry.");
    }

    [Fact]
    public void ParseAtomFeed_EntryWithSummaryOnly_UsesSummaryAsContent()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");
        // Third entry has only <summary>, no <content>
        var thirdEntry = result.Items[2];

        thirdEntry.Content.Should().Be("An old entry from 2023.");
    }

    [Fact]
    public void ParseAtomFeed_EntryWithAuthor_ExtractsAuthor()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");

        result.Items[0].Author.Should().Be("Alice Author");
        result.Items[1].Author.Should().Be("Bob Writer");
    }

    [Fact]
    public void ParseAtomFeed_EntryWithPublished_UsesPublishedDate()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");
        var firstEntry = result.Items[0];

        firstEntry.PublishedDate.Should().HaveValue();
        firstEntry.PublishedDate!.Value.Year.Should().Be(2024);
        firstEntry.PublishedDate.Value.Month.Should().Be(1);
        firstEntry.PublishedDate.Value.Day.Should().Be(15);
    }

    [Fact]
    public void ParseAtomFeed_EntryWithCategory_ExtractsCategory()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");

        result.Items[0].Category.Should().Be("Science");
        result.Items[1].Category.Should().Be("Technology");
    }

    [Fact]
    public void ParseAtomFeed_EntryLink_ExtractsAlternateLink()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");

        result.Items[0].Url.Should().Be("https://example.com/atom/first-entry");
        result.Items[1].Url.Should().Be("https://example.com/atom/second-entry");
    }

    // ─── RDF Feed Parsing Tests ──────────────────────────────────────────────

    [Fact]
    public void ParseRdfFeed_ValidXml_ReturnsFeedInfo()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRdfXml);

        var result = service.ParseFeed(doc, "https://example.com/rdf");

        result.Should().NotBeNull();
        result.Title.Should().Be("Test RDF Feed");
        result.Url.Should().Be("https://example.com/rdf");
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public void ParseRdfFeed_Item_HasCorrectData()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRdfXml);

        var result = service.ParseFeed(doc, "https://example.com/rdf");
        var item = result.Items[0];

        item.Title.Should().Be("RDF First Post");
        item.Url.Should().Be("https://example.com/rdf/first");
        item.Author.Should().Be("RDF Author");
    }

    // ─── Edge Case Tests ────────────────────────────────────────────────────

    [Fact]
    public void ParseFeed_UnrecognizedFormat_ThrowsInvalidOperationException()
    {
        var service = CreateService();
        var doc = XDocument.Parse("<html><body>Not a feed</body></html>");

        var act = () => service.ParseFeed(doc, "https://example.com/notafeed");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Unrecognized feed format*");
    }

    [Fact]
    public void ParseRssFeed_EmptyChannel_ReturnsEmptyItems()
    {
        var service = CreateService();
        var doc = XDocument.Parse(@"<?xml version=""1.0""?>
<rss version=""2.0"">
  <channel>
    <title>Empty Feed</title>
    <link>https://example.com/empty</link>
  </channel>
</rss>");

        var result = service.ParseFeed(doc, "https://example.com/empty");

        result.Title.Should().Be("Empty Feed");
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public void ParseAtomFeed_EntryWithoutContent_FallsBackToSummary()
    {
        var service = CreateService();
        var doc = XDocument.Parse(@"<?xml version=""1.0""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <title>Summary Only Feed</title>
  <link href=""https://example.com"" rel=""alternate""/>
  <entry>
    <title>Summary Only Entry</title>
    <link href=""https://example.com/summary-entry"" rel=""alternate""/>
    <id>urn:uuid:summary-entry</id>
    <updated>2024-01-15T09:00:00Z</updated>
    <summary>Only a summary, no content element.</summary>
  </entry>
</feed>");

        var result = service.ParseFeed(doc, "https://example.com/summary");

        result.Items[0].Content.Should().Be("Only a summary, no content element.");
    }

    [Fact]
    public void ParseRssFeed_LastBuildDate_ParsesAsLastUpdated()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");

        result.LastUpdated.Should().HaveValue();
        result.LastUpdated!.Value.Year.Should().Be(2024);
    }

    [Fact]
    public void ParseAtomFeed_FeedUpdated_ParsesAsLastUpdated()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");

        result.LastUpdated.Should().HaveValue();
        result.LastUpdated!.Value.Year.Should().Be(2024);
    }

    // ─── Date Filtering (GetNewItemsAsync logic) ────────────────────────────

    [Fact]
    public void ParseRssFeed_DateFiltering_ReturnsOnlyNewItems()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");

        // Items from January 2024 should be "new" relative to December 2023
        var since = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newItems = result.Items
            .Where(item => item.PublishedDate.HasValue && item.PublishedDate.Value > since)
            .ToList();

        newItems.Should().HaveCount(2);
        newItems.Should().OnlyContain(i => i.PublishedDate!.Value > since);
    }

    [Fact]
    public void ParseAtomFeed_DateFiltering_ReturnsOnlyNewItems()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleAtomXml);

        var result = service.ParseFeed(doc, "https://example.com/atom");

        // Items from January 2024 should be "new" relative to December 2023
        var since = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newItems = result.Items
            .Where(item => item.PublishedDate.HasValue && item.PublishedDate.Value > since)
            .ToList();

        newItems.Should().HaveCount(2);
        newItems.Should().OnlyContain(i => i.PublishedDate!.Value > since);
    }

    [Fact]
    public void ParseRssFeed_AllItemsNew_ReturnsAllItems()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");

        // All items should be "new" relative to 2020
        var since = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newItems = result.Items
            .Where(item => item.PublishedDate.HasValue && item.PublishedDate.Value > since)
            .ToList();

        newItems.Should().HaveCount(3);
    }

    [Fact]
    public void ParseRssFeed_NoItemsNew_ReturnsEmptyList()
    {
        var service = CreateService();
        var doc = XDocument.Parse(SampleRssXml);

        var result = service.ParseFeed(doc, "https://example.com/rss");

        // No items should be "new" relative to 2025
        var since = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newItems = result.Items
            .Where(item => item.PublishedDate.HasValue && item.PublishedDate.Value > since)
            .ToList();

        newItems.Should().BeEmpty();
    }

    // ─── URL Fallback Tests ─────────────────────────────────────────────────

    [Fact]
    public void ParseAtomFeed_FeedWithoutAlternateLink_UsesSourceUrl()
    {
        var service = CreateService();
        var doc = XDocument.Parse(@"<?xml version=""1.0""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <title>No Link Feed</title>
  <link href=""https://example.com/atom.xml"" rel=""self""/>
  <updated>2024-01-15T10:00:00Z</updated>
  <entry>
    <title>Entry</title>
    <link href=""https://example.com/entry"" rel=""alternate""/>
    <id>urn:uuid:entry</id>
    <updated>2024-01-15T09:00:00Z</updated>
    <summary>Summary</summary>
  </entry>
</feed>");

        var result = service.ParseFeed(doc, "https://example.com/fallback-url");

        // Feed URL should fall back to sourceUrl since there's no rel="alternate" link on the feed
        result.Url.Should().Be("https://example.com/fallback-url");
    }

    // ─── Helper ─────────────────────────────────────────────────────────────

    private FeedService CreateService()
    {
        return new FeedService(_logger);
    }
}
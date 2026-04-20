using AgentX.Core.Services.Web;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class StructuredDataExtractorTests
{
    private readonly StructuredDataExtractor _extractor;

    public StructuredDataExtractorTests()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.ForContext<StructuredDataExtractor>()).Returns(loggerMock.Object);
        _extractor = new StructuredDataExtractor(loggerMock.Object);
    }

    // ─── Constructor ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Throws_WhenLoggerIsNull()
    {
        var act = () => new StructuredDataExtractor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─── ExtractJsonLd ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractJsonLd_ParsesArticleSchema()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   {
                       "@context": "https://schema.org",
                       "@type": "Article",
                       "name": "Test Article Title",
                       "author": { "@type": "Person", "name": "Jane Doe" },
                       "description": "A comprehensive test article.",
                       "datePublished": "2024-06-15T10:30:00Z"
                   }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Type.Should().Be("Article");
        result.Name.Should().Be("Test Article Title");
        result.Author.Should().Be("Jane Doe");
        result.Description.Should().Be("A comprehensive test article.");
        result.DatePublished.Should().NotBeNull();
        result.DatePublished!.Value.Year.Should().Be(2024);
    }

    [Fact]
    public void ExtractJsonLd_ParsesBlogPosting_WithHeadlineField()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   {
                       "@context": "https://schema.org",
                       "@type": "BlogPosting",
                       "headline": "Blog Headline Used as Name",
                       "author": "Author as String"
                   }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Type.Should().Be("BlogPosting");
        result.Name.Should().Be("Blog Headline Used as Name");
        result.Author.Should().Be("Author as String");
    }

    [Fact]
    public void ExtractJsonLd_HandlesMultipleJsonLdBlocks_ReturnsFirstValid()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@type": "WebSite", "name": "Site Name" }
                   </script>
                   <script type="application/ld+json">
                   { "@type": "Article", "name": "Article Name", "author": "Author" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Type.Should().Be("WebSite");
        result.Name.Should().Be("Site Name");
    }

    [Fact]
    public void ExtractJsonLd_HandlesArrayofJsonLdObjects()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   [
                       { "@type": "BreadcrumbList", "name": "Breadcrumb" },
                       { "@type": "Article", "name": "Array Article", "author": "Array Author" }
                   ]
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Type.Should().Be("BreadcrumbList");
        result.Name.Should().Be("Breadcrumb");
    }

    [Fact]
    public void ExtractJsonLd_HandlesGraphStructure()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   {
                       "@context": "https://schema.org",
                       "@graph": [
                           { "@type": "Article", "name": "Graph Article", "author": "Graph Author" }
                       ]
                   }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Graph Article");
        result!.Author.Should().Be("Graph Author");
    }

    [Fact]
    public void ExtractJsonLd_HandlesMalformedJson_Gracefully()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">{ this is not valid json }</script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractJsonLd_HandlesMalformedThenValidBlock()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">{ broken }</script>
                   <script type="application/ld+json">
                   { "@type": "Article", "name": "Valid After Malformed" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Valid After Malformed");
    }

    [Fact]
    public void ExtractJsonLd_ReturnsNull_WhenNoLdJsonScripts()
    {
        var html = """
                   <html><head>
                   <title>Plain Page</title>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractJsonLd_ReturnsNull_ForEmptyHtml()
    {
        var result = _extractor.ExtractJsonLd(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractJsonLd_ReturnsNull_ForNullHtml()
    {
        var result = _extractor.ExtractJsonLd(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractJsonLd_ReturnsNull_ForWhitespaceHtml()
    {
        var result = _extractor.ExtractJsonLd("   \n\t  ");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractJsonLd_HandlesAuthorAsString()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@type": "Article", "author": "String Author Name" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Author.Should().Be("String Author Name");
    }

    [Fact]
    public void ExtractJsonLd_HandlesAuthorAsArray()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@type": "Article", "author": ["First Author", { "name": "Second Author" }] }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.Author.Should().Be("First Author");
    }

    [Fact]
    public void ExtractJsonLd_HandlesDatePublished_Parsing()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   {
                       "@type": "Article",
                       "datePublished": "2024-12-25"
                   }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().NotBeNull();
        result!.DatePublished.Should().NotBeNull();
        result.DatePublished!.Value.Year.Should().Be(2024);
        result.DatePublished!.Value.Month.Should().Be(12);
        result.DatePublished!.Value.Day.Should().Be(25);
    }

    // ─── ExtractOpenGraph ──────────────────────────────────────────────────

    [Fact]
    public void ExtractOpenGraph_PullsAllOgProperties()
    {
        var html = """
                   <html><head>
                   <meta property="og:title" content="OG Title" />
                   <meta property="og:description" content="OG Description" />
                   <meta property="og:image" content="https://example.com/image.jpg" />
                   <meta property="og:url" content="https://example.com/article" />
                   <meta property="og:type" content="article" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractOpenGraph(html);

        result.Should().NotBeNull();
        result!.Title.Should().Be("OG Title");
        result.Description.Should().Be("OG Description");
        result.Image.Should().Be("https://example.com/image.jpg");
        result.Url.Should().Be("https://example.com/article");
        result.Type.Should().Be("article");
    }

    [Fact]
    public void ExtractOpenGraph_ReturnsPartial_WhenSomeTagsMissing()
    {
        var html = """
                   <html><head>
                   <meta property="og:title" content="Only Title" />
                   <meta property="og:type" content="website" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractOpenGraph(html);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Only Title");
        result.Description.Should().BeNull();
        result.Image.Should().BeNull();
        result.Type.Should().Be("website");
    }

    [Fact]
    public void ExtractOpenGraph_ReturnsNull_WhenNoOgTags()
    {
        var html = """
                   <html><head>
                   <meta name="author" content="Someone" />
                   <title>No OG Tags</title>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractOpenGraph(html);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOpenGraph_ReturnsNull_ForEmptyHtml()
    {
        var result = _extractor.ExtractOpenGraph(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOpenGraph_ReturnsNull_ForNullHtml()
    {
        var result = _extractor.ExtractOpenGraph(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOpenGraph_ReturnsNull_ForWhitespaceHtml()
    {
        var result = _extractor.ExtractOpenGraph("   \n\t  ");
        result.Should().BeNull();
    }

    // ─── ExtractMetaTags ───────────────────────────────────────────────────

    [Fact]
    public void ExtractMetaTags_ExtractsAllMetaTags()
    {
        var html = """
                   <html><head>
                   <meta name="author" content="Test Author" />
                   <meta name="description" content="A test page" />
                   <meta property="og:title" content="OG Title" />
                   <meta property="og:description" content="OG Desc" />
                   <meta name="twitter:card" content="summary_large_image" />
                   <meta charset="utf-8" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractMetaTags(html);

        result.Should().NotBeEmpty();
        // charset has no content or name/property, so it should be excluded
        result.Count.Should().Be(5);

        result.Should().Contain(t =>
            t.Name == "author" && t.Content == "Test Author" && t.Property == "name");
        result.Should().Contain(t =>
            t.Name == "og:title" && t.Content == "OG Title" && t.Property == "property");
        result.Should().Contain(t =>
            t.Name == "twitter:card" && t.Content == "summary_large_image" && t.Property == "name");
    }

    [Fact]
    public void ExtractMetaTags_ReturnsEmptyList_ForNoMetaTags()
    {
        var html = "<html><head><title>No Meta</title></head><body></body></html>";

        var result = _extractor.ExtractMetaTags(html);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMetaTags_ReturnsEmptyList_ForEmptyHtml()
    {
        var result = _extractor.ExtractMetaTags(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMetaTags_ReturnsEmptyList_ForNullHtml()
    {
        var result = _extractor.ExtractMetaTags(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMetaTags_ReturnsEmptyList_ForWhitespaceHtml()
    {
        var result = _extractor.ExtractMetaTags("   \n\t  ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMetaTags_SkipsMetaTagsWithoutContent()
    {
        var html = """
                   <html><head>
                   <meta name="viewport" />
                   <meta name="author" content="Valid Author" />
                   <meta property="og:title" content="" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractMetaTags(html);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("author");
        result[0].Content.Should().Be("Valid Author");
    }

    // ─── ExtractAuthor ────────────────────────────────────────────────────

    [Fact]
    public void ExtractAuthor_PrefersJsonLdAuthor_OverMetaAuthor()
    {
        var html = """
                   <html><head>
                   <meta name="author" content="Meta Author" />
                   <script type="application/ld+json">
                   { "@type": "Article", "author": "JSON-LD Author" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("JSON-LD Author");
    }

    [Fact]
    public void ExtractAuthor_FallsBackToMetaAuthor_WhenNoJsonLd()
    {
        var html = """
                   <html><head>
                   <meta name="author" content="Meta Author" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Meta Author");
    }

    [Fact]
    public void ExtractAuthor_FallsBackToArticleAuthorMeta()
    {
        var html = """
                   <html><head>
                   <meta property="article:author" content="Article Author" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Article Author");
    }

    [Fact]
    public void ExtractAuthor_FallsBackToDcCreator()
    {
        var html = """
                   <html><head>
                   <meta name="dc.creator" content="DC Creator" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("DC Creator");
    }

    [Fact]
    public void ExtractAuthor_FallsBackToByl()
    {
        var html = """
                   <html><head>
                   <meta name="byl" content="Byline Author" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Byline Author");
    }

    [Fact]
    public void ExtractAuthor_FallsBackToRelAuthorLink()
    {
        var html = """
                   <html><head><title>Article</title></head>
                   <body><a rel="author" href="/authors/bob">Bob Writer</a></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Bob Writer");
    }

    [Fact]
    public void ExtractAuthor_ReturnsNull_WhenNoAuthorData()
    {
        var html = """
                   <html><head><title>No Author</title></head>
                   <body><p>Some content without any author information.</p></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractAuthor_ReturnsNull_ForEmptyHtml()
    {
        var result = _extractor.ExtractAuthor(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractAuthor_ReturnsNull_ForNullHtml()
    {
        var result = _extractor.ExtractAuthor(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractAuthor_ResolvesFullFallbackChain()
    {
        // When JSON-LD is malformed, falls to meta, then to rel=author
        var html = """
                   <html><head>
                   <script type="application/ld+json">{ invalid }</script>
                   <a rel="author" href="/about">Link Author</a>
                   </head>
                   <body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Link Author");
    }

    [Fact]
    public void ExtractAuthor_HandlesJsonLdAuthorAsObject()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@type": "Article", "author": { "@type": "Person", "name": "Object Author" } }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Object Author");
    }

    [Fact]
    public void ExtractAuthor_HandlesJsonLdAuthorInGraph()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   {
                       "@context": "https://schema.org",
                       "@graph": [
                           { "@type": "Article", "author": "Graph Author" }
                       ]
                   }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractAuthor(html);

        result.Should().Be("Graph Author");
    }

    // ─── Handles Missing Structured Data Gracefully ────────────────────────

    [Fact]
    public void ExtractJsonLd_SkipsJsonLdBlockWithoutType()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@context": "https://schema.org", "name": "No Type Object" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractJsonLd(html);

        result.Should().BeNull();
    }

    [Fact]
    public void AllMethods_HandleMalformedHtml_Gracefully()
    {
        var malformedHtml = "<html><head><body><div>Unclosed everything";

        var jsonLdResult = _extractor.ExtractJsonLd(malformedHtml);
        var ogResult = _extractor.ExtractOpenGraph(malformedHtml);
        var metaResult = _extractor.ExtractMetaTags(malformedHtml);
        var authorResult = _extractor.ExtractAuthor(malformedHtml);

        jsonLdResult.Should().BeNull();
        ogResult.Should().BeNull();
        metaResult.Should().NotBeNull();
        authorResult.Should().BeNull();
    }

    [Fact]
    public void ExtractMetaTags_HandlesMetaWithOnlyNameOrProperty()
    {
        var html = """
                   <html><head>
                   <meta name="robots" content="index,follow" />
                   <meta property="og:title" content="Title" />
                   </head><body></body></html>
                   """;

        var result = _extractor.ExtractMetaTags(html);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "robots" && t.Property == "name");
        result.Should().Contain(t => t.Name == "og:title" && t.Property == "property");
    }
}

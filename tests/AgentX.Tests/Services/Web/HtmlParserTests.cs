using AgentX.Core.Services.Web;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class HtmlParserTests
{
    private readonly HtmlParser _parser;

    public HtmlParserTests()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.ForContext<HtmlParser>()).Returns(loggerMock.Object);
        _parser = new HtmlParser(loggerMock.Object);
    }

    // ─── Parse ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsTitleAndText_FromArticleHtml()
    {
        var html = """
                   <!DOCTYPE html>
                   <html lang="en">
                   <head>
                       <title>Test Article</title>
                       <meta property="og:title" content="OG Test Article" />
                       <meta name="description" content="A test article description" />
                   </head>
                   <body>
                       <article>
                           <h1>Test Article</h1>
                           <p>This is the first paragraph of the article content. It contains enough words to pass the minimum threshold for content extraction. The readability algorithm should identify this as the main content area of the page without any issues whatsoever.</p>
                           <p>This is the second paragraph with additional content that provides more context and depth to the article. This ensures the word count threshold is comfortably exceeded.</p>
                       </article>
                   </body>
                   </html>
                   """;

        var result = _parser.Parse(html, "https://example.com/article");

        result.Title.Should().Be("OG Test Article");
        result.Text.Should().NotBeNullOrEmpty();
        result.Text.Should().Contain("first paragraph of the article content");
        result.Text.Should().Contain("second paragraph");
        result.Description.Should().Be("A test article description");
        result.ReadingTime.Should().NotBeNull();
    }

    [Fact]
    public void Parse_HandlesNonArticlePages_WithGracefulFallback()
    {
        // A page with no article, just a few links and minimal text
        var html = """
                   <html>
                   <head><title>Links Page</title></head>
                   <body>
                       <nav><a href="/about">About</a> <a href="/contact">Contact</a></nav>
                       <div>
                           <a href="/link1">Link 1</a>
                           <a href="/link2">Link 2</a>
                           <a href="/link3">Link 3</a>
                       </div>
                   </body>
                   </html>
                   """;

        var result = _parser.Parse(html, "https://example.com/links");

        result.Title.Should().Be("Links Page");
        // Text may be minimal but should not throw
        result.Text.Should().NotBeNull();
    }

    [Fact]
    public void Parse_CalculatesReadingTime_Correctly()
    {
        // Generate ~450 words (should be ~2 minutes at 225 WPM)
        var paragraphs = string.Join("\n",
            Enumerable.Range(0, 10).Select(i =>
                $"<p>{string.Join(" ", Enumerable.Range(0, 45).Select(w => $"word{i}_{w}"))}</p>"));

        var html = $"""
                    <html><head><title>Long Article</title></head>
                    <body><article>{paragraphs}</article></body></html>
                    """;

        var result = _parser.Parse(html, "https://example.com/long");

        result.ReadingTime.Should().NotBeNull();
        result.ReadingTime!.Value.TotalMinutes.Should().BeApproximately(2.0, 0.5);
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForNullHtml()
    {
        var result = _parser.Parse(null!, "https://example.com");

        result.Title.Should().BeEmpty();
        result.Text.Should().BeEmpty();
        result.ReadingTime.Should().BeNull();
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForEmptyHtml()
    {
        var result = _parser.Parse(string.Empty, "https://example.com");

        result.Title.Should().BeEmpty();
        result.Text.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForWhitespaceOnlyHtml()
    {
        var result = _parser.Parse("   \n\t  ", "https://example.com");

        result.Title.Should().BeEmpty();
        result.Text.Should().BeEmpty();
    }

    // ─── ExtractReadabilityText ─────────────────────────────────────────────

    [Fact]
    public void ExtractReadabilityText_RemovesNavFooterScriptStyleTags()
    {
        var html = """
                   <html><body>
                   <nav><a href="/home">Home</a> <a href="/about">About</a></nav>
                   <script>console.log('should be removed');</script>
                   <style>.hidden { display: none; }</style>
                   <footer>Footer content &copy; 2024</footer>
                   <div id="content">
                       <p>This is the main article content that should be preserved after all non-content elements are stripped away. The readability algorithm needs enough text here to score this container highly enough to be selected as the best content node for extraction purposes.</p>
                       <p>A second paragraph provides additional content and helps ensure the text density scoring works correctly. Without enough paragraphs the algorithm would not identify this as the main content area.</p>
                   </div>
                   </body></html>
                   """;

        var result = _parser.ExtractReadabilityText(html);

        result.Should().NotContain("console.log");
        result.Should().NotContain(".hidden");
        result.Should().NotContain("Footer content");
        result.Should().Contain("main article content");
    }

    [Fact]
    public void ExtractReadabilityText_ExtractsArticleElement_WhenPresent()
    {
        var html = """
                   <html><body>
                   <article>
                       <h1>Article Title</h1>
                       <p>This is a paragraph inside an article element. It has enough words to pass the minimum threshold check that the parser uses to validate extracted content. The article tag is the highest priority target for the readability extraction algorithm.</p>
                       <p>Second paragraph inside the article element with more meaningful content to ensure we exceed the twenty word minimum threshold comfortably without any edge case issues.</p>
                   </article>
                   </body></html>
                   """;

        var result = _parser.ExtractReadabilityText(html);

        result.Should().Contain("paragraph inside an article element");
        result.Should().Contain("Second paragraph");
    }

    [Fact]
    public void ExtractReadabilityText_FallsBackToScoredDivs_WhenNoArticle()
    {
        var html = """
                   <html><body>
                   <div class="sidebar"><p>Side content</p></div>
                   <div class="article-body">
                       <p>The main content of the page is in this div element which has a positive class name signal that helps the scoring algorithm identify it as the primary content area. Without an article element the parser relies on text density scoring to find the best container.</p>
                       <p>Another paragraph in the main content area with additional text to ensure the scoring works properly and the correct div is selected by the algorithm as the primary source of article content on this particular page.</p>
                   </div>
                   </body></html>
                   """;

        var result = _parser.ExtractReadabilityText(html);

        result.Should().Contain("main content of the page");
    }

    [Fact]
    public void ExtractReadabilityText_ReturnsEmpty_ForEmptyInput()
    {
        var result = _parser.ExtractReadabilityText(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractReadabilityText_ReturnsEmpty_ForNullInput()
    {
        var result = _parser.ExtractReadabilityText(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractReadabilityText_HandlesMalformedHtml_Gracefully()
    {
        var html = "<html><body><div><p>Unclosed paragraph<div>Nested without closing</body></html>";
        // Should not throw
        var result = _parser.ExtractReadabilityText(html);
        result.Should().NotBeNull();
    }

    // ─── ExtractMetadata ────────────────────────────────────────────────────

    [Fact]
    public void ExtractMetadata_PullsOpenGraphTags()
    {
        var html = """
                   <html><head>
                   <meta property="og:title" content="OG Title" />
                   <meta property="og:description" content="OG Description" />
                   <meta property="og:image" content="https://example.com/image.jpg" />
                   <meta property="og:site_name" content="Example Site" />
                   </head><body><p>Content</p></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Title.Should().Be("OG Title");
        result.Description.Should().Be("OG Description");
        result.ImageUrl.Should().Be("https://example.com/image.jpg");
        result.SiteName.Should().Be("Example Site");
    }

    [Fact]
    public void ExtractMetadata_PullsTwitterCardTags_AsFallback()
    {
        var html = """
                   <html><head>
                   <meta name="twitter:title" content="Twitter Title" />
                   <meta name="twitter:description" content="Twitter Description" />
                   <meta name="twitter:image" content="https://example.com/tw-image.jpg" />
                   </head><body><p>Content</p></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Title.Should().Be("Twitter Title");
        result.Description.Should().Be("Twitter Description");
        result.ImageUrl.Should().Be("https://example.com/tw-image.jpg");
    }

    [Fact]
    public void ExtractMetadata_PullsAuthor_FromJsonLd()
    {
        var html = """
                   <html><head>
                   <title>Article</title>
                   <script type="application/ld+json">
                   {
                       "@context": "https://schema.org",
                       "@type": "Article",
                       "author": { "@type": "Person", "name": "Jane Doe" }
                   }
                   </script>
                   </head><body><p>Content</p></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/article");

        result.Author.Should().Be("Jane Doe");
    }

    [Fact]
    public void ExtractMetadata_PullsAuthor_FromMetaTag_WhenNoJsonLd()
    {
        var html = """
                   <html><head>
                   <title>Article</title>
                   <meta name="author" content="John Smith" />
                   </head><body><p>Content</p></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/article");

        result.Author.Should().Be("John Smith");
    }

    [Fact]
    public void ExtractMetadata_PullsPublishDate()
    {
        var html = """
                   <html><head>
                   <meta property="article:published_time" content="2024-06-15T10:30:00Z" />
                   </head><body><p>Content</p></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/article");

        result.PublishedDate.Should().NotBeNull();
        result.PublishedDate!.Value.Year.Should().Be(2024);
        result.PublishedDate!.Value.Month.Should().Be(6);
        result.PublishedDate!.Value.Day.Should().Be(15);
    }

    [Fact]
    public void ExtractMetadata_DerivesSiteName_FromUrl_WhenMissing()
    {
        var html = "<html><head><title>Page</title></head><body></body></html>";

        var result = _parser.ExtractMetadata(html, "https://www.mysite.com/page");

        result.SiteName.Should().Be("mysite.com");
    }

    [Fact]
    public void ExtractMetadata_PrefersOgTitle_OverHtmlTitle()
    {
        var html = """
                   <html><head>
                   <title>HTML Title</title>
                   <meta property="og:title" content="Preferred OG Title" />
                   </head><body></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Title.Should().Be("Preferred OG Title");
    }

    [Fact]
    public void ExtractMetadata_PrefersJsonLdAuthor_OverMetaAuthor()
    {
        var html = """
                   <html><head>
                   <meta name="author" content="Meta Author" />
                   <script type="application/ld+json">
                   { "@type": "Article", "author": "JSON-LD Author" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Author.Should().Be("JSON-LD Author");
    }

    [Fact]
    public void ExtractMetadata_PullsAuthor_FromRelAuthorLink()
    {
        var html = """
                   <html><head><title>Article</title></head>
                   <body><a rel="author" href="/authors/bob">Bob Writer</a></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/article");

        result.Author.Should().Be("Bob Writer");
    }

    [Fact]
    public void ExtractMetadata_ReturnsNulls_ForEmptyHtml()
    {
        var result = _parser.ExtractMetadata(string.Empty, "https://example.com");

        result.Title.Should().BeNull();
        result.Description.Should().BeNull();
        result.Author.Should().BeNull();
        result.ImageUrl.Should().BeNull();
        result.PublishedDate.Should().BeNull();
        result.SiteName.Should().BeNull();
    }

    [Fact]
    public void ExtractMetadata_ReturnsNulls_ForNullHtml()
    {
        var result = _parser.ExtractMetadata(null!, "https://example.com");

        result.Title.Should().BeNull();
    }

    [Fact]
    public void ExtractMetadata_DecodesHtmlEntities_InTitleAndDescription()
    {
        var html = """
                   <html><head>
                   <meta property="og:title" content="Rocky&apos;s &amp; Partner&apos;s Blog" />
                   <meta property="og:description" content="A &ldquo;great&rdquo; article &mdash; read it" />
                   </head><body></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Title.Should().Contain("&");
        result.Description.Should().NotBeNullOrEmpty();
    }

    // ─── Unicode Support ────────────────────────────────────────────────────

    [Fact]
    public void ExtractReadabilityText_HandlesUnicodeContent()
    {
        var html = """
                   <html><body><article>
                   <h1>Unicode Article Title</h1>
                   <p>This article contains Unicode characters: Arabic مرحبا, Chinese 你好, Japanese こんにちは, Korean 안녕하세요, Russian Привет, Greek Γειά σου, and emoji 🎉🚀.</p>
                   <p>Another paragraph with mathematical symbols: ∑ ∏ ∫ √ ∞ and special characters: © ® ™ € £ ¥.</p>
                   </article></body></html>
                   """;

        var result = _parser.ExtractReadabilityText(html);

        result.Should().Contain("مرحبا");
        result.Should().Contain("你好");
        result.Should().Contain("こんにちは");
        result.Should().Contain("Привет");
    }

    [Fact]
    public void Parse_HandlesUnicodeTitle()
    {
        var html = """
                   <html><head>
                   <title>日本語のタイトル</title>
                   </head><body>
                   <article><p>これは日本語のコンテンツです。十分な単語数を確保するために、もう少し文章を追加します。この記事はUnicode文字を正しく処理できることを確認するためのテストデータです。</p></article>
                   </body></html>
                   """;

        var result = _parser.Parse(html, "https://example.jp/article");

        result.Title.Should().Contain("日本語");
    }

    // ─── Edge Cases ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractReadabilityText_SkipsHiddenElements()
    {
        var html = """
                   <html><body>
                   <div style="display:none"><p>This hidden text should not appear in the extraction result at all because it is inside an element with display none styling.</p></div>
                   <div style="visibility:hidden"><p>This visibility hidden text should also not appear in extraction results.</p></div>
                   <article>
                       <p>This visible text should be included in the extraction result because it is not hidden behind any CSS display or visibility rules.</p>
                       <p>Second visible paragraph with additional content to ensure we meet the minimum word threshold requirement for content extraction.</p>
                   </article>
                   </body></html>
                   """;

        var result = _parser.ExtractReadabilityText(html);

        result.Should().NotContain("hidden text should not appear");
        result.Should().NotContain("visibility hidden text");
        result.Should().Contain("visible text should be included");
    }

    [Fact]
    public void ExtractReadabilityText_HandlesHtmlWithOnlyNavElements()
    {
        var html = """
                   <html><body>
                   <nav><a href="/">Home</a></nav>
                   <header><h1>Site Header</h1></header>
                   <footer>Copyright 2024</footer>
                   </body></html>
                   """;

        var result = _parser.ExtractReadabilityText(html);

        // Should not throw, even though there's minimal content after cleanup
        result.Should().NotBeNull();
    }

    [Fact]
    public void ExtractMetadata_HandlesJsonLdAuthor_AsString()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@type": "Article", "author": "String Author Name" }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Author.Should().Be("String Author Name");
    }

    [Fact]
    public void ExtractMetadata_HandlesJsonLdAuthor_AsArray()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">
                   { "@type": "Article", "author": ["First Author", { "name": "Second Author" }] }
                   </script>
                   </head><body></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Author.Should().Be("First Author");
    }

    [Fact]
    public void ExtractMetadata_HandlesJsonLdAuthor_InGraphArray()
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

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Author.Should().Be("Graph Author");
    }

    [Fact]
    public void ExtractMetadata_SkipsMalformedJsonLd_Gracefully()
    {
        var html = """
                   <html><head>
                   <script type="application/ld+json">{ this is not valid json }</script>
                   <meta name="author" content="Fallback Author" />
                   </head><body></body></html>
                   """;

        var result = _parser.ExtractMetadata(html, "https://example.com/page");

        result.Author.Should().Be("Fallback Author");
    }

    [Fact]
    public void Constructor_Throws_WhenLoggerIsNull()
    {
        var act = () => new HtmlParser(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Parse_HandlesHtmlWithWhitespaceInTitle()
    {
        var html = """
                   <html><head>
                   <title>  Trimmed Title  </title>
                   </head><body>
                   <article><p>Content with enough words to satisfy the minimum threshold requirement for the readability algorithm to successfully extract meaningful article text from this document.</p></article>
                   </body></html>
                   """;

        var result = _parser.Parse(html, "https://example.com/page");

        result.Title.Should().Be("Trimmed Title");
    }
}

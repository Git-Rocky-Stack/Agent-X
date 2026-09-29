using AgentX.Core.Search.Models;
using AgentX.Core.Services.Chat;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Chat;

/// <summary>
/// The stored form of the sources an answer cites. Chat saved none, so a Research Mode answer
/// lost its sources once the conversation was reopened, and exports had nothing to list.
/// </summary>
public sealed class MessageCitationsTests
{
    [Fact]
    public void Web_sources_come_back_in_the_order_they_were_saved()
    {
        var sources = new[]
        {
            new WebCitation { Title = "Release notes", Url = "https://example.org/notes", Snippet = "Version 2 ships." },
            new WebCitation { Title = "Blog", Url = "https://example.org/blog", Snippet = "Why it changed." },
        };

        var parsed = MessageCitations.ParseWebCitations(MessageCitations.Serialize(sources));

        parsed.Select(citation => (citation.Title, citation.Url, citation.Snippet)).Should().Equal(
            ("Release notes", "https://example.org/notes", "Version 2 ships."),
            ("Blog", "https://example.org/blog", "Why it changed."));
        parsed.Should().OnlyContain(citation => citation.Source == WebCitationSource.Web);
    }

    [Fact]
    public void An_answer_without_sources_stores_nothing()
    {
        MessageCitations.Serialize(null).Should().BeNull();
        MessageCitations.Serialize(Array.Empty<WebCitation>()).Should().BeNull();
    }

    [Fact]
    public void Describe_names_a_web_page_by_title_and_address_and_a_document_by_file_and_page()
    {
        const string json = """
            [
              {"kind":"web","title":"Release notes","url":"https://example.org/notes","snippet":"Version 2 ships."},
              {"kind":"web","title":"","url":"https://example.org/untitled"},
              {"fileName":"report.pdf","pageNumber":7,"excerpt":"Quarterly revenue grew."},
              {"fileName":"notes.md"}
            ]
            """;

        MessageCitations.Describe(json).Should().Equal(
            "Release notes - https://example.org/notes",
            "https://example.org/untitled",
            "report.pdf, page 7 - \"Quarterly revenue grew.\"",
            "notes.md");
    }

    [Fact]
    public void Describe_shortens_a_long_document_excerpt()
    {
        var excerpt = new string('a', 100);
        var json = $$"""[{"fileName":"long.txt","excerpt":"{{excerpt}}"}]""";

        MessageCitations.Describe(json).Should().Equal($"long.txt - \"{new string('a', 80)}...\"");
    }

    [Fact]
    public void Only_web_entries_come_back_as_web_sources()
    {
        const string json = """[{"fileName":"report.pdf"},{"kind":"web","title":"Blog","url":"https://example.org/blog"}]""";

        MessageCitations.ParseWebCitations(json).Should().ContainSingle()
            .Which.Url.Should().Be("https://example.org/blog");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"kind\":\"web\"}")]
    [InlineData("[1, \"text\", null]")]
    public void Unusable_metadata_yields_no_sources(string? json)
    {
        MessageCitations.Describe(json).Should().BeEmpty();
        MessageCitations.ParseWebCitations(json).Should().BeEmpty();
    }

    [Fact]
    public void Entries_with_unexpected_value_types_are_read_without_failing()
    {
        // The exporters read entries with GetString and GetInt32, which throw on a number where
        // a file name belongs or on a fractional page number, and so failed the whole export.
        const string json = """[{"fileName":42,"pageNumber":3.5,"excerpt":true}]""";

        MessageCitations.Describe(json).Should().Equal("Unknown");
    }
}

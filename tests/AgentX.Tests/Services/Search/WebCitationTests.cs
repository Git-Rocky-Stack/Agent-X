using AgentX.Core.Search.Models;
using AgentX.Core.Services.Search;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class WebCitationTests
{
    [Fact]
    public void WebCitation_DefaultValues_AreEmptyStrings()
    {
        var citation = new WebCitation();

        citation.Title.Should().BeEmpty();
        citation.Url.Should().BeEmpty();
        citation.Snippet.Should().BeEmpty();
        citation.Source.Should().Be(WebCitationSource.Vault); // default enum value
        citation.DocumentName.Should().BeNull();
    }

    [Fact]
    public void WebCitation_InitValues_AreSetCorrectly()
    {
        var citation = new WebCitation
        {
            Title = "Research Paper",
            Url = "https://arxiv.org/abs/1234",
            Snippet = "Key finding about...",
            Source = WebCitationSource.Web,
            DocumentName = null
        };

        citation.Title.Should().Be("Research Paper");
        citation.Url.Should().Be("https://arxiv.org/abs/1234");
        citation.Snippet.Should().Be("Key finding about...");
        citation.Source.Should().Be(WebCitationSource.Web);
        citation.DocumentName.Should().BeNull();
    }

    [Fact]
    public void WebCitation_VaultSource_CanReferenceDocument()
    {
        var citation = new WebCitation
        {
            Title = "Internal Document",
            Url = @"C:\Users\Docs\report.pdf",
            Snippet = "Summary of findings...",
            Source = WebCitationSource.Vault,
            DocumentName = "report.pdf"
        };

        citation.Source.Should().Be(WebCitationSource.Vault);
        citation.DocumentName.Should().Be("report.pdf");
    }

    [Fact]
    public void WebCitationSource_Enum_HasExpectedValues()
    {
        Enum.GetNames<WebCitationSource>().Should().Contain(["Vault", "Web"]);
    }

    [Fact]
    public void RagResponse_WebCitations_DefaultIsNull()
    {
        var response = new AgentX.Core.Search.Models.RagResponse();
        response.WebCitations.Should().BeNull();
    }

    [Fact]
    public void RagResponse_WebCitations_CanBeSet()
    {
        var webCitations = new List<WebCitation>
        {
            new() { Title = "Web Source 1", Url = "https://example.com", Source = WebCitationSource.Web },
            new() { Title = "Web Source 2", Url = "https://example.org", Source = WebCitationSource.Web }
        };

        var response = new AgentX.Core.Search.Models.RagResponse
        {
            WebCitations = webCitations
        };

        response.WebCitations.Should().NotBeNull();
        response.WebCitations.Should().HaveCount(2);
    }
}

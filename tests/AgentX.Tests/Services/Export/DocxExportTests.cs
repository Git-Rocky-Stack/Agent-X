using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

public class DocxExportTests
{
    [Fact]
    public void ExportFormat_Enum_ContainsDocx()
    {
        var format = ExportFormat.Docx;
        ((int)format).Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void ExportFormat_Enum_ContainsPptx()
    {
        var format = ExportFormat.Pptx;
        ((int)format).Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void ExportFormat_Has8Members()
    {
        var values = Enum.GetValues<ExportFormat>();
        values.Should().HaveCount(8);
    }

    [Fact]
    public void ExportFormat_Docx_IsDistinctFromOtherFormats()
    {
        var format = ExportFormat.Docx;
        format.Should().NotBe(ExportFormat.Markdown);
        format.Should().NotBe(ExportFormat.Html);
        format.Should().NotBe(ExportFormat.Pdf);
        format.Should().NotBe(ExportFormat.Json);
        format.Should().NotBe(ExportFormat.PlainText);
        format.Should().NotBe(ExportFormat.Csv);
        format.Should().NotBe(ExportFormat.Pptx);
    }

    [Fact]
    public void ExportFormat_Pptx_IsDistinctFromOtherFormats()
    {
        var format = ExportFormat.Pptx;
        format.Should().NotBe(ExportFormat.Markdown);
        format.Should().NotBe(ExportFormat.Html);
        format.Should().NotBe(ExportFormat.Pdf);
        format.Should().NotBe(ExportFormat.Json);
        format.Should().NotBe(ExportFormat.PlainText);
        format.Should().NotBe(ExportFormat.Csv);
        format.Should().NotBe(ExportFormat.Docx);
    }
}
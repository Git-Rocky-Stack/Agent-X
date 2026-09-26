using AgentX.Core.Documents;
using AgentX.Core.Documents.Processors;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Documents.Processors;

/// <summary>
/// Tests for <see cref="DocxProcessor"/>: the formats it claims and how unreadable files are
/// reported.
/// </summary>
public sealed class DocxProcessorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly DocxProcessor _processor = new();

    public DocxProcessorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "agentx-docxprocessor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void SupportedExtensions_DoNotIncludeTheLegacyBinaryWordFormat()
    {
        // The OpenXml SDK cannot open legacy binary Word files; claiming them produced imports
        // that failed only after the user had picked the file.
        _processor.SupportedExtensions.Should().BeEquivalentTo(new[] { ".docx" });
        _processor.CanProcess("report.doc").Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_CorruptFile_ThrowsAnExtractionFailure()
    {
        var path = Path.Combine(_tempDirectory, "broken.docx");
        await File.WriteAllTextAsync(path, "not a zip package");

        var act = () => _processor.ProcessAsync(path);

        (await act.Should().ThrowAsync<DocumentExtractionException>())
            .Which.Message.Should().Contain("broken.docx");
    }
}

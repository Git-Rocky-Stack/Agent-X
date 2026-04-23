using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Intelligence;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class HierarchicalSummaryServiceTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly ILogger _logger = Log.ForContext<HierarchicalSummaryServiceTests>();

    [Fact]
    public async Task BuildSummaryAsync_with_multiple_sections_returns_layered_result()
    {
        var responses = new Queue<string>(
        [
            "Section one summary",
            "Section two summary",
            "Combined document summary",
            "1. First key point\n2. Second key point"
        ]);

        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => responses.Dequeue());

        var sut = new HierarchicalSummaryService(_aiService.Object, _logger);

        var result = await sut.BuildSummaryAsync(
            "Quarterly Review",
            ["Section alpha content", "Section beta content"]);

        result.DocumentTitle.Should().Be("Quarterly Review");
        result.DocumentSummary.Should().Be("Combined document summary");
        result.SectionSummaries.Should().Equal("Section one summary", "Section two summary");
        result.KeyPoints.Should().Equal("First key point", "Second key point");
        result.TotalSections.Should().Be(2);
        result.SectionsIncluded.Should().Be(2);
        result.WasSectionLimitApplied.Should().BeFalse();

        _aiService.Verify(service => service.ChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task BuildSummaryAsync_limits_sections_and_marks_truncation()
    {
        var responses = new Queue<string>(
        [
            "Summary 1",
            "Summary 2",
            "Summary 3",
            "Summary 4",
            "Summary 5",
            "Summary 6",
            "Synthesized summary",
            "1. Single point"
        ]);

        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => responses.Dequeue());

        var sut = new HierarchicalSummaryService(_aiService.Object, _logger);

        var result = await sut.BuildSummaryAsync(
            "Architecture Notes",
            Enumerable.Range(1, 8).Select(index => $"Section {index}").ToList());

        result.DocumentSummary.Should().Be("Synthesized summary");
        result.SectionSummaries.Should().HaveCount(6);
        result.TotalSections.Should().Be(8);
        result.SectionsIncluded.Should().Be(6);
        result.WasSectionLimitApplied.Should().BeTrue();
        result.KeyPoints.Should().ContainSingle().Which.Should().Be("Single point");

        _aiService.Verify(service => service.ChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(8));
    }
}

using AgentX.Core.AI;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ComparisonService"/>.
/// Since ComparisonService depends on IAiService, IDocumentService, and ISemanticSearchService,
/// we use Moq to create test doubles.
/// </summary>
public sealed class ComparisonServiceTests : IDisposable
{
    private readonly Mock<IAiService> _mockAiService;
    private readonly Mock<IDocumentService> _mockDocumentService;
    private readonly Mock<ISemanticSearchService> _mockSearchService;
    private readonly ILogger _logger;
    private readonly ComparisonService _sut;

    public ComparisonServiceTests()
    {
        _mockAiService = new Mock<IAiService>();
        _mockDocumentService = new Mock<IDocumentService>();
        _mockSearchService = new Mock<ISemanticSearchService>();
        _logger = Log.ForContext<ComparisonService>();

        _sut = new ComparisonService(
            _mockAiService.Object,
            _mockDocumentService.Object,
            _mockSearchService.Object,
            _logger);
    }

    public void Dispose()
    {
        // ComparisonService has no disposable resources
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Constructor Validation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WithNullAiService_ThrowsArgumentNullException()
    {
        var act = () => new ComparisonService(
            null!,
            _mockDocumentService.Object,
            _mockSearchService.Object,
            _logger);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullDocumentService_ThrowsArgumentNullException()
    {
        var act = () => new ComparisonService(
            _mockAiService.Object,
            null!,
            _mockSearchService.Object,
            _logger);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullSearchService_ThrowsArgumentNullException()
    {
        var act = () => new ComparisonService(
            _mockAiService.Object,
            _mockDocumentService.Object,
            null!,
            _logger);

        act.Should().Throw<ArgumentNullException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — Input Validation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_WithEmptyDocumentIds_ThrowsArgumentException()
    {
        var act = () => _sut.CompareDocumentsAsync(Array.Empty<long>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompareDocumentsAsync_WithSingleDocumentId_ThrowsArgumentException()
    {
        var act = () => _sut.CompareDocumentsAsync([1]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompareDocumentsAsync_WithNullDocumentIds_ThrowsArgumentException()
    {
        var act = () => _sut.CompareDocumentsAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExportComparisonAsMarkdown
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportComparisonAsMarkdownAsync_WithNullReport_ThrowsArgumentNullException()
    {
        var act = () => _sut.ExportComparisonAsMarkdownAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
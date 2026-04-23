using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class ComparisonViewModelTests
{
    private readonly Mock<IComparisonService> _comparisonService = new();
    private readonly Mock<IDocumentService> _documentService = new();

    [Fact]
    public async Task InitializeAsync_and_checkbox_changes_keep_selected_documents_in_sync()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateDocument(1, "alpha.md"),
                CreateDocument(2, "beta.md"),
                CreateDocument(3, "gamma.md"),
            ]);

        var viewModel = new ComparisonViewModel(_comparisonService.Object, _documentService.Object);

        await viewModel.InitializeAsync();

        viewModel.AvailableDocuments.Should().HaveCount(3);
        viewModel.SelectedDocuments.Should().BeEmpty();

        viewModel.AvailableDocuments[0].IsSelected = true;
        viewModel.AvailableDocuments[2].IsSelected = true;

        viewModel.SelectedDocuments.Select(item => item.Id)
            .Should()
            .BeEquivalentTo([1L, 3L], options => options.WithStrictOrdering());

        viewModel.AvailableDocuments[0].IsSelected = false;

        viewModel.SelectedDocuments.Select(item => item.Id)
            .Should()
            .BeEquivalentTo([3L], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task CompareDocumentsAsync_populates_unique_points_from_report()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateDocument(11, "roadmap.md"),
                CreateDocument(12, "research.md"),
            ]);

        _comparisonService
            .Setup(service => service.CompareDocumentsAsync(
                It.IsAny<IReadOnlyList<long>>(),
                It.IsAny<ComparisonOptions?>(),
                It.IsAny<IProgress<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComparisonReport
            {
                Summary = "Shared platform direction with document-specific findings.",
                Similarities = ["Both discuss local AI infrastructure."],
                UniquePoints = new Dictionary<string, List<string>>
                {
                    ["roadmap.md"] = ["Contains milestone sequencing for rollout."],
                    ["research.md"] = ["Includes external benchmark references."]
                },
                DurationMs = 321,
                TotalTokensUsed = 1234
            });

        var viewModel = new ComparisonViewModel(_comparisonService.Object, _documentService.Object);
        await viewModel.InitializeAsync();

        viewModel.AvailableDocuments[0].IsSelected = true;
        viewModel.AvailableDocuments[1].IsSelected = true;

        await viewModel.CompareDocumentsCommand.ExecuteAsync(null);

        viewModel.HasReport.Should().BeTrue();
        viewModel.HasUniquePoints.Should().BeTrue();
        viewModel.ReportSummary.Should().Contain("document-specific findings");
        viewModel.UniquePoints.Should().HaveCount(2);
        viewModel.UniquePoints[0].DocumentName.Should().Be("roadmap.md");
        viewModel.UniquePoints[0].Points.Should().ContainSingle()
            .Which.Should().Be("Contains milestone sequencing for rollout.");
        viewModel.UniquePoints[1].DocumentName.Should().Be("research.md");
        viewModel.StatusMessage.Should().Contain("Comparison complete");
    }

    private static DocumentEntity CreateDocument(long id, string fileName)
    {
        return new DocumentEntity
        {
            Id = id,
            FileName = fileName,
            FilePath = $@"C:\docs\{fileName}",
            FileType = Path.GetExtension(fileName).TrimStart('.'),
            ContentHash = $"hash-{id}",
            FileSizeBytes = 1024,
            ImportedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            FileModifiedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            IndexingStatus = "completed"
        };
    }
}

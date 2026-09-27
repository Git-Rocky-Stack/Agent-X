using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class DigestViewModelTests
{
    private readonly Mock<IDigestService> _digestService = new();

    [Fact]
    public async Task InitializeAsync_without_reports_says_how_to_get_one()
    {
        _digestService.Setup(service => service.GetReportHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DigestReportEntity>());
        var viewModel = CreateViewModel();
        viewModel.StatusMessage.Should().Be("No digest reports yet");

        await viewModel.InitializeAsync();

        viewModel.HasReport.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("No digest reports yet. Generate one to see your weekly summary.");
    }

    [Fact]
    public async Task InitializeAsync_formats_the_dates_and_trends_from_the_resources()
    {
        // The generated time used an English "at" inside its date pattern, and the trend
        // labels were English literals; both now come from the resources.
        var generatedAt = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Local).ToUniversalTime();
        var report = new DigestReportEntity
        {
            Id = 3,
            GeneratedAt = generatedAt,
            PeriodStart = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            PeriodEnd = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            IsRead = true,
            TopSearchesJson = "[{\"query\":\"budget\",\"count\":5,\"previousCount\":2,\"deltaCount\":3,\"trend\":\"up\"}," +
                              "{\"query\":\"hiring\",\"count\":1,\"previousCount\":3,\"deltaCount\":-2,\"trend\":\"down\"}]",
            TopCollectionsJson = "[{\"name\":\"Research\",\"count\":4,\"previousCount\":0,\"deltaCount\":4,\"trend\":\"new\"}]",
            FileTypeBreakdownJson = "[{\"type\":\"pdf\",\"count\":2,\"previousCount\":2,\"deltaCount\":0,\"trend\":\"flat\"}]"
        };
        _digestService.Setup(service => service.GetLatestReportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _digestService.Setup(service => service.GetReportHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { report });
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        var shown = viewModel.CurrentReport!;
        shown.GeneratedAtFormatted.Should().Be(generatedAt.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt"));
        shown.PeriodFormatted.Should().Be(
            $"{report.PeriodStart.ToLocalTime():MMM d} - {report.PeriodEnd.ToLocalTime():MMM d, yyyy}");
        shown.ShortPeriodFormatted.Should().Be(
            $"{report.PeriodStart.ToLocalTime():MMM d} - {report.PeriodEnd.ToLocalTime():MMM d}");
        viewModel.StatusMessage.Should().Be($"Last generated {shown.GeneratedAtFormatted}");
        shown.TopSearches.Select(item => item.TrendLabel).Should().Equal("+3 vs prior period", "-2 vs prior period");
        shown.TopCollections.Single().TrendLabel.Should().Be("new this period");
        shown.FileTypeBreakdown.Single().TrendLabel.Should().Be("flat vs prior period");
        viewModel.ReportHistory.Single().ShortPeriodFormatted.Should().Be(shown.ShortPeriodFormatted);
    }

    [Fact]
    public async Task GenerateDigestAsync_reports_a_failure()
    {
        _digestService.Setup(service => service.GenerateDigestAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database locked"));
        var viewModel = CreateViewModel();

        await viewModel.GenerateDigestCommand.ExecuteAsync(null);

        viewModel.StatusMessage.Should().Be("Failed to generate digest");
        viewModel.IsGenerating.Should().BeFalse();
    }

    private DigestViewModel CreateViewModel() => new(_digestService.Object, EnglishResources.Create());
}

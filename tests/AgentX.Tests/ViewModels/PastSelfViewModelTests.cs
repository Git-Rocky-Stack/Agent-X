using AgentX.App.ViewModels;
using AgentX.Core.Services.TemporalIdentity;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class PastSelfViewModelTests
{
    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();

    // ── Voice profile ────────────────────────────────────────────────────────
    // With no captured samples the panel used to show an invented 15-word average and a
    // "Balanced" style, which reads as a measurement of the user's writing rather than
    // the absence of one.

    [Fact]
    public async Task LoadVoiceProfileAsync_WithNoSamples_ReportsNoMeasurementRatherThanInventedOnes()
    {
        _temporalIdentity
            .Setup(service => service.GetVoiceProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentX.Core.Services.TemporalIdentity.Models.VoiceProfileEntity?)null);

        var viewModel = new PastSelfViewModel(_temporalIdentity.Object);

        await viewModel.LoadVoiceProfileCommand.ExecuteAsync(null);

        viewModel.VoiceProfile.Should().NotBeNull();
        viewModel.VoiceProfile!.SampleCount.Should().Be(0);
        viewModel.VoiceProfile.AvgSentenceLength.Should().Be(0);
        viewModel.VoiceProfile.FormalityLabel.Should().Be("Not enough data");
    }

    // ── Relevant insights ────────────────────────────────────────────────────
    // PastSelfResult raises no change notifications, and the insights were written onto the
    // result after it had been published, so the insights section never appeared.

    [Fact]
    public async Task GetRelevantInsightsAsync_AfterASearch_PublishesAResultThatCarriesTheInsights()
    {
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync("remote work", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentX.Core.Services.TemporalIdentity.Models.PastSelfResponse
            {
                Topic = "remote work",
                Stance = "in favour",
                EvidenceExcerpts = [],
                RelatedConversations = [],
                RelatedDocuments = []
            });
        SetupInsights("Async standups beat meetings");
        var viewModel = new PastSelfViewModel(_temporalIdentity.Object) { SearchQuery = "remote work" };
        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);
        var searchResult = viewModel.CurrentResult;

        PastSelfResult? published = null;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PastSelfViewModel.CurrentResult))
            {
                published = viewModel.CurrentResult;
            }
        };

        await viewModel.GetRelevantInsightsCommand.ExecuteAsync(null);

        published.Should().NotBeNull().And.NotBeSameAs(searchResult);
        published!.RelevantInsights.Should().ContainSingle(insight => insight.Insight == "Async standups beat meetings");
        published.Stance.Should().Be("in favour", "the search result is kept and extended");
    }

    [Fact]
    public async Task GetRelevantInsightsAsync_WithoutAResult_PublishesTheInsights()
    {
        SetupInsights("Async standups beat meetings");
        var viewModel = new PastSelfViewModel(_temporalIdentity.Object) { SearchQuery = "remote work" };

        await viewModel.GetRelevantInsightsCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.RelevantInsights.Should().ContainSingle();
    }

    // ── Time ranges ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.5, "today")]
    [InlineData(7, "about a week ago")]
    [InlineData(21, "about 3 weeks ago")]
    [InlineData(30, "about a month ago")]
    [InlineData(120, "about 4 months ago")]
    [InlineData(365, "about a year ago")]
    [InlineData(1100, "about 3 years ago")]
    public void FormatTimeAgo_DescribesTheRangeInWords(double daysAgo, string expected)
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

        PastSelfViewModel.FormatTimeAgo(now.AddDays(-daysAgo), now).Should().Be(expected);
    }

    [Fact]
    public async Task SearchPastSelfAsync_ForAllTime_ReadsAsASentence()
    {
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync("remote work", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentX.Core.Services.TemporalIdentity.Models.PastSelfResponse
            {
                Topic = "remote work",
                EvidenceExcerpts = [],
                RelatedConversations = [],
                RelatedDocuments = []
            });
        var viewModel = new PastSelfViewModel(_temporalIdentity.Object)
        {
            SearchQuery = "remote work",
            SelectedTimeRange = 0
        };

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.Message.Should().Be("Here's what you thought about remote work.");
    }

    [Fact]
    public void SelectedTimeRange_StartsOnThePastMonth_TheChoiceThePageShows()
    {
        // The page used to force this back to the past month on every visit while its radio
        // buttons kept showing the operator's last choice.
        new PastSelfViewModel(_temporalIdentity.Object).SelectedTimeRange.Should().Be(2);
    }

    private void SetupInsights(string insight) =>
        _temporalIdentity
            .Setup(service => service.GetRelevantInsightsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AgentX.Core.Services.TemporalIdentity.Models.ResurfacedInsight
                {
                    Insight = insight,
                    RelevanceReason = "same topic",
                    Context = "chat"
                }
            ]);

    [Fact]
    public void FormalityLabel_WithSamples_DescribesTheMeasuredStyle()
    {
        new VoiceProfileDisplay { SampleCount = 40, FormalityScore = 0.1 }
            .FormalityLabel.Should().Be("Casual");
        new VoiceProfileDisplay { SampleCount = 40, FormalityScore = 0.5 }
            .FormalityLabel.Should().Be("Balanced");
        new VoiceProfileDisplay { SampleCount = 40, FormalityScore = 0.9 }
            .FormalityLabel.Should().Be("Formal");
    }
}

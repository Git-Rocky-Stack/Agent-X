using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class PastSelfViewModelTests
{
    private const string RemoteWork = "Remote work is better for focus";

    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();
    private readonly Mock<IAiService> _ai = new();
    private readonly Mock<ILocalizationService> _localization = new();

    /// <summary>The system prompt the provider was sent, once it was.</summary>
    private string? _instructions;

    public PastSelfViewModelTests()
    {
        // Resource lookups come back as their keys and arguments.
        _localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        _localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}: {string.Join(" | ", args)}");
    }

    /// <summary>
    /// The view model over the real draft service, whose AI provider and records are mocked. The
    /// resources come back as their keys and arguments unless <paramref name="localization"/>
    /// (such as <see cref="EnglishResources"/>) is given.
    /// </summary>
    private PastSelfViewModel CreateViewModel(ILocalizationService? localization = null) => new(
        _temporalIdentity.Object,
        new VoiceDraftService(_temporalIdentity.Object, _ai.Object, Logger.None),
        localization ?? _localization.Object);

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

        var viewModel = CreateViewModel(EnglishResources.Create());

        await viewModel.LoadVoiceProfileCommand.ExecuteAsync(null);

        viewModel.VoiceProfile.Should().NotBeNull();
        viewModel.VoiceProfile!.SampleCount.Should().Be(0);
        viewModel.VoiceProfile.AvgSentenceLength.Should().Be(0);
        viewModel.VoiceProfile.FormalityLabel.Should().Be("Not enough data");
    }

    // --- Relevant insights ---
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
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "remote work";
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
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "remote work";

        await viewModel.GetRelevantInsightsCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.RelevantInsights.Should().ContainSingle();
    }

    // --- Time ranges ---

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

        PastSelfViewModel.FormatTimeAgo(EnglishResources.Create(), now.AddDays(-daysAgo), now).Should().Be(expected);
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
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "remote work";
        viewModel.SelectedTimeRange = 0;

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.Message.Should().Be("Here's what you thought about remote work.");
    }

    [Fact]
    public void SelectedTimeRange_StartsOnThePastMonth_TheChoiceThePageShows()
    {
        // The page used to force this back to the past month on every visit while its radio
        // buttons kept showing the operator's last choice.
        CreateViewModel().SelectedTimeRange.Should().Be(2);
    }

    private void SetUpLookup(PastSelfResponse? response) =>
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private void SetUpActiveTopics(params ActiveTopic[] topics) =>
        _temporalIdentity
            .Setup(service => service.GetActiveTopicDetailsAsync(30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics.ToList());

    private void SetupInsights(string insight) =>
        _temporalIdentity
            .Setup(service => service.GetRelevantInsightsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AgentX.Core.Services.TemporalIdentity.Models.ResurfacedInsight
                {
                    Insight = insight,
                    RelatedTopics = ["remote work", "team rituals", "third topic"]
                }
            ]);

    [Theory]
    [InlineData(0.1, "Casual")]
    [InlineData(0.5, "Balanced")]
    [InlineData(0.9, "Formal")]
    public async Task LoadVoiceProfileAsync_WithSamples_DescribesTheMeasuredStyle(double formality, string expected)
    {
        _temporalIdentity
            .Setup(service => service.GetVoiceProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceProfileEntity { SampleCount = 40, FormalityScore = formality });
        var viewModel = CreateViewModel(EnglishResources.Create());

        await viewModel.LoadVoiceProfileCommand.ExecuteAsync(null);

        viewModel.VoiceProfile!.FormalityLabel.Should().Be(expected);
    }

    [Fact]
    public async Task LoadVoiceProfileAsync_ShowsTheSentenceLengthToOneDecimal()
    {
        // The bound double printed in full, such as 14.100000000000001.
        _temporalIdentity
            .Setup(service => service.GetVoiceProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceProfileEntity { SampleCount = 3, AvgSentenceLength = (15 * 0.9) + (6 * 0.1) });
        var viewModel = CreateViewModel(EnglishResources.Create());

        await viewModel.LoadVoiceProfileCommand.ExecuteAsync(null);

        viewModel.VoiceProfile!.AvgSentenceLengthText.Should().Be(14.1.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));
    }

    // --- Messages in the user's language ---
    // The page's messages, the style labels and the insight reasons were English literals in the
    // view model and in Core; they are resources now.

    [Fact]
    public async Task PageMessages_AreReadFromTheResources()
    {
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PastSelfResponse?)null);
        _temporalIdentity
            .Setup(service => service.GetBeliefEvolutionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemporalBeliefEntity
            {
                Topic = "Remote work",
                HasEvolved = true,
                FirstDetectedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            });
        SetUpActiveTopics(
            new ActiveTopic("Remote work", DateTime.UtcNow.AddDays(-9), DateTime.UtcNow.AddDays(-2)),
            new ActiveTopic("Async standups", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1)));
        _temporalIdentity
            .Setup(service => service.GetVoiceProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceProfileEntity { SampleCount = 40, FormalityScore = 0.9 });
        SetupInsights("Async standups beat meetings");
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "remote work";

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);
        viewModel.CurrentResult!.Message.Should().Be("PastSelf_NoRecords: remote work");

        await viewModel.ShowBeliefEvolutionCommand.ExecuteAsync(null);
        viewModel.CurrentResult!.Message.Should().StartWith("PastSelf_BeliefEvolved: Remote work | ");

        await viewModel.GetRelevantInsightsCommand.ExecuteAsync(null);
        viewModel.CurrentResult!.RelevantInsights!.Single().RelevanceReason
            .Should().Be("PastSelf_InsightRelatedTo: remote work, team rituals", "the first two topics are named");

        await viewModel.GetActiveTopicsCommand.ExecuteAsync(null);
        viewModel.ActiveTopicsStatus.Should().Be("PastSelf_ActiveTopicsFound: 2");
        viewModel.ActiveTopics[1].Recorded.Should().StartWith("PastSelf_TopicRecorded: ");

        await viewModel.LoadVoiceProfileCommand.ExecuteAsync(null);
        viewModel.VoiceProfile!.FormalityLabel.Should().Be("PastSelf_StyleFormal");
    }

    [Fact]
    public async Task SearchPastSelfAsync_ForAPastPeriod_SaysWhenInEnglishToo()
    {
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync("remote work", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PastSelfResponse
            {
                Topic = "Remote work",
                EvidenceExcerpts = [],
                RelatedConversations = [],
                RelatedDocuments = []
            });
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "remote work";
        viewModel.SelectedTimeRange = 1;

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.Message.Should().Be("Here's what you thought about Remote work about a week ago.");
    }

    [Fact]
    public async Task SearchPastSelfAsync_Failing_ReportsTheErrorFromTheResources()
    {
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "remote work";

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        viewModel.ErrorMessage.Should().Be("Could not search Past Self: database is locked");
    }

    // --- Belief evolution ---

    [Fact]
    public async Task ShowBeliefEvolutionAsync_ShowsTheRecordedConfidence()
    {
        // The result carried no confidence, so the page's Confidence bar read 0 for every belief.
        _temporalIdentity
            .Setup(service => service.GetBeliefEvolutionAsync("remote work", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemporalBeliefEntity
            {
                Topic = "remote work",
                CurrentStance = "in favour",
                ConfidenceLevel = 0.8,
                FirstDetectedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            });
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "remote work";

        await viewModel.ShowBeliefEvolutionCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.Confidence.Should().Be(0.8);
    }

    [Fact]
    public async Task ShowBeliefEvolutionAsync_ForAChangedView_ShowsTheEarlierViewAndTodaysSinceWhen()
    {
        // The result carried only today's stance, so the earlier view never appeared.
        var changedAt = new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);
        _temporalIdentity
            .Setup(service => service.GetBeliefEvolutionAsync("monoliths", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemporalBeliefEntity
            {
                Topic = "Monoliths",
                PreviousStance = "I think that monoliths never scale",
                CurrentStance = "Monoliths are fine at small scale",
                HasEvolved = true,
                StanceChangedAt = changedAt,
                ConfidenceLevel = 0.7,
                FirstDetectedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            });
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "monoliths";

        await viewModel.ShowBeliefEvolutionCommand.ExecuteAsync(null);

        var result = viewModel.CurrentResult!;
        result.Stance.Should().Be("I think that monoliths never scale", "the earlier view leads");
        result.ShowsEvolution.Should().BeTrue();
        result.CurrentStance.Should().Be("Monoliths are fine at small scale");
        result.CurrentStanceLabel.Should().Be(
            "Your view since " + changedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
    }

    // --- A view that changed after the chosen time ---
    // The page's "Your view has evolved" badge was hard-coded Collapsed, and today's stance and
    // when it changed, which the lookup returns, were never shown.

    [Fact]
    public async Task SearchPastSelfAsync_WhenTheViewChangedAfterTheChosenTime_ShowsTodaysViewAndSinceWhen()
    {
        var changedAt = new DateTime(2026, 9, 12, 15, 30, 0, DateTimeKind.Utc);
        SetUpLookup(new PastSelfResponse
        {
            Topic = "Monoliths",
            Stance = "I think that monoliths never scale",
            HasEvolved = true,
            CurrentStance = "Monoliths are fine at small scale",
            StanceChangedAt = changedAt,
            EvidenceExcerpts = [],
            RelatedConversations = [],
            RelatedDocuments = []
        });
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "monoliths";

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        var result = viewModel.CurrentResult!;
        result.HasStance.Should().BeTrue();
        result.Stance.Should().Be("I think that monoliths never scale", "the stance held then leads");
        result.ShowsEvolution.Should().BeTrue();
        result.CurrentStance.Should().Be("Monoliths are fine at small scale");
        result.CurrentStanceLabel.Should().Be(
            "Your view since " + changedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
    }

    [Fact]
    public async Task SearchPastSelfAsync_WhenTheViewHasNotChangedSince_ShowsNoEvolution()
    {
        SetUpLookup(new PastSelfResponse
        {
            Topic = "Monoliths",
            Stance = "Monoliths are fine at small scale",
            EvidenceExcerpts = [],
            RelatedConversations = [],
            RelatedDocuments = []
        });
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "monoliths";

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.ShowsEvolution.Should().BeFalse();
        viewModel.CurrentResult.CurrentStanceLabel.Should().BeEmpty();
        viewModel.CurrentResult.HasStance.Should().BeTrue();
    }

    [Fact]
    public async Task SearchPastSelfAsync_WithNothingRecorded_ShowsTheMessageWithoutAStanceOrConfidence()
    {
        // The page showed an empty stance, a Confidence bar at 0 and empty related lists under
        // "No records found".
        SetUpLookup(null);
        var viewModel = CreateViewModel(EnglishResources.Create());
        viewModel.SearchQuery = "monoliths";

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        var result = viewModel.CurrentResult!;
        result.Message.Should().Be("No records found about \"monoliths\" from the selected time period.");
        result.HasStance.Should().BeFalse();
        result.ShowsEvolution.Should().BeFalse();
        result.HasRelatedItems.Should().BeFalse();
    }

    [Fact]
    public async Task SearchPastSelfAsync_WithRelatedItems_ListsThem()
    {
        SetUpLookup(new PastSelfResponse
        {
            Topic = "Monoliths",
            Stance = "Monoliths are fine at small scale",
            EvidenceExcerpts = [],
            RelatedConversations = [],
            RelatedDocuments = ["monoliths.pdf"]
        });
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "monoliths";

        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);

        viewModel.CurrentResult!.HasRelatedItems.Should().BeTrue();
    }

    // --- Active topics ---
    // Get Active Topics published its topics as a belief result called "Active Topics", so the
    // page showed an empty stance and a Confidence bar at 0 for something that is not a belief.

    [Fact]
    public async Task GetActiveTopicsAsync_ListsTheTopicsAsRecordedWithWhen_AndPublishesNoBeliefResult()
    {
        var now = DateTime.UtcNow;
        SetUpActiveTopics(
            new ActiveTopic("Remote work is better for focus", now.AddDays(-20).AddMinutes(-1), now.AddDays(-3).AddMinutes(-1)),
            new ActiveTopic("Ai safety matters", now.AddHours(-5).AddMinutes(-1), now.AddHours(-5).AddMinutes(-1)));
        var viewModel = CreateViewModel(EnglishResources.Create());

        await viewModel.GetActiveTopicsCommand.ExecuteAsync(null);

        viewModel.CurrentResult.Should().BeNull("a topic list has no stance and no confidence");
        viewModel.ActiveTopicsStatus.Should().Be("Topics explored recently: 2");
        viewModel.ActiveTopics.Select(topic => topic.Topic)
            .Should().Equal(["Remote work is better for focus", "Ai safety matters"], "the recorded wording is what a search finds");
        viewModel.ActiveTopics[0].Recorded.Should().Be("First recorded 20d ago; last recorded 3d ago");
        viewModel.ActiveTopics[1].Recorded.Should().Be("Recorded 5h ago", "one time when both read the same");
    }

    [Fact]
    public async Task GetActiveTopicsAsync_AfterASearch_LeavesTheSearchResultAsItWas()
    {
        SetUpLookup(new PastSelfResponse
        {
            Topic = "Monoliths",
            Stance = "Monoliths are fine at small scale",
            EvidenceExcerpts = [],
            RelatedConversations = [],
            RelatedDocuments = []
        });
        SetUpActiveTopics(new ActiveTopic("Monoliths", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1)));
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "monoliths";
        await viewModel.SearchPastSelfCommand.ExecuteAsync(null);
        var searchResult = viewModel.CurrentResult;

        await viewModel.GetActiveTopicsCommand.ExecuteAsync(null);

        viewModel.CurrentResult.Should().BeSameAs(searchResult);
        viewModel.ActiveTopics.Should().ContainSingle(topic => topic.Topic == "Monoliths");
    }

    [Fact]
    public async Task GetActiveTopicsAsync_WithNone_SaysSoInTheTopicsPanel()
    {
        SetUpActiveTopics();
        var viewModel = CreateViewModel(EnglishResources.Create());

        await viewModel.GetActiveTopicsCommand.ExecuteAsync(null);

        viewModel.ActiveTopics.Should().BeEmpty();
        viewModel.ActiveTopicsStatus.Should().Be("No active topics detected in the past 30 days.");
        viewModel.CurrentResult.Should().BeNull();
    }

    // --- Draft as Me ---
    // The draft used to be canned template sentences assembled around the context, with no
    // model involved, shown as text written in the user's voice. The active AI provider now
    // writes it from the voice profile and the views held at the chosen time.

    [Fact]
    public async Task GenerateDraftAsMe_ShowsTheDraftTheProviderWrites_AndWhatItWasBasedOn()
    {
        SetUpRecords(Profile(samples: 24), topics: [RemoteWork], stance: "I think that remote work is better for focus");
        SetUpProvider(_ => Pieces("Hi Dana, ", "two remote days ", "would help me focus. "));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A note to my manager asking for two remote days a week";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftContent.Should().Be("Hi Dana, two remote days would help me focus.");
        viewModel.DraftErrorMessage.Should().BeNull();
        viewModel.DraftStatus.Should().BeEmpty();
        viewModel.IsGeneratingDraft.Should().BeFalse();
        viewModel.DraftBasis.Should().Contain("PastSelf_DraftBasisModel: llama3.2 (Ollama)")
            .And.Contain("PastSelf_DraftBasisVoice")
            .And.Contain("PastSelf_DraftBasisViews: ")
            .And.Contain(RemoteWork);
        viewModel.VoiceProfile!.SampleCount.Should().Be(24);
    }

    [Fact]
    public async Task GenerateDraftAsMe_FollowsTheViewsHeldAtTheTimePeriodChosenAbove()
    {
        var chosen = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        SetUpRecords(Profile(samples: 24), topics: [RemoteWork]);
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync(RemoteWork, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, DateTime? at, CancellationToken _) =>
                Stance(topic, at == chosen ? "I think that remote work is lonely" : "a view from another time"));
        SetUpProvider(_ => Pieces("Draft."));
        var viewModel = CreateViewModel();
        viewModel.SelectedTimeRange = 4;
        viewModel.SelectedDate = chosen;
        viewModel.DraftContext = "Reply to the remote work survey";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        _instructions.Should().Contain("\"I think that remote work is lonely\"")
            .And.Contain("BY 2026-03-01")
            .And.NotContain("a view from another time");
    }

    [Fact]
    public async Task GenerateDraftAsMe_OnFirstUse_WritesInANeutralVoiceAndSaysSo()
    {
        SetUpRecords(profile: null, topics: []);
        SetUpProvider(_ => Pieces("Thanks, everyone."));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "Thank the team for the launch";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftContent.Should().Be("Thanks, everyone.");
        _instructions.Should().Contain("No writing samples from them have been measured yet");
        viewModel.DraftBasis.Should().Contain("PastSelf_DraftBasisNoVoice").And.Contain("PastSelf_DraftBasisNoViews");
        viewModel.VoiceProfile!.SampleCount.Should().Be(0);
        viewModel.VoiceProfile.FormalityLabel.Should().Be("PastSelf_StyleNotEnoughData");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateDraftAsMe_WithoutAProvider_SaysSoAndShowsNoDraft(bool initialized)
    {
        SetUpRecords(Profile(samples: 24), topics: [RemoteWork], stance: "I think that remote work is better for focus");
        SetUpProvider(_ => Pieces("Should never be shown."));
        if (initialized)
        {
            _ai.Setup(ai => ai.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        }
        else
        {
            _ai.SetupGet(ai => ai.ActiveProvider)
                .Throws(new InvalidOperationException("AI service has not been initialized."));
        }

        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A note about remote work";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftErrorMessage.Should().Be("PastSelf_DraftNoProvider");
        viewModel.DraftContent.Should().BeEmpty();
        viewModel.DraftBasis.Should().BeEmpty();
        viewModel.IsGeneratingDraft.Should().BeFalse();
        _instructions.Should().BeNull("nothing is sent without a provider");
    }

    [Fact]
    public async Task GenerateDraftAsMe_WhenTheProviderFails_ShowsTheErrorAndNoPartialDraft()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider(_ => PiecesThenFailure("Hi ", new HttpRequestException("Connection refused")));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A greeting";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftContent.Should().BeEmpty("a half-written draft is not kept");
        viewModel.DraftErrorMessage.Should().Be("PastSelf_DraftFailed: Connection refused");
        viewModel.DraftBasis.Should().BeEmpty();
        viewModel.IsGeneratingDraft.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateDraftAsMe_WhenTheProviderWritesNothing_SaysSo()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider(_ => Pieces("  ", "\n"));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A greeting";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftContent.Should().BeEmpty();
        viewModel.DraftErrorMessage.Should().Be("PastSelf_DraftEmpty");
    }

    [Fact]
    public async Task GenerateDraftAsMe_WithoutContext_AsksForItAndWritesNoDraft()
    {
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "   ";

        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftErrorMessage.Should().Be("PastSelf_DraftNeedsContext");
        viewModel.DraftContent.Should().BeEmpty("the request for context used to be shown, and copied, as the draft");
        _ai.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateDraftAsMe_WithoutContext_LeavesThePreviousDraftAsItWas()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider(_ => Pieces("First draft."));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A greeting";
        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);
        var basis = viewModel.DraftBasis;

        viewModel.DraftContext = string.Empty;
        await viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);

        viewModel.DraftErrorMessage.Should().Be("PastSelf_DraftNeedsContext");
        viewModel.DraftContent.Should().Be("First draft.");
        viewModel.DraftBasis.Should().Be(basis).And.NotBeEmpty();
    }

    [Fact]
    public async Task CancelDraft_WhileTheDraftIsBeingWritten_StopsItAndKeepsNothing()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        var firstPieceShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetUpProvider(ct => PieceThenWait("Hi ", firstPieceShown, ct));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A greeting";

        var generating = viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null);
        await firstPieceShown.Task.WaitAsync(TimeSpan.FromSeconds(10));
        viewModel.IsGeneratingDraft.Should().BeTrue();
        viewModel.DraftContent.Should().Be("Hi ", "the draft streams in as it is written");

        viewModel.CancelDraftCommand.Execute(null);
        await generating.WaitAsync(TimeSpan.FromSeconds(10));

        viewModel.DraftContent.Should().BeEmpty();
        viewModel.DraftStatus.Should().Be("PastSelf_DraftCancelled");
        viewModel.DraftErrorMessage.Should().BeNull();
        viewModel.IsGeneratingDraft.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateDraftAsMe_WritesTheBoundPropertiesOnTheUiThread()
    {
        SetUpRecords(Profile(samples: 24), topics: [RemoteWork], stance: "I think that remote work is better for focus");
        SetUpProvider(_ => Pieces("Hi ", "there."));
        var viewModel = CreateViewModel();
        viewModel.DraftContext = "A note about remote work";

        using var ui = new SingleThreadSynchronizationContext();
        var offThreadWrites = new ConcurrentQueue<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (Environment.CurrentManagedThreadId != ui.ThreadId)
                offThreadWrites.Enqueue(e.PropertyName ?? "?");
        };

        await ui.RunAsync(() => viewModel.GenerateDraftAsMeCommand.ExecuteAsync(null));

        offThreadWrites.Should().BeEmpty("x:Bind rejects writes from other threads");
        viewModel.DraftContent.Should().Be("Hi there.");
    }

    private void SetUpRecords(VoiceProfileEntity? profile, IEnumerable<string> topics, string? stance = null)
    {
        _temporalIdentity.Setup(service => service.GetVoiceProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _temporalIdentity.Setup(service => service.GetActiveTopicsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics.ToList());
        _temporalIdentity
            .Setup(service => service.GetPastSelfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, DateTime? _, CancellationToken _) => stance is null ? null : Stance(topic, stance));
        _temporalIdentity.Setup(service => service.GetRelevantInsightsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void SetUpProvider(Func<CancellationToken, IAsyncEnumerable<string>> stream)
    {
        var provider = new Mock<IAiProvider>();
        provider.SetupGet(p => p.ProviderId).Returns("ollama");
        provider.SetupGet(p => p.DisplayName).Returns("Ollama");
        _ai.SetupGet(ai => ai.ActiveProvider).Returns(provider.Object);
        _ai.SetupGet(ai => ai.ActiveModelId).Returns("llama3.2");
        _ai.Setup(ai => ai.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ai.Setup(ai => ai.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> _, string? instructions, ChatOptions? _, CancellationToken ct) =>
            {
                _instructions = instructions;
                return stream(ct);
            });
    }

    private static VoiceProfileEntity Profile(int samples) => new()
    {
        SampleCount = samples,
        AvgSentenceLength = 12,
        FormalityScore = 0.5,
    };

    private static PastSelfResponse Stance(string topic, string stance) => new()
    {
        Topic = topic,
        Stance = stance,
        EvidenceExcerpts = [],
        RelatedConversations = [],
        RelatedDocuments = [],
    };

    private static async IAsyncEnumerable<string> Pieces(params string[] pieces)
    {
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return piece;
        }
    }

    private static async IAsyncEnumerable<string> PiecesThenFailure(string piece, Exception failure)
    {
        await Task.Yield();
        yield return piece;
        yield return await Task.FromException<string>(failure);
    }

    /// <summary>Yields one piece, reports once the page has taken it, then writes until cancelled.</summary>
    private static async IAsyncEnumerable<string> PieceThenWait(
        string piece, TaskCompletionSource shown, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return piece;
        shown.SetResult();
        await Task.Delay(Timeout.Infinite, ct);
    }
}

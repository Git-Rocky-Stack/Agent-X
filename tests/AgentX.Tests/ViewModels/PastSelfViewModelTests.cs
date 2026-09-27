using System.Collections.Concurrent;
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

    /// <summary>The view model over the real draft service, whose AI provider and records are mocked.</summary>
    private PastSelfViewModel CreateViewModel() => new(
        _temporalIdentity.Object,
        new VoiceDraftService(_temporalIdentity.Object, _ai.Object, Logger.None),
        _localization.Object);

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

        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();
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
        viewModel.VoiceProfile.FormalityLabel.Should().Be("Not enough data");
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

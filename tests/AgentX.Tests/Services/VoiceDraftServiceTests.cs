using System.Runtime.CompilerServices;
using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// "Draft as Me" used no language model at all: the Temporal Identity service assembled canned
/// template sentences around the context and the page presented them as a draft written in the
/// user's voice. <see cref="VoiceDraftService"/> has the active AI provider write it, from what
/// Temporal Identity has recorded: the voice profile and the stances held at the chosen time.
/// </summary>
public sealed class VoiceDraftServiceTests
{
    private const string RemoteWork = "Remote work is better for focus";
    private const string Microservices = "Microservices rock";

    private static readonly DateTime Chosen = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();
    private readonly Mock<IAiService> _ai = new();

    /// <summary>What the service handed to the provider, once it did.</summary>
    private (IReadOnlyList<ChatMessage> Messages, string? Instructions, ChatOptions? Options, CancellationToken Token)? _sent;

    private VoiceDraftService CreateService() => new(_temporalIdentity.Object, _ai.Object, Logger.None);

    // ── The prompt ───────────────────────────────────────────────────────────

    [Fact]
    public async Task StartDraftAsync_PutsTheVoiceProfileAndTheStanceHeldAtTheChosenTimeInThePrompt()
    {
        SetUpRecords(
            Profile(samples: 24, sentenceLength: 9.4, formality: 0.72, phrasesJson: """["to be fair"]"""),
            topics: [Microservices, RemoteWork],
            pastSelf: (topic, at) => topic == RemoteWork
                ? Stance(topic, at == Chosen ? "I think that remote work is better for focus" : "a later view")
                : Stance(topic, "I believe that microservices rock"));
        SetUpProvider();

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest(
            "A note to my manager asking for two remote days a week",
            "Get a yes this quarter",
            Chosen));

        draft.Should().NotBeNull();
        var instructions = _sent!.Value.Instructions;
        instructions.Should().Contain("Measured from 24 of their chat messages")
            .And.Contain("about 9 words")
            .And.Contain("Formality: formal (0.72")
            .And.Contain("\"to be fair\"")
            .And.Contain("WHAT THEY HAD SAID BY 2026-03-01")
            .And.Contain("\"I think that remote work is better for focus\"")
            .And.NotContain("a later view", "only the stance held at the chosen time is the user's view then")
            .And.NotContain("microservices", "a topic that shares no word with the request is not related to it");
        instructions.Should().Contain("Do not invent facts about them")
            .And.Contain("do not give them opinions they have not expressed");

        var request = _sent.Value.Messages.Should().ContainSingle().Subject;
        request.Role.Should().Be("user");
        request.Content.Should().Contain("A note to my manager asking for two remote days a week")
            .And.Contain("Get a yes this quarter");

        _temporalIdentity.Verify(s => s.GetPastSelfAsync(RemoteWork, Chosen, It.IsAny<CancellationToken>()), Times.Once);
        _temporalIdentity.Verify(
            s => s.GetPastSelfAsync(Microservices, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);

        draft!.Basis.WrittenBy.Should().Be("llama3.2 (Ollama)");
        draft.Basis.AsOf.Should().Be(Chosen);
        draft.Basis.VoiceProfile!.SampleCount.Should().Be(24);
        draft.Basis.Views.Should().ContainSingle()
            .Which.Should().Be(new VoiceDraftView(RemoteWork, "I think that remote work is better for focus"));
    }

    [Fact]
    public async Task StartDraftAsync_WithoutAChosenTime_UsesTheViewsRecordedUpToNow()
    {
        SetUpRecords(Profile(samples: 30), topics: [RemoteWork], pastSelf: (topic, _) => Stance(topic, "today's view"));
        SetUpProvider();

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Remote work policy reply", null, null));

        _temporalIdentity.Verify(
            s => s.GetPastSelfAsync(
                RemoteWork,
                It.Is<DateTime?>(at => at.HasValue && at.Value > DateTime.UtcNow.AddMinutes(-1)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        draft!.Basis.AsOf.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        _sent!.Value.Instructions.Should().Contain("\"today's view\"");
    }

    [Fact]
    public async Task StartDraftAsync_OnFirstUse_AsksForANeutralVoiceAndInventsNoViews()
    {
        // No message has been learned from yet: no profile, no beliefs.
        SetUpRecords(profile: null, topics: []);
        SetUpProvider();

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Thank the team for the launch", null, Chosen));

        draft.Should().NotBeNull("a first draft is still written, in a neutral voice");
        _sent!.Value.Instructions.Should()
            .Contain("No writing samples from them have been measured yet")
            .And.Contain("None of their recorded views relate to this request")
            .And.NotContain("Average sentence length");
        draft!.Basis.VoiceProfile.Should().BeNull();
        draft.Basis.Views.Should().BeEmpty();
    }

    [Fact]
    public async Task StartDraftAsync_WithFewSamples_CallsTheVoiceARoughGuide()
    {
        SetUpRecords(Profile(samples: 3), topics: []);
        SetUpProvider();

        await CreateService().StartDraftAsync(new VoiceDraftRequest("Thank the team", null, null));

        _sent!.Value.Instructions.Should().Contain("only a few messages");
    }

    [Fact]
    public async Task StartDraftAsync_CarriesOnlyInsightsSavedByTheChosenTime_AsBackground()
    {
        SetUpRecords(
            Profile(samples: 24),
            topics: [],
            insights:
            [
                Insight("Async standups beat meetings", Chosen.AddDays(-10)),
                Insight("Saved after the chosen time", Chosen.AddDays(10)),
            ]);
        SetUpProvider();

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Proposal for async standups", null, Chosen));

        _sent!.Value.Instructions.Should()
            .Contain("\"Async standups beat meetings\" (saved 2026-02-19)")
            .And.Contain("not necessarily their own words or views")
            .And.NotContain("Saved after the chosen time");
        draft!.Basis.Insights.Should().ContainSingle();
        _temporalIdentity.Verify(s => s.GetRelevantInsightsAsync(
            It.Is<string[]>(words => words.Contains("async") && words.Contains("standups") && !words.Contains("for")),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task StartDraftAsync_CarriesAtMostFiveViews_ThoseSharingTheMostWordsFirst()
    {
        var topics = Enumerable.Range(1, 8).Select(i => $"Remote topic {i}").ToList();
        topics.Add("Remote work culture matters");
        SetUpRecords(Profile(samples: 24), topics, pastSelf: (topic, _) => Stance(topic, "view on " + topic));
        SetUpProvider();

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Remote work culture", null, null));

        draft!.Basis.Views.Should().HaveCount(VoiceDraftService.MaxViews);
        draft.Basis.Views[0].Topic.Should().Be("Remote work culture matters", "it shares three words with the request");
    }

    // ── No provider ──────────────────────────────────────────────────────────

    public enum MissingProvider
    {
        NotInitialized,
        NoneActive,
        Unreachable,
        CheckFailed,
    }

    [Theory]
    [InlineData(MissingProvider.NotInitialized)]
    [InlineData(MissingProvider.NoneActive)]
    [InlineData(MissingProvider.Unreachable)]
    [InlineData(MissingProvider.CheckFailed)]
    public async Task StartDraftAsync_WithoutAnAvailableProvider_ReturnsNoDraftAndSendsNothing(MissingProvider missing)
    {
        SetUpRecords(Profile(samples: 24), topics: [RemoteWork]);
        SetUpProvider();
        switch (missing)
        {
            case MissingProvider.NotInitialized:
                _ai.SetupGet(a => a.ActiveProvider)
                    .Throws(new InvalidOperationException("AI service has not been initialized."));
                break;
            case MissingProvider.NoneActive:
                _ai.SetupGet(a => a.ActiveProvider).Returns((IAiProvider)null!);
                break;
            case MissingProvider.Unreachable:
                _ai.Setup(a => a.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>())).ReturnsAsync(false);
                break;
            case MissingProvider.CheckFailed:
                _ai.Setup(a => a.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("Connection refused"));
                break;
        }

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Remote work reply", null, null));

        draft.Should().BeNull();
        _sent.Should().BeNull("nothing is sent without a provider");
        _temporalIdentity.Invocations.Should().BeEmpty("nothing is gathered for a prompt that cannot be sent");
    }

    // ── Streaming, cancellation and failures ────────────────────────────────

    [Fact]
    public async Task StartDraftAsync_StreamsTheProvidersText()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider(_ => Pieces("Hi ", "team."));

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Greeting", null, null));

        (await DrainAsync(draft!.Text)).Should().Be("Hi team.");
    }

    [Fact]
    public async Task StartDraftAsync_Cancelled_StopsTheProviderMidDraft()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider(PieceThenWait);
        using var cts = new CancellationTokenSource();

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Greeting", null, null), cts.Token);

        _sent!.Value.Token.Should().Be(cts.Token, "the provider gets the caller's cancellation");
        await using var pieces = draft!.Text.GetAsyncEnumerator(cts.Token);
        (await pieces.MoveNextAsync()).Should().BeTrue();
        pieces.Current.Should().Be("Hi ");

        cts.Cancel();

        var next = async () => await pieces.MoveNextAsync();
        await next.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StartDraftAsync_CancelledBeforeItStarts_Throws()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider();
        _ai.Setup(a => a.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => Task.FromCanceled<bool>(ct));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var start = () => CreateService().StartDraftAsync(new VoiceDraftRequest("Greeting", null, null), cts.Token);

        await start.Should().ThrowAsync<OperationCanceledException>();
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task StartDraftAsync_ProviderFailure_SurfacesFromTheText()
    {
        SetUpRecords(Profile(samples: 24), topics: []);
        SetUpProvider(_ => Failing(new HttpRequestException("Connection refused")));

        var draft = await CreateService().StartDraftAsync(new VoiceDraftRequest("Greeting", null, null));

        var read = () => DrainAsync(draft!.Text);
        await read.Should().ThrowAsync<HttpRequestException>().WithMessage("Connection refused");
    }

    [Fact]
    public async Task StartDraftAsync_WithoutContext_Throws()
    {
        var start = () => CreateService().StartDraftAsync(new VoiceDraftRequest("  ", "goal", null));

        await start.Should().ThrowAsync<ArgumentException>();
        _ai.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void BuildRequest_CutsAContextLongerThanThePageAllows()
    {
        var request = VoiceDraftService.BuildRequest(
            new VoiceDraftRequest(new string('a', VoiceDraftService.MaxContextLength + 50), null, null));

        request.Should().Contain(new string('a', VoiceDraftService.MaxContextLength))
            .And.NotContain(new string('a', VoiceDraftService.MaxContextLength + 1))
            .And.NotContain("WHAT IT SHOULD ACHIEVE", "no goal was given");
    }

    [Fact]
    public void SignificantWords_KeepsWordsThatCanNameATopic()
    {
        VoiceDraftService.SignificantWords("An email to the team: I think we should ship the Q4 roadmap, roadmap!")
            .Should().Equal("team", "ship", "roadmap");
    }

    // ── Composition ──────────────────────────────────────────────────────────

    [Fact]
    public void PastSelfPage_ResolvesWithTheRegistrationTheCompositionRootNeeds()
    {
        // App.xaml.cs registers ITemporalIdentityService, IAiService, Serilog.ILogger and
        // ILocalizationService; the Past Self view model now also needs IVoiceDraftService.
        var services = new ServiceCollection();
        services.AddSingleton(_temporalIdentity.Object);
        services.AddSingleton(_ai.Object);
        services.AddSingleton<Serilog.ILogger>(Logger.None);
        services.AddSingleton(Mock.Of<ILocalizationService>());
        services.AddSingleton<IVoiceDraftService, VoiceDraftService>();
        services.AddTransient<PastSelfViewModel>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<PastSelfViewModel>().Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetUpRecords(
        VoiceProfileEntity? profile,
        IEnumerable<string> topics,
        Func<string, DateTime?, PastSelfResponse?>? pastSelf = null,
        List<ResurfacedInsight>? insights = null)
    {
        _temporalIdentity.Setup(s => s.GetVoiceProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _temporalIdentity.Setup(s => s.GetActiveTopicsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics.ToList());
        _temporalIdentity.Setup(s => s.GetPastSelfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, DateTime? at, CancellationToken _) => pastSelf?.Invoke(topic, at));
        _temporalIdentity.Setup(s => s.GetRelevantInsightsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(insights ?? []);
    }

    private void SetUpProvider(Func<CancellationToken, IAsyncEnumerable<string>>? stream = null)
    {
        var provider = new Mock<IAiProvider>();
        provider.SetupGet(p => p.ProviderId).Returns("ollama");
        provider.SetupGet(p => p.DisplayName).Returns("Ollama");
        _ai.SetupGet(a => a.ActiveProvider).Returns(provider.Object);
        _ai.SetupGet(a => a.ActiveModelId).Returns("llama3.2");
        _ai.Setup(a => a.IsProviderAvailableAsync("ollama", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ai.Setup(a => a.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> messages, string? instructions, ChatOptions? options, CancellationToken ct) =>
            {
                _sent = (messages, instructions, options, ct);
                return (stream ?? (_ => Pieces("Draft.")))(ct);
            });
    }

    private static VoiceProfileEntity Profile(
        int samples, double sentenceLength = 12, double formality = 0.5, string phrasesJson = "[]") => new()
        {
            SampleCount = samples,
            AvgSentenceLength = sentenceLength,
            FormalityScore = formality,
            CharacteristicPhrasesJson = phrasesJson,
        };

    private static PastSelfResponse Stance(string topic, string stance) => new()
    {
        Topic = topic,
        Stance = stance,
        EvidenceExcerpts = [],
        RelatedConversations = [],
        RelatedDocuments = [],
    };

    private static ResurfacedInsight Insight(string text, DateTime savedAt) => new()
    {
        Insight = text,
        OriginalDate = savedAt,
        RelevanceReason = "related",
        Context = "From UserExplicitSave",
    };

    private static async IAsyncEnumerable<string> Pieces(params string[] pieces)
    {
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return piece;
        }
    }

    private static async IAsyncEnumerable<string> PieceThenWait([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return "Hi ";
        await Task.Delay(Timeout.Infinite, ct);
    }

    /// <summary>A provider that fails before its first piece.</summary>
    private static async IAsyncEnumerable<string> Failing(Exception failure)
    {
        await Task.Yield();
        yield return await Task.FromException<string>(failure);
    }

    private static async Task<string> DrainAsync(IAsyncEnumerable<string> text)
    {
        var drained = new System.Text.StringBuilder();
        await foreach (var piece in text)
            drained.Append(piece);

        return drained.ToString();
    }
}

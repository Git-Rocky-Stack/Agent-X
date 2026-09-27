using System.Globalization;
using System.Text;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for "Past Self" mode — query what you believed, thought, and discovered at previous points in time.
/// </summary>
public partial class PastSelfViewModel : ObservableObject
{
    private readonly ITemporalIdentityService _temporalIdentity;
    private readonly IVoiceDraftService _voiceDraft;
    private readonly ILocalizationService _localization;

    /// <summary>Stops the draft being written; null while none is.</summary>
    private CancellationTokenSource? _draftCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.UtcNow;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private PastSelfResult? _currentResult;

    // 0=All, 1=PastWeek, 2=PastMonth, 3=PastYear, 4=Custom. Defaults to the past month, the
    // choice the page's radio buttons start on; the page keeps its own state between visits.
    [ObservableProperty]
    private int _selectedTimeRange = 2;

    public PastSelfViewModel(
        ITemporalIdentityService temporalIdentity,
        IVoiceDraftService voiceDraft,
        ILocalizationService localization)
    {
        _temporalIdentity = temporalIdentity;
        _voiceDraft = voiceDraft;
        _localization = localization;
    }

    /// <summary>
    /// Search for what the user believed/thought about the query topic.
    /// </summary>
    [RelayCommand]
    public async Task SearchPastSelfAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsLoading = true;
        ErrorMessage = null;
        CurrentResult = null;

        try
        {
            var targetDate = GetTargetDate();
            var result = await _temporalIdentity.GetPastSelfAsync(SearchQuery, targetDate);

            if (result == null)
            {
                CurrentResult = new PastSelfResult
                {
                    Topic = SearchQuery,
                    Found = false,
                    Message = _localization.GetString("PastSelf_NoRecords", SearchQuery)
                };
            }
            else
            {
                var timeAgo = FormatTimeAgo(_localization, targetDate, DateTime.UtcNow);
                CurrentResult = new PastSelfResult
                {
                    Topic = result.Topic,
                    Found = true,
                    TimePeriod = result.TimePeriod,
                    Stance = result.Stance,
                    Confidence = result.Confidence,
                    EvidenceExcerpts = result.EvidenceExcerpts,
                    RelatedConversations = result.RelatedConversations,
                    RelatedDocuments = result.RelatedDocuments,
                    HasEvolved = result.HasEvolved,
                    CurrentStance = result.CurrentStance,
                    EvolutionChanged = result.StanceChangedAt,
                    // When the view changed after the chosen time, the page shows today's stance
                    // under this label, which says since when. The lookup returned both, but the
                    // page never showed them.
                    CurrentStanceLabel = result.HasEvolved && result.StanceChangedAt is { } changedAt
                        ? _localization.GetString("PastSelf_ViewSince", FormatDate(changedAt))
                        : string.Empty,
                    Message = string.IsNullOrEmpty(timeAgo)
                        ? _localization.GetString("PastSelf_ThoughtAbout", result.Topic)
                        : _localization.GetString("PastSelf_ThoughtAboutWhen", result.Topic, timeAgo)
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.GetString("PastSelf_SearchFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Get insights relevant to the current query.
    /// </summary>
    [RelayCommand]
    public async Task GetRelevantInsightsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var keywords = SearchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var insights = await _temporalIdentity.GetRelevantInsightsAsync(keywords);

            var insightItems = insights.Select(i => new InsightDisplay
            {
                Insight = i.Insight,
                OriginalDate = i.OriginalDate,
                RelevanceReason = i.RelatedTopics.Count == 0
                    ? string.Empty
                    : _localization.GetString("PastSelf_InsightRelatedTo", string.Join(", ", i.RelatedTopics.Take(2))),
                Significance = i.Significance
            }).ToList();

            // PastSelfResult does not notify, so the insights must be on the result before it
            // is published: setting them on the published result afterwards reached no binding,
            // and the insights section never appeared.
            var result = CurrentResult?.WithRelevantInsights(insightItems) ?? new PastSelfResult
            {
                Topic = SearchQuery,
                Found = false,
                Message = insights.Any()
                    ? _localization.GetString("PastSelf_InsightsFound", insights.Count)
                    : _localization.GetString("PastSelf_NoInsights"),
                RelevantInsights = insightItems
            };

            CurrentResult = result;
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.GetString("PastSelf_InsightsFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Show belief evolution for the current topic.
    /// </summary>
    [RelayCommand]
    public async Task ShowBeliefEvolutionAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var belief = await _temporalIdentity.GetBeliefEvolutionAsync(SearchQuery);

            if (belief == null)
            {
                CurrentResult = new PastSelfResult
                {
                    Topic = SearchQuery,
                    Found = false,
                    Message = _localization.GetString("PastSelf_NoEvolution", SearchQuery)
                };
            }
            else
            {
                var since = belief.FirstDetectedAt.ToString("Y", CultureInfo.CurrentCulture);

                // An evolved belief leads with the earlier stance and shows today's below it,
                // labelled with when it changed, as a Past Self answer does. The result carried
                // only today's stance, so the earlier one never appeared.
                var showsChange = belief.HasEvolved && !string.IsNullOrWhiteSpace(belief.PreviousStance);
                CurrentResult = new PastSelfResult
                {
                    Topic = belief.Topic,
                    Found = true,
                    HasEvolved = belief.HasEvolved,
                    Stance = showsChange ? belief.PreviousStance : belief.CurrentStance,
                    CurrentStance = showsChange ? belief.CurrentStance : null,
                    CurrentStanceLabel = showsChange && belief.StanceChangedAt is { } changedAt
                        ? _localization.GetString("PastSelf_ViewSince", FormatDate(changedAt))
                        : string.Empty,
                    // The page's Confidence bar showed 0 for every belief here: the stored
                    // confidence was never copied onto the result.
                    Confidence = belief.ConfidenceLevel,
                    EvolutionStart = belief.FirstDetectedAt,
                    EvolutionChanged = belief.StanceChangedAt,
                    PreviousStance = belief.PreviousStance,
                    Message = belief.HasEvolved
                        ? _localization.GetString("PastSelf_BeliefEvolved", belief.Topic, since)
                        : _localization.GetString("PastSelf_BeliefConsistent", belief.Topic, since)
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.GetString("PastSelf_EvolutionFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────

    private DateTime? GetTargetDate()
    {
        return SelectedTimeRange switch
        {
            1 => DateTime.UtcNow.AddDays(-7),
            2 => DateTime.UtcNow.AddMonths(-1),
            3 => DateTime.UtcNow.AddYears(-1),
            4 => SelectedDate,
            _ => null
        };
    }

    /// <summary>
    /// Describes how long before <paramref name="now"/> the <paramref name="date"/> was, as the
    /// phrase that completes "Here's what you thought about X ...", in the user's language. The
    /// past week used to read "about a month ago" and the past year "about 1 years ago".
    /// </summary>
    internal static string FormatTimeAgo(ILocalizationService localization, DateTime? date, DateTime now)
    {
        if (!date.HasValue) return "";

        var days = (now - date.Value).TotalDays;
        if (days < 0) return "";
        if (days < 1) return localization.GetString("PastSelf_TimeToday");
        if (days < 2) return localization.GetString("PastSelf_TimeYesterday");
        if (days < 7) return localization.GetString("PastSelf_TimeDaysAgo", (int)days);
        if (days < 14) return localization.GetString("PastSelf_TimeAboutAWeekAgo");
        if (days < 28) return localization.GetString("PastSelf_TimeWeeksAgo", (int)(days / 7));
        if (days < 60) return localization.GetString("PastSelf_TimeAboutAMonthAgo");
        if (days < 365) return localization.GetString("PastSelf_TimeMonthsAgo", (int)(days / 30));
        if (days < 730) return localization.GetString("PastSelf_TimeAboutAYearAgo");
        return localization.GetString("PastSelf_TimeYearsAgo", (int)(days / 365));
    }

    /// <summary>A recorded time as the user's short date. Records are kept in UTC.</summary>
    private static string FormatDate(DateTime recordedAt) =>
        recordedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    /// <summary>
    /// Lists, in the Active Topics panel, the topics the user stated views on in the last 30
    /// days: in the wording they were recorded under, with when each was recorded.
    /// </summary>
    [RelayCommand]
    public async Task GetActiveTopicsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var topics = await _temporalIdentity.GetActiveTopicDetailsAsync(days: 30);

            // The topics have their own panel. They used to be published as a belief result
            // too, which showed an empty stance and a Confidence bar at 0 for no belief at all.
            ActiveTopics = topics.Select(ToTopicDisplay).ToList();
            ActiveTopicsStatus = topics.Count > 0
                ? _localization.GetString("PastSelf_ActiveTopicsFound", topics.Count)
                : _localization.GetString("PastSelf_NoActiveTopics");
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.GetString("PastSelf_ActiveTopicsFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// The topics Get Active Topics found, as recorded. Empty until it runs, and when it finds none.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<ActiveTopicDisplay> _activeTopics = [];

    /// <summary>What Get Active Topics found: how many topics, or that there were none. Empty until it runs.</summary>
    [ObservableProperty]
    private string _activeTopicsStatus = string.Empty;

    /// <summary>
    /// A topic as the panel lists it. When its first and latest recording read the same, as for
    /// a topic recorded once, the time is given once.
    /// </summary>
    private ActiveTopicDisplay ToTopicDisplay(ActiveTopic topic)
    {
        var first = FormatHelper.TimeAgo(topic.FirstRecordedAt);
        var last = FormatHelper.TimeAgo(topic.LastRecordedAt);
        return new ActiveTopicDisplay
        {
            Topic = topic.Topic,
            Recorded = first == last
                ? _localization.GetString("PastSelf_TopicRecorded", last)
                : _localization.GetString("PastSelf_TopicRecordedSpan", first, last),
        };
    }

    // ─── Generative Identity: "Draft as Me" ─────────────────────────────────────────────

    [ObservableProperty]
    private string _draftContext = string.Empty;

    [ObservableProperty]
    private string _draftGoal = string.Empty;

    /// <summary>The draft; it fills in as the AI provider writes it. Empty when there is none.</summary>
    [ObservableProperty]
    private string _draftContent = string.Empty;

    [ObservableProperty]
    private bool _isGeneratingDraft;

    /// <summary>Why no draft was written (no context, no AI provider, a provider error), or null.</summary>
    [ObservableProperty]
    private string? _draftErrorMessage;

    /// <summary>A neutral note about the last draft request, such as that it was cancelled.</summary>
    [ObservableProperty]
    private string _draftStatus = string.Empty;

    /// <summary>What the draft on the page was written from: the model, the voice, the views used.</summary>
    [ObservableProperty]
    private string _draftBasis = string.Empty;

    [ObservableProperty]
    private VoiceProfileDisplay? _voiceProfile;

    /// <summary>
    /// Has the active AI provider write the draft in the user's voice, guided by the learned voice
    /// profile and by the views recorded by the time period chosen for the search above. The draft
    /// streams in as it is written and can be cancelled. Without a provider, or when the provider
    /// fails, the page says so and shows no draft. This used to assemble canned sentences around
    /// the context, with no model involved, and present them as a draft in the user's voice.
    /// </summary>
    [RelayCommand]
    public async Task GenerateDraftAsMeAsync()
    {
        if (IsGeneratingDraft)
            return;

        DraftStatus = string.Empty;
        if (string.IsNullOrWhiteSpace(DraftContext))
        {
            // Said beside the button. It used to be put in the draft itself, so it showed (and
            // copied) as generated text.
            DraftErrorMessage = _localization.GetString("PastSelf_DraftNeedsContext");
            return;
        }

        DraftErrorMessage = null;
        DraftContent = string.Empty;
        DraftBasis = string.Empty;
        IsGeneratingDraft = true;
        var cts = new CancellationTokenSource();
        _draftCts = cts;

        try
        {
            var draft = await _voiceDraft.StartDraftAsync(
                new VoiceDraftRequest(DraftContext, DraftGoal, GetTargetDate()), cts.Token);

            if (draft is null)
            {
                DraftErrorMessage = _localization.GetString("PastSelf_DraftNoProvider");
                return;
            }

            // Continues on the UI thread, so each piece is shown as it arrives.
            var written = new StringBuilder();
            await foreach (var piece in draft.Text.WithCancellation(cts.Token))
            {
                written.Append(piece);
                DraftContent = written.ToString();
            }

            DraftContent = written.ToString().Trim();
            if (DraftContent.Length == 0)
            {
                DraftErrorMessage = _localization.GetString("PastSelf_DraftEmpty");
                return;
            }

            DraftBasis = DescribeBasis(draft.Basis);
            VoiceProfile = ToDisplay(draft.Basis.VoiceProfile);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            DraftContent = string.Empty;
            DraftStatus = _localization.GetString("PastSelf_DraftCancelled");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Draft as Me: the AI provider could not write the draft");
            DraftContent = string.Empty;
            DraftErrorMessage = _localization.GetString("PastSelf_DraftFailed", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_draftCts, cts))
                _draftCts = null;

            cts.Dispose();
            IsGeneratingDraft = false;
        }
    }

    /// <summary>Stops the draft being written; none of it is kept.</summary>
    [RelayCommand]
    public void CancelDraft()
    {
        try
        {
            _draftCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The draft finished meanwhile.
        }
    }

    /// <summary>
    /// One line each for the model that wrote the draft, the voice it followed, the views it
    /// used (as of when) and the insights, so the draft never reads as more than it is.
    /// </summary>
    private string DescribeBasis(VoiceDraftBasis basis)
    {
        var asOf = (basis.AsOf.Kind == DateTimeKind.Utc ? basis.AsOf.ToLocalTime() : basis.AsOf)
            .ToString("d", CultureInfo.CurrentCulture);
        var topics = string.Join("; ", basis.Views.Select(view => view.Topic));

        var lines = new List<string>
        {
            _localization.GetString("PastSelf_DraftBasisModel", basis.WrittenBy),
            basis.VoiceProfile is { SampleCount: > 0 }
                ? _localization.GetString("PastSelf_DraftBasisVoice")
                : _localization.GetString("PastSelf_DraftBasisNoVoice"),
            basis.Views.Count > 0
                ? _localization.GetString("PastSelf_DraftBasisViews", asOf, topics)
                : _localization.GetString("PastSelf_DraftBasisNoViews", asOf),
        };

        if (basis.Insights.Count > 0)
            lines.Add(_localization.GetString("PastSelf_DraftBasisInsights", basis.Insights.Count));

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Load the current voice profile metrics.
    /// </summary>
    [RelayCommand]
    public async Task LoadVoiceProfileAsync()
    {
        try
        {
            VoiceProfile = ToDisplay(await _temporalIdentity.GetVoiceProfileAsync());
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.GetString("PastSelf_VoiceProfileFailed", ex.Message);
        }
    }

    /// <summary>
    /// The voice profile as the page shows it. With no profile, no samples captured yet: that
    /// absence is reported rather than plausible-looking numbers, since a 15-word average and a
    /// "Balanced" style read as measurements of the user's writing when nothing was measured.
    /// </summary>
    private VoiceProfileDisplay ToDisplay(VoiceProfileEntity? profile) => profile is null
        ? new VoiceProfileDisplay
        {
            SampleCount = 0,
            AvgSentenceLength = 0,
            FormalityScore = 0,
            FirstSampleAt = DateTime.MinValue,
            LastSampleAt = DateTime.MinValue,
            FormalityLabel = DescribeFormality(sampleCount: 0, score: 0)
        }
        : new VoiceProfileDisplay
        {
            SampleCount = profile.SampleCount,
            AvgSentenceLength = profile.AvgSentenceLength,
            FormalityScore = profile.FormalityScore,
            FirstSampleAt = profile.FirstSampleAt,
            LastSampleAt = profile.LastSampleAt,
            FormalityLabel = DescribeFormality(profile.SampleCount, profile.FormalityScore)
        };

    /// <summary>
    /// The measured writing style, or plainly that nothing has been measured. Without the
    /// sample-count guard a profile with no data reads "Casual", which is indistinguishable from
    /// a real reading.
    /// </summary>
    private string DescribeFormality(int sampleCount, double score) => sampleCount == 0
        ? _localization.GetString("PastSelf_StyleNotEnoughData")
        : score switch
        {
            < 0.3 => _localization.GetString("PastSelf_StyleCasual"),
            < 0.6 => _localization.GetString("PastSelf_StyleBalanced"),
            _ => _localization.GetString("PastSelf_StyleFormal")
        };
}

// ─── Result Models ───────────────────────────────────────────────────────────────

public class PastSelfResult
{
    public string Topic { get; set; } = string.Empty;
    public bool Found { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? TimePeriod { get; set; }
    public string? Stance { get; set; }
    public double Confidence { get; set; }
    public string[]? EvidenceExcerpts { get; set; }
    public string[]? RelatedConversations { get; set; }
    public string[]? RelatedDocuments { get; set; }
    public bool HasEvolved { get; set; }
    public string? CurrentStance { get; set; }

    /// <summary>Since when <see cref="CurrentStance"/> has been held, as the page labels it; empty without one.</summary>
    public string CurrentStanceLabel { get; set; } = string.Empty;

    public DateTime? EvolutionStart { get; set; }
    public DateTime? EvolutionChanged { get; set; }
    public string? PreviousStance { get; set; }
    public List<InsightDisplay>? RelevantInsights { get; set; }

    /// <summary>
    /// A stance was found. A result that only carries a message (nothing recorded, insights
    /// only) shows no stance and no confidence.
    /// </summary>
    public bool HasStance => Found && !string.IsNullOrWhiteSpace(Stance);

    /// <summary>The view changed after the time asked about, so the page shows today's view as well.</summary>
    public bool ShowsEvolution => HasEvolved && !string.IsNullOrWhiteSpace(CurrentStance);

    /// <summary>There are related conversations or documents to list.</summary>
    public bool HasRelatedItems => RelatedConversations is { Length: > 0 } || RelatedDocuments is { Length: > 0 };

    /// <summary>
    /// A copy of this result carrying <paramref name="insights"/>. The result is published whole
    /// because it raises no change notifications of its own.
    /// </summary>
    public PastSelfResult WithRelevantInsights(List<InsightDisplay> insights)
    {
        var copy = (PastSelfResult)MemberwiseClone();
        copy.RelevantInsights = insights;
        return copy;
    }
}

/// <summary>A topic in the Active Topics panel: the wording it was recorded under, and when.</summary>
public class ActiveTopicDisplay
{
    public string Topic { get; set; } = string.Empty;

    /// <summary>When the topic was first and most recently recorded, in the user's language.</summary>
    public string Recorded { get; set; } = string.Empty;
}

public class InsightDisplay
{
    public string Insight { get; set; } = string.Empty;
    public DateTime OriginalDate { get; set; }
    public string RelevanceReason { get; set; } = string.Empty;
    public double Significance { get; set; }
}

public class VoiceProfileDisplay
{
    public int SampleCount { get; set; }
    public double AvgSentenceLength { get; set; }

    /// <summary>
    /// The average sentence length as the page shows it, to one decimal: the bound double used to
    /// print in full, such as 14.100000000000001.
    /// </summary>
    public string AvgSentenceLengthText => AvgSentenceLength.ToString("0.#", CultureInfo.CurrentCulture);

    public double FormalityScore { get; set; }
    public DateTime FirstSampleAt { get; set; }
    public DateTime LastSampleAt { get; set; }

    /// <summary>The measured writing style in the user's language (Casual, Balanced, Formal), or "Not enough data".</summary>
    public string FormalityLabel { get; set; } = string.Empty;
}

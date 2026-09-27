using AgentX.Core.Services.TemporalIdentity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for "Past Self" mode — query what you believed, thought, and discovered at previous points in time.
/// </summary>
public partial class PastSelfViewModel : ObservableObject
{
    private readonly ITemporalIdentityService _temporalIdentity;

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

    public PastSelfViewModel(ITemporalIdentityService temporalIdentity)
    {
        _temporalIdentity = temporalIdentity;
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
                    Message = $"No records found about \"{SearchQuery}\" from the selected time period."
                };
            }
            else
            {
                var timeAgo = FormatTimeAgo(targetDate);
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
                    Message = string.IsNullOrEmpty(timeAgo)
                        ? $"Here's what you thought about {result.Topic}."
                        : $"Here's what you thought about {result.Topic} {timeAgo}."
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to search past self: {ex.Message}";
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
                RelevanceReason = i.RelevanceReason,
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
                    ? $"Found {insights.Count} relevant insights from your past."
                    : "No relevant insights found.",
                RelevantInsights = insightItems
            };

            CurrentResult = result;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to get insights: {ex.Message}";
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
                    Message = $"No belief evolution tracked for \"{SearchQuery}\" yet."
                };
            }
            else
            {
                CurrentResult = new PastSelfResult
                {
                    Topic = belief.Topic,
                    Found = true,
                    HasEvolved = belief.HasEvolved,
                    Stance = belief.CurrentStance,
                    // The page's Confidence bar showed 0 for every belief here: the stored
                    // confidence was never copied onto the result.
                    Confidence = belief.ConfidenceLevel,
                    EvolutionStart = belief.FirstDetectedAt,
                    EvolutionChanged = belief.StanceChangedAt,
                    PreviousStance = belief.PreviousStance,
                    Message = belief.HasEvolved
                        ? $"Your belief about {belief.Topic} has evolved since {belief.FirstDetectedAt:yyyy-MM}."
                        : $"Your belief about {belief.Topic} has been consistent since {belief.FirstDetectedAt:yyyy-MM}."
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to get belief evolution: {ex.Message}";
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
    /// Describes how long ago <paramref name="date"/> was. The past week used to read "about a
    /// month ago" and the past year "about 1 years ago".
    /// </summary>
    internal static string FormatTimeAgo(DateTime? date) => FormatTimeAgo(date, DateTime.UtcNow);

    internal static string FormatTimeAgo(DateTime? date, DateTime now)
    {
        if (!date.HasValue) return "";

        var days = (now - date.Value).TotalDays;
        if (days < 0) return "";
        if (days < 1) return "today";
        if (days < 2) return "yesterday";
        if (days < 7) return $"{(int)days} days ago";
        if (days < 14) return "about a week ago";
        if (days < 28) return $"about {(int)(days / 7)} weeks ago";
        if (days < 60) return "about a month ago";
        if (days < 365) return $"about {(int)(days / 30)} months ago";
        if (days < 730) return "about a year ago";
        return $"about {(int)(days / 365)} years ago";
    }

    /// <summary>
    /// Get topics the user has been exploring recently.
    /// Displays in the Active Topics panel.
    /// </summary>
    [RelayCommand]
    public async Task GetActiveTopicsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var topics = await _temporalIdentity.GetActiveTopicsAsync(days: 30);

            if (CurrentResult == null)
            {
                CurrentResult = new PastSelfResult
                {
                    Topic = "Active Topics",
                    Found = true,
                    Message = topics.Any()
                        ? $"You've been exploring {topics.Count} topics recently."
                        : "No active topics detected in the past month."
                };
            }

            // Store topics for display in the ActiveTopicsPanel
            // The view will bind to this through the panel's ItemsControl
            ActiveTopics = topics;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to get active topics: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [ObservableProperty]
    private List<string>? _activeTopics;

    // ─── Generative Identity: "Draft as Me" ─────────────────────────────────────────────

    [ObservableProperty]
    private string _draftContext = string.Empty;

    [ObservableProperty]
    private string _draftGoal = string.Empty;

    [ObservableProperty]
    private string _draftContent = string.Empty;

    [ObservableProperty]
    private bool _isGeneratingDraft;

    [ObservableProperty]
    private VoiceProfileDisplay? _voiceProfile;

    /// <summary>
    /// Generate text in the user's voice based on context and goal.
    /// </summary>
    [RelayCommand]
    public async Task GenerateDraftAsMeAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftContext))
        {
            DraftContent = "Please provide some context about what you want to write.";
            return;
        }

        IsGeneratingDraft = true;
        ErrorMessage = null;

        try
        {
            DraftContent = await _temporalIdentity.GenerateAsUserAsync(DraftContext, DraftGoal);

            // Load voice profile for display
            var profile = await _temporalIdentity.GetVoiceProfileAsync();
            if (profile != null)
            {
                VoiceProfile = new VoiceProfileDisplay
                {
                    SampleCount = profile.SampleCount,
                    AvgSentenceLength = profile.AvgSentenceLength,
                    FormalityScore = profile.FormalityScore,
                    FirstSampleAt = profile.FirstSampleAt,
                    LastSampleAt = profile.LastSampleAt
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to generate draft: {ex.Message}";
        }
        finally
        {
            IsGeneratingDraft = false;
        }
    }

    /// <summary>
    /// Load the current voice profile metrics.
    /// </summary>
    [RelayCommand]
    public async Task LoadVoiceProfileAsync()
    {
        try
        {
            var profile = await _temporalIdentity.GetVoiceProfileAsync();
            if (profile != null)
            {
                VoiceProfile = new VoiceProfileDisplay
                {
                    SampleCount = profile.SampleCount,
                    AvgSentenceLength = profile.AvgSentenceLength,
                    FormalityScore = profile.FormalityScore,
                    FirstSampleAt = profile.FirstSampleAt,
                    LastSampleAt = profile.LastSampleAt
                };
            }
            else
            {
                // No samples captured yet. Report the absence rather than plausible-looking
                // numbers: a 15-word average and a "Balanced" style read as measurements of
                // the user's writing when nothing has actually been measured.
                VoiceProfile = new VoiceProfileDisplay
                {
                    SampleCount = 0,
                    AvgSentenceLength = 0,
                    FormalityScore = 0,
                    FirstSampleAt = DateTime.MinValue,
                    LastSampleAt = DateTime.MinValue
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load voice profile: {ex.Message}";
        }
    }
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
    public DateTime? EvolutionStart { get; set; }
    public DateTime? EvolutionChanged { get; set; }
    public string? PreviousStance { get; set; }
    public List<InsightDisplay>? RelevantInsights { get; set; }

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
    public double FormalityScore { get; set; }
    public DateTime FirstSampleAt { get; set; }
    public DateTime LastSampleAt { get; set; }

    /// <summary>
    /// Describes the measured writing style, or says so plainly when nothing has been
    /// measured. Without the sample-count guard a profile with no data reports "Casual",
    /// which is indistinguishable from a real reading.
    /// </summary>
    public string FormalityLabel => SampleCount == 0
        ? "Not enough data"
        : FormalityScore switch
        {
            < 0.3 => "Casual",
            < 0.6 => "Balanced",
            _ => "Formal"
        };

    public string StyleDescription => SampleCount < 10
        ? "Still learning your voice..."
        : $"Based on {SampleCount} of your messages";
}

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

    [ObservableProperty]
    private int _selectedTimeRange; // 0=All, 1=PastWeek, 2=PastMonth, 3=PastYear, 4=Custom

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
                    Message = $"Here's what you thought about {result.Topic} {FormatTimeAgo(targetDate)}."
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

            if (CurrentResult == null)
            {
                CurrentResult = new PastSelfResult
                {
                    Topic = SearchQuery,
                    Found = false,
                    Message = insights.Any()
                        ? $"Found {insights.Count} relevant insights from your past."
                        : "No relevant insights found."
                };
            }

            CurrentResult.RelevantInsights = insights.Select(i => new InsightDisplay
            {
                Insight = i.Insight,
                OriginalDate = i.OriginalDate,
                RelevanceReason = i.RelevanceReason,
                Significance = i.Significance
            }).ToList();
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

    private string FormatTimeAgo(DateTime? date)
    {
        if (!date.HasValue) return "";

        var span = DateTime.UtcNow - date.Value;
        if (span.TotalDays < 30) return "about a month ago";
        if (span.TotalDays < 90) return "a few months ago";
        if (span.TotalDays < 365) return "about " + (int)(span.TotalDays / 30) + " months ago";
        return "about " + (int)(span.TotalDays / 365) + " years ago";
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
                VoiceProfile = new VoiceProfileDisplay
                {
                    SampleCount = 0,
                    AvgSentenceLength = 15,
                    FormalityScore = 0.5,
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

    public string FormalityLabel => FormalityScore switch
    {
        < 0.3 => "Casual",
        < 0.6 => "Balanced",
        _ => "Formal"
    };

    public string StyleDescription => SampleCount < 10
        ? "Still learning your voice..."
        : $"Based on {SampleCount} of your messages";
}

namespace AgentX.Core.Services.TemporalIdentity.Models;

/// <summary>
/// Temporal Identity — tracks how the user's beliefs, expertise, and communication patterns evolve over time.
/// This is the foundation for "Past Self" mode and generative identity features.
/// </summary>

// ─── Core Entities ─────────────────────────────────────────────────────────────

/// <summary>
/// A belief, opinion, or stance held by the user at a point in time.
/// Tracked across conversations, documents, and interactions to detect evolution and conflicts.
/// </summary>
public class TemporalBeliefEntity
{
    public long Id { get; set; }
    public DateTime FirstDetectedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The topic/concept this belief is about (e.g., "remote work", "AI safety", "microservices").
    /// Extracted from message content, annotations, and document themes.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// The normalized stance (positive/neutral/negative) with confidence score.
    /// Range: -1.0 (strongly opposed) to +1.0 (strongly supportive). 0 = neutral/uncertain.
    /// </summary>
    public double SentimentScore { get; set; }

    /// <summary>
    /// How certain the user seems about this belief.
    /// Inferred from language strength, repetition, and willingness to defend.
    /// </summary>
    public double ConfidenceLevel { get; set; }

    /// <summary>
    /// The current stance in natural language.
    /// A summary of the user's position as expressed in their own words.
    /// </summary>
    public string CurrentStance { get; set; } = string.Empty;

    /// <summary>
    /// Source evidence for this belief.
    /// JSON array of {type: "message"|"annotation"|"document", id: long, excerpt: string}.
    /// </summary>
    public string EvidenceJson { get; set; } = "[]";

    /// <summary>
    /// Has this belief significantly changed since first detection?
    /// Triggers "You believed X, now you believe Y" conflict alerts.
    /// </summary>
    public bool HasEvolved { get; set; }

    /// <summary>
    /// If evolved, what was the previous stance?
    /// Stores the sentiment and summary before the shift.
    /// </summary>
    public string? PreviousStance { get; set; }

    /// <summary>
    /// When was the previous stance active?
    /// </summary>
    public DateTime? StanceChangedAt { get; set; }
}

/// <summary>
/// A moment of insight — when the user had a "click," breakthrough, or meaningful realization.
/// Harvested from message sentiment spikes, annotation intensity, and re-visit patterns.
/// </summary>
public class InsightMomentEntity
{
    public long Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// What the insight was about.
    /// Extracted from the surrounding conversation or annotation context.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// The insight itself in the user's words (or close paraphrase).
    /// </summary>
    public string InsightText { get; set; } = string.Empty;

    /// <summary>
    /// How significant this insight was.
    /// Based on message length, annotation activity, and subsequent re-visits.
    /// </summary>
    public double SignificanceScore { get; set; }

    /// <summary>
    /// Where this insight came from.
    /// </summary>
    public InsightSource SourceType { get; set; }

    /// <summary>
    /// ID of the source (message_id, annotation_id, etc.)
    /// </summary>
    public long? SourceId { get; set; }

    /// <summary>
    /// Has this insight been resurfaced to the user?
    /// Track whether we've shown this back to them in a relevant context.
    /// </summary>
    public bool HasBeenResurfaced { get; set; }

    /// <summary>
    /// When was this last resurfaced?
    /// Prevents showing the same insight too frequently.
    /// </summary>
    public DateTime? LastResurfacedAt { get; set; }

    /// <summary>
    /// How many times has this been resurfaced?
    /// </summary>
    public int ResurfaceCount { get; set; }

    /// <summary>
    /// Related topics for contextual resurfacing.
    /// JSON array of topic strings that should trigger this insight.
    /// </summary>
    public string RelatedTopicsJson { get; set; } = "[]";
}

public enum InsightSource
{
    ConversationMessage,
    DocumentAnnotation,
    SearchBreakthrough,
    WorkflowSuccess,
    UserExplicitSave,
}

/// <summary>
/// Tracks user engagement with specific content over time.
/// Enables "what you were deeply interested in" queries and engagement-weighted significance.
/// </summary>
public class EngagementMetricsEntity
{
    public long Id { get; set; }
    public DateTime FirstEngagedAt { get; set; }
    public DateTime LastEngagedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// What type of content this is.
    /// </summary>
    public EngagementTargetType TargetType { get; set; }

    /// <summary>
    /// ID of the target content.
    /// </summary>
    public long TargetId { get; set; }

    /// <summary>
    /// Cumulative time spent with this content (seconds).
    /// Estimated from document open time, conversation duration, etc.
    /// </summary>
    public int TotalSecondsSpent { get; set; }

    /// <summary>
    /// How many times the user has returned to this content.
    /// Re-visits signal lasting value.
    /// </summary>
    public int RevisitCount { get; set; }

    /// <summary>
    /// Depth of engagement.
    /// Based on annotations made, questions asked, follow-up conversations.
    /// </summary>
    public EngagementDepth Depth { get; set; }

    /// <summary>
    /// Has the user's sentiment toward this content changed?
    /// From positive → negative or vice versa.
    /// </summary>
    public bool SentimentShifted { get; set; }

    /// <summary>
    /// Current sentiment score.
    /// </summary>
    public double CurrentSentiment { get; set; }

    /// <summary>
    /// Extracted topics/themes for this content.
    /// JSON array for query matching.
    /// </summary>
    public string TopicsJson { get; set; } = "[]";
}

public enum EngagementTargetType
{
    Document,
    Conversation,
    Annotation,
    WorkflowRun,
    WebClip,
}

public enum EngagementDepth
{
    Skimmed,      // Brief view, no meaningful interaction
    Read,         // Full consumption, minimal engagement
    Engaged,      // Annotations, questions, follow-up actions
    Deep,         // Multiple re-visits, extensive notes, referenced in other work
    Core,         // Seminal material that shaped thinking—referenced frequently
}

/// <summary>
/// Detected belief conflicts — when current stance contradicts past self.
/// These are the "You said X then, Y now" moments that trigger self-reflection.
/// </summary>
public class BeliefConflictEntity
{
    public long Id { get; set; }
    public DateTime DetectedAt { get; set; }

    /// <summary>
    /// The belief that has conflicted.
    /// </summary>
    public long BeliefId { get; set; }

    /// <summary>
    /// Reference to the belief entity.
    /// </summary>
    public TemporalBeliefEntity? Belief { get; set; }

    /// <summary>
    /// What the user used to believe.
    /// </summary>
    public string PreviousStance { get; set; } = string.Empty;

    /// <summary>
    /// What the user believes now.
    /// </summary>
    public string CurrentStance { get; set; } = string.Empty;

    /// <summary>
    /// When was the previous stance active?
    /// </summary>
    public DateTime PreviousStancePeriod { get; set; }

    /// <summary>
    /// When did the stance change?
    /// </summary>
    public DateTime StanceChangedAt { get; set; }

    /// <summary>
    /// How significant is this conflict?
    /// Larger delta = more meaningful evolution.
    /// </summary>
    public double ConflictMagnitude { get; set; }

    /// <summary>
    /// Has the user acknowledged/dismissed this conflict?
    /// </summary>
    public bool HasBeenAcknowledged { get; set; }

    /// <summary>
    /// When was this acknowledged?
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }
}

/// <summary>
/// Communication style profile — learns how the user writes and speaks.
/// Enables "draft as me" generative identity.
/// </summary>
public class VoiceProfileEntity
{
    public long Id { get; set; }
    public DateTime FirstSampleAt { get; set; }
    public DateTime LastSampleAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// How many messages/communications have been analyzed.
    /// </summary>
    public int SampleCount { get; set; }

    /// <summary>
    /// Average sentence length (words).
    /// </summary>
    public double AvgSentenceLength { get; set; }

    /// <summary>
    /// Average paragraph length (sentences).
    /// </summary>
    public double AvgParagraphLength { get; set; }

    /// <summary>
    /// Formality score: 0 (casual) to 1 (formal).
    /// </summary>
    public double FormalityScore { get; set; }

    /// <summary>
    /// Frequently used phrases/idioms.
    /// JSON array of strings.
    /// </summary>
    public string CharacteristicPhrasesJson { get; set; } = "[]";

    /// <summary>
    /// Common sentence structures/templates.
    /// JSON array of pattern strings.
    /// </summary>
    public string SentencePatternsJson { get; set; } = "[]";

    /// <summary>
    /// Greeting and sign-off preferences.
    /// JSON: { greetings: [], signoffs: [] }
    /// </summary>
    public string BookendsJson { get; set; } = "{}";

    /// <summary>
    /// Pronoun preference (I/we, formal/informal).
    /// </summary>
    public string PronounPatterns { get; set; } = string.Empty;

    /// <summary>
    /// Emoji and punctuation patterns.
    /// JSON: { emojiFrequency: 0-1, exclamations: 0-1, questions: 0-1 }
    /// </summary>
    public string StylisticTraitsJson { get; set; } = "{}";
}

// ─── DTOs for Queries ─────────────────────────────────────────────────────────────

/// <summary>
/// Response for "Past Self" queries.
/// Shows what the user thought about a topic at a previous point in time.
/// </summary>
public class PastSelfResponse
{
    public required string Topic { get; set; }
    public DateTime TimePeriod { get; set; }
    public string Stance { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public required string[] EvidenceExcerpts { get; set; }
    public required string[] RelatedConversations { get; set; }
    public required string[] RelatedDocuments { get; set; }
    public bool HasEvolved { get; set; }
    public string? CurrentStance { get; set; }
}

/// <summary>
/// Response for temporal queries about problem-solving patterns.
/// "How did I solve similar problems before?"
/// </summary>
public class ProblemSolvingPattern
{
    public required string ProblemType { get; set; }
    public DateTime[] SolvedAt { get; set; } = [];
    public required string[] Solutions { get; set; }
    public required string[] Outcomes { get; set; }
    public double SuccessRate { get; set; }
}

/// <summary>
/// Insight that should be resurfaced based on current context.
/// </summary>
public class ResurfacedInsight
{
    public long Id { get; set; }
    public required string Insight { get; set; }
    public DateTime OriginalDate { get; set; }
    public required string RelevanceReason { get; set; }
    public double Significance { get; set; }
    public required string Context { get; set; }
}

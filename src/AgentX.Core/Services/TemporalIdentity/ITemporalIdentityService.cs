using AgentX.Core.Services.TemporalIdentity.Models;

namespace AgentX.Core.Services.TemporalIdentity;

/// <summary>
/// Temporal Identity Service — mines user's past to enable "Past Self" mode.
///
/// Core capabilities:
/// - Track belief/opinion evolution over time
/// - Harvest and resurface insight moments
/// - Detect conflicts between current and past self
/// - Learn user's voice for generative identity
/// - Answer "what did I think about X?" queries
/// </summary>
public interface ITemporalIdentityService
{
    // ─── Belief Tracking ────────────────────────────────────────────────────────

    /// <summary>
    /// Analyze a new message for beliefs and update temporal tracking.
    /// Should be called after each user message in conversations. A significant sentiment
    /// shift on a known topic marks the belief as evolved and records a
    /// <see cref="BeliefConflictEntity"/> (previous and new stance), which feeds
    /// <see cref="GetBeliefConflictsAsync"/> and <see cref="GetPastSelfAsync"/>.
    /// </summary>
    Task ProcessMessageAsync(long messageId, CancellationToken ct = default);

    /// <summary>
    /// Analyze an annotation for belief signals.
    /// Annotations are strong belief indicators (user chose to highlight).
    /// </summary>
    Task ProcessAnnotationAsync(long annotationId, CancellationToken ct = default);

    /// <summary>
    /// Get what the user believed about a topic at a specific point in time.
    /// Core of "Past Self" mode. <see cref="PastSelfResponse.Stance"/> is the stance held at
    /// <paramref name="at"/> (the earliest recorded stance when <paramref name="at"/> is null);
    /// <see cref="PastSelfResponse.CurrentStance"/> is today's stance when the belief has evolved.
    /// Returns null when the topic is unknown, or was first recorded after <paramref name="at"/>.
    /// </summary>
    Task<PastSelfResponse?> GetPastSelfAsync(
        string topic,
        DateTime? at = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get belief evolution for a topic across all time.
    /// Shows the journey from "believed X" to "now believes Y".
    /// </summary>
    Task<TemporalBeliefEntity?> GetBeliefEvolutionAsync(
        string topic,
        CancellationToken ct = default);

    /// <summary>
    /// Find all topics where the user's beliefs have significantly changed.
    /// Returns detected conflicts for self-reflection.
    /// </summary>
    Task<List<BeliefConflictEntity>> GetBeliefConflictsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Acknowledge (dismiss) a detected belief conflict so it is no longer resurfaced.
    /// Persists <c>HasBeenAcknowledged</c> + <c>AcknowledgedAt</c> to the database;
    /// <see cref="GetBeliefConflictsAsync"/> filters acknowledged conflicts out, so without
    /// this persistence a dismissed conflict reappears after an app restart.
    /// </summary>
    /// <param name="conflictId">The id of the <c>BeliefConflictEntity</c> to acknowledge.</param>
    /// <returns><c>true</c> if the conflict existed and is now acknowledged; <c>false</c> if no such conflict was found.</returns>
    Task<bool> AcknowledgeConflictAsync(long conflictId, CancellationToken ct = default);

    // ─── Insight Harvesting ─────────────────────────────────────────────────────

    /// <summary>
    /// Manually capture an insight moment.
    /// User explicitly marks something as important.
    /// </summary>
    /// <param name="significance">Optional significance score (0-1). When omitted, the
    /// user-specified default of 0.7 (high significance) is used.</param>
    Task CaptureInsightAsync(
        string topic,
        string insight,
        InsightSource source,
        long? sourceId,
        double? significance = null,
        CancellationToken ct = default);

    /// <summary>
    /// Auto-detect insight moments from message spikes.
    /// Looks for breakthrough language, excitement markers, etc. Messages already captured
    /// are skipped, so this can be called after every turn of a conversation.
    /// </summary>
    Task DetectInsightsAsync(long conversationId, CancellationToken ct = default);

    /// <summary>
    /// Get insights relevant to the current context/topic.
    /// For resurfacing "what you discovered before" moments.
    /// </summary>
    Task<List<ResurfacedInsight>> GetRelevantInsightsAsync(
        string[] currentTopics,
        CancellationToken ct = default);

    /// <summary>
    /// Get top insights by significance.
    /// For "my greatest hits" views.
    /// </summary>
    Task<List<InsightMomentEntity>> GetTopInsightsAsync(
        int count = 10,
        CancellationToken ct = default);

    // ─── Engagement Tracking ───────────────────────────────────────────────────

    /// <summary>
    /// Record engagement with a piece of content.
    /// Call when user opens, reads, annotates, or revisits.
    /// </summary>
    Task RecordEngagementAsync(
        EngagementTargetType targetType,
        long targetId,
        int secondsSpent,
        CancellationToken ct = default);

    /// <summary>
    /// Get what the user was most deeply engaged with in a time period.
    /// "What was I most interested in last week/month?"
    /// </summary>
    Task<List<EngagementMetricsEntity>> GetMostEngagedContentAsync(
        DateTime start,
        DateTime end,
        int count = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Get content related to a topic, ordered by engagement depth.
    /// Shows what mattered most to the user in this area.
    /// </summary>
    Task<List<EngagementMetricsEntity>> GetEngagedContentForTopicAsync(
        string topic,
        CancellationToken ct = default);

    // ─── Voice Learning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Analyze a message to learn the user's communication patterns.
    /// Builds the voice profile that <see cref="IVoiceDraftService"/> ("draft as me") describes
    /// to the AI provider.
    /// </summary>
    Task LearnFromMessageAsync(long messageId, CancellationToken ct = default);

    /// <summary>
    /// Get the current voice profile.
    /// Shows what we've learned about how the user communicates.
    /// </summary>
    Task<VoiceProfileEntity?> GetVoiceProfileAsync(CancellationToken ct = default);

    // ─── Pattern Recognition ─────────────────────────────────────────────────────

    /// <summary>
    /// Find similar problems the user has solved before.
    /// "How did I handle situations like this in the past?"
    /// </summary>
    Task<List<ProblemSolvingPattern>> FindSimilarProblemsAsync(
        string currentProblem,
        CancellationToken ct = default);

    /// <summary>
    /// Get the user's expertise level on a topic.
    /// Based on engagement, recency, and depth of interaction.
    /// </summary>
    Task<double> GetExpertiseLevelAsync(
        string topic,
        CancellationToken ct = default);

    /// <summary>
    /// Get topics the user has been exploring recently.
    /// Shows active curiosity areas.
    /// </summary>
    Task<List<string>> GetActiveTopicsAsync(
        int days = 30,
        CancellationToken ct = default);
}

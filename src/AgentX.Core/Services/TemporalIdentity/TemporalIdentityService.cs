using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.TemporalIdentity.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentX.Core.Services.TemporalIdentity;

/// <summary>
/// Temporal Identity Service — implementation.
///
/// Mines the user's conversational and document interaction history to build
/// a temporal model of their evolving beliefs, insights, and voice.
/// </summary>
/// <remarks>
/// The context is the app-wide one, and the chat calls this service from background work after
/// every reply. EF operations on it are serialized, but its change tracker is not safe to mutate
/// from two threads at once. So reads here do not track, updates run as <c>ExecuteUpdate</c>
/// statements, and each new row is saved in a short gated section and detached again: nothing
/// this service writes stays in the shared tracker for another flow to trip over.
/// </remarks>
public class TemporalIdentityService : ITemporalIdentityService
{
    private readonly AgentXDbContext _db;

    public TemporalIdentityService(AgentXDbContext db)
    {
        _db = db;
    }

    // ─── Belief Tracking ────────────────────────────────────────────────────────

    public async Task ProcessMessageAsync(long messageId, CancellationToken ct = default)
    {
        var message = await _db.Messages
            .AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Role, m.Content })
            .FirstOrDefaultAsync(ct);

        if (message == null || message.Role != "user") return;

        // Extract topics and sentiment from the message
        var topicAnalysis = AnalyzeBeliefContent(message.Content);

        foreach (var topic in topicAnalysis.Topics)
        {
            // A topic is unique, so a concurrent pass may insert it first; the second attempt
            // then finds the row and updates it instead.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await ObserveBeliefAsync(messageId, message.Content, topic, topicAnalysis, ct);
                    break;
                }
                catch (DbUpdateException) when (attempt == 0)
                {
                }
            }
        }
    }

    private async Task ObserveBeliefAsync(
        long messageId, string content, string topic, BeliefAnalysis analysis, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var existing = await _db.Set<TemporalBeliefEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Topic == topic, ct);

        if (existing == null)
        {
            await InsertAsync(new TemporalBeliefEntity
            {
                Topic = topic,
                FirstDetectedAt = now,
                // Left at DateTime.MinValue before, so GetActiveTopicsAsync (which filters on
                // LastObservedAt) never listed a belief seen only once.
                LastObservedAt = now,
                UpdatedAt = now,
                SentimentScore = analysis.Sentiment,
                ConfidenceLevel = analysis.Confidence,
                CurrentStance = SummarizeStance(content, topic),
                EvidenceJson = JsonSerializer.Serialize(new[]
                {
                    new { type = "message", id = messageId, excerpt = GetExcerpt(content, topic) }
                }),
            }, ct);
            return;
        }

        var newStance = SummarizeStance(content, topic);
        var hasEvolved = existing.HasEvolved;
        var previousStance = existing.PreviousStance;
        var stanceChangedAt = existing.StanceChangedAt;

        // Check for belief evolution
        var sentimentDelta = Math.Abs(existing.SentimentScore - analysis.Sentiment);
        if (sentimentDelta > 0.5) // Significant shift
        {
            // Record the change so the dashboard can show "you believed X, now Y" and
            // GetPastSelfAsync can answer with the stance held before it. Nothing
            // created conflict rows before, so both always came back empty.
            await InsertAsync(new BeliefConflictEntity
            {
                BeliefId = existing.Id,
                Topic = topic,
                DetectedAt = now,
                PreviousStance = existing.CurrentStance,
                CurrentStance = newStance,
                PreviousStancePeriod = existing.StanceChangedAt ?? existing.FirstDetectedAt,
                StanceChangedAt = now,
                ConflictMagnitude = sentimentDelta,
            }, ct);

            hasEvolved = true;
            previousStance = string.Create(
                CultureInfo.InvariantCulture, $"{existing.SentimentScore:F2}: {existing.CurrentStance}");
            stanceChangedAt = now;
        }

        var sentiment = (existing.SentimentScore * 0.7) + (analysis.Sentiment * 0.3); // EMA
        var confidence = Math.Min(1.0, existing.ConfidenceLevel + 0.05);

        await _db.Set<TemporalBeliefEntity>()
            .Where(b => b.Id == existing.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.HasEvolved, hasEvolved)
                .SetProperty(b => b.PreviousStance, previousStance)
                .SetProperty(b => b.StanceChangedAt, stanceChangedAt)
                .SetProperty(b => b.LastObservedAt, now)
                .SetProperty(b => b.SentimentScore, sentiment)
                .SetProperty(b => b.ConfidenceLevel, confidence)
                .SetProperty(b => b.CurrentStance, newStance)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    public async Task<PastSelfResponse?> GetPastSelfAsync(
        string topic,
        DateTime? at = null,
        CancellationToken ct = default)
    {
        var belief = await FindBeliefAsync(topic, ct);

        if (belief == null) return null;

        // Nothing was recorded on the topic by then. The stance recorded later used to come back
        // as what the user had thought at that time.
        if (at < belief.FirstDetectedAt) return null;

        // If no time specified, return earliest recorded stance
        var targetTime = at ?? belief.FirstDetectedAt;

        return new PastSelfResponse
        {
            Topic = belief.Topic,
            TimePeriod = targetTime,
            // Was always the current stance, so "Past Self" repeated today's view.
            Stance = await GetStanceAtAsync(belief, targetTime, ct),
            Confidence = belief.ConfidenceLevel,
            EvidenceExcerpts = GetEvidenceExcerpts(belief.EvidenceJson),
            RelatedConversations = await GetRelatedConversationsAsync(belief.Topic, targetTime, ct),
            RelatedDocuments = await GetRelatedDocumentsAsync(belief.Topic, targetTime, ct),
            HasEvolved = belief.HasEvolved,
            CurrentStance = belief.HasEvolved ? belief.CurrentStance : null,
        };
    }

    public async Task<List<BeliefConflictEntity>> GetBeliefConflictsAsync(CancellationToken ct = default)
    {
        // Include the belief: the dashboard shows Belief.Topic and fell back to "Unknown Topic".
        return await _db.Set<BeliefConflictEntity>()
            .AsNoTracking()
            .Include(c => c.Belief)
            .Where(c => !c.HasBeenAcknowledged)
            .OrderByDescending(c => c.ConflictMagnitude)
            .ToListAsync(ct);
    }

    public async Task<bool> AcknowledgeConflictAsync(long conflictId, CancellationToken ct = default)
    {
        var exists = await _db.Set<BeliefConflictEntity>()
            .AsNoTracking()
            .AnyAsync(c => c.Id == conflictId, ct);

        if (!exists) return false;

        // Idempotent: only write on the first acknowledgement so the original timestamp stands.
        var now = DateTime.UtcNow;
        await _db.Set<BeliefConflictEntity>()
            .Where(c => c.Id == conflictId && !c.HasBeenAcknowledged)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.HasBeenAcknowledged, true)
                .SetProperty(c => c.AcknowledgedAt, now)
                .SetProperty(c => c.UpdatedAt, now), ct);

        return true;
    }

    // ─── Insight Harvesting ─────────────────────────────────────────────────────

    public async Task CaptureInsightAsync(
        string topic,
        string insight,
        InsightSource source,
        long? sourceId,
        double? significance = null,
        CancellationToken ct = default)
    {
        var insightMoment = new InsightMomentEntity
        {
            CapturedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Topic = topic,
            InsightText = insight,
            SignificanceScore = significance ?? 0.7, // omitted = user-specified = high significance
            SourceType = source,
            SourceId = sourceId,
            RelatedTopicsJson = JsonSerializer.Serialize(new[] { topic }),
        };

        await InsertAsync(insightMoment, ct);
    }

    public async Task<List<ResurfacedInsight>> GetRelevantInsightsAsync(
        string[] currentTopics,
        CancellationToken ct = default)
    {
        var allInsights = await _db.Set<InsightMomentEntity>()
            .AsNoTracking()
            .Where(i => i.SignificanceScore > 0.5)
            .OrderByDescending(i => i.SignificanceScore)
            .ToListAsync(ct);

        var relevant = new List<ResurfacedInsight>();

        foreach (var insight in allInsights)
        {
            var insightTopics = JsonSerializer.Deserialize<string[]>(insight.RelatedTopicsJson) ?? [];
            var overlap = currentTopics.Intersect(insightTopics, StringComparer.OrdinalIgnoreCase).Count();

            if (overlap > 0 || currentTopics.Any(t => insight.InsightText.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                relevant.Add(new ResurfacedInsight
                {
                    Id = insight.Id,
                    Insight = insight.InsightText,
                    OriginalDate = insight.CapturedAt,
                    RelatedTopics = insightTopics,
                    Significance = insight.SignificanceScore,
                    Context = string.Create(
                        CultureInfo.InvariantCulture, $"From {insight.SourceType} on {insight.CapturedAt:yyyy-MM-dd}"),
                });
            }
        }

        return relevant.OrderByDescending(i => i.Significance).Take(5).ToList();
    }

    // ─── Engagement Tracking ───────────────────────────────────────────────────

    public async Task RecordEngagementAsync(
        EngagementTargetType targetType,
        long targetId,
        int secondsSpent,
        CancellationToken ct = default)
    {
        // One row per target (a unique index): add to it when it exists, otherwise create it. A
        // concurrent first engagement can win the insert, in which case this one is added to it.
        if (await AddToEngagementAsync(targetType, targetId, secondsSpent, ct) > 0)
            return;

        var now = DateTime.UtcNow;
        try
        {
            await InsertAsync(new EngagementMetricsEntity
            {
                FirstEngagedAt = now,
                // Left at DateTime.MinValue before, so GetMostEngagedContentAsync (which filters
                // on LastEngagedAt) skipped content engaged with only once.
                LastEngagedAt = now,
                UpdatedAt = now,
                TargetType = targetType,
                TargetId = targetId,
                TotalSecondsSpent = secondsSpent,
                RevisitCount = 0,
                Depth = EngagementDepth.Read,
            }, ct);
        }
        catch (DbUpdateException)
        {
            if (await AddToEngagementAsync(targetType, targetId, secondsSpent, ct) == 0)
                throw;
        }
    }

    /// <summary>
    /// Adds a revisit to an existing engagement row in one statement, so concurrent visits are
    /// all counted, and upgrades its depth from the new totals. Returns the rows updated.
    /// </summary>
    private Task<int> AddToEngagementAsync(
        EngagementTargetType targetType, long targetId, int secondsSpent, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return _db.Set<EngagementMetricsEntity>()
            .Where(e => e.TargetType == targetType && e.TargetId == targetId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.LastEngagedAt, now)
                .SetProperty(e => e.UpdatedAt, now)
                .SetProperty(e => e.TotalSecondsSpent, e => e.TotalSecondsSpent + secondsSpent)
                .SetProperty(e => e.RevisitCount, e => e.RevisitCount + 1)
                // Auto-upgrade depth based on patterns
                .SetProperty(e => e.Depth, e =>
                    e.TotalSecondsSpent + secondsSpent > 300 && e.RevisitCount + 1 > 2
                        ? EngagementDepth.Deep
                        : e.TotalSecondsSpent + secondsSpent > 60
                            ? EngagementDepth.Engaged
                            : e.Depth), ct);
    }

    public async Task<List<EngagementMetricsEntity>> GetMostEngagedContentAsync(
        DateTime start,
        DateTime end,
        int count = 10,
        CancellationToken ct = default)
    {
        return await _db.Set<EngagementMetricsEntity>()
            .AsNoTracking()
            .Where(e => e.LastEngagedAt >= start && e.LastEngagedAt <= end)
            .OrderByDescending(e => e.TotalSecondsSpent * (int)e.Depth)
            .Take(count)
            .ToListAsync(ct);
    }

    // ─── Voice Learning ─────────────────────────────────────────────────────────

    public async Task LearnFromMessageAsync(long messageId, CancellationToken ct = default)
    {
        var message = await _db.Messages
            .AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Role, m.Content })
            .FirstOrDefaultAsync(ct);
        if (message == null || message.Role != "user") return;

        var analysis = AnalyzeVoicePattern(message.Content);
        var now = DateTime.UtcNow;

        // Update with exponential moving average, computed in the statement so concurrent
        // samples all count.
        var profileId = await _db.Set<VoiceProfileEntity>()
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (profileId is long id)
        {
            await _db.Set<VoiceProfileEntity>()
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.SampleCount, p => p.SampleCount + 1)
                    .SetProperty(p => p.LastSampleAt, now)
                    .SetProperty(p => p.UpdatedAt, now)
                    .SetProperty(p => p.AvgSentenceLength, p => (p.AvgSentenceLength * 0.9) + (analysis.AvgSentenceLength * 0.1))
                    .SetProperty(p => p.FormalityScore, p => (p.FormalityScore * 0.95) + (analysis.Formality * 0.05)), ct);
            return;
        }

        // The first sample starts from the neutral baseline (15 words, 0.5 formality).
        await InsertAsync(new VoiceProfileEntity
        {
            FirstSampleAt = now,
            LastSampleAt = now,
            UpdatedAt = now,
            SampleCount = 1,
            AvgSentenceLength = (15 * 0.9) + (analysis.AvgSentenceLength * 0.1),
            FormalityScore = (0.5 * 0.95) + (analysis.Formality * 0.05),
            CharacteristicPhrasesJson = "[]",
            SentencePatternsJson = "[]",
            BookendsJson = "{}",
            StylisticTraitsJson = "{}",
        }, ct);
    }

    // Drafting in the user's voice is VoiceDraftService: it composes the queries here with the
    // active AI provider. A template generator used to live here and returned canned sentences
    // around the user's context as if they had been written in their voice.

    // ─── Pattern Recognition ─────────────────────────────────────────────────────

    public async Task<List<ProblemSolvingPattern>> FindSimilarProblemsAsync(
        string currentProblem,
        CancellationToken ct = default)
    {
        // Search past conversations for similar problem patterns
        var keywords = ExtractKeywords(currentProblem);

        var similarConversations = await _db.Conversations
            .Where(c => c.Title != null && keywords.Any(k => c.Title.Contains(k)))
            .OrderByDescending(c => c.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        return similarConversations.Select(c => new ProblemSolvingPattern
        {
            ProblemType = ExtractProblemType(c.Title ?? ""),
            SolvedAt = new[] { c.CreatedAt },
            Solutions = new[] { c.Title ?? "" },
            Outcomes = new[] { "View conversation for details" },
            SuccessRate = c.TokensUsed > 1000 ? 0.8 : 0.5, // Heuristic
        }).ToList();
    }

    public async Task<double> GetExpertiseLevelAsync(
        string topic,
        CancellationToken ct = default)
    {
        // Calculate based on:
        // - Number of conversations about this topic
        // - Depth of engagement with related documents
        // - Recency of activity
        var conversationCount = await _db.Conversations
            .CountAsync(c => c.Title != null && c.Title.Contains(topic), ct);

        var engagement = await _db.Set<EngagementMetricsEntity>()
            .Where(e => e.TopicsJson.Contains(topic))
            .SumAsync(e => e.TotalSecondsSpent, ct);

        // Normalize to 0-1
        return Math.Min(1.0, (conversationCount * 0.1) + (engagement / 3600.0));
    }

    public async Task<List<string>> GetActiveTopicsAsync(
        int days = 30,
        CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var beliefs = await _db.Set<TemporalBeliefEntity>()
            .Where(b => b.LastObservedAt >= since)
            .OrderByDescending(b => b.ConfidenceLevel * b.LastObservedAt.Ticks)
            .Take(15)
            .Select(b => b.Topic)
            .ToListAsync(ct);

        return beliefs;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private BeliefAnalysis AnalyzeBeliefContent(string content)
    {
        // Simplified NLP — in production, use AI model
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var topics = ExtractTopics(content);
        var sentiment = AnalyzeSentiment(content);
        var confidence = ComputeConfidence(content, sentiment);

        return new BeliefAnalysis(topics, sentiment, confidence);
    }

    private List<string> ExtractTopics(string content)
    {
        // Extract noun phrases, quoted terms, and key concepts
        var topics = new List<string>();
        var sentences = content.Split('.', '!', '?');

        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length > 20 && trimmed.Length < 100)
            {
                // Look for "I think/believe/feel that X" patterns
                if (trimmed.Contains("I think", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("I believe", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("I feel", StringComparison.OrdinalIgnoreCase))
                {
                    var topicStart = trimmed.IndexOf(" that ", StringComparison.OrdinalIgnoreCase);
                    if (topicStart > 0)
                    {
                        var topic = trimmed.Substring(topicStart + 5).Trim();
                        if (topic.Length > 3 && topic.Length < 50)
                            topics.Add(NormalizeTopic(topic));
                    }
                }
            }
        }

        return topics.Distinct().Take(5).ToList();
    }

    private string NormalizeTopic(string topic)
    {
        return char.ToUpper(topic[0]) + topic.Substring(1).ToLower();
    }

    private double AnalyzeSentiment(string content)
    {
        // Very basic sentiment — should use AI in production
        var positiveWords = new[] { "good", "great", "love", "excellent", "agree", "support", "believe" };
        var negativeWords = new[] { "bad", "hate", "terrible", "disagree", "oppose", "wrong", "problem" };

        var lower = content.ToLower();
        var score = 0.0;

        foreach (var word in positiveWords)
            if (lower.Contains(word)) score += 0.2;

        foreach (var word in negativeWords)
            if (lower.Contains(word)) score -= 0.2;

        return Math.Clamp(score, -1.0, 1.0);
    }

    private double ComputeConfidence(string content, double sentiment)
    {
        // Confidence based on language strength
        var absoluteSentiment = Math.Abs(sentiment);
        var strongLanguage = content.Contains("definitely", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("certainly", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("absolutely", StringComparison.OrdinalIgnoreCase);

        var baseConfidence = strongLanguage ? 0.8 : 0.5;
        return Math.Min(1.0, baseConfidence + (absoluteSentiment * 0.3));
    }

    private string SummarizeStance(string content, string topic)
    {
        // Extract the first sentence that mentions the topic
        var sentences = content.Split('.', '!', '?');
        foreach (var sentence in sentences)
        {
            if (sentence.Contains(topic, StringComparison.OrdinalIgnoreCase))
            {
                return sentence.Trim();
            }
        }
        return content.Length > 100 ? content.Substring(0, 97) + "..." : content;
    }

    private string GetExcerpt(string content, string topic)
    {
        var index = content.IndexOf(topic, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return content.Substring(0, Math.Min(100, content.Length));

        var start = Math.Max(0, index - 20);
        var end = Math.Min(content.Length, index + topic.Length + 20);
        return "..." + content.Substring(start, end - start) + "...";
    }

    private string[] GetEvidenceExcerpts(string evidenceJson)
    {
        var evidence = JsonSerializer.Deserialize<List<EvidenceItem>>(evidenceJson);
        return evidence?.Select(e => e.excerpt).ToArray() ?? [];
    }

    /// <summary>
    /// The stance the user held at <paramref name="targetTime"/>: the stance recorded just before
    /// the first change after that time, or the current stance when nothing changed since.
    /// </summary>
    private async Task<string> GetStanceAtAsync(TemporalBeliefEntity belief, DateTime targetTime, CancellationToken ct)
    {
        if (!belief.HasEvolved)
            return belief.CurrentStance;

        var stanceBeforeNextChange = await _db.Set<BeliefConflictEntity>()
            .AsNoTracking()
            .Where(c => c.BeliefId == belief.Id && c.StanceChangedAt > targetTime)
            .OrderBy(c => c.StanceChangedAt)
            .Select(c => c.PreviousStance)
            .FirstOrDefaultAsync(ct);

        if (stanceBeforeNextChange is not null)
            return stanceBeforeNextChange;

        // Beliefs that changed before conflict rows were recorded only keep the latest previous
        // stance, stored as "<sentiment>: <stance>".
        if (belief.PreviousStance is not null &&
            belief.StanceChangedAt is { } changedAt &&
            targetTime < changedAt)
        {
            return SentimentPrefix.Replace(belief.PreviousStance, string.Empty, 1);
        }

        return belief.CurrentStance;
    }

    /// <summary>Matches the "0.40: " sentiment prefix of <see cref="TemporalBeliefEntity.PreviousStance"/>.</summary>
    private static readonly Regex SentimentPrefix = new(@"^-?\d+[.,]\d+: ", RegexOptions.CultureInvariant);

    private async Task<string[]> GetRelatedConversationsAsync(string topic, DateTime around, CancellationToken ct)
    {
        // DateTime subtraction is not translatable by the SQLite provider; materialise the
        // title matches, then apply the ±30-day window + proximity ordering in memory. LIKE
        // matches regardless of case, where Contains (instr) missed "AI safety notes" for the
        // stored topic "Ai safety".
        var pattern = ContainsPattern(topic);
        var candidates = await _db.Conversations
            .Where(c => c.Title != null && EF.Functions.Like(c.Title, pattern, LikeEscape))
            .Select(c => new { c.Title, c.CreatedAt })
            .ToListAsync(ct);

        return candidates
            .Where(c => Math.Abs((c.CreatedAt - around).TotalDays) < 30)
            .OrderBy(c => Math.Abs((c.CreatedAt - around).TotalDays))
            .Take(3)
            .Select(c => c.Title ?? "")
            .ToArray();
    }

    private async Task<string[]> GetRelatedDocumentsAsync(string topic, DateTime around, CancellationToken ct)
    {
        // Same untranslatable DateTime arithmetic as above; window in memory.
        var pattern = ContainsPattern(topic);
        var candidates = await _db.Documents
            .Where(d => d.FileName != null && EF.Functions.Like(d.FileName, pattern, LikeEscape))
            .Select(d => new { d.FileName, d.ImportedAt })
            .ToListAsync(ct);

        return candidates
            .Where(d => Math.Abs((d.ImportedAt - around).TotalDays) < 30)
            .Take(3)
            .Select(d => d.FileName ?? "")
            .ToArray();
    }

    private VoiceAnalysis AnalyzeVoicePattern(string content)
    {
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var avgLength = sentences.Any() ? sentences.Average(s => s.Split(' ').Length) : 15;

        // Formality based on contractions, slang, etc.
        var contractions = content.Count(c => c == '\'' || c == '\'');
        var formalWords = content.Contains("therefore", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("however", StringComparison.OrdinalIgnoreCase);
        var formality = formalWords ? 0.8 : Math.Max(0, 0.5 - (contractions * 0.05));

        return new VoiceAnalysis(avgLength, formality);
    }

    private string[] ExtractKeywords(string text)
    {
        // Simple keyword extraction
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 4)
            .Select(w => w.Trim().ToLower())
            .Distinct()
            .Take(10)
            .ToArray()!;
    }

    private string ExtractProblemType(string title)
    {
        // Extract the core problem type from a title
        if (title.Contains("error", StringComparison.OrdinalIgnoreCase)) return "Error Resolution";
        if (title.Contains("how to", StringComparison.OrdinalIgnoreCase)) return "How-To";
        if (title.Contains("best", StringComparison.OrdinalIgnoreCase)) return "Optimization";
        return "General Problem";
    }

    // ─── Full Implementation of Placeholder Methods ───────────────────────────────

    public async Task ProcessAnnotationAsync(long annotationId, CancellationToken ct = default)
    {
        // Annotations are strong belief indicators — user chose to highlight
        var annotation = await _db.Annotations
            .AsNoTracking()
            .Where(a => a.Id == annotationId)
            .Select(a => new
            {
                a.NoteText,
                a.HighlightedText,
                DocumentFileName = a.Document == null ? null : a.Document.FileName,
            })
            .FirstOrDefaultAsync(ct);

        if (annotation == null) return;

        // Extract topic from annotation note or highlighted text
        var topicText = !string.IsNullOrWhiteSpace(annotation.NoteText)
            ? annotation.NoteText
            : annotation.HighlightedText;

        var topic = topicText.Length > 0
            ? NormalizeTopic(topicText.Substring(0, Math.Min(50, topicText.Length)))
            : "Untitled Annotation";

        // Capture as an insight moment (annotations = high significance)
        var content = !string.IsNullOrWhiteSpace(annotation.NoteText)
            ? annotation.NoteText
            : annotation.HighlightedText;

        if (annotation.DocumentFileName != null && string.IsNullOrWhiteSpace(content))
        {
            content = $"Annotation on document: {annotation.DocumentFileName}";
        }

        await CaptureInsightAsync(
            topic,
            !string.IsNullOrWhiteSpace(content) ? content : "User marked this as important",
            InsightSource.DocumentAnnotation,
            annotationId,
            ct: ct);
    }

    public Task<TemporalBeliefEntity?> GetBeliefEvolutionAsync(string topic, CancellationToken ct = default)
        => FindBeliefAsync(topic, ct);

    /// <summary>
    /// The belief recorded under <paramref name="topic"/>, ignoring surrounding spaces and the
    /// case of its letters. Topics are stored as extracted and sentence-cased ("Ai safety
    /// matters"), so the exact, case-sensitive lookup this replaces missed nearly every topic as
    /// typed. An exact match wins; otherwise SQLite's NOCASE comparison is used, which folds
    /// ASCII letters only.
    /// </summary>
    private async Task<TemporalBeliefEntity?> FindBeliefAsync(string topic, CancellationToken ct)
    {
        var key = topic?.Trim();
        if (string.IsNullOrEmpty(key)) return null;

        var beliefs = _db.Set<TemporalBeliefEntity>().AsNoTracking();
        return await beliefs.FirstOrDefaultAsync(b => b.Topic == key, ct)
            ?? await beliefs
                .Where(b => EF.Functions.Collate(b.Topic, "NOCASE") == key)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(ct);
    }

    /// <summary>The escape character of <see cref="ContainsPattern"/>.</summary>
    private const string LikeEscape = "\\";

    /// <summary>A LIKE pattern matching text that contains <paramref name="value"/> literally.</summary>
    private static string ContainsPattern(string value) =>
        "%" + value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public async Task DetectInsightsAsync(long conversationId, CancellationToken ct = default)
    {
        // Auto-detect insight moments from conversation spikes
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Role == "assistant")
            .OrderBy(m => m.Timestamp)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        // The chat flow calls this after every turn for the whole conversation. Skip messages
        // already captured: previously each call captured every qualifying message again, so the
        // insight table grew quadratically with the length of a conversation.
        var messageIds = messages.Select(m => m.Id).ToList();
        var alreadyCaptured = (await _db.Set<InsightMomentEntity>()
            .Where(i => i.SourceType == InsightSource.ConversationMessage
                        && i.SourceId != null
                        && messageIds.Contains(i.SourceId.Value))
            .Select(i => i.SourceId!.Value)
            .ToListAsync(ct))
            .ToHashSet();

        foreach (var message in messages)
        {
            if (alreadyCaptured.Contains(message.Id)) continue;

            // Look for breakthrough language patterns
            var content = message.Content.ToLowerInvariant();
            var breakthroughMarkers = new[] { "breakthrough", "key insight", "important", "realize", "discover", "aha", "eureka" };
            var excitementMarkers = new[] { "!", " amazing", " incredible", " fascinating", " interesting" };

            var hasBreakthrough = breakthroughMarkers.Any(m => content.Contains(m));
            var hasExcitement = excitementMarkers.Any(m => content.Contains(m));

            if (hasBreakthrough || hasExcitement)
            {
                // Extract topic from message
                var topics = ExtractTopics(message.Content);
                var topic = topics.FirstOrDefault() ?? "General Insight";

                // Calculate significance based on markers
                var significance = 0.6;
                if (hasBreakthrough) significance += 0.2;
                if (hasExcitement) significance += 0.1;

                var insightText = message.Content.Length > 500
                    ? message.Content.Substring(0, 500) + "..."
                    : message.Content;

                await CaptureInsightAsync(
                    topic,
                    insightText,
                    InsightSource.ConversationMessage,
                    message.Id,
                    significance,
                    ct);
            }
        }
    }

    public async Task<List<InsightMomentEntity>> GetTopInsightsAsync(int count = 10, CancellationToken ct = default)
    {
        return await _db.Set<InsightMomentEntity>()
            .OrderByDescending(i => i.SignificanceScore)
            .ThenByDescending(i => i.CapturedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<List<EngagementMetricsEntity>> GetEngagedContentForTopicAsync(string topic, CancellationToken ct = default)
    {
        // Get content with engagement metrics related to the topic
        var allMetrics = await _db.Set<EngagementMetricsEntity>()
            .AsNoTracking()
            .Where(e => e.TopicsJson != null)
            .OrderByDescending(e => e.TotalSecondsSpent)
            .ThenByDescending(e => e.Depth)
            .ToListAsync(ct);

        // Filter by topic in JSON (requires client-side filtering)
        var related = allMetrics
            .Where(e => e.TopicsJson.Contains(topic, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        return related;
    }

    public Task<VoiceProfileEntity?> GetVoiceProfileAsync(CancellationToken ct = default)
        => _db.Set<VoiceProfileEntity>().AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Saves one new row in a short section of the shared change tracker and detaches it again.
    /// The database gate is held throughout, so no other flow's query or save walks the tracker
    /// while the row is in it; ConfigureAwait(false) keeps a UI caller from deadlocking against
    /// a flow waiting on that gate.
    /// </summary>
    private async Task InsertAsync<TEntity>(TEntity entity, CancellationToken ct)
        where TEntity : class
    {
        using var gate = _db.EnterDatabaseGate();
        var entry = _db.Set<TEntity>().Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    // ─── Internal Types ───────────────────────────────────────────────────────────

    private record BeliefAnalysis(List<string> Topics, double Sentiment, double Confidence);
    private record VoiceAnalysis(double AvgSentenceLength, double Formality);
    private record EvidenceItem(string type, long id, string excerpt);
    private record TopicAnalysis(double Sentiment, double Confidence, List<string> Topics);
}

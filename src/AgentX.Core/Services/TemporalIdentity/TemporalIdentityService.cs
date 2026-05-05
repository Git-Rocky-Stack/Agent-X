using Microsoft.EntityFrameworkCore;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.TemporalIdentity.Models;
using System.Text;
using System.Text.Json;

namespace AgentX.Core.Services.TemporalIdentity;

/// <summary>
/// Temporal Identity Service — implementation.
///
/// Mines the user's conversational and document interaction history to build
/// a temporal model of their evolving beliefs, insights, and voice.
/// </summary>
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
            .Include(m => m.Conversation)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (message == null || message.Role != "user") return;

        // Extract topics and sentiment from the message
        var topicAnalysis = AnalyzeBeliefContent(message.Content);

        foreach (var topic in topicAnalysis.Topics)
        {
            var existing = await _db.Set<TemporalBeliefEntity>()
                .FirstOrDefaultAsync(b => b.Topic == topic, ct);

            if (existing == null)
            {
                existing = new TemporalBeliefEntity
                {
                    Topic = topic,
                    FirstDetectedAt = DateTime.UtcNow,
                    SentimentScore = topicAnalysis.Sentiment,
                    ConfidenceLevel = topicAnalysis.Confidence,
                    CurrentStance = SummarizeStance(message.Content, topic),
                    EvidenceJson = JsonSerializer.Serialize(new[]
                    {
                        new { type = "message", id = messageId, excerpt = GetExcerpt(message.Content, topic) }
                    }),
                };
                _db.Set<TemporalBeliefEntity>().Add(existing);
            }
            else
            {
                // Check for belief evolution
                var sentimentDelta = Math.Abs(existing.SentimentScore - topicAnalysis.Sentiment);
                if (sentimentDelta > 0.5) // Significant shift
                {
                    existing.HasEvolved = true;
                    existing.PreviousStance = $"{existing.SentimentScore:F2}: {existing.CurrentStance}";
                    existing.StanceChangedAt = DateTime.UtcNow;
                }

                existing.LastObservedAt = DateTime.UtcNow;
                existing.SentimentScore = (existing.SentimentScore * 0.7) + (topicAnalysis.Sentiment * 0.3); // EMA
                existing.ConfidenceLevel = Math.Min(1.0, existing.ConfidenceLevel + 0.05);
                existing.CurrentStance = SummarizeStance(message.Content, topic);
            }

            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<PastSelfResponse?> GetPastSelfAsync(
        string topic,
        DateTime? at = null,
        CancellationToken ct = default)
    {
        var belief = await _db.Set<TemporalBeliefEntity>()
            .FirstOrDefaultAsync(b => b.Topic == topic, ct);

        if (belief == null) return null;

        // If no time specified, return earliest recorded stance
        var targetTime = at ?? belief.FirstDetectedAt;

        return new PastSelfResponse
        {
            Topic = belief.Topic,
            TimePeriod = targetTime,
            Stance = belief.CurrentStance,
            Confidence = belief.ConfidenceLevel,
            EvidenceExcerpts = GetEvidenceExcerpts(belief.EvidenceJson),
            RelatedConversations = await GetRelatedConversationsAsync(topic, targetTime, ct),
            RelatedDocuments = await GetRelatedDocumentsAsync(topic, targetTime, ct),
            HasEvolved = belief.HasEvolved,
            CurrentStance = belief.HasEvolved ? belief.CurrentStance : null,
        };
    }

    public async Task<List<BeliefConflictEntity>> GetBeliefConflictsAsync(CancellationToken ct = default)
    {
        return await _db.Set<BeliefConflictEntity>()
            .Where(c => !c.HasBeenAcknowledged)
            .OrderByDescending(c => c.ConflictMagnitude)
            .ToListAsync(ct);
    }

    // ─── Insight Harvesting ─────────────────────────────────────────────────────

    public async Task CaptureInsightAsync(
        string topic,
        string insight,
        InsightSource source,
        long? sourceId,
        CancellationToken ct = default)
    {
        var insightMoment = new InsightMomentEntity
        {
            CapturedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Topic = topic,
            InsightText = insight,
            SignificanceScore = 0.7, // User-specified = high significance
            SourceType = source,
            SourceId = sourceId,
            RelatedTopicsJson = JsonSerializer.Serialize(new[] { topic }),
        };

        _db.Set<InsightMomentEntity>().Add(insightMoment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<ResurfacedInsight>> GetRelevantInsightsAsync(
        string[] currentTopics,
        CancellationToken ct = default)
    {
        var allInsights = await _db.Set<InsightMomentEntity>()
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
                    RelevanceReason = $"Related to {string.Join(", ", insightTopics.Take(2))}",
                    Significance = insight.SignificanceScore,
                    Context = $"From {insight.SourceType} on {insight.CapturedAt:yyyy-MM-dd}",
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
        var existing = await _db.Set<EngagementMetricsEntity>()
            .FirstOrDefaultAsync(e => e.TargetType == targetType && e.TargetId == targetId, ct);

        if (existing == null)
        {
            existing = new EngagementMetricsEntity
            {
                FirstEngagedAt = DateTime.UtcNow,
                TargetType = targetType,
                TargetId = targetId,
                TotalSecondsSpent = secondsSpent,
                RevisitCount = 0,
                Depth = EngagementDepth.Read,
            };
            _db.Set<EngagementMetricsEntity>().Add(existing);
        }
        else
        {
            existing.LastEngagedAt = DateTime.UtcNow;
            existing.TotalSecondsSpent += secondsSpent;
            existing.RevisitCount++;

            // Auto-upgrade depth based on patterns
            if (existing.TotalSecondsSpent > 300 && existing.RevisitCount > 2)
                existing.Depth = EngagementDepth.Deep;
            else if (existing.TotalSecondsSpent > 60)
                existing.Depth = EngagementDepth.Engaged;
        }

        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<EngagementMetricsEntity>> GetMostEngagedContentAsync(
        DateTime start,
        DateTime end,
        int count = 10,
        CancellationToken ct = default)
    {
        return await _db.Set<EngagementMetricsEntity>()
            .Where(e => e.LastEngagedAt >= start && e.LastEngagedAt <= end)
            .OrderByDescending(e => e.TotalSecondsSpent * (int)e.Depth)
            .Take(count)
            .ToListAsync(ct);
    }

    // ─── Voice Learning ─────────────────────────────────────────────────────────

    public async Task LearnFromMessageAsync(long messageId, CancellationToken ct = default)
    {
        var message = await _db.Messages.FindAsync(new object[] { messageId }, ct);
        if (message == null || message.Role != "user") return;

        var profile = await _db.Set<VoiceProfileEntity>().FirstOrDefaultAsync(ct);
        if (profile == null)
        {
            profile = new VoiceProfileEntity
            {
                FirstSampleAt = DateTime.UtcNow,
                SampleCount = 0,
                AvgSentenceLength = 15,
                FormalityScore = 0.5,
                CharacteristicPhrasesJson = "[]",
                SentencePatternsJson = "[]",
                BookendsJson = "{}",
                StylisticTraitsJson = "{}",
            };
            _db.Set<VoiceProfileEntity>().Add(profile);
        }

        var analysis = AnalyzeVoicePattern(message.Content);

        // Update with exponential moving average
        profile.SampleCount++;
        profile.LastSampleAt = DateTime.UtcNow;
        profile.AvgSentenceLength = (profile.AvgSentenceLength * 0.9) + (analysis.AvgSentenceLength * 0.1);
        profile.FormalityScore = (profile.FormalityScore * 0.95) + (analysis.Formality * 0.05);

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> GenerateAsUserAsync(
        string context,
        string goal,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context))
            return "Please provide context so I can draft something useful.";

        var profile = await GetVoiceProfileAsync(ct);
        var cleanContext = NormalizeDraftInput(context);
        var cleanGoal = NormalizeDraftInput(goal);

        if (profile == null || profile.SampleCount == 0)
        {
            return BuildBaselineDraft(cleanContext, cleanGoal);
        }

        var opening = profile.FormalityScore >= 0.65
            ? "I recommend we approach this deliberately."
            : profile.FormalityScore <= 0.35
                ? "Here is how I would frame it."
                : "I would keep this clear and grounded.";

        var targetSentenceCount = profile.AvgSentenceLength <= 10 ? 3 : 4;
        var lines = new List<string>
        {
            opening,
            $"The core point is this: {ToSentence(cleanContext)}",
        };

        if (!string.IsNullOrWhiteSpace(cleanGoal))
        {
            lines.Add($"The goal is to {LowercaseFirst(cleanGoal)}.");
        }

        lines.Add(profile.FormalityScore >= 0.65
            ? "I would rather be precise now than create avoidable churn later."
            : "That keeps the message honest, useful, and easy to act on.");

        return string.Join(" ", lines.Take(targetSentenceCount));
    }

    private static string BuildBaselineDraft(string context, string goal)
    {
        var sb = new StringBuilder();
        sb.Append("I want to be clear about this: ");
        sb.Append(ToSentence(context));

        if (!string.IsNullOrWhiteSpace(goal))
        {
            sb.Append(' ');
            sb.Append("The intent is to ");
            sb.Append(LowercaseFirst(goal));
            sb.Append('.');
        }

        sb.Append(" I recommend we keep the next step concrete and accountable.");
        return sb.ToString();
    }

    private static string NormalizeDraftInput(string value)
    {
        return string.Join(' ', (value ?? string.Empty).Split(
            [' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ToSentence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?')
            ? trimmed
            : trimmed + ".";
    }

    private static string LowercaseFirst(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return char.ToLowerInvariant(trimmed[0]) + trimmed[1..].TrimEnd('.', '!', '?');
    }

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

    private async Task<string[]> GetRelatedConversationsAsync(string topic, DateTime around, CancellationToken ct)
    {
        return (await _db.Conversations
            .Where(c => c.Title != null && c.Title.Contains(topic))
            .Where(c => Math.Abs((c.CreatedAt - around).TotalDays) < 30)
            .OrderBy(c => Math.Abs((c.CreatedAt - around).TotalDays))
            .Take(3)
            .Select(c => c.Title ?? "")
            .ToListAsync(ct)).ToArray();
    }

    private async Task<string[]> GetRelatedDocumentsAsync(string topic, DateTime around, CancellationToken ct)
    {
        return (await _db.Documents
            .Where(d => d.FileName != null && d.FileName.Contains(topic))
            .Where(d => Math.Abs((d.ImportedAt - around).TotalDays) < 30)
            .Take(3)
            .Select(d => d.FileName ?? "")
            .ToListAsync(ct)).ToArray();
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
            .Include(a => a.Document)
            .FirstOrDefaultAsync(a => a.Id == annotationId, ct);

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

        if (annotation.Document != null && string.IsNullOrWhiteSpace(content))
        {
            content = $"Annotation on document: {annotation.Document.FileName}";
        }

        await CaptureInsightAsync(
            topic,
            !string.IsNullOrWhiteSpace(content) ? content : "User marked this as important",
            InsightSource.DocumentAnnotation,
            annotationId,
            ct);
    }

    public async Task<TemporalBeliefEntity?> GetBeliefEvolutionAsync(string topic, CancellationToken ct = default)
    {
        return await _db.Set<TemporalBeliefEntity>()
            .FirstOrDefaultAsync(b => b.Topic == topic, ct);
    }

    public async Task DetectInsightsAsync(long conversationId, CancellationToken ct = default)
    {
        // Auto-detect insight moments from conversation spikes
        var messages = await _db.Messages
            .Where(m => m.ConversationId == conversationId && m.Role == "assistant")
            .OrderBy(m => m.Timestamp)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
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
        => _db.Set<VoiceProfileEntity>().FirstOrDefaultAsync(ct);

    // ─── Internal Types ───────────────────────────────────────────────────────────

    private record BeliefAnalysis(List<string> Topics, double Sentiment, double Confidence);
    private record VoiceAnalysis(double AvgSentenceLength, double Formality);
    private record EvidenceItem(string type, long id, string excerpt);
    private record TopicAnalysis(double Sentiment, double Confidence, List<string> Topics);
}

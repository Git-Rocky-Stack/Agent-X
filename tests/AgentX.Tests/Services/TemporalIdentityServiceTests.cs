using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class TemporalIdentityServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();

    // Drafting in the user's voice moved to VoiceDraftService (VoiceDraftServiceTests): the
    // template generator tested here returned canned sentences and involved no model.

    [Fact]
    public async Task AcknowledgeConflictAsync_persists_so_conflict_does_not_resurface_after_restart()
    {
        // Regression for KNOWN-ISSUE #8: the dashboard previously flipped HasBeenAcknowledged in
        // memory only and never called SaveChanges, so an acknowledged conflict reappeared on the
        // next launch (GetBeliefConflictsAsync filters !HasBeenAcknowledged at the database level).
        long conflictId;
        using (var seed = _dbFactory.CreateContext())
        {
            var belief = new TemporalBeliefEntity
            {
                Topic = "remote work",
                FirstDetectedAt = DateTime.UtcNow.AddMonths(-6),
                CurrentStance = "prefers in-office",
            };
            seed.Set<TemporalBeliefEntity>().Add(belief);
            await seed.SaveChangesAsync();

            seed.Set<BeliefConflictEntity>().Add(new BeliefConflictEntity
            {
                BeliefId = belief.Id,
                DetectedAt = DateTime.UtcNow,
                PreviousStance = "fully remote",
                CurrentStance = "prefers in-office",
                ConflictMagnitude = 0.8,
                HasBeenAcknowledged = false,
            });
            await seed.SaveChangesAsync();
            conflictId = (await seed.Set<BeliefConflictEntity>().SingleAsync()).Id;
        }

        // It surfaces before acknowledgement.
        using (var db = _dbFactory.CreateContext())
        {
            var before = await new TemporalIdentityService(db).GetBeliefConflictsAsync();
            before.Should().ContainSingle(c => c.Id == conflictId);
        }

        // Acknowledge through the service.
        using (var db = _dbFactory.CreateContext())
        {
            var acknowledged = await new TemporalIdentityService(db).AcknowledgeConflictAsync(conflictId);
            acknowledged.Should().BeTrue();
        }

        // A brand-new context (simulating an app restart) must NOT resurface it, and the row
        // must carry the persisted acknowledgement.
        using (var db = _dbFactory.CreateContext())
        {
            var after = await new TemporalIdentityService(db).GetBeliefConflictsAsync();
            after.Should().NotContain(c => c.Id == conflictId);

            var row = await db.Set<BeliefConflictEntity>().FirstAsync(c => c.Id == conflictId);
            row.HasBeenAcknowledged.Should().BeTrue();
            row.AcknowledgedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task AcknowledgeConflictAsync_returns_false_for_unknown_conflict()
    {
        using var db = _dbFactory.CreateContext();

        var acknowledged = await new TemporalIdentityService(db).AcknowledgeConflictAsync(999_999);

        acknowledged.Should().BeFalse();
    }

    // ─── Seed helpers (append) ───────────────────────────────────────────────────

    private async Task<MessageEntity> SeedMessageAsync(
        AgentXDbContext db, string role, string content, string convTitle = "chat")
    {
        var conv = new ConversationEntity { Title = convTitle, CreatedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();
        var msg = new MessageEntity
        {
            ConversationId = conv.Id,
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow,
        };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();
        return msg;
    }

    // ─── ProcessMessageAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_ignores_missing_and_non_user_messages()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.ProcessMessageAsync(424242);
        var assistant = await SeedMessageAsync(db, "assistant", "I believe that assistants have beliefs too.");
        await svc.ProcessMessageAsync(assistant.Id);

        (await db.Set<TemporalBeliefEntity>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessMessage_creates_belief_with_topic_sentiment_confidence_and_evidence()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        // Sentence 1 (33 chars, in 21..99): topic after " that " = "microservices rock"
        // Sentiment: "believe" +0.2, "great" +0.2 = 0.4. Confidence: 0.5 + 0.4*0.3 = 0.62.
        var msg = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");

        await svc.ProcessMessageAsync(msg.Id);

        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.Topic.Should().Be("Microservices rock");
        belief.SentimentScore.Should().BeApproximately(0.4, 0.001);
        belief.ConfidenceLevel.Should().BeApproximately(0.62, 0.001);
        belief.CurrentStance.Should().Be("I believe that microservices rock");
        belief.HasEvolved.Should().BeFalse();
        belief.FirstDetectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        belief.EvidenceJson.Should().Contain("\"type\":\"message\"").And.Contain("microservices rock");
    }

    [Fact]
    public async Task ProcessMessage_large_sentiment_shift_flags_evolution_and_applies_ema()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        var first = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");
        await svc.ProcessMessageAsync(first.Id); // sentiment 0.4

        // Same extracted topic; sentiment: believe +0.2, wrong -0.2, problem -0.2, bad -0.2 = -0.4.
        // Delta |0.4 - (-0.4)| = 0.8 > 0.5 -> evolution. EMA: 0.7*0.4 + 0.3*(-0.4) = 0.16.
        var second = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. That was wrong, a problem, and bad news.");
        await svc.ProcessMessageAsync(second.Id);

        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.HasEvolved.Should().BeTrue();
        belief.PreviousStance.Should().StartWith("0.40:");
        belief.StanceChangedAt.Should().NotBeNull();
        belief.SentimentScore.Should().BeApproximately(0.16, 0.001);
        belief.ConfidenceLevel.Should().BeApproximately(0.67, 0.001); // 0.62 + 0.05
        belief.LastObservedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ProcessMessage_small_sentiment_shift_updates_without_evolution()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        var first = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");
        await svc.ProcessMessageAsync(first.Id);

        var repeat = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");
        await svc.ProcessMessageAsync(repeat.Id); // identical sentiment -> delta 0

        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.HasEvolved.Should().BeFalse();
        belief.PreviousStance.Should().BeNull();
    }

    // ─── GetPastSelfAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPastSelf_unknown_topic_returns_null()
    {
        using var db = _dbFactory.CreateContext();
        (await new TemporalIdentityService(db).GetPastSelfAsync("nothing")).Should().BeNull();
    }

    [Fact]
    public async Task GetPastSelf_returns_stance_evidence_and_time_windowed_related_items()
    {
        using var db = _dbFactory.CreateContext();
        var anchor = DateTime.UtcNow.AddMonths(-3);
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "remote work",
            FirstDetectedAt = anchor,
            CurrentStance = "remote work needs strong writing culture",
            ConfidenceLevel = 0.8,
            EvidenceJson = """[{"type":"message","id":1,"excerpt":"remote work excerpt"}]""",
        });
        db.Conversations.AddRange(
            new ConversationEntity { Title = "remote work rituals", CreatedAt = anchor.AddDays(5) },
            new ConversationEntity { Title = "remote work fatigue", CreatedAt = anchor.AddDays(200) }, // outside ±30d
            new ConversationEntity { Title = "unrelated", CreatedAt = anchor });
        db.Documents.AddRange(
            new DocumentEntity { FileName = "remote work handbook.pdf", ImportedAt = anchor.AddDays(-3) },
            new DocumentEntity { FileName = "remote work retro.pdf", ImportedAt = anchor.AddDays(120) }); // outside
        await db.SaveChangesAsync();

        var past = await new TemporalIdentityService(db).GetPastSelfAsync("remote work");

        past.Should().NotBeNull();
        past!.Topic.Should().Be("remote work");
        past.TimePeriod.Should().Be(anchor); // no `at` -> FirstDetectedAt
        past.Stance.Should().Be("remote work needs strong writing culture");
        past.Confidence.Should().Be(0.8);
        past.EvidenceExcerpts.Should().BeEquivalentTo("remote work excerpt");
        past.RelatedConversations.Should().BeEquivalentTo("remote work rituals");
        past.RelatedDocuments.Should().BeEquivalentTo("remote work handbook.pdf");
        past.HasEvolved.Should().BeFalse();
        past.CurrentStance.Should().BeNull(); // only exposed when evolved
    }

    [Fact]
    public async Task GetPastSelf_evolved_belief_exposes_current_stance_and_honors_explicit_time()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "monoliths",
            FirstDetectedAt = DateTime.UtcNow.AddYears(-1),
            CurrentStance = "monoliths are fine at small scale",
            HasEvolved = true,
        });
        await db.SaveChangesAsync();
        var at = DateTime.UtcNow.AddMonths(-2);

        var past = await new TemporalIdentityService(db).GetPastSelfAsync("monoliths", at);

        past!.TimePeriod.Should().Be(at);
        past.HasEvolved.Should().BeTrue();
        past.CurrentStance.Should().Be("monoliths are fine at small scale");
    }

    // ─── Insights ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureInsight_persists_a_high_significance_row()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.CaptureInsightAsync("caching", "Cache keys must encode tenant.", InsightSource.UserExplicitSave, 77);

        var row = await db.Set<InsightMomentEntity>().SingleAsync();
        row.Topic.Should().Be("caching");
        row.InsightText.Should().Be("Cache keys must encode tenant.");
        row.SignificanceScore.Should().Be(0.7);
        row.SourceType.Should().Be(InsightSource.UserExplicitSave);
        row.SourceId.Should().Be(77);
        row.RelatedTopicsJson.Should().Contain("caching");
    }

    [Fact]
    public async Task GetTopInsights_orders_by_significance_then_recency_and_caps()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<InsightMomentEntity>().AddRange(
            new InsightMomentEntity { Topic = "a", InsightText = "older-high", SignificanceScore = 0.9, CapturedAt = DateTime.UtcNow.AddDays(-2) },
            new InsightMomentEntity { Topic = "b", InsightText = "newer-high", SignificanceScore = 0.9, CapturedAt = DateTime.UtcNow.AddDays(-1) },
            new InsightMomentEntity { Topic = "c", InsightText = "low", SignificanceScore = 0.5, CapturedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var top = await new TemporalIdentityService(db).GetTopInsightsAsync(count: 2);

        top.Should().HaveCount(2);
        top[0].InsightText.Should().Be("newer-high");
        top[1].InsightText.Should().Be("older-high");
    }

    [Fact]
    public async Task GetRelevantInsights_matches_by_topic_overlap_or_text_and_returns_top_five()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<InsightMomentEntity>().AddRange(
            new InsightMomentEntity
            {
                Topic = "d",
                InsightText = "container insight",
                SignificanceScore = 0.9,
                CapturedAt = DateTime.UtcNow,
                RelatedTopicsJson = """["docker"]"""
            },                    // topic overlap (case-insensitive)
            new InsightMomentEntity
            {
                Topic = "k",
                InsightText = "we should docker-ise this",
                SignificanceScore = 0.8,
                CapturedAt = DateTime.UtcNow,
                RelatedTopicsJson = """["k8s"]"""
            },                       // text contains
            new InsightMomentEntity
            {
                Topic = "p",
                InsightText = "php memories",
                SignificanceScore = 0.9,
                CapturedAt = DateTime.UtcNow,
                RelatedTopicsJson = """["php"]"""
            },                       // no match
            new InsightMomentEntity
            {
                Topic = "weak",
                InsightText = "docker but insignificant",
                SignificanceScore = 0.4,
                CapturedAt = DateTime.UtcNow,
                RelatedTopicsJson = """["docker"]"""
            });                   // filtered: <= 0.5
        for (int i = 0; i < 6; i++)
        {
            db.Set<InsightMomentEntity>().Add(new InsightMomentEntity
            {
                Topic = $"extra{i}",
                InsightText = $"extra docker note {i}",
                SignificanceScore = 0.6 + i * 0.01,
                CapturedAt = DateTime.UtcNow,
                RelatedTopicsJson = """["docker"]""",
            });
        }
        await db.SaveChangesAsync();

        var relevant = await new TemporalIdentityService(db).GetRelevantInsightsAsync(new[] { "Docker" });

        relevant.Should().HaveCount(5); // 8 candidates match, capped at 5
        relevant.Select(r => r.Significance).Should().BeInDescendingOrder();
        relevant.Should().NotContain(r => r.Insight == "php memories");
        relevant.Should().NotContain(r => r.Insight == "docker but insignificant");
        relevant[0].RelatedTopics.Should().Equal("docker");
        relevant[0].Context.Should().Contain("From ");
    }

    // ─── Engagement ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordEngagement_creates_then_accumulates_and_upgrades_depth()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        // The service updates rows with ExecuteUpdate, so the checks read the database rather than
        // an instance an earlier tracked read left in this shared context.
        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 50);
        var created = await db.Set<EngagementMetricsEntity>().AsNoTracking().SingleAsync();
        created.Depth.Should().Be(EngagementDepth.Read);
        created.RevisitCount.Should().Be(0);
        created.TotalSecondsSpent.Should().Be(50);
        created.FirstEngagedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 20); // 70s -> Engaged
        (await db.Set<EngagementMetricsEntity>().AsNoTracking().SingleAsync()).Depth.Should().Be(EngagementDepth.Engaged);

        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 200); // 270s, revisit 2
        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 100); // 370s, revisit 3 -> Deep
        var final = await db.Set<EngagementMetricsEntity>().AsNoTracking().SingleAsync();
        final.TotalSecondsSpent.Should().Be(370);
        final.RevisitCount.Should().Be(3);
        final.Depth.Should().Be(EngagementDepth.Deep);
    }

    [Fact]
    public async Task GetMostEngagedContent_filters_window_and_orders_by_time_weighted_depth()
    {
        using var db = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        db.Set<EngagementMetricsEntity>().AddRange(
            new EngagementMetricsEntity
            {
                TargetType = EngagementTargetType.Document,
                TargetId = 1,
                LastEngagedAt = now.AddDays(-1),
                TotalSecondsSpent = 100,
                Depth = EngagementDepth.Deep
            },   // 100*3=300
            new EngagementMetricsEntity
            {
                TargetType = EngagementTargetType.Document,
                TargetId = 2,
                LastEngagedAt = now.AddDays(-2),
                TotalSecondsSpent = 200,
                Depth = EngagementDepth.Read
            },   // 200*1=200
            new EngagementMetricsEntity
            {
                TargetType = EngagementTargetType.Document,
                TargetId = 3,
                LastEngagedAt = now.AddDays(-40),
                TotalSecondsSpent = 9999,
                Depth = EngagementDepth.Core
            }); // outside window
        await db.SaveChangesAsync();

        var top = await new TemporalIdentityService(db)
            .GetMostEngagedContentAsync(now.AddDays(-7), now, count: 5);

        top.Select(e => e.TargetId).Should().Equal(1, 2);
    }

    [Fact]
    public async Task GetEngagedContentForTopic_filters_topics_json_client_side()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<EngagementMetricsEntity>().AddRange(
            new EngagementMetricsEntity { TargetId = 1, TotalSecondsSpent = 300, TopicsJson = """["Docker","ci"]""" },
            new EngagementMetricsEntity { TargetId = 2, TotalSecondsSpent = 100, TopicsJson = """["docker"]""" },
            new EngagementMetricsEntity { TargetId = 3, TotalSecondsSpent = 900, TopicsJson = """["php"]""" });
        await db.SaveChangesAsync();

        var rows = await new TemporalIdentityService(db).GetEngagedContentForTopicAsync("docker");

        rows.Select(r => r.TargetId).Should().Equal(1, 2); // ordered by time desc, php excluded
    }

    // ─── Voice learning ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LearnFromMessage_skips_missing_and_non_user_messages()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        await svc.LearnFromMessageAsync(313131);
        var assistant = await SeedMessageAsync(db, "assistant", "However, this is formal.");
        await svc.LearnFromMessageAsync(assistant.Id);

        (await db.Set<VoiceProfileEntity>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task LearnFromMessage_creates_profile_and_applies_ema()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        // One sentence, 6 words -> analysis.AvgSentenceLength 6; "however" -> formality 0.8.
        var msg = await SeedMessageAsync(db, "user", "However the plan needs revising now.");

        await svc.LearnFromMessageAsync(msg.Id);

        var profile = await db.Set<VoiceProfileEntity>().SingleAsync();
        profile.SampleCount.Should().Be(1);
        profile.AvgSentenceLength.Should().BeApproximately(15 * 0.9 + 6 * 0.1, 0.01);   // 14.1
        profile.FormalityScore.Should().BeApproximately(0.5 * 0.95 + 0.8 * 0.05, 0.001); // 0.515
        profile.LastSampleAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ─── Pattern recognition ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindSimilarProblems_maps_matching_titles_to_typed_patterns()
    {
        using var db = _dbFactory.CreateContext();
        db.Conversations.AddRange(
            new ConversationEntity { Title = "nightly build error triage", CreatedAt = DateTime.UtcNow.AddDays(-1), TokensUsed = 2000 },
            new ConversationEntity { Title = "how to deploy workers", CreatedAt = DateTime.UtcNow.AddDays(-2), TokensUsed = 500 },
            new ConversationEntity { Title = "best practices for retries", CreatedAt = DateTime.UtcNow.AddDays(-3), TokensUsed = 1500 },
            new ConversationEntity { Title = "random chatter", CreatedAt = DateTime.UtcNow, TokensUsed = 9000 });
        await db.SaveChangesAsync();

        // keywords (>4 chars, lowered): "error", "deploy", "retries" — titles are lowercase on purpose
        // because string.Contains translates case-sensitively.
        var patterns = await new TemporalIdentityService(db)
            .FindSimilarProblemsAsync("error deploy retries");

        patterns.Should().HaveCount(3);
        patterns.Select(p => p.ProblemType)
            .Should().BeEquivalentTo("Error Resolution", "How-To", "Optimization");
        patterns.Single(p => p.ProblemType == "Error Resolution").SuccessRate.Should().Be(0.8); // TokensUsed > 1000
        patterns.Single(p => p.ProblemType == "How-To").SuccessRate.Should().Be(0.5);
    }

    [Fact]
    public async Task GetExpertiseLevel_combines_conversation_count_and_engagement_hours()
    {
        using var db = _dbFactory.CreateContext();
        db.Conversations.AddRange(
            new ConversationEntity { Title = "docker networking", CreatedAt = DateTime.UtcNow },
            new ConversationEntity { Title = "docker compose tips", CreatedAt = DateTime.UtcNow });
        db.Set<EngagementMetricsEntity>().AddRange(
            new EngagementMetricsEntity { TargetId = 1, TotalSecondsSpent = 1000, TopicsJson = """["docker"]""" },
            new EngagementMetricsEntity { TargetId = 2, TotalSecondsSpent = 800, TopicsJson = """["docker"]""" });
        await db.SaveChangesAsync();

        var level = await new TemporalIdentityService(db).GetExpertiseLevelAsync("docker");

        level.Should().BeApproximately(2 * 0.1 + 1800 / 3600.0, 0.001); // 0.7
        (await new TemporalIdentityService(db).GetExpertiseLevelAsync("cobol")).Should().Be(0.0);
    }

    [Fact]
    public async Task GetActiveTopics_returns_recent_topics_weighted_by_confidence_and_recency()
    {
        using var db = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        db.Set<TemporalBeliefEntity>().AddRange(
            new TemporalBeliefEntity { Topic = "strong-recent", LastObservedAt = now.AddDays(-1), ConfidenceLevel = 0.9 },
            new TemporalBeliefEntity { Topic = "weak-recent", LastObservedAt = now.AddDays(-1), ConfidenceLevel = 0.1 },
            new TemporalBeliefEntity { Topic = "stale", LastObservedAt = now.AddDays(-90), ConfidenceLevel = 1.0 });
        await db.SaveChangesAsync();

        var topics = await new TemporalIdentityService(db).GetActiveTopicsAsync(days: 30);

        topics.Should().Equal("strong-recent", "weak-recent"); // stale excluded, weighted order
    }

    // ─── Annotations & auto-detected insights ───────────────────────────────────

    private async Task<AnnotationEntity> SeedAnnotationAsync(
        AgentXDbContext db, string highlighted, string? note, string docName = "guide.pdf")
    {
        var doc = new DocumentEntity { FileName = docName, ImportedAt = DateTime.UtcNow };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var ann = new AnnotationEntity
        {
            DocumentId = doc.Id,
            HighlightedText = highlighted,
            NoteText = note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Annotations.Add(ann);
        await db.SaveChangesAsync();
        return ann;
    }

    [Fact]
    public async Task ProcessAnnotation_missing_id_is_a_noop()
    {
        using var db = _dbFactory.CreateContext();
        await new TemporalIdentityService(db).ProcessAnnotationAsync(515151);
        (await db.Set<InsightMomentEntity>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAnnotation_prefers_note_text_for_topic_and_insight()
    {
        using var db = _dbFactory.CreateContext();
        var ann = await SeedAnnotationAsync(db, "highlighted words", "Container orchestration simplifies deployments");

        await new TemporalIdentityService(db).ProcessAnnotationAsync(ann.Id);

        var insight = await db.Set<InsightMomentEntity>().SingleAsync();
        insight.InsightText.Should().Be("Container orchestration simplifies deployments");
        insight.Topic.Should().Be("Container orchestration simplifies deployments");
        insight.SourceType.Should().Be(InsightSource.DocumentAnnotation);
        insight.SourceId.Should().Be(ann.Id);
    }

    [Fact]
    public async Task ProcessAnnotation_falls_back_to_highlighted_text_then_document_name()
    {
        using var db = _dbFactory.CreateContext();
        var highlightOnly = await SeedAnnotationAsync(db, "Latency budgets matter", note: null);
        var emptyBoth = await SeedAnnotationAsync(db, "", note: null, docName: "empty-ann.pdf");
        var svc = new TemporalIdentityService(db);

        await svc.ProcessAnnotationAsync(highlightOnly.Id);
        await svc.ProcessAnnotationAsync(emptyBoth.Id);

        var insights = await db.Set<InsightMomentEntity>().OrderBy(i => i.Id).ToListAsync();
        insights[0].InsightText.Should().Be("Latency budgets matter");
        insights[1].InsightText.Should().Be("Annotation on document: empty-ann.pdf");
    }

    [Fact]
    public async Task GetBeliefEvolution_returns_row_or_null()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity { Topic = "graphql" });
        await db.SaveChangesAsync();
        var svc = new TemporalIdentityService(db);

        (await svc.GetBeliefEvolutionAsync("graphql"))!.Topic.Should().Be("graphql");
        (await svc.GetBeliefEvolutionAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task DetectInsights_captures_breakthrough_and_excitement_assistant_messages_only()
    {
        using var db = _dbFactory.CreateContext();
        var conv = new ConversationEntity { Title = "session", CreatedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();
        var longTail = new string('x', 520);
        db.Messages.AddRange(
            new MessageEntity
            {
                ConversationId = conv.Id,
                Role = "assistant",
                Timestamp = DateTime.UtcNow.AddMinutes(-3),
                Content = "This is the key insight about cache stampedes. " + longTail
            },   // breakthrough, >500 chars
            new MessageEntity
            {
                ConversationId = conv.Id,
                Role = "assistant",
                Timestamp = DateTime.UtcNow.AddMinutes(-2),
                Content = "The results look amazing overall."
            },                            // excitement
            new MessageEntity
            {
                ConversationId = conv.Id,
                Role = "assistant",
                Timestamp = DateTime.UtcNow.AddMinutes(-1),
                Content = "Routine summary of steps."
            },                                    // neither
            new MessageEntity
            {
                ConversationId = conv.Id,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                Content = "What a breakthrough."
            });                                        // wrong role
        await db.SaveChangesAsync();

        await new TemporalIdentityService(db).DetectInsightsAsync(conv.Id);

        var insights = await db.Set<InsightMomentEntity>().OrderBy(i => i.Id).ToListAsync();
        insights.Should().HaveCount(2);
        insights[0].InsightText.Should().EndWith("...");          // truncated at 500
        insights[0].InsightText.Length.Should().Be(503);
        insights[0].SignificanceScore.Should().BeApproximately(0.8, 0.001); // 0.6 + 0.2 breakthrough
        insights[0].Topic.Should().Be("General Insight");         // no "I think that" pattern
        insights[1].SignificanceScore.Should().BeApproximately(0.7, 0.001); // 0.6 + 0.1 excitement
        insights.Should().OnlyContain(i => i.SourceType == InsightSource.ConversationMessage);
    }

    [Fact]
    public async Task GetVoiceProfile_returns_null_when_unlearned()
    {
        using var db = _dbFactory.CreateContext();
        (await new TemporalIdentityService(db).GetVoiceProfileAsync()).Should().BeNull();
    }

    // TI16 regressions

    private const string PositiveMicroservices =
        "I believe that microservices rock. Great stuff happens with them for sure.";

    // Same topic; the first sentence mentioning it becomes the new stance. Sentiment: believe
    // +0.2, wrong -0.2, bad -0.2, problem(s) -0.2 = -0.4, a shift of 0.8 from the first message.
    private const string NegativeMicroservices =
        "Microservices rock was a wrong and bad slogan full of problems. I believe that microservices rock.";

    [Fact]
    public async Task DetectInsights_called_after_every_turn_captures_each_message_once()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        var conv = new ConversationEntity { Title = "session", CreatedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        db.Messages.Add(new MessageEntity
        {
            ConversationId = conv.Id,
            Role = "assistant",
            Timestamp = DateTime.UtcNow.AddMinutes(-2),
            Content = "That is the key insight here!",
        });
        await db.SaveChangesAsync();
        await svc.DetectInsightsAsync(conv.Id); // turn 1

        db.Messages.Add(new MessageEntity
        {
            ConversationId = conv.Id,
            Role = "assistant",
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
            Content = "Fascinating, that works!",
        });
        await db.SaveChangesAsync();
        await svc.DetectInsightsAsync(conv.Id); // turn 2
        await svc.DetectInsightsAsync(conv.Id); // turn 3, no new assistant message

        // Previously every call captured every qualifying message again: 1 + 2 + 2 = 5 rows.
        var insights = await db.Set<InsightMomentEntity>().ToListAsync();
        insights.Should().HaveCount(2);
        insights.Select(i => i.SourceId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ProcessMessage_new_belief_is_immediately_an_active_topic()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        var msg = await SeedMessageAsync(db, "user", PositiveMicroservices);

        await svc.ProcessMessageAsync(msg.Id);

        // LastObservedAt was left at DateTime.MinValue, so a belief seen once was filtered out.
        (await svc.GetActiveTopicsAsync(days: 30)).Should().Equal("Microservices rock");
    }

    [Fact]
    public async Task RecordEngagement_first_engagement_counts_as_recent()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.RecordEngagementAsync(EngagementTargetType.Document, 42, 30);

        var recent = await svc.GetMostEngagedContentAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1));
        recent.Select(e => e.TargetId).Should().Equal(42);
    }

    [Fact]
    public async Task ProcessMessage_stance_change_records_a_conflict_with_both_stances()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        await svc.ProcessMessageAsync((await SeedMessageAsync(db, "user", PositiveMicroservices)).Id);
        var firstDetectedAt = (await db.Set<TemporalBeliefEntity>().SingleAsync()).FirstDetectedAt;

        await svc.ProcessMessageAsync((await SeedMessageAsync(db, "user", NegativeMicroservices)).Id);

        // A fresh context, like the dashboard after a restart: the topic must come from the
        // database (Include), not from an entity that happens to be tracked.
        using var fresh = _dbFactory.CreateContext();
        var conflict = (await new TemporalIdentityService(fresh).GetBeliefConflictsAsync()).Should().ContainSingle().Subject;
        conflict.Belief!.Topic.Should().Be("Microservices rock");
        conflict.Topic.Should().Be("Microservices rock");
        conflict.PreviousStance.Should().Be("I believe that microservices rock");
        conflict.CurrentStance.Should().Be("Microservices rock was a wrong and bad slogan full of problems");
        conflict.ConflictMagnitude.Should().BeApproximately(0.8, 0.001);
        conflict.PreviousStancePeriod.Should().Be(firstDetectedAt);
        conflict.StanceChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        conflict.DetectedAt.Should().Be(conflict.StanceChangedAt);
    }

    [Fact]
    public async Task ProcessMessage_without_a_stance_change_records_no_conflict()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.ProcessMessageAsync((await SeedMessageAsync(db, "user", PositiveMicroservices)).Id);
        await svc.ProcessMessageAsync((await SeedMessageAsync(db, "user", PositiveMicroservices)).Id);

        (await svc.GetBeliefConflictsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetPastSelf_returns_the_stance_held_before_a_change_and_today_after_it()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        await svc.ProcessMessageAsync((await SeedMessageAsync(db, "user", PositiveMicroservices)).Id);
        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.FirstDetectedAt = DateTime.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();
        await svc.ProcessMessageAsync((await SeedMessageAsync(db, "user", NegativeMicroservices)).Id);

        var earliest = await svc.GetPastSelfAsync("Microservices rock");
        var lastWeek = await svc.GetPastSelfAsync("Microservices rock", DateTime.UtcNow.AddDays(-7));
        var afterChange = await svc.GetPastSelfAsync("Microservices rock", DateTime.UtcNow.AddMinutes(1));

        // Previously Stance was always the current stance.
        earliest!.Stance.Should().Be("I believe that microservices rock");
        earliest.HasEvolved.Should().BeTrue();
        earliest.CurrentStance.Should().Be("Microservices rock was a wrong and bad slogan full of problems");
        lastWeek!.Stance.Should().Be("I believe that microservices rock");
        afterChange!.Stance.Should().Be("Microservices rock was a wrong and bad slogan full of problems");
    }

    [Fact]
    public async Task GetPastSelf_for_a_belief_that_changed_before_conflicts_were_recorded_uses_its_previous_stance()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "monoliths",
            FirstDetectedAt = DateTime.UtcNow.AddDays(-60),
            LastObservedAt = DateTime.UtcNow.AddDays(-10),
            CurrentStance = "monoliths are fine at small scale",
            HasEvolved = true,
            PreviousStance = "-0,40: monoliths never scale", // written under a comma-decimal culture
            StanceChangedAt = DateTime.UtcNow.AddDays(-10),
        });
        await db.SaveChangesAsync();
        var svc = new TemporalIdentityService(db);

        (await svc.GetPastSelfAsync("monoliths", DateTime.UtcNow.AddDays(-20)))!.Stance
            .Should().Be("monoliths never scale");
        (await svc.GetPastSelfAsync("monoliths", DateTime.UtcNow.AddDays(-5)))!.Stance
            .Should().Be("monoliths are fine at small scale");
    }

    [Fact]
    public async Task GetPastSelf_before_the_topic_was_first_recorded_finds_no_stance()
    {
        // Asked about a month ago, a belief first recorded last week came back with that stance,
        // so the page said "Here's what you thought about it about a month ago".
        using var db = _dbFactory.CreateContext();
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "Remote work is better for focus",
            FirstDetectedAt = DateTime.UtcNow.AddDays(-7),
            LastObservedAt = DateTime.UtcNow.AddDays(-7),
            CurrentStance = "I think that remote work is better for focus",
        });
        await db.SaveChangesAsync();
        var svc = new TemporalIdentityService(db);

        (await svc.GetPastSelfAsync("Remote work is better for focus", DateTime.UtcNow.AddMonths(-1)))
            .Should().BeNull();
        (await svc.GetPastSelfAsync("Remote work is better for focus", DateTime.UtcNow.AddDays(-1)))!.Stance
            .Should().Be("I think that remote work is better for focus");
        (await svc.GetPastSelfAsync("Remote work is better for focus"))!.Stance
            .Should().Be("I think that remote work is better for focus", "no time asks for the earliest stance");
    }

    [Fact]
    public async Task Topic_lookups_ignore_case_and_surrounding_spaces()
    {
        // Topics are stored sentence-cased as extracted ("I think that AI safety matters" is
        // recorded as "Ai safety matters"), and the lookup was exact, so the topic as typed was
        // almost never found.
        using var db = _dbFactory.CreateContext();
        var anchor = DateTime.UtcNow.AddDays(-10);
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "Ai safety matters",
            FirstDetectedAt = anchor,
            LastObservedAt = anchor,
            CurrentStance = "I think that AI safety matters",
        });
        db.Conversations.Add(new ConversationEntity { Title = "Reading group: AI SAFETY MATTERS", CreatedAt = anchor });
        db.Documents.Add(new DocumentEntity { FileName = "ai safety matters - notes.pdf", ImportedAt = anchor });
        await db.SaveChangesAsync();
        var svc = new TemporalIdentityService(db);

        var past = await svc.GetPastSelfAsync("  AI safety matters ");

        past!.Topic.Should().Be("Ai safety matters");
        past.Stance.Should().Be("I think that AI safety matters");
        past.RelatedConversations.Should().Equal("Reading group: AI SAFETY MATTERS");
        past.RelatedDocuments.Should().Equal("ai safety matters - notes.pdf");
        (await svc.GetBeliefEvolutionAsync("ai SAFETY matters"))!.Topic.Should().Be("Ai safety matters");
        (await svc.GetPastSelfAsync("   ")).Should().BeNull();
    }

    [Fact]
    public async Task Topic_lookups_prefer_the_exact_wording_and_take_related_titles_literally()
    {
        using var db = _dbFactory.CreateContext();
        var anchor = DateTime.UtcNow.AddDays(-10);
        db.Set<TemporalBeliefEntity>().AddRange(
            new TemporalBeliefEntity { Topic = "Remote work", FirstDetectedAt = anchor, CurrentStance = "first" },
            new TemporalBeliefEntity { Topic = "remote work", FirstDetectedAt = anchor, CurrentStance = "second" },
            new TemporalBeliefEntity { Topic = "50% remote", FirstDetectedAt = anchor, CurrentStance = "half" });
        db.Conversations.AddRange(
            new ConversationEntity { Title = "Plan for 50% remote", CreatedAt = anchor },
            new ConversationEntity { Title = "500 remote days", CreatedAt = anchor });
        await db.SaveChangesAsync();
        var svc = new TemporalIdentityService(db);

        (await svc.GetPastSelfAsync("remote work"))!.Stance.Should().Be("second");
        (await svc.GetPastSelfAsync("Remote work"))!.Stance.Should().Be("first");
        (await svc.GetPastSelfAsync("REMOTE WORK"))!.Stance.Should().Be("first", "without an exact match the first recorded wins");
        (await svc.GetPastSelfAsync("50% Remote"))!.RelatedConversations
            .Should().Equal(new[] { "Plan for 50% remote" }, "% in a topic is a literal character, not a wildcard");
    }

    [Fact]
    public async Task Learning_passes_leave_nothing_in_the_shared_change_tracker()
    {
        // The chat runs these after every reply, off the UI thread, on the app-wide context. The
        // messages, conversations and rows they used to leave tracked were exposed to other
        // flows' saves and to cross-thread tracker mutation.
        long firstPromptId, secondPromptId, answerConversationId, annotationId;
        using (var seed = _dbFactory.CreateContext())
        {
            firstPromptId = (await SeedMessageAsync(seed, "user", PositiveMicroservices)).Id;
            secondPromptId = (await SeedMessageAsync(seed, "user", NegativeMicroservices)).Id;
            answerConversationId = (await SeedMessageAsync(seed, "assistant", "That is the key insight!")).ConversationId;
            annotationId = (await SeedAnnotationAsync(seed, "Latency budgets matter", note: null)).Id;
        }

        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.ProcessMessageAsync(firstPromptId);
        await svc.ProcessMessageAsync(secondPromptId); // updates the belief and records a conflict
        await svc.LearnFromMessageAsync(firstPromptId);
        await svc.LearnFromMessageAsync(secondPromptId); // updates the profile
        await svc.DetectInsightsAsync(answerConversationId);
        await svc.RecordEngagementAsync(EngagementTargetType.Conversation, answerConversationId, 30);
        await svc.RecordEngagementAsync(EngagementTargetType.Conversation, answerConversationId, 45);
        await svc.ProcessAnnotationAsync(annotationId);

        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.Set<TemporalBeliefEntity>().CountAsync()).Should().Be(1);
        (await db.Set<BeliefConflictEntity>().CountAsync()).Should().Be(1);
        (await db.Set<InsightMomentEntity>().CountAsync()).Should().Be(2);
        (await svc.GetVoiceProfileAsync())!.SampleCount.Should().Be(2);
        var engagement = (await svc.GetMostEngagedContentAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1))).Single();
        engagement.TotalSecondsSpent.Should().Be(75);
        engagement.RevisitCount.Should().Be(1);
        engagement.Depth.Should().Be(EngagementDepth.Engaged);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
    }
}

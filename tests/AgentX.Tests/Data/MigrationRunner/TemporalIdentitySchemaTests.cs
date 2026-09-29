using AgentX.Core.Data;
using AgentX.Core.Services.TemporalIdentity.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentX.Tests.Data.MigrationRunner;

/// <summary>
/// The AddTemporalIdentity migration created its five tables without seven columns the
/// entities map and with NOT NULL columns the entities did not map, so on any database built
/// by the real migration path every temporal identity insert failed ("NOT NULL constraint
/// failed: temporal_beliefs.CreatedAt", "no such column: HasBeenResurfaced"). The unit tests
/// that use EnsureCreated never saw it. These tests go through MigrationRunner like the app.
/// </summary>
public sealed class TemporalIdentitySchemaTests
{
    [Fact]
    public void ModelSnapshot_MatchesTheRuntimeModel()
    {
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AgentXDbContext(options);

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "a snapshot that drifts from the model makes the next scaffolded migration recreate or drop schema");
    }

    [Fact]
    public async Task MigratedDatabase_RoundTripsEveryTemporalIdentityEntity()
    {
        await WithMigratedDatabaseAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var belief = new TemporalBeliefEntity
            {
                Topic = "remote work",
                CurrentStance = "positive",
                FirstDetectedAt = now,
                LastObservedAt = now,
                UpdatedAt = now,
            };
            db.TemporalBeliefs.Add(belief);
            db.InsightMoments.Add(new InsightMomentEntity
            {
                Topic = "caching",
                InsightText = "the cache key must include the model",
                CapturedAt = now,
                UpdatedAt = now,
                HasBeenResurfaced = true,
                ResurfaceCount = 2,
                LastResurfacedAt = now,
            });
            db.EngagementMetrics.Add(new EngagementMetricsEntity
            {
                TargetId = 7,
                FirstEngagedAt = now,
                LastEngagedAt = now,
                UpdatedAt = now,
                SentimentShifted = true,
                CurrentSentiment = 0.4,
            });
            db.VoiceProfiles.Add(new VoiceProfileEntity
            {
                FirstSampleAt = now,
                LastSampleAt = now,
                UpdatedAt = now,
                AvgParagraphLength = 3.5,
                PronounPatterns = "{}",
            });
            await db.SaveChangesAsync();

            db.BeliefConflicts.Add(new BeliefConflictEntity
            {
                BeliefId = belief.Id,
                Topic = belief.Topic,
                PreviousStance = "negative",
                CurrentStance = "positive",
                PreviousStancePeriod = now.AddMonths(-3),
                StanceChangedAt = now,
                DetectedAt = now,
                ConflictMagnitude = 1.2,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await db.TemporalBeliefs.SingleAsync()).Topic.Should().Be("remote work");
            (await db.InsightMoments.SingleAsync()).ResurfaceCount.Should().Be(2);
            (await db.EngagementMetrics.SingleAsync()).SentimentShifted.Should().BeTrue();
            (await db.VoiceProfiles.SingleAsync()).AvgParagraphLength.Should().Be(3.5);
            (await db.BeliefConflicts.SingleAsync()).Topic.Should().Be("remote work");
        });
    }

    [Fact]
    public async Task RunAsync_IsIdempotentOnceTheTemporalColumnsExist()
    {
        await WithMigratedDatabaseAsync(async db =>
        {
            var second = await new Core.Data.MigrationRunner.MigrationRunner(db).RunAsync();

            second.AppliedMigrations.Should().BeEmpty();
            (await db.InsightMoments.CountAsync()).Should().Be(0);
        });
    }

    private static async Task WithMigratedDatabaseAsync(Func<AgentXDbContext, Task> body)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agentx-temporal-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var db = new AgentXDbContext(options);
        try
        {
            await new Core.Data.MigrationRunner.MigrationRunner(db).RunAsync();
            await body(db);
        }
        finally
        {
            await db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}

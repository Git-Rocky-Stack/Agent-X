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

    [Fact]
    public async Task GenerateAsUserAsync_WithoutVoiceProfile_ReturnsUsableDraftInsteadOfPlaceholder()
    {
        using var db = _dbFactory.CreateContext();
        var service = new TemporalIdentityService(db);

        var draft = await service.GenerateAsUserAsync(
            "A note to the product team about delaying launch until the installer smoke test passes.",
            "Keep the tone direct and accountable.");

        draft.Should().Contain("installer smoke test");
        draft.Should().Contain("direct and accountable");
        draft.Should().NotContain("[Voice profile not yet learned]");
        draft.Should().NotContain("placeholder");
    }

    [Fact]
    public async Task GenerateAsUserAsync_WithVoiceProfile_UsesLearnedToneSignals()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<VoiceProfileEntity>().Add(new VoiceProfileEntity
        {
            FirstSampleAt = DateTime.UtcNow.AddDays(-3),
            LastSampleAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SampleCount = 12,
            AvgSentenceLength = 9,
            AvgParagraphLength = 2,
            FormalityScore = 0.72,
            CharacteristicPhrasesJson = "[]",
            SentencePatternsJson = "[]",
            BookendsJson = "{}",
            StylisticTraitsJson = "{}",
        });
        await db.SaveChangesAsync();

        var service = new TemporalIdentityService(db);

        var draft = await service.GenerateAsUserAsync(
            "A customer update about the new browser-extension connection status.",
            "Reassure users that setup is stable.");

        draft.Should().Contain("customer update");
        draft.Should().Contain("setup is stable");
        draft.Should().Contain("I recommend");
        draft.Should().NotContain("[Draft in your voice");
    }

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

    public void Dispose()
    {
        _dbFactory.Dispose();
    }
}

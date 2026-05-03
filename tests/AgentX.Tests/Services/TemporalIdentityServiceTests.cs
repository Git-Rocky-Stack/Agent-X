using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
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

    public void Dispose()
    {
        _dbFactory.Dispose();
    }
}

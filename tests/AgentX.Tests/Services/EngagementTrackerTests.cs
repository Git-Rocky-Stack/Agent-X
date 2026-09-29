using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class EngagementTrackerTests
{
    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();
    private DateTime _now = new(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);

    private EngagementTracker CreateTracker() =>
        new(_temporalIdentity.Object, EngagementTargetType.Conversation, () => _now);

    [Fact]
    public async Task Opening_another_item_records_the_whole_seconds_the_previous_one_was_shown()
    {
        var tracker = CreateTracker();

        await tracker.OpenAsync(42);
        _now = _now.AddSeconds(40.7);
        await tracker.OpenAsync(84);

        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(EngagementTargetType.Conversation, 42, 40, It.IsAny<CancellationToken>()),
            Times.Once);
        tracker.OpenTargetId.Should().Be(84);
    }

    [Fact]
    public async Task Reopening_the_open_item_keeps_its_running_time()
    {
        var tracker = CreateTracker();

        await tracker.OpenAsync(42);
        _now = _now.AddSeconds(10);
        await tracker.OpenAsync(42);
        _now = _now.AddSeconds(15);
        await tracker.CloseAsync();

        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(EngagementTargetType.Conversation, 42, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_view_shorter_than_a_second_or_a_discarded_item_records_nothing()
    {
        var tracker = CreateTracker();

        await tracker.OpenAsync(42);
        _now = _now.AddMilliseconds(600);
        await tracker.OpenAsync(84);
        _now = _now.AddMinutes(5);
        tracker.Discard();
        await tracker.CloseAsync();

        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(
                It.IsAny<EngagementTargetType>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tracker.OpenTargetId.Should().BeNull();
    }

    [Fact]
    public async Task An_item_left_open_while_away_counts_for_at_most_the_view_cap()
    {
        var tracker = CreateTracker();

        await tracker.OpenAsync(42);
        _now = _now.AddHours(9);
        await tracker.CloseAsync();

        _temporalIdentity.Verify(
            service => service.RecordEngagementAsync(
                EngagementTargetType.Conversation, 42, (int)EngagementTracker.MaxViewDuration.TotalSeconds, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_recording_failure_is_swallowed()
    {
        _temporalIdentity
            .Setup(service => service.RecordEngagementAsync(
                It.IsAny<EngagementTargetType>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is busy"));
        var tracker = CreateTracker();

        await tracker.OpenAsync(42);
        _now = _now.AddSeconds(30);
        var close = () => tracker.CloseAsync();

        await close.Should().NotThrowAsync();
    }
}

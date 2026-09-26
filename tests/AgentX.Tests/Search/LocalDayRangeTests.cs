using AgentX.Core.Search.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Search;

/// <summary>
/// Tests for <see cref="LocalDayRange"/>: calendar days picked in the UI become UTC bounds
/// for comparing with timestamps stored in UTC, and the "before" day is included whole.
/// Custom time zones keep the results independent of the machine's zone.
/// </summary>
public sealed class LocalDayRangeTests
{
    private static readonly TimeZoneInfo PlusFiveThirty =
        TimeZoneInfo.CreateCustomTimeZone("Test+05:30", TimeSpan.FromHours(5.5), "Test+05:30", "Test+05:30");

    private static readonly TimeZoneInfo MinusFive =
        TimeZoneInfo.CreateCustomTimeZone("Test-05:00", TimeSpan.FromHours(-5), "Test-05:00", "Test-05:00");

    [Fact]
    public void StartUtc_EastOfUtc_StartsOnThePreviousUtcDay()
    {
        LocalDayRange.StartUtc(new DateTime(2026, 3, 2), PlusFiveThirty)
            .Should().Be(new DateTime(2026, 3, 1, 18, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EndUtc_IncludesTheWholeChosenDay()
    {
        var end = LocalDayRange.EndUtc(new DateTime(2026, 3, 5), MinusFive);

        end.Should().Be(new DateTime(2026, 3, 6, 4, 59, 59, DateTimeKind.Utc).AddTicks(9_999_999));
        end.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Bounds_IgnoreTheTimeOfDayOfThePickedValue()
    {
        var picked = new DateTime(2026, 3, 2, 15, 45, 0);

        LocalDayRange.StartUtc(picked, MinusFive).Should().Be(new DateTime(2026, 3, 2, 5, 0, 0, DateTimeKind.Utc));
        LocalDayRange.EndUtc(picked, MinusFive).Should().Be(new DateTime(2026, 3, 3, 5, 0, 0, DateTimeKind.Utc).AddTicks(-1));
    }

    [Fact]
    public void StartUtc_WhenMidnightIsSkippedByDaylightSaving_StartsAtTheFirstValidTime()
    {
        // Clocks jump from 00:00 to 01:00 on 1 November, so local midnight does not exist.
        var zone = ZoneWithDaylightSaving(
            start: TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1),
            end: TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 2, 15));

        // 01:00 daylight time (UTC-2) is 03:00 UTC, the same instant standard midnight would be.
        LocalDayRange.StartUtc(new DateTime(2026, 11, 1), zone)
            .Should().Be(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StartUtc_WhenMidnightHappensTwice_StartsAtTheEarlierInstant()
    {
        // Clocks fall back from 01:00 daylight time to 00:00 on 15 February, so 00:00 to
        // 01:00 happens twice; the day starts at the first 00:00 (UTC-2, 02:00 UTC).
        var zone = ZoneWithDaylightSaving(
            start: TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1),
            end: TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 2, 15));

        LocalDayRange.StartUtc(new DateTime(2027, 2, 15), zone)
            .Should().Be(new DateTime(2027, 2, 15, 2, 0, 0, DateTimeKind.Utc));
        LocalDayRange.EndUtc(new DateTime(2027, 2, 14), zone)
            .Should().Be(new DateTime(2027, 2, 15, 2, 0, 0, DateTimeKind.Utc).AddTicks(-1));
    }

    /// <summary>A UTC-3 zone with one hour of daylight saving between the given transitions.</summary>
    private static TimeZoneInfo ZoneWithDaylightSaving(TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1),
            new DateTime(2099, 12, 31),
            TimeSpan.FromHours(1),
            start,
            end);

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test-03:00-DST", TimeSpan.FromHours(-3), "Test-03:00-DST", "Standard", "Daylight", new[] { rule });
    }
}

using AgentX.App.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Helpers;

public sealed class StatusToneResolverTests
{
    [Theory]
    [InlineData("Current", StatusTone.Success)]
    [InlineData("Enabled", StatusTone.Success)]
    [InlineData("5 pending", StatusTone.Warning)]
    [InlineData("3 conflicts", StatusTone.Warning)]
    [InlineData("Stale", StatusTone.Warning)]
    [InlineData("Refresh error", StatusTone.Danger)]
    [InlineData("Failed", StatusTone.Danger)]
    [InlineData("Running", StatusTone.Info)]
    [InlineData("Installed", StatusTone.Neutral)]
    [InlineData("History", StatusTone.Neutral)]
    public void Resolve_returns_expected_tone_for_operations_statuses(string status, StatusTone expected)
    {
        var tone = StatusToneResolver.Resolve(status);

        tone.Should().Be(expected);
    }

    [Fact]
    public void Resolve_returns_neutral_for_missing_status()
    {
        StatusToneResolver.Resolve(null).Should().Be(StatusTone.Neutral);
        StatusToneResolver.Resolve("   ").Should().Be(StatusTone.Neutral);
    }
}

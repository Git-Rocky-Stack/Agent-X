using System.Globalization;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// Pseudo-locale coverage for the RTL detector that drives the WinUI 3
/// <c>FlowDirection</c> binding in <c>MainWindow</c>. Agent-X has no RTL
/// locales today, but this fixture proves ar-SA / he-IL / fa-IR flip correctly
/// the moment their resw bundles ship — zero XAML changes required.
/// </summary>
public class PseudoLocaleFlowDirectionTests
{
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("de", false)]
    [InlineData("es", false)]
    [InlineData("fr", false)]
    [InlineData("ja", false)]
    [InlineData("zh-CN", false)]
    [InlineData("ar-SA", true)]   // Future locale — detector must already support it
    [InlineData("he-IL", true)]   // Future locale — detector must already support it
    [InlineData("fa-IR", true)]   // Future locale — detector must already support it
    public void IsRightToLeft_returns_expected_direction(string cultureName, bool expectedRtl)
    {
        var culture = new CultureInfo(cultureName);
        RtlDetector.IsRightToLeft(culture).Should().Be(expectedRtl);
    }

    [Fact]
    public void CurrentIsRightToLeft_follows_CurrentUICulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");
            RtlDetector.CurrentIsRightToLeft().Should().BeTrue();

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            RtlDetector.CurrentIsRightToLeft().Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}

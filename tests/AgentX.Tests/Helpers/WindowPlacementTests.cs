using AgentX.App.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Helpers;

/// <summary>
/// First-launch placement (SH25): the preferred 1440x900 must never place the window,
/// and its caption buttons, outside the display's work area.
/// </summary>
public sealed class WindowPlacementTests
{
    [Fact]
    public void CenterWithin_WorkAreaLargerThanPreferred_KeepsPreferredSizeCentered()
    {
        var bounds = WindowPlacement.CenterWithin(new PixelRect(0, 0, 1920, 1040), 1440, 900);

        bounds.Should().Be(new PixelRect(240, 70, 1440, 900));
    }

    [Fact]
    public void CenterWithin_1366x768Laptop_ClampsSoTheTitleBarStaysOnScreen()
    {
        // 768 minus a 40px taskbar. The old fixed size centered to (-37, -86).
        var workArea = new PixelRect(0, 0, 1366, 728);

        var bounds = WindowPlacement.CenterWithin(workArea, 1440, 900);

        bounds.Should().Be(new PixelRect(0, 0, 1366, 728));
        bounds.Y.Should().BeGreaterThanOrEqualTo(workArea.Y, "the title bar must not start above the work area");
    }

    [Fact]
    public void CenterWithin_ClampsEachAxisIndependently()
    {
        var bounds = WindowPlacement.CenterWithin(new PixelRect(0, 0, 1600, 860), 1440, 900);

        bounds.Should().Be(new PixelRect(80, 0, 1440, 860));
    }

    [Fact]
    public void CenterWithin_HonoursTheWorkAreaOrigin()
    {
        // A secondary monitor to the left of the primary, with a taskbar docked at its top.
        var workArea = new PixelRect(-1920, 48, 1920, 1032);

        var bounds = WindowPlacement.CenterWithin(workArea, 1440, 900);

        bounds.Should().Be(new PixelRect(-1680, 114, 1440, 900));
    }

    [Fact]
    public void CenterWithin_DegenerateWorkArea_StaysAtTheOriginWithAPositiveSize()
    {
        var bounds = WindowPlacement.CenterWithin(new PixelRect(10, 20, 0, 0), 1440, 900);

        bounds.Should().Be(new PixelRect(10, 20, 1, 1));
    }
}

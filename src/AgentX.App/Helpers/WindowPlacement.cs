namespace AgentX.App.Helpers;

/// <summary>
/// A rectangle in physical screen pixels, the unit AppWindow and DisplayArea use.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// First-launch window placement. The shell asks for a preferred size, but the display's
/// work area (the screen minus the taskbar) can be smaller: a fixed 1440x900 on a
/// 1366x768 laptop centered to a negative offset and pushed the title bar, and with it
/// the caption buttons, above the top edge of the screen. Clamping to the work area and
/// centering inside it, from the work area's own origin, keeps the whole window on the
/// display it opens on, including a secondary monitor or a top or left docked taskbar.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Returns the preferred size clamped to <paramref name="workArea"/>, centered in it.
    /// </summary>
    public static PixelRect CenterWithin(PixelRect workArea, int preferredWidth, int preferredHeight)
    {
        var width = Math.Max(1, Math.Min(preferredWidth, workArea.Width));
        var height = Math.Max(1, Math.Min(preferredHeight, workArea.Height));

        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

        return new PixelRect(x, y, width, height);
    }
}

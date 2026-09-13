namespace AgentX.App.Helpers;

/// <summary>
/// Width of a page's content column: the scroller's viewport, capped at the column's
/// MaxWidth. Bound to the column grid's MinWidth from XAML.
/// <para>
/// Why a helper exists for one line of arithmetic: a WinUI element with
/// <c>HorizontalAlignment="Stretch"</c> and a MaxWidth is arranged at the MaxWidth but
/// positioned where its content-sized self would be centered, so Weekly Digest and
/// Model Manager rendered up to 540px right of center and off the window (measured
/// 2026-09-06, reproduced on two Stretch variants). <c>Center</c> places the column
/// correctly but sizes it to its content, which left those pages narrower than
/// Dashboard. Binding MinWidth to <see cref="Fill"/> gives both: the column is as wide
/// as the cap allows, never wider than the viewport, and centered.
/// </para>
/// </summary>
public static class ContentColumn
{
    /// <summary>The column width: <paramref name="viewportWidth"/> capped at <paramref name="maxWidth"/>.</summary>
    public static double Fill(double viewportWidth, double maxWidth)
    {
        if (double.IsNaN(viewportWidth) || double.IsInfinity(viewportWidth) || viewportWidth <= 0)
        {
            return 0;
        }

        return System.Math.Min(viewportWidth, maxWidth);
    }
}

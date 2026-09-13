using Microsoft.UI.Xaml.Media;

namespace AgentX.App.Helpers;

/// <summary>
/// The five highlighter inks an annotation can carry, by the name persisted on
/// <c>AnnotationEntity.Color</c> ("yellow", "green", "blue", "red", "purple").
/// <para>
/// This is user content, not chassis chrome. DESIGN.md governs the instrument's
/// palette and bans purple-violet from it; a highlight the operator saved as
/// "purple" still has to render purple, and the five names are a database
/// contract that cannot move. This file is the only source exempt from
/// <c>NoBannedPaletteHuesTests</c>, and that guard checks it holds nothing but
/// these six literals.
/// </para>
/// </summary>
public static class AnnotationInk
{
    /// <summary>Brush for a persisted annotation colour name; grey for an unknown name.</summary>
    public static SolidColorBrush BrushFor(string color) => color.ToLowerInvariant() switch
    {
        "yellow" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 204, 21)),
        "green" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 222, 128)),
        "blue" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250)),
        "red" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
        "purple" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 132, 252)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
    };
}

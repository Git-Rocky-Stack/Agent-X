namespace AgentX.App.Helpers;

/// <summary>
/// The theme tokens that paint the system caption buttons (minimize, maximize, close).
/// <para>
/// Those buttons are drawn by the system over the extended title bar, so XAML
/// ThemeResource bindings never reach them: the colors have to be pushed to
/// AppWindowTitleBar in code, and pushed again on every shift change. They used to be
/// hardcoded near-white, which read fine on Night Ops and nearly vanished on Day Shift
/// silver. Each key below is defined in both the Default (Night Ops) and Light (Day
/// Shift) ThemeDictionaries of Styles/Colors.xaml, so each shift gets its own silver
/// text ramp and hairline tints (DESIGN.md Color: Text and Hairlines).
/// </para>
/// </summary>
public static class CaptionButtonPalette
{
    /// <summary>Resource keys for each caption-button state; each must resolve to a SolidColorBrush.</summary>
    public sealed record Tokens(
        string Foreground,
        string HoverForeground,
        string PressedForeground,
        string InactiveForeground,
        string HoverBackground,
        string PressedBackground);

    /// <summary>The Night Ops and Day Shift mapping. Resting backgrounds stay transparent.</summary>
    public static readonly Tokens Hardware = new(
        Foreground: "TextSecondaryBrush",
        HoverForeground: "TextPrimaryBrush",
        PressedForeground: "TextSecondaryBrush",
        InactiveForeground: "TextTertiaryBrush",
        HoverBackground: "HairlineBrush",
        PressedBackground: "HairlineStrongBrush");

    /// <summary>
    /// The tokens for the current contrast mode, or null under HighContrast. HighContrast
    /// is exempt from the hardware skin (DESIGN.md Theme Policy): null means every caption
    /// color is reset so the system draws the buttons in the user's contrast theme.
    /// </summary>
    public static Tokens? For(bool highContrast) => highContrast ? null : Hardware;
}

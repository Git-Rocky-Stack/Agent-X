using System.Globalization;

namespace AgentX.Core.Services.Localization;

/// <summary>
/// Detects whether a <see cref="CultureInfo"/> is right-to-left. Kept in Core (no WinUI
/// dependency) so tests can cover it without pulling the Windows App SDK into the test
/// project. The WinUI 3 <c>FlowDirectionHelper</c> in <c>AgentX.App</c> delegates here
/// and translates the bool into a <c>Microsoft.UI.Xaml.FlowDirection</c> enum value.
/// </summary>
public static class RtlDetector
{
    /// <summary>Returns <c>true</c> for right-to-left cultures (ar-*, he-*, fa-*, ps-*, ur-*, etc.).</summary>
    public static bool IsRightToLeft(CultureInfo culture)
        => culture?.TextInfo.IsRightToLeft ?? false;

    /// <summary>Returns <c>true</c> if <see cref="CultureInfo.CurrentUICulture"/> is right-to-left.</summary>
    public static bool CurrentIsRightToLeft() => IsRightToLeft(CultureInfo.CurrentUICulture);
}

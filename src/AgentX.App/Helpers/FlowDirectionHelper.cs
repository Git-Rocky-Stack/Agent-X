using System.Globalization;
using AgentX.Core.Services.Localization;
using Microsoft.UI.Xaml;

namespace AgentX.App.Helpers;

/// <summary>
/// Computes the correct <see cref="FlowDirection"/> for the current UI culture.
/// Agent-X ships with LTR locales only today (de / en-US / es / fr / ja / zh-CN), but
/// this helper is wired from day one so ar-SA / he-IL / fa-IR can be added later without
/// touching XAML — a new resw bundle is all that's needed.
/// </summary>
/// <remarks>
/// Delegates to <see cref="RtlDetector"/> in <c>AgentX.Core</c>. Core owns the pure
/// culture-to-bool mapping (unit-tested there); this shim exists solely to translate
/// the bool into the WinUI 3 enum.
/// </remarks>
public static class FlowDirectionHelper
{
    /// <summary>Returns <see cref="FlowDirection.RightToLeft"/> for RTL cultures, otherwise LTR.</summary>
    public static FlowDirection ForCulture(CultureInfo culture)
        => RtlDetector.IsRightToLeft(culture) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Current UI-culture-derived flow direction.</summary>
    public static FlowDirection Current() => ForCulture(CultureInfo.CurrentUICulture);
}

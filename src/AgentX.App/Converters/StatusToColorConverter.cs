using AgentX.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a status string (e.g., "connected", "disconnected", "processing") to a
/// <see cref="SolidColorBrush"/> for visual status indication in the UI.
/// The comparison is case-insensitive.
///
/// Tones are shift-aware: Night Shift uses the LED text colors as-is (they sit on
/// dark grounds), Day Shift uses the darkened Led*TextBrush ramp from Colors.xaml
/// so status text stays legible on silver surfaces. The theme is read from the
/// window root's ActualTheme because ThemeService applies themes per root element,
/// which app-level resource lookups do not follow.
/// </summary>
public sealed class StatusToColorConverter : IValueConverter
{
    // ── Pre-allocated brushes to avoid repeated allocations ─────────────

    // Night Shift: LED text tones on dark grounds (Colors.xaml Default dict)
    private static readonly SolidColorBrush NightGreen =
        new(Color.FromArgb(255, 0x41, 0xE2, 0x5E));    // LedGo
    private static readonly SolidColorBrush NightRed =
        new(Color.FromArgb(255, 0xC8, 0x45, 0x3E));    // LedNoGo (steady terminal fault)
    private static readonly SolidColorBrush NightAmber =
        new(Color.FromArgb(255, 0xFF, 0xB0, 0x00));    // LedHold
    private static readonly SolidColorBrush NightBlue =
        new(Color.FromArgb(255, 0x58, 0xC4, 0xBC));    // LedScope
    private static readonly SolidColorBrush NightGray =
        new(Color.FromArgb(255, 0xB3, 0xB3, 0xB3));    // silver ramp neutral

    // Day Shift: darkened LED text tones on silver grounds (Colors.xaml Light dict)
    private static readonly SolidColorBrush DayGreen =
        new(Color.FromArgb(255, 0x17, 0x7A, 0x3D));    // LedGoText (Day)
    private static readonly SolidColorBrush DayRed =
        new(Color.FromArgb(255, 0xC8, 0x1E, 0x13));    // LedNoGoText (Day)
    private static readonly SolidColorBrush DayAmber =
        new(Color.FromArgb(255, 0x99, 0x63, 0x00));    // LedHoldText (Day)
    private static readonly SolidColorBrush DayBlue =
        new(Color.FromArgb(255, 0x25, 0x6F, 0x69));    // LedScopeText (Day)
    private static readonly SolidColorBrush DayGray =
        new(Color.FromArgb(255, 0x62, 0x62, 0x5E));    // silver ramp neutral (Day)

    private static bool IsDayShift()
    {
        try
        {
            return App.MainWindow?.Content is FrameworkElement root
                && root.ActualTheme == ElementTheme.Light;
        }
        catch
        {
            return false;
        }
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var day = IsDayShift();
        return StatusToneResolver.Resolve(value?.ToString()) switch
        {
            StatusTone.Success => day ? DayGreen : NightGreen,
            StatusTone.Danger => day ? DayRed : NightRed,
            StatusTone.Warning => day ? DayAmber : NightAmber,
            StatusTone.Info => day ? DayBlue : NightBlue,
            _ => day ? DayGray : NightGray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Cannot meaningfully map a brush back to a status string
        throw new NotSupportedException(
            $"{nameof(StatusToColorConverter)} does not support ConvertBack.");
    }
}

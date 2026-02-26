using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a status string (e.g., "connected", "disconnected", "processing") to a
/// <see cref="SolidColorBrush"/> for visual status indication in the UI.
/// The comparison is case-insensitive.
/// </summary>
public sealed class StatusToColorConverter : IValueConverter
{
    // ── Pre-allocated brushes to avoid repeated allocations ─────────────

    // Green for connected/online/active/ready/success/completed
    private static readonly SolidColorBrush GreenBrush =
        new(Color.FromArgb(255, 16, 185, 129));    // #10B981 (Emerald 500)

    // Red for disconnected/offline/error/failed
    private static readonly SolidColorBrush RedBrush =
        new(Color.FromArgb(255, 239, 68, 68));      // #EF4444 (Red 500)

    // Amber for processing/loading/pending/syncing/warning
    private static readonly SolidColorBrush AmberBrush =
        new(Color.FromArgb(255, 245, 158, 11));      // #F59E0B (Amber 500)

    // Blue for info/downloading/indexing/updating
    private static readonly SolidColorBrush BlueBrush =
        new(Color.FromArgb(255, 59, 130, 246));      // #3B82F6 (Blue 500)

    // Gray for unknown/idle/paused/disabled/default
    private static readonly SolidColorBrush GrayBrush =
        new(Color.FromArgb(255, 107, 114, 128));     // #6B7280 (Gray 500)

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;

        return status switch
        {
            // Green statuses
            "connected" or "online" or "active" or "ready"
                or "success" or "completed" or "healthy" => GreenBrush,

            // Red statuses
            "disconnected" or "offline" or "error" or "failed"
                or "critical" or "unavailable" => RedBrush,

            // Amber statuses
            "processing" or "loading" or "pending" or "syncing"
                or "warning" or "busy" or "queued" => AmberBrush,

            // Blue statuses
            "info" or "downloading" or "indexing" or "updating"
                or "installing" or "running" => BlueBrush,

            // Gray / fallback
            "idle" or "paused" or "disabled" or "unknown"
                or "" => GrayBrush,

            _ => GrayBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Cannot meaningfully map a brush back to a status string
        throw new NotSupportedException(
            $"{nameof(StatusToColorConverter)} does not support ConvertBack.");
    }
}

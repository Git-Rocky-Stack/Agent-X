namespace AgentX.Core.Helpers;

public static class FormatHelper
{
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public static string FormatDuration(double milliseconds) => milliseconds switch
    {
        < 1000 => $"{milliseconds:F0}ms",
        < 60_000 => $"{milliseconds / 1000:F1}s",
        _ => $"{milliseconds / 60_000:F1}m"
    };

    public static string FormatNumber(long number) => number switch
    {
        < 1_000 => number.ToString(),
        < 1_000_000 => $"{number / 1_000.0:F1}K",
        _ => $"{number / 1_000_000.0:F1}M"
    };

    public static string FormatTokens(long tokens) => tokens switch
    {
        < 1_000 => $"{tokens} tokens",
        < 1_000_000 => $"{tokens / 1_000.0:F1}K tokens",
        _ => $"{tokens / 1_000_000.0:F1}M tokens"
    };

    public static string TimeAgo(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 43200 => $"{(int)span.TotalDays}d ago",
            _ => dateTime.ToString("MMM d, yyyy")
        };
    }

    public static string FormatPercent(double value) => $"{value:F0}%";

    public static string FormatLatency(double milliseconds) => milliseconds switch
    {
        < 1000 => $"{milliseconds:F0}ms",
        < 60_000 => $"{milliseconds / 1000:F1}s",
        < 3_600_000 => $"{milliseconds / 60_000:F1}m",
        _ => $"{milliseconds / 3_600_000:F1}h"
    };

    public static string TimeAgoWithMonths(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 10080 => $"{(int)span.TotalDays}d ago",
            < 43200 => $"{(int)(span.TotalDays / 7)}w ago",
            < 525600 => $"{(int)(span.TotalDays / 30)}mo ago",
            _ => dateTime.ToString("MMM d, yyyy")
        };
    }
}

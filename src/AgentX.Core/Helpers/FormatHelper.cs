using System.Globalization;

namespace AgentX.Core.Helpers;

public static class FormatHelper
{
    /// <summary>
    /// Words the text Core builds for the user in the user's language: <see cref="TimeAgo(DateTime)"/>,
    /// <see cref="TimeAgoWithMonths(DateTime)"/>, the chat context inspector's story, chips and
    /// explanations, and the dashboard's privacy disclosures. Given a resource key such as
    /// "TimeAgo_MinutesAgo" it returns that resource, or null, an empty string or the key itself
    /// when there is none. Core cannot read the app's resources, so the app sets this at startup
    /// to its localization service's GetString. Until then, and for a resource that is missing or
    /// does not format, the wording is English ("5m ago", "just now"). See <see cref="LocalizedWords"/>.
    /// </summary>
    public static Func<string, string?>? LocalizedText { get; set; }

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

    public static string TimeAgo(DateTime dateTime) => TimeAgo(dateTime, DateTime.UtcNow, LocalizedText);

    /// <summary>
    /// How long before <paramref name="utcNow"/> the UTC <paramref name="dateTime"/> was, worded
    /// by <paramref name="localizedText"/>: just now, then minutes, hours and days, then the date
    /// from 30 days on.
    /// </summary>
    internal static string TimeAgo(DateTime dateTime, DateTime utcNow, Func<string, string?>? localizedText)
    {
        var words = new LocalizedWords(localizedText);
        var span = utcNow - dateTime;
        return span.TotalMinutes switch
        {
            < 1 => JustNow(words),
            < 60 => MinutesAgo(words, (int)span.TotalMinutes),
            < 1440 => HoursAgo(words, (int)span.TotalHours),
            < 43200 => DaysAgo(words, (int)span.TotalDays),
            _ => DateOf(words, dateTime)
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

    public static string TimeAgoWithMonths(DateTime dateTime) =>
        TimeAgoWithMonths(dateTime, DateTime.UtcNow, LocalizedText);

    /// <summary>
    /// <see cref="TimeAgo(DateTime, DateTime, Func{string, string})"/> with weeks and months
    /// between the days and the date, which it only reaches after a year.
    /// </summary>
    internal static string TimeAgoWithMonths(DateTime dateTime, DateTime utcNow, Func<string, string?>? localizedText)
    {
        var words = new LocalizedWords(localizedText);
        var span = utcNow - dateTime;
        return span.TotalMinutes switch
        {
            < 1 => JustNow(words),
            < 60 => MinutesAgo(words, (int)span.TotalMinutes),
            < 1440 => HoursAgo(words, (int)span.TotalHours),
            < 10080 => DaysAgo(words, (int)span.TotalDays),
            < 43200 => WeeksAgo(words, (int)(span.TotalDays / 7)),
            < 525600 => MonthsAgo(words, (int)(span.TotalDays / 30)),
            _ => DateOf(words, dateTime)
        };
    }

    // One method per unit, with the singular and the plural as separate resources so that each
    // language words both ("1 minute" and "5 minutes" differ in most of them). Every key is named
    // literally in its GetString call, which is how the locale audit finds it.

    private static string JustNow(LocalizedWords words) =>
        words.GetString("TimeAgo_JustNow", "just now");

    private static string MinutesAgo(LocalizedWords words, int minutes) => minutes == 1
        ? words.GetString("TimeAgo_MinuteAgo", "1m ago", minutes)
        : words.GetString("TimeAgo_MinutesAgo", "{0}m ago", minutes);

    private static string HoursAgo(LocalizedWords words, int hours) => hours == 1
        ? words.GetString("TimeAgo_HourAgo", "1h ago", hours)
        : words.GetString("TimeAgo_HoursAgo", "{0}h ago", hours);

    private static string DaysAgo(LocalizedWords words, int days) => days == 1
        ? words.GetString("TimeAgo_DayAgo", "1d ago", days)
        : words.GetString("TimeAgo_DaysAgo", "{0}d ago", days);

    private static string WeeksAgo(LocalizedWords words, int weeks) => weeks == 1
        ? words.GetString("TimeAgo_WeekAgo", "1w ago", weeks)
        : words.GetString("TimeAgo_WeeksAgo", "{0}w ago", weeks);

    private static string MonthsAgo(LocalizedWords words, int months) => months == 1
        ? words.GetString("TimeAgo_MonthAgo", "1mo ago", months)
        : words.GetString("TimeAgo_MonthsAgo", "{0}mo ago", months);

    /// <summary>The date itself, in the order the user's language writes a date.</summary>
    private static string DateOf(LocalizedWords words, DateTime dateTime)
    {
        const string EnglishPattern = "MMM d, yyyy";
        var pattern = words.GetString("TimeAgo_DateFormat", EnglishPattern);
        try
        {
            return dateTime.ToString(pattern, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return dateTime.ToString(EnglishPattern, CultureInfo.CurrentCulture);
        }
    }
}

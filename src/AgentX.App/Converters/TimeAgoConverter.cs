using AgentX.Core.Helpers;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> to a relative
/// time-ago string using <see cref="FormatHelper.TimeAgo"/>.
/// Examples: "just now", "5m ago", "2h ago", "3d ago", or "Jan 15, 2026".
/// </summary>
public sealed class TimeAgoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            DateTime dateTime => FormatHelper.TimeAgo(dateTime),
            DateTimeOffset dateTimeOffset => FormatHelper.TimeAgo(dateTimeOffset.UtcDateTime),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Cannot parse a relative time-ago string back to a DateTime
        throw new NotSupportedException(
            $"{nameof(TimeAgoConverter)} does not support ConvertBack.");
    }
}

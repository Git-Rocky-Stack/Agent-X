using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a percentage value (0.0 to 1.0) to an actual pixel width by multiplying
/// it against a maximum width supplied via the converter parameter.
/// </summary>
/// <example>
/// XAML usage where the progress bar has a max width of 200:
///   Width="{x:Bind Progress, Converter={StaticResource PercentToWidthConverter}, ConverterParameter=200}"
/// </example>
public sealed class PercentToWidthConverter : IValueConverter
{
    private const double DefaultMaxWidth = 100.0;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var percent = value switch
        {
            double d => d,
            float f => (double)f,
            int i => (double)i,
            long l => (double)l,
            decimal dec => (double)dec,
            _ => 0.0
        };

        // Clamp the percentage to [0.0, 1.0]
        percent = Math.Clamp(percent, 0.0, 1.0);

        // Parse maximum width from the converter parameter, defaulting to 100
        var maxWidth = DefaultMaxWidth;
        if (parameter is not null && double.TryParse(parameter.ToString(), out var parsed))
            maxWidth = Math.Max(0.0, parsed);

        return percent * maxWidth;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Reverse: divide actual width by max width to get percentage
        if (value is not double actualWidth)
            return 0.0;

        var maxWidth = DefaultMaxWidth;
        if (parameter is not null && double.TryParse(parameter.ToString(), out var parsed))
            maxWidth = Math.Max(0.0, parsed);

        if (maxWidth <= 0.0)
            return 0.0;

        return Math.Clamp(actualWidth / maxWidth, 0.0, 1.0);
    }
}

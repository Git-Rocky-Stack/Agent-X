using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a percentage value (0-100) to a pixel height for bar chart rendering.
/// The maximum height is supplied via the converter parameter (default: 120px).
/// A minimum visible height of 4px is enforced so that bars with very small
/// values remain visible rather than collapsing to zero.
/// </summary>
/// <example>
/// XAML usage with a max bar height of 120:
///   Height="{Binding BarHeightPercent, Converter={StaticResource PercentToHeight}, ConverterParameter=120}"
/// </example>
public sealed class PercentToHeightConverter : IValueConverter
{
    private const double DefaultMaxHeight = 120.0;
    private const double MinimumVisibleHeight = 4.0;

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

        // Clamp the percentage to [0, 100]
        percent = Math.Clamp(percent, 0.0, 100.0);

        // Parse maximum height from the converter parameter, defaulting to 120
        var maxHeight = DefaultMaxHeight;
        if (parameter is not null && double.TryParse(parameter.ToString(), out var parsed))
            maxHeight = Math.Max(0.0, parsed);

        var height = percent / 100.0 * maxHeight;

        // Enforce minimum visible height for non-zero values
        if (percent > 0.0 && height < MinimumVisibleHeight)
            height = MinimumVisibleHeight;

        return height;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Reverse: divide actual height by max height to get percentage
        if (value is not double actualHeight)
            return 0.0;

        var maxHeight = DefaultMaxHeight;
        if (parameter is not null && double.TryParse(parameter.ToString(), out var parsed))
            maxHeight = Math.Max(0.0, parsed);

        if (maxHeight <= 0.0)
            return 0.0;

        return Math.Clamp(actualHeight / maxHeight * 100.0, 0.0, 100.0);
    }
}

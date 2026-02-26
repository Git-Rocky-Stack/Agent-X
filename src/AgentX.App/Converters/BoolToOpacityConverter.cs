using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a <see cref="bool"/> value to an opacity <see cref="double"/>.
/// By default, <c>true</c> maps to 1.0 (fully opaque) and <c>false</c> maps to 0.5 (half opacity).
/// The false-state opacity can be overridden by passing a numeric value as the converter parameter.
/// </summary>
/// <example>
/// XAML usage with default values:
///   Opacity="{x:Bind IsEnabled, Converter={StaticResource BoolToOpacityConverter}}"
///
/// XAML usage with custom disabled opacity of 0.3:
///   Opacity="{x:Bind IsEnabled, Converter={StaticResource BoolToOpacityConverter}, ConverterParameter=0.3}"
/// </example>
public sealed class BoolToOpacityConverter : IValueConverter
{
    private const double DefaultTrueOpacity = 1.0;
    private const double DefaultFalseOpacity = 0.5;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is true;

        if (boolValue)
            return DefaultTrueOpacity;

        // Allow the false-state opacity to be specified via the converter parameter
        if (parameter is not null && double.TryParse(parameter.ToString(), out var customOpacity))
            return Math.Clamp(customOpacity, 0.0, 1.0);

        return DefaultFalseOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Consider anything at or above the midpoint between true and false opacity as "true"
        if (value is double opacity)
            return opacity >= DefaultTrueOpacity;

        return false;
    }
}

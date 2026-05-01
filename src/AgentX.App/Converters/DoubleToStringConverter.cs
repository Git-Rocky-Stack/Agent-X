using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a double value to its string representation.
/// Used for displaying numeric values in XAML data templates.
/// </summary>
public sealed class DoubleToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
        {
            // Format with 2 decimal places for readability
            return d.ToString("F2");
        }
        return "0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string s && double.TryParse(s, out double result))
        {
            return result;
        }
        return 0.0;
    }
}

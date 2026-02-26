using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Inverts a <see cref="bool"/> value. <c>true</c> becomes <c>false</c> and vice versa.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is not true;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a <see cref="bool"/> value to a <see cref="Visibility"/> value.
/// By default, <c>true</c> maps to <see cref="Visibility.Visible"/> and
/// <c>false</c> maps to <see cref="Visibility.Collapsed"/>.
/// Set <see cref="IsInverted"/> to <c>true</c> to reverse the mapping.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// When <c>true</c>, inverts the conversion so that <c>false</c> becomes
    /// <see cref="Visibility.Visible"/> and <c>true</c> becomes <see cref="Visibility.Collapsed"/>.
    /// </summary>
    public bool IsInverted { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is true;

        if (IsInverted)
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility visibility && visibility == Visibility.Visible;

        if (IsInverted)
            isVisible = !isVisible;

        return isVisible;
    }
}

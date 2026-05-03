using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a string value to Visibility. Non-empty strings become Visible,
/// empty or null strings become Collapsed.
/// </summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// When <c>true</c>, inverts the conversion so that empty strings become Visible.
    /// </summary>
    public bool IsInverted { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasContent = !string.IsNullOrEmpty(value as string);
        var invert = IsInverted || (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase));

        if (invert)
            hasContent = !hasContent;

        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return DependencyProperty.UnsetValue;
    }
}

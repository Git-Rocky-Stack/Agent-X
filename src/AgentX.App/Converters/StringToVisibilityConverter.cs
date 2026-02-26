using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a <see cref="string"/> value to a <see cref="Visibility"/> value.
/// A non-null, non-empty, non-whitespace string maps to <see cref="Visibility.Visible"/>;
/// null, empty, or whitespace-only strings map to <see cref="Visibility.Collapsed"/>.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasContent = value is string str && !string.IsNullOrWhiteSpace(str);

        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Cannot meaningfully convert Visibility back to a string
        throw new NotSupportedException(
            $"{nameof(StringToVisibilityConverter)} does not support ConvertBack.");
    }
}

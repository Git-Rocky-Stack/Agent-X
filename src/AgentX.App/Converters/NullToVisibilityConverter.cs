using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a nullable reference to a <see cref="Visibility"/> value.
/// By default, a non-null value maps to <see cref="Visibility.Visible"/> and
/// a null value maps to <see cref="Visibility.Collapsed"/>.
/// Set <see cref="IsInverted"/> to <c>true</c> to reverse the mapping
/// (null becomes Visible, non-null becomes Collapsed).
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// When <c>true</c>, inverts the conversion so that null becomes
    /// <see cref="Visibility.Visible"/> and non-null becomes <see cref="Visibility.Collapsed"/>.
    /// </summary>
    public bool IsInverted { get; set; }

    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        var isNotNull = value is not null;

        if (IsInverted)
            isNotNull = !isNotNull;

        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Cannot meaningfully convert Visibility back to an arbitrary reference type
        throw new NotSupportedException(
            $"{nameof(NullToVisibilityConverter)} does not support ConvertBack.");
    }
}

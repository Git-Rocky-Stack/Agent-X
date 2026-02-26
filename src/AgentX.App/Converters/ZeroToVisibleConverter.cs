using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Returns Visible when the input value is zero (or null/empty), Collapsed otherwise.
/// Useful for showing empty-state panels when a collection has no items.
/// </summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => 0
        };

        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

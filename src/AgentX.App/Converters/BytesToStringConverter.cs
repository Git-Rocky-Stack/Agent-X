using AgentX.Core.Helpers;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a numeric byte count (<see cref="long"/>, <see cref="int"/>, <see cref="double"/>, etc.)
/// to a human-readable string using <see cref="FormatHelper.FormatBytes"/>.
/// Examples: 1024 becomes "1.0 KB", 5242880 becomes "5.0 MB".
/// </summary>
public sealed class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var bytes = value switch
        {
            long l => l,
            int i => (long)i,
            double d => (long)d,
            float f => (long)f,
            uint u => (long)u,
            ulong ul => (long)ul,
            _ => 0L
        };

        return FormatHelper.FormatBytes(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Parsing a formatted byte string back to a numeric value is not supported
        throw new NotSupportedException(
            $"{nameof(BytesToStringConverter)} does not support ConvertBack.");
    }
}

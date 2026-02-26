using AgentX.Core.Helpers;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a numeric token count (<see cref="long"/>, <see cref="int"/>, etc.)
/// to a human-readable string using <see cref="FormatHelper.FormatTokens"/>.
/// Examples: 500 becomes "500 tokens", 1500 becomes "1.5K tokens".
/// </summary>
public sealed class TokensToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var tokens = value switch
        {
            long l => l,
            int i => (long)i,
            double d => (long)d,
            float f => (long)f,
            uint u => (long)u,
            ulong ul => (long)ul,
            _ => 0L
        };

        return FormatHelper.FormatTokens(tokens);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Parsing a formatted token string back to a numeric value is not supported
        throw new NotSupportedException(
            $"{nameof(TokensToStringConverter)} does not support ConvertBack.");
    }
}

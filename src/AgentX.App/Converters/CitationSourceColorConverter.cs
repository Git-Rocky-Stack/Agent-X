using AgentX.Core.Search.Models;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AgentX.App.Converters;

/// <summary>
/// Converts a <see cref="WebCitationSource"/> value to a colored brush
/// for display in citation badges. Vault citations are blue; Web citations are orange.
/// </summary>
public class CitationSourceColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is WebCitationSource source
            ? source == WebCitationSource.Vault
                ? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue)
                : new SolidColorBrush(Microsoft.UI.Colors.Orange)
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a <see cref="WebCitationSource"/> value to a human-readable label.
/// Vault becomes "Vault"; Web becomes "Web".
/// </summary>
public class CitationSourceTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is WebCitationSource source
            ? source == WebCitationSource.Vault ? "Vault" : "Web"
            : "Unknown";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
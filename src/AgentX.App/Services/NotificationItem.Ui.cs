using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AgentX.App.Services;

/// <summary>
/// WinUI-only presentation members for <see cref="NotificationItem"/>.
/// Kept in a separate partial so the core notification model in
/// <c>NotificationService.cs</c> stays UI-framework-free and can be compiled
/// into non-WinUI projects (e.g. the unit-test project links it directly).
/// </summary>
public partial class NotificationItem
{
    /// <summary>
    /// Resolved semantic brush for the severity icon, looked up from the app
    /// resource dictionary via <see cref="SeverityColorKey"/>. Falls back to white
    /// if the keyed resource is unavailable (e.g. design-time).
    /// </summary>
    public Brush SeverityBrush =>
        Application.Current?.Resources is { } resources
        && resources.TryGetValue(SeverityColorKey, out var brush)
        && brush is Brush severityBrush
            ? severityBrush
            : new SolidColorBrush(Microsoft.UI.Colors.White);
}

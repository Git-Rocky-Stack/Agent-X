using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.Services;

/// <summary>
/// Severity level for toast notifications.
/// </summary>
public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Represents a single toast notification displayed in the app overlay.
/// </summary>
public partial class NotificationItem : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int DurationMs { get; set; } = 4000;

    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private double _opacity = 1.0;

    public string GlyphIcon => Severity switch
    {
        NotificationSeverity.Success => "\uE73E",   // Checkmark
        NotificationSeverity.Warning => "\uE7BA",   // Warning
        NotificationSeverity.Error => "\uEA39",     // Error badge
        _ => "\uE946"                                // Info
    };

    public string SeverityColorKey => Severity switch
    {
        NotificationSeverity.Success => "LedGoLampBrush",
        NotificationSeverity.Warning => "LedHoldLampBrush",
        NotificationSeverity.Error => "LedNoGoLampBrush",
        _ => "LedScopeLampBrush"
    };
}

/// <summary>
/// Application-wide toast notification service. Manages a queue of notifications
/// that auto-dismiss after their duration. Used by pages and services to surface
/// non-blocking status messages (errors, confirmations, completions).
/// </summary>
public interface INotificationService
{
    ObservableCollection<NotificationItem> Notifications { get; }

    void Show(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, int durationMs = 4000);
    void ShowSuccess(string title, string message, int durationMs = 3000);
    void ShowError(string title, string message, int durationMs = 6000);
    void ShowWarning(string title, string message, int durationMs = 5000);
    void ShowInfo(string title, string message, int durationMs = 4000);
    void Dismiss(string notificationId);
}

/// <summary>
/// Default implementation of <see cref="INotificationService"/>.
/// Notifications auto-dismiss via DispatcherQueue timers.
/// </summary>
public class NotificationService : INotificationService
{
    private const int MaxVisible = 5;

    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    public void Show(string title, string message, NotificationSeverity severity, int durationMs = 4000)
    {
        var item = new NotificationItem
        {
            Title = title,
            Message = message,
            Severity = severity,
            DurationMs = durationMs
        };

        // Limit visible count
        while (Notifications.Count >= MaxVisible)
        {
            Notifications.RemoveAt(0);
        }

        Notifications.Add(item);

        // Auto-dismiss after duration
        _ = AutoDismissAsync(item);
    }

    public void ShowSuccess(string title, string message, int durationMs = 3000) =>
        Show(title, message, NotificationSeverity.Success, durationMs);

    public void ShowError(string title, string message, int durationMs = 6000) =>
        Show(title, message, NotificationSeverity.Error, durationMs);

    public void ShowWarning(string title, string message, int durationMs = 5000) =>
        Show(title, message, NotificationSeverity.Warning, durationMs);

    public void ShowInfo(string title, string message, int durationMs = 4000) =>
        Show(title, message, NotificationSeverity.Info, durationMs);

    public void Dismiss(string notificationId)
    {
        var item = Notifications.FirstOrDefault(n => n.Id == notificationId);
        if (item is not null)
        {
            Notifications.Remove(item);
        }
    }

    private async Task AutoDismissAsync(NotificationItem item)
    {
        await Task.Delay(item.DurationMs);

        // Fade out
        item.Opacity = 0;
        await Task.Delay(300);

        if (Notifications.Contains(item))
        {
            Notifications.Remove(item);
        }
    }
}

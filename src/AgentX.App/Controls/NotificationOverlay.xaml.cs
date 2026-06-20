using AgentX.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Controls;

/// <summary>
/// Overlay control that displays toast notifications anchored to the top-right of its parent.
/// Bind <see cref="ItemsSource"/> to the notification service's collection.
/// </summary>
public sealed partial class NotificationOverlay : UserControl
{
    private INotificationService? _notificationService;

    public NotificationOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _notificationService = App.GetService<INotificationService>();
            NotificationRepeater.ItemsSource = _notificationService.Notifications;
        }
        catch
        {
            // Service not yet available — will be bound later
        }
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id && _notificationService is not null)
        {
            _notificationService.Dismiss(id);
        }
    }
}

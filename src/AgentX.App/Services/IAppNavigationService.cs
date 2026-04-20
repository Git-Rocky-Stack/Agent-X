using System;

namespace AgentX.App.Services;

/// <summary>
/// Manages page navigation within the main window's content frame and NavigationView.
/// Keeps the frame and nav pane selection in sync, and exposes navigation for
/// command palette, shortcuts, and tray menu actions.
/// </summary>
public interface IAppNavigationService
{
    /// <summary>
    /// Initializes the service with the page map, nav item map, content frame,
    /// and NavigationView references. Must be called once during window construction.
    /// </summary>
    void Initialize(
        System.Collections.Generic.Dictionary<string, Type> pageMap,
        System.Collections.Generic.Dictionary<string, Microsoft.UI.Xaml.Controls.NavigationViewItem> navItemMap,
        Microsoft.UI.Xaml.Controls.Frame contentFrame,
        Microsoft.UI.Xaml.Controls.NavigationView navView);

    /// <summary>
    /// Navigates to a page by its tag name, updating both the content frame and
    /// the NavigationView selection indicator.
    /// </summary>
    void NavigateToPage(string pageKey);

    /// <summary>
    /// Ensures the NavigationView pane is visible and open.
    /// Used after onboarding completes or when recovering from hidden-pane states.
    /// </summary>
    void EnsureNavPaneVisible();

    /// <summary>
    /// Executes a non-navigation action by action ID (e.g., "NewConversation", "ToggleTheme").
    /// </summary>
    void ExecuteAction(string actionId);

    /// <summary>
    /// The tag of the currently displayed page, or null if unknown.
    /// </summary>
    string? CurrentPage { get; }

    /// <summary>
    /// Raised after a successful navigation to a new page.
    /// The event arg is the page tag.
    /// </summary>
    event EventHandler<string>? PageChanged;

    /// <summary>
    /// Gets or sets whether navigation events should be suppressed.
    /// Used during onboarding when NavView state is being programmatically modified.
    /// </summary>
    bool SuppressNavigation { get; set; }
}

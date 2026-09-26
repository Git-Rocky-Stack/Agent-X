namespace AgentX.App.Services;

/// <summary>
/// The slice of shell navigation state the first-run flow drives: whether rail
/// selections navigate, and whether the rail is shown. Split out of
/// <c>IAppNavigationService</c> (whose Initialize signature carries WinUI types) so
/// the onboarding logic that drives it can be unit tested without a WinUI runtime.
/// </summary>
public interface INavigationGate
{
    /// <summary>
    /// Gets or sets whether NavigationView selections are ignored instead of navigating.
    /// Set while the onboarding wizard owns the shell and cleared the moment it stops
    /// owning it. Nothing else may leave it set: while it is true every rail click is
    /// dropped.
    /// </summary>
    bool SuppressNavigation { get; set; }

    /// <summary>
    /// Ensures the NavigationView pane is visible and open.
    /// Used after onboarding ends or when recovering from hidden-pane states.
    /// </summary>
    void EnsureNavPaneVisible();
}

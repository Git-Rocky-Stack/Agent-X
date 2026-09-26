using System.Threading.Tasks;

namespace AgentX.App.Services;

/// <summary>
/// Manages the first-run onboarding flow. Checks whether onboarding has been
/// completed and coordinates the navigation to/from the onboarding wizard.
/// </summary>
public interface IOnboardingService
{
    /// <summary>
    /// True from <see cref="BeginOnboarding"/> until the wizard is finished or left.
    /// While it is true the wizard owns the shell and rail selections are suppressed.
    /// </summary>
    bool IsOnboardingActive { get; }

    /// <summary>
    /// Checks whether onboarding should be shown. Returns true if this is the first run.
    /// </summary>
    Task<bool> ShouldShowOnboardingAsync();

    /// <summary>
    /// Begins onboarding and suppresses normal navigation while the wizard is active.
    /// </summary>
    bool BeginOnboarding();

    /// <summary>
    /// Reports a completed shell navigation. Leaving the wizard by any route other than
    /// Finish (a shortcut, the command palette, Jump-To, the tray menu, a status lamp)
    /// ends it with <see cref="SkipOnboardingAsync"/> semantics: the rail navigates again
    /// and the wizard does not come back on the next launch. A no-op when onboarding is
    /// not active or the destination is the wizard itself. Never throws.
    /// </summary>
    Task OnNavigatedAsync(bool destinationIsOnboarding);

    /// <summary>
    /// Restores normal navigation and marks onboarding complete when the wizard cannot be
    /// shown or is left before Finish.
    /// </summary>
    Task SkipOnboardingAsync();

    /// <summary>
    /// Marks onboarding as complete in persistent settings and restores the nav pane.
    /// Called by the OnboardingViewModel when the user finishes the wizard.
    /// </summary>
    Task CompleteOnboardingAsync();
}

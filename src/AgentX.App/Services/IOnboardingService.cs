using System.Threading.Tasks;

namespace AgentX.App.Services;

/// <summary>
/// Manages the first-run onboarding flow. Checks whether onboarding has been
/// completed and coordinates the navigation to/from the onboarding wizard.
/// </summary>
public interface IOnboardingService
{
    /// <summary>
    /// Checks whether onboarding should be shown. Returns true if this is the first run.
    /// </summary>
    Task<bool> ShouldShowOnboardingAsync();

    /// <summary>
    /// Begins onboarding and suppresses normal navigation while the wizard is active.
    /// </summary>
    bool BeginOnboarding();

    /// <summary>
    /// Restores normal navigation and marks onboarding complete when the wizard cannot be shown.
    /// </summary>
    Task SkipOnboardingAsync();

    /// <summary>
    /// Marks onboarding as complete in persistent settings and restores the nav pane.
    /// Called by the OnboardingViewModel when the user finishes the wizard.
    /// </summary>
    Task CompleteOnboardingAsync();
}

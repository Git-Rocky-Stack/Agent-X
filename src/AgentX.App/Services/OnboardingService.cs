using System;
using System.Threading.Tasks;
using Serilog;
using AgentX.Core.Services.Settings;

namespace AgentX.App.Services;

/// <summary>
/// Manages the first-run onboarding flow. Checks whether onboarding has been
/// completed via user settings, and coordinates navigation to the onboarding wizard
/// with nav pane suppression.
/// </summary>
public sealed class OnboardingService : IOnboardingService
{
    private readonly ISettingsService _settingsService;
    private readonly IAppNavigationService _navigationService;

    /// <summary>
    /// Raised when onboarding completes. MainWindow subscribes to navigate to Dashboard.
    /// </summary>
    public event Action? OnboardingCompleted;

    public OnboardingService(ISettingsService settingsService, IAppNavigationService navigationService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    /// <inheritdoc />
    public async Task<bool> ShouldShowOnboardingAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return !settings.OnboardingCompleted;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check onboarding status, skipping onboarding");
            return false;
        }
    }

    /// <summary>
    /// Begins the onboarding flow: suppresses navigation, hides the nav pane.
    /// Returns true if onboarding was successfully started.
    /// The caller (MainWindow) should navigate to the OnboardingPage after this returns true.
    /// </summary>
    public bool BeginOnboarding()
    {
        try
        {
            _navigationService.SuppressNavigation = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to begin onboarding flow");
            _navigationService.SuppressNavigation = false;
            _navigationService.EnsureNavPaneVisible();
            return false;
        }
    }

    /// <summary>
    /// Called when the onboarding navigation attempt fails or is skipped.
    /// Cleans up the suppressed navigation state and marks onboarding as complete.
    /// </summary>
    public async Task SkipOnboardingAsync()
    {
        _navigationService.EnsureNavPaneVisible();
        _navigationService.SuppressNavigation = false;

        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.OnboardingCompleted = true;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to mark onboarding as complete during skip");
        }
    }

    /// <inheritdoc />
    public async Task CompleteOnboardingAsync()
    {
        _navigationService.EnsureNavPaneVisible();
        _navigationService.SuppressNavigation = false;

        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.OnboardingCompleted = true;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist onboarding completion");
        }

        OnboardingCompleted?.Invoke();
        Log.Information("Onboarding completed");
    }
}

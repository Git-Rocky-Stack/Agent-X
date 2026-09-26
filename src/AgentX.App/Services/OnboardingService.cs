using System;
using System.Threading.Tasks;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Manages the first-run onboarding flow. Checks whether onboarding has been
/// completed via user settings, and coordinates navigation to the onboarding wizard
/// with nav pane suppression.
/// </summary>
/// <remarks>
/// This file holds the WinUI-free logic and is linked into AgentX.Tests. The
/// constructor that DI uses (it takes the WinUI-typed IAppNavigationService) lives in
/// OnboardingService.WinUI.cs.
/// </remarks>
public sealed partial class OnboardingService : IOnboardingService
{
    private readonly ISettingsService _settingsService;
    private readonly INavigationGate _navigationGate;
    private bool _isActive;

    /// <summary>
    /// Raised when onboarding completes. MainWindow subscribes to navigate to Dashboard.
    /// </summary>
    public event Action? OnboardingCompleted;

    internal OnboardingService(ISettingsService settingsService, INavigationGate navigationGate)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _navigationGate = navigationGate ?? throw new ArgumentNullException(nameof(navigationGate));
    }

    /// <inheritdoc />
    public bool IsOnboardingActive => _isActive;

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
            _navigationGate.SuppressNavigation = true;
            _isActive = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to begin onboarding flow");
            _isActive = false;
            _navigationGate.SuppressNavigation = false;
            _navigationGate.EnsureNavPaneVisible();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task OnNavigatedAsync(bool destinationIsOnboarding)
    {
        if (!_isActive || destinationIsOnboarding)
        {
            return;
        }

        try
        {
            Log.Information("Onboarding left before Finish; treating it as skipped");
            await SkipOnboardingAsync();
        }
        catch (Exception ex)
        {
            // Raised from a Frame.Navigated handler: never let it escape to the dispatcher.
            Log.Warning(ex, "Failed to end onboarding after navigating away from it");
        }
    }

    /// <summary>
    /// Called when the onboarding navigation attempt fails, or when the wizard is left
    /// before Finish. Cleans up the suppressed navigation state and marks onboarding as
    /// complete so the wizard does not come back on the next launch.
    /// </summary>
    public async Task SkipOnboardingAsync()
    {
        EndSession();
        await PersistCompletedAsync("skip");
    }

    /// <inheritdoc />
    public async Task CompleteOnboardingAsync()
    {
        EndSession();
        await PersistCompletedAsync("finish");

        OnboardingCompleted?.Invoke();
        Log.Information("Onboarding completed");
    }

    /// <summary>
    /// Hands the shell back: the rail navigates again and is visible. Runs before any
    /// await so it takes effect even if persisting the flag later fails.
    /// </summary>
    private void EndSession()
    {
        _isActive = false;
        _navigationGate.EnsureNavPaneVisible();
        _navigationGate.SuppressNavigation = false;
    }

    private async Task PersistCompletedAsync(string outcome)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.OnboardingCompleted = true;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist onboarding completion after {Outcome}", outcome);
        }
    }
}

using AgentX.Core.Services.Settings;

namespace AgentX.App.Services;

public sealed partial class OnboardingService
{
    /// <summary>
    /// The constructor DI resolves. IAppNavigationService carries WinUI types, so it
    /// lives here, outside the file AgentX.Tests links; tests use the internal
    /// constructor that takes the <see cref="INavigationGate"/> slice directly.
    /// </summary>
    public OnboardingService(ISettingsService settingsService, IAppNavigationService navigationService)
        : this(settingsService, (INavigationGate)navigationService)
    {
    }
}

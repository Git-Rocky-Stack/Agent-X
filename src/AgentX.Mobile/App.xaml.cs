namespace AgentX.Mobile;

/// <summary>
/// Root MAUI Application class for Agent-X Mobile.
/// Sets the <see cref="AppShell"/> as the root navigation host. The persisted API pairing token
/// is applied by <see cref="Services.AgentXApiClient"/> itself, which awaits it before its first
/// request instead of racing a fire-and-forget load from here.
/// </summary>
public sealed partial class App : Application
{
    public App(AppShell shell)
    {
        InitializeComponent();
        MainPage = shell;
    }
}

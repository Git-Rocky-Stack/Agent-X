namespace AgentX.Mobile;

/// <summary>
/// Root MAUI Application class for Agent-X Mobile.
/// Sets the <see cref="AppShell"/> as the root navigation host.
/// </summary>
public sealed partial class App : Application
{
    public App(AppShell shell)
    {
        InitializeComponent();
        MainPage = shell;
    }
}

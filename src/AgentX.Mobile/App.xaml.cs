using AgentX.Mobile.Services;

namespace AgentX.Mobile;

/// <summary>
/// Root MAUI Application class for Agent-X Mobile.
/// Sets the <see cref="AppShell"/> as the root navigation host and applies the persisted
/// API pairing token to the shared <see cref="AgentXApiClient"/> on startup.
/// </summary>
public sealed partial class App : Application
{
    private readonly SettingsService _settings;
    private readonly AgentXApiClient _api;

    public App(AppShell shell, SettingsService settings, AgentXApiClient api)
    {
        InitializeComponent();
        _settings = settings;
        _api = api;
        MainPage = shell;
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Apply the persisted bearer token (from secure storage) before the user reaches the data
        // pages, so an already-paired install loads content without a manual trip through Settings.
        _ = ApplyStoredTokenAsync();
    }

    private async Task ApplyStoredTokenAsync()
    {
        var token = await _settings.GetApiTokenAsync().ConfigureAwait(false);
        _api.SetToken(token);
    }
}

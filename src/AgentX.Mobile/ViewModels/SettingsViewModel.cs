using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Mobile.Models;
using AgentX.Mobile.Services;

namespace AgentX.Mobile.ViewModels;

/// <summary>
/// Backing view-model for <c>SettingsPage</c>.
/// Manages the API URL configuration and allows the user to test connectivity.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AgentXApiClient _api;
    private readonly SettingsService _settings;

    public SettingsViewModel(AgentXApiClient api, SettingsService settings)
    {
        _api = api;
        _settings = settings;

        // Load the persisted URL into the editable field
        _apiUrl = _settings.ApiUrl;
    }

    /// <summary>
    /// Loads persisted settings, including the bearer token from secure storage. Called by the
    /// page on appearing because the token read is asynchronous.
    /// </summary>
    public async Task LoadAsync()
    {
        ApiUrl = _settings.ApiUrl;
        ApiToken = await _settings.GetApiTokenAsync().ConfigureAwait(true) ?? string.Empty;
    }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _apiUrl = string.Empty;

    [ObservableProperty]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _testSucceeded;

    [ObservableProperty]
    private bool _testFailed;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private HealthDto? _healthInfo;

    /// <summary>
    /// The mobile app's own version, read from platform package metadata
    /// (ApplicationDisplayVersion → AppInfo) so it tracks the single product version
    /// rather than a hardcoded label (AX-QA-014). Distinct from <see cref="HealthDto.Version"/>,
    /// which is the connected desktop's version.
    /// </summary>
    public string AppVersion => Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;

    // ── Commands ──────────────────────────────────────────────────────────────

    private bool CanSave => !string.IsNullOrWhiteSpace(ApiUrl);

    /// <summary>
    /// Persists the API URL and updates the shared <see cref="AgentXApiClient"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var trimmed = ApiUrl.Trim().TrimEnd('/');
        _settings.ApiUrl = trimmed;
        _api.SetBaseUrl(trimmed);

        // Persist and apply the pairing token (stored in secure storage).
        var token = ApiToken?.Trim();
        await _settings.SetApiTokenAsync(token).ConfigureAwait(true);
        _api.SetToken(token);

        // Reset any previous test result since the connection settings changed
        TestSucceeded = false;
        TestFailed = false;
        ConnectionStatus = "Saved. Tap 'Test Connection' to verify.";
    }

    /// <summary>
    /// Hits GET /api/health on the current URL to verify connectivity.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken ct = default)
    {
        IsTesting = true;
        TestSucceeded = false;
        TestFailed = false;
        ConnectionStatus = "Testing connection…";
        HealthInfo = null;

        try
        {
            // Apply the current field values for the test even if not yet saved
            var testUrl = ApiUrl.Trim().TrimEnd('/');
            _api.SetBaseUrl(testUrl);
            _api.SetToken(ApiToken?.Trim());

            var health = await _api.GetHealthAsync(ct).ConfigureAwait(true);

            if (health is not null)
            {
                HealthInfo = health;
                TestSucceeded = true;
                ConnectionStatus =
                    $"Connected — Agent-X v{health.Version}, " +
                    $"{health.DocumentCount:N0} docs, " +
                    $"{health.ConversationCount:N0} conversations.";
            }
            else
            {
                TestFailed = true;
                ConnectionStatus = "Connection failed. Ensure Agent-X is running, the URL is correct, and the API token matches AgentX → Settings → Connections.";
            }
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = "Connection test cancelled.";
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>Resets the URL field to the factory default.</summary>
    [RelayCommand]
    private void ResetToDefault()
    {
        ApiUrl = "http://localhost:9846";
        TestSucceeded = false;
        TestFailed = false;
        ConnectionStatus = string.Empty;
        HealthInfo = null;
    }
}

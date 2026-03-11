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

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _apiUrl = string.Empty;

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

    // ── Commands ──────────────────────────────────────────────────────────────

    private bool CanSave => !string.IsNullOrWhiteSpace(ApiUrl);

    /// <summary>
    /// Persists the API URL and updates the shared <see cref="AgentXApiClient"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var trimmed = ApiUrl.Trim().TrimEnd('/');
        _settings.ApiUrl = trimmed;
        _api.SetBaseUrl(trimmed);

        // Reset any previous test result since the URL changed
        TestSucceeded = false;
        TestFailed = false;
        ConnectionStatus = "URL saved. Tap 'Test Connection' to verify.";
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
            // Apply the current field value for the test even if not yet saved
            var testUrl = ApiUrl.Trim().TrimEnd('/');
            _api.SetBaseUrl(testUrl);

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
                ConnectionStatus = "Connection failed. Ensure Agent-X is running and the URL is correct.";
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

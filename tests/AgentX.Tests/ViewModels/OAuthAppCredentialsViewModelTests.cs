using System.Collections.Concurrent;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The OAuth App Credentials form on the Calendar and Email connector pages. Before it, the
/// Google and Microsoft connectors could only be set up by editing settings.json by hand and
/// restarting Agent-X.
/// </summary>
public sealed class OAuthAppCredentialsViewModelTests
{
    private const string GoogleId = "123456789012-abc123.apps.googleusercontent.com";
    private const string GoogleSecret = "GOCSPX-google-secret-value";
    private const string MicrosoftId = "11111111-2222-3333-4444-555555555555";

    private readonly AppSettings _settings = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IOAuthService> _oauth = new();
    private readonly CollectingSink _logEvents = new();

    public OAuthAppCredentialsViewModelTests()
    {
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(_settings);
    }

    private OAuthAppCredentialsViewModel CreateViewModel()
    {
        // Resource lookups come back as their keys (and arguments), so the tests read which
        // message the form shows without depending on the English text.
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}: {string.Join(" ", args)}");
        var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_logEvents).CreateLogger();

        return new OAuthAppCredentialsViewModel(_settingsService.Object, _oauth.Object, localization.Object, logger);
    }

    private void Saved(string googleId = "", string googleSecret = "", string microsoftId = "", string microsoftSecret = "")
    {
        _settings.OAuth.Google.ClientId = googleId;
        _settings.OAuth.Google.ClientSecret = googleSecret;
        _settings.OAuth.Microsoft.ClientId = microsoftId;
        _settings.OAuth.Microsoft.ClientSecret = microsoftSecret;
    }

    private void Connected(string provider, string refreshToken = "refresh") =>
        _oauth.Setup(o => o.GetCredentialAsync(provider)).ReturnsAsync(
            new OAuthCredential { ProviderId = provider, AccessToken = "access", RefreshToken = refreshToken });

    private void VerifyNothingSavedOrApplied()
    {
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
        _oauth.Verify(o => o.ApplyProviderSettings(It.IsAny<OAuthSettings>()), Times.Never);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    // -- Loading --------------------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_ShowsTheSavedCredentials_WithTheSecretMasked()
    {
        Saved(GoogleId, GoogleSecret, MicrosoftId);
        var vm = CreateViewModel();
        vm.IsGoogleClientSecretRevealed = true;

        await vm.LoadAsync();

        vm.GoogleClientId.Should().Be(GoogleId);
        vm.GoogleClientSecret.Should().Be(GoogleSecret);
        vm.MicrosoftClientId.Should().Be(MicrosoftId);
        vm.MicrosoftRedirectUri.Should().Be("http://localhost:8401/oauth/callback",
            "the Microsoft app registration has to list it");
        vm.IsGoogleClientSecretRevealed.Should().BeFalse("the secret is masked each time the form is shown");
        vm.HasSaveError.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_WhenTheSettingsCannotBeRead_SaysSo()
    {
        _settingsService.Setup(s => s.GetSettingsAsync()).ThrowsAsync(new IOException("settings locked"));
        var vm = CreateViewModel();

        await vm.LoadAsync();

        vm.HasSaveError.Should().BeTrue();
        vm.SaveError.Should().Be("OAuthApp_Failed: settings locked");
    }

    // -- Saving ---------------------------------------------------------------------------------

    [Fact]
    public async Task SaveCommand_StoresTrimmedCredentials_ThenAppliesThemWithoutARestart()
    {
        var calls = new List<string>();
        _settingsService.Setup(s => s.SaveSettingsAsync(_settings))
            .Callback(() => calls.Add("save")).Returns(Task.CompletedTask);
        _oauth.Setup(o => o.ApplyProviderSettings(_settings.OAuth))
            .Callback(() => calls.Add("apply"));
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.GoogleClientId = $"  {GoogleId}\n";
        vm.GoogleClientSecret = $" {GoogleSecret} ";
        vm.MicrosoftClientId = $"\t{MicrosoftId} ";

        await vm.SaveCommand.ExecuteAsync(null);

        _settings.OAuth.Google.ClientId.Should().Be(GoogleId);
        _settings.OAuth.Google.ClientSecret.Should().Be(GoogleSecret);
        _settings.OAuth.Microsoft.ClientId.Should().Be(MicrosoftId);
        calls.Should().Equal(new[] { "save", "apply" }, "what runs is what was saved");
        vm.GoogleClientId.Should().Be(GoogleId, "the form shows the values as saved");
        vm.SavedMessage.Should().Be("OAuthApp_Saved");
        vm.HasSavedMessage.Should().BeTrue();
        vm.HasSaveError.Should().BeFalse();
    }

    [Theory]
    [InlineData("123 456.apps.googleusercontent.com", GoogleSecret, "", "GoogleClientId", "OAuthApp_ErrNoSpaces")]
    [InlineData("my-cloud-project-123", GoogleSecret, "", "GoogleClientId", "OAuthApp_ErrGoogleClientId")]
    [InlineData(".apps.googleusercontent.com", GoogleSecret, "", "GoogleClientId", "OAuthApp_ErrGoogleClientId")]
    [InlineData(GoogleId, "", "", "GoogleClientSecret", "OAuthApp_ErrGoogleSecretRequired")]
    [InlineData(GoogleId, "GOCSPX abc", "", "GoogleClientSecret", "OAuthApp_ErrNoSpaces")]
    [InlineData("", "", "not-a-guid", "MicrosoftClientId", "OAuthApp_ErrMicrosoftClientId")]
    [InlineData("", "", "{11111111-2222-3333-4444-555555555555}", "MicrosoftClientId", "OAuthApp_ErrMicrosoftClientId")]
    [InlineData("", "", "11111111-2222-3333-4444-555555555555 x", "MicrosoftClientId", "OAuthApp_ErrNoSpaces")]
    public async Task SaveCommand_RejectsAnImplausibleValue_InlineAndWithoutSaving(
        string googleId, string googleSecret, string microsoftId, string field, string expectedError)
    {
        var vm = CreateViewModel();
        vm.GoogleClientId = googleId;
        vm.GoogleClientSecret = googleSecret;
        vm.MicrosoftClientId = microsoftId;

        await vm.SaveCommand.ExecuteAsync(null);

        var errors = new Dictionary<string, (string Message, bool Shown)>
        {
            ["GoogleClientId"] = (vm.GoogleClientIdError, vm.HasGoogleClientIdError),
            ["GoogleClientSecret"] = (vm.GoogleClientSecretError, vm.HasGoogleClientSecretError),
            ["MicrosoftClientId"] = (vm.MicrosoftClientIdError, vm.HasMicrosoftClientIdError),
        };
        errors[field].Should().Be((expectedError, true));
        foreach (var (otherField, (message, shown)) in errors.Where(e => e.Key != field))
        {
            message.Should().BeEmpty(otherField);
            shown.Should().BeFalse(otherField);
        }
        vm.HasSavedMessage.Should().BeFalse();
        VerifyNothingSavedOrApplied();
    }

    [Fact]
    public async Task SaveCommand_WithTheClientIdsCleared_RemovesBothProviders()
    {
        Saved(GoogleId, GoogleSecret, MicrosoftId, "hand-entered-microsoft-secret");
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.GoogleClientId = string.Empty;
        vm.GoogleClientSecret = "left over with spaces";
        vm.MicrosoftClientId = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasGoogleClientSecretError.Should().BeFalse("without a client ID the secret is not checked");
        _settings.OAuth.Google.ClientId.Should().BeEmpty();
        _settings.OAuth.Google.ClientSecret.Should().BeEmpty("a secret is of no use without its client ID");
        _settings.OAuth.Microsoft.ClientId.Should().BeEmpty();
        _settings.OAuth.Microsoft.ClientSecret.Should().BeEmpty();
        _settingsService.Verify(s => s.SaveSettingsAsync(_settings), Times.Once);
        _oauth.Verify(o => o.ApplyProviderSettings(It.Is<OAuthSettings>(oauth =>
            oauth.Google.ClientId.Length == 0 && oauth.Microsoft.ClientId.Length == 0)), Times.Once);
        vm.GoogleClientSecret.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCommand_WithUnchangedClientIds_KeepsConnectedAccountsWorking()
    {
        // A new Google secret (for example a rotated one) works with the same client, and the
        // Microsoft secret entered by hand for a confidential registration stays.
        Saved(GoogleId, "old-secret", MicrosoftId, "hand-entered-microsoft-secret");
        Connected(OAuthProviderRegistry.ProviderIdGoogle);
        Connected(OAuthProviderRegistry.ProviderIdMicrosoft);
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.GoogleClientSecret = GoogleSecret;
        vm.MicrosoftClientId = MicrosoftId.ToUpperInvariant();

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasGoogleClientIdError.Should().BeFalse();
        vm.HasMicrosoftClientIdError.Should().BeFalse();
        _settings.OAuth.Google.ClientSecret.Should().Be(GoogleSecret);
        _settings.OAuth.Microsoft.ClientSecret.Should().Be("hand-entered-microsoft-secret");
        _oauth.Verify(o => o.ApplyProviderSettings(_settings.OAuth), Times.Once);
        _oauth.Verify(o => o.RevokeAsync(It.IsAny<string>()), Times.Never);
        vm.SavedMessage.Should().Be("OAuthApp_Saved");
    }

    [Theory]
    [InlineData("google", "987654321098-xyz.apps.googleusercontent.com")]
    [InlineData("google", "")]
    [InlineData("microsoft", "66666666-7777-8888-9999-000000000000")]
    [InlineData("microsoft", "")]
    public async Task SaveCommand_WhileAnAccountIsConnected_AsksToDisconnectItBeforeTheClientIdChanges(
        string provider, string newClientId)
    {
        // The account's refresh token belongs to the current client: after a change it would
        // look connected until its sign-in expired, and then fail.
        Saved(GoogleId, GoogleSecret, MicrosoftId);
        Connected(provider);
        var vm = CreateViewModel();
        await vm.LoadAsync();
        if (provider == "google")
            vm.GoogleClientId = newClientId;
        else
            vm.MicrosoftClientId = newClientId;

        await vm.SaveCommand.ExecuteAsync(null);

        if (provider == "google")
            vm.GoogleClientIdError.Should().Be("OAuthApp_DisconnectGoogleFirst");
        else
            vm.MicrosoftClientIdError.Should().Be("OAuthApp_DisconnectMicrosoftFirst");
        VerifyNothingSavedOrApplied();
        _settings.OAuth.Google.ClientId.Should().Be(GoogleId);
        _settings.OAuth.Microsoft.ClientId.Should().Be(MicrosoftId);
    }

    [Fact]
    public async Task SaveCommand_ChangesTheClientId_WhenTheStoredSignInCannotBeRenewedAnyway()
    {
        // A credential without a refresh token shows as "Reconnect required" and the page offers
        // no Disconnect for it; one that cannot be read is no better. Neither locks the client.
        Saved(GoogleId, GoogleSecret, MicrosoftId);
        Connected(OAuthProviderRegistry.ProviderIdGoogle, refreshToken: string.Empty);
        _oauth.Setup(o => o.GetCredentialAsync(OAuthProviderRegistry.ProviderIdMicrosoft))
            .ThrowsAsync(new InvalidOperationException("Failed to decrypt the access token"));
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.GoogleClientId = "987654321098-xyz.apps.googleusercontent.com";
        vm.MicrosoftClientId = "66666666-7777-8888-9999-000000000000";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasGoogleClientIdError.Should().BeFalse();
        vm.HasMicrosoftClientIdError.Should().BeFalse();
        _settings.OAuth.Google.ClientId.Should().Be("987654321098-xyz.apps.googleusercontent.com");
        _settings.OAuth.Microsoft.ClientId.Should().Be("66666666-7777-8888-9999-000000000000");
        _oauth.Verify(o => o.ApplyProviderSettings(_settings.OAuth), Times.Once);
    }

    [Fact]
    public async Task SaveCommand_ChangingTheMicrosoftClientId_DropsTheSecretOfThePreviousRegistration()
    {
        // The form sets up a public client; Microsoft would reject the old registration's secret.
        Saved(microsoftId: MicrosoftId, microsoftSecret: "hand-entered-microsoft-secret");
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.MicrosoftClientId = "66666666-7777-8888-9999-000000000000";

        await vm.SaveCommand.ExecuteAsync(null);

        _settings.OAuth.Microsoft.ClientId.Should().Be("66666666-7777-8888-9999-000000000000");
        _settings.OAuth.Microsoft.ClientSecret.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCommand_WhenTheSettingsCannotBeSaved_SaysSoAndLeavesTheProvidersAlone()
    {
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()))
            .ThrowsAsync(new IOException("disk full"));
        var vm = CreateViewModel();
        vm.GoogleClientId = GoogleId;
        vm.GoogleClientSecret = GoogleSecret;

        await vm.SaveCommand.ExecuteAsync(null);

        vm.SaveError.Should().Be("OAuthApp_Failed: disk full");
        vm.HasSaveError.Should().BeTrue();
        vm.HasSavedMessage.Should().BeFalse();
        _oauth.Verify(o => o.ApplyProviderSettings(It.IsAny<OAuthSettings>()), Times.Never);
    }

    [Fact]
    public async Task EditingAField_ClearsItsErrorAndTheLastSaveResult()
    {
        var vm = CreateViewModel();
        vm.GoogleClientId = "not-a-client-id";
        vm.MicrosoftClientId = "not-a-guid";
        await vm.SaveCommand.ExecuteAsync(null);
        vm.HasGoogleClientIdError.Should().BeTrue();
        vm.HasMicrosoftClientIdError.Should().BeTrue();

        vm.GoogleClientId = GoogleId;

        vm.HasGoogleClientIdError.Should().BeFalse();
        vm.HasMicrosoftClientIdError.Should().BeTrue("only the edited field's error goes");

        vm.GoogleClientSecret = GoogleSecret;
        vm.MicrosoftClientId = MicrosoftId;
        await vm.SaveCommand.ExecuteAsync(null);
        vm.HasSavedMessage.Should().BeTrue();

        vm.GoogleClientSecret = "GOCSPX-another-secret";

        vm.HasSavedMessage.Should().BeFalse("the form no longer shows what was saved");
    }

    [Fact]
    public async Task TheClientSecret_NeverReachesTheLog()
    {
        Saved(GoogleId, GoogleSecret, MicrosoftId);
        var vm = CreateViewModel();
        await vm.LoadAsync();
        await vm.SaveCommand.ExecuteAsync(null);
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()))
            .ThrowsAsync(new IOException("disk full"));
        vm.GoogleClientSecret = GoogleSecret + "-rotated";
        await vm.SaveCommand.ExecuteAsync(null);

        _logEvents.Events.Should().NotBeEmpty();
        foreach (var logEvent in _logEvents.Events)
        {
            var text = logEvent.RenderMessage() + " " +
                       string.Join(" ", logEvent.Properties.Values.Select(v => v.ToString())) + " " +
                       logEvent.Exception;
            text.Should().NotContain(GoogleSecret);
        }
    }

    [Fact]
    public async Task Commands_WriteBoundPropertiesOnlyOnTheUiThread()
    {
        // Every service call completes on the thread pool. A command body that awaited with
        // ConfigureAwait(false) would go on writing bound properties from there, which WinUI's
        // x:Bind rejects.
        static Task<T> Later<T>(T value) => Task.Delay(5).ContinueWith(_ => value, TaskScheduler.Default);
        Saved(GoogleId, GoogleSecret, MicrosoftId);
        _settingsService.Setup(s => s.GetSettingsAsync()).Returns(() => Later(_settings));
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<AppSettings>())).Returns(() => Later(true));
        _oauth.Setup(o => o.GetCredentialAsync(It.IsAny<string>())).Returns(() => Later<OAuthCredential?>(null));
        var vm = CreateViewModel();

        using var ui = new SingleThreadSynchronizationContext();
        var offThreadWrites = new ConcurrentQueue<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (Environment.CurrentManagedThreadId != ui.ThreadId)
                offThreadWrites.Enqueue(e.PropertyName ?? "?");
        };

        await ui.RunAsync(() => vm.LoadAsync());
        await ui.RunAsync(() =>
        {
            vm.GoogleClientId = "987654321098-xyz.apps.googleusercontent.com";
            return vm.SaveCommand.ExecuteAsync(null);
        });

        offThreadWrites.Should().BeEmpty();
        vm.SavedMessage.Should().Be("OAuthApp_Saved");
        _settings.OAuth.Google.ClientId.Should().Be("987654321098-xyz.apps.googleusercontent.com");
    }
}

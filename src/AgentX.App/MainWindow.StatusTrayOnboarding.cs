using AgentX.App.Controls;
using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.UI;

namespace AgentX.App;

public sealed partial class MainWindow
{
    // LCD phosphor tones for the instrument strip (night values; the
    // strip stays dark in both shifts per the displays-stay-dark rule).
    private static readonly Color LcdGreen = Color.FromArgb(0xFF, 0x41, 0xE2, 0x5E);
    private static readonly Color LcdAmber = Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00);

    private void ConfigureStatusBar()
    {
        // Phosphor glow behind the LCD values (no-op under High Contrast).
        CompositionGlow.Attach(StatusText, MdlGlowHost, LcdGreen, blurRadius: 8f, opacity: 0.7f);
        CompositionGlow.Attach(IndexingText, IdxGlowHost, LcdGreen, blurRadius: 8f, opacity: 0.7f);
        CompositionGlow.Attach(DocCountText, VaultGlowHost, LcdGreen, blurRadius: 8f, opacity: 0.7f);

        // Lit lamps teleport to their source view (DESIGN.md annunciator rule).
        WireTeleport(MdlLamp, "ModelManager");
        WireTeleport(LocalLamp, "Settings");
        WireTeleport(InboxLamp, "Inbox");
        WireTeleport(SyncLamp, "SyncSettings");
        WireTeleport(JobsLamp, "Operations");
        WireTeleport(BakLamp, "BackupRestore");

        _statusBarService.StateChanged += OnStatusBarStateChanged;
        _statusBarService.StartPolling();

        _annunciatorService.StateChanged += OnAnnunciatorStateChanged;
        _annunciatorService.StartPolling();
    }

    /// <summary>
    /// Only a lit lamp is a teleport. LampTile raises Invoked in every state, and an
    /// unlit lamp has nothing to report, so a click on one stays put instead of
    /// navigating (and, during first run, instead of silently ending onboarding).
    /// </summary>
    private void WireTeleport(LampTile lamp, string pageTag)
    {
        lamp.Invoked += (_, _) =>
        {
            if (lamp.State != LampState.Off)
            {
                _navigationService.NavigateToPage(pageTag);
            }
        };
    }

    private void OnAnnunciatorStateChanged(object? sender, AnnunciatorState state)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnAnnunciatorStateChanged(sender, state));
            return;
        }

        // INBOX: queued triage work holds amber; an empty queue stays unlit.
        InboxLamp.State = state.InboxPendingCount > 0 ? LampState.Hold : LampState.Off;

        // SYNC: unconfigured stays unlit; error is a steady terminal NO-GO;
        // an active pass reads scope; configured-and-idle is GO.
        SyncLamp.State = !state.SyncConfigured
            ? LampState.Off
            : state.SyncState switch
            {
                AgentX.Core.Services.Sync.Models.SyncState.Error => LampState.NoGo,
                AgentX.Core.Services.Sync.Models.SyncState.Syncing => LampState.Scope,
                _ => LampState.Go,
            };

        // JOBS: a running workflow reads scope; a failed latest run holds
        // steady NO-GO until a newer run succeeds; otherwise unlit.
        JobsLamp.State = state.JobsRunning
            ? LampState.Scope
            : state.JobsLastRunFailed ? LampState.NoGo : LampState.Off;

        // BAK: fresh backup (7 days) is GO; a stale one holds amber;
        // no backup history stays unlit.
        BakLamp.State = state.LastBackupUtc is null
            ? LampState.Off
            : DateTime.UtcNow - state.LastBackupUtc.Value <= TimeSpan.FromDays(7)
                ? LampState.Go
                : LampState.Hold;
    }

    internal void ConfigureWindowLifecycleServices()
    {
        ConfigureSystemTray();
    }

    private void OnStatusBarStateChanged(object? sender, StatusBarState state)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnStatusBarStateChanged(sender, state));
            return;
        }

        if (!NavView.IsPaneVisible && ContentFrame.Content is not OnboardingPage)
        {
            _navigationService.EnsureNavPaneVisible();
            Log.Warning("Nav pane was hidden outside of onboarding - restored");
        }

        // MDL well: model name on phosphor when linked; amber caution when not.
        // "Ollama not detected" preserves the pre-strip disconnected wording.
        StatusText.Text = state.IsConnected
            ? (string.IsNullOrWhiteSpace(state.ActiveModelName) ? state.ConnectionStatus : state.ActiveModelName)
            : "Ollama not detected";
        StatusText.Foreground = (Brush)Application.Current.Resources[
            state.IsConnected ? "LcdPhosphorBrush" : "LcdAmberBrush"];
        CompositionGlow.SetColor(StatusText, state.IsConnected ? LcdGreen : LcdAmber);
        MdlLamp.State = state.IsConnected ? LampState.Go : LampState.Hold;

        // IDX well: queue depth burns amber while embedding, rests dim at zero.
        // Instruments always read a value - they never go blank.
        IndexingText.Text = state.IsIndexing ? state.IndexingQueueLength.ToString() : "0";
        IndexingText.Foreground = (Brush)Application.Current.Resources[
            state.IsIndexing ? "LcdAmberBrush" : "LcdPhosphorBrush"];
        IndexingText.Opacity = state.IsIndexing ? 1.0 : 0.35;
        CompositionGlow.SetColor(IndexingText, state.IsIndexing ? LcdAmber : LcdGreen);

        // VAULT well: document count, zero included.
        DocCountText.Text = state.DocumentCount.ToString("N0");

        // LOCAL/NET privacy lamp: re-evaluated on every poll so the posture
        // is always one glance away (AX-QA-008 state-aware disclosure).
        _ = UpdatePrivacyLampAsync();

        _systemTrayService.UpdateTooltip(
            state.IsConnected ? "Connected" : "Disconnected",
            state.ActiveModelName,
            state.DocumentCount);
    }

    private async Task UpdatePrivacyLampAsync()
    {
        try
        {
            var status = await _privacyStatusService.GetCurrentAsync();
            if (!DispatcherQueue.HasThreadAccess)
            {
                _ = DispatcherQueue.TryEnqueue(() => ApplyPrivacyLamp(status.IsFullyLocal));
            }
            else
            {
                ApplyPrivacyLamp(status.IsFullyLocal);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Privacy lamp refresh failed; keeping previous state");
        }
    }

    private void ApplyPrivacyLamp(bool isFullyLocal)
    {
        LocalLamp.Code = isFullyLocal ? "LOCAL" : "NET";
        LocalLamp.State = isFullyLocal ? LampState.Go : LampState.Hold;
    }

    private void ConfigureSystemTray()
    {
        _systemTrayService.ConfigureTray(this, TrayIcon, new DelegateCommand(_systemTrayService.RestoreFromTray));

        _systemTrayService.RestoreRequested += _systemTrayService.RestoreFromTray;
        _systemTrayService.QuitRequested += _systemTrayService.CloseAppForReal;
        _systemTrayService.QuickChatRequested += OpenQuickChat;
        _systemTrayService.SettingsRequested += () =>
        {
            _systemTrayService.RestoreFromTray();
            _navigationService.NavigateToPage("Settings");
        };
    }

    private void OpenQuickChat()
    {
        // Screen awareness reads the window the operator was looking at, so it is recorded
        // before Quick Chat is activated and becomes the foreground window itself.
        if (_quickChatWindow != null)
        {
            try
            {
                _quickChatWindow.RememberTargetWindow();
                _quickChatWindow.Activate();
                return;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Existing Quick Chat window could not be activated");
                _quickChatWindow = null;
            }
        }

        try
        {
            var vm = App.GetService<QuickChatViewModel>();
            _quickChatWindow = new QuickChatWindow(vm);
            _quickChatWindow.Closed += (_, _) => _quickChatWindow = null;
            _quickChatWindow.RememberTargetWindow();
            _quickChatWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create Quick Chat overlay window");
        }
    }

    private void TrayMenu_OpenAgentX(object sender, RoutedEventArgs e) => _systemTrayService.RestoreFromTray();
    private void TrayMenu_QuickChat(object sender, RoutedEventArgs e) => OpenQuickChat();
    private void TrayMenu_Settings(object sender, RoutedEventArgs e) => _systemTrayService.InvokeSettingsRequested();
    private void TrayMenu_Exit(object sender, RoutedEventArgs e) => _systemTrayService.CloseAppForReal();

    private async Task CheckOnboardingAsync()
    {
        try
        {
            if (!await _onboardingService.ShouldShowOnboardingAsync()) return;

            if (_onboardingService.BeginOnboarding())
            {
                var navigated = ContentFrame.Navigate(typeof(OnboardingPage));
                if (navigated)
                {
                    NavView.SelectedItem = null;
                    NavView.IsPaneVisible = false;
                    Log.Information("First run detected - navigating to Onboarding wizard");
                }
                else
                {
                    Log.Error("Frame.Navigate returned false for OnboardingPage, skipping onboarding");
                    await _onboardingService.SkipOnboardingAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check onboarding status, proceeding to Dashboard");
            if (_onboardingService.IsOnboardingActive)
            {
                // The wizard began but never took over the frame: release the rail it
                // suppressed, exactly as the Navigate-returned-false branch above does.
                await _onboardingService.SkipOnboardingAsync();
            }
            else
            {
                _navigationService.EnsureNavPaneVisible();
            }
        }
    }

    /// <summary>
    /// Finish button entry point, called by OnboardingViewModel. It is async void, so
    /// nothing may escape it: an exception here would reach the dispatcher unobserved.
    /// </summary>
    public async void CompleteOnboarding()
    {
        try
        {
            await _onboardingService.CompleteOnboardingAsync();

            // One navigation that also moves the rail highlight. Setting SelectedItem and
            // then calling Frame.Navigate as well navigated to the Dashboard twice.
            _navigationService.NavigateToPage("Dashboard");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to finish onboarding and return to the Dashboard");
            _navigationService.EnsureNavPaneVisible();
        }
    }

    private sealed class DelegateCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;

        public DelegateCommand(Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}

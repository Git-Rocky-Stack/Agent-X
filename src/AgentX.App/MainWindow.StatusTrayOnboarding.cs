using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.Views;

namespace AgentX.App;

public sealed partial class MainWindow
{
    private void ConfigureStatusBar()
    {
        _statusBarService.StateChanged += OnStatusBarStateChanged;
        _statusBarService.StartPolling();
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

        StatusIndicator.Fill = state.IsConnected
            ? (SolidColorBrush)Application.Current.Resources["OnlineBrush"]
            : (SolidColorBrush)Application.Current.Resources["OfflineBrush"];

        StatusText.Text = state.IsConnected ? state.ConnectionStatus : "Ollama not detected";
        IndexingRing.IsActive = state.IsIndexing;
        IndexingText.Text = state.IsIndexing ? $"Indexing ({state.IndexingQueueLength} remaining)" : "";
        DocCountText.Text = state.DocumentCount > 0 ? $"{state.DocumentCount} docs" : "";

        _systemTrayService.UpdateTooltip(
            state.IsConnected ? "Connected" : "Disconnected",
            state.ActiveModelName,
            state.DocumentCount);
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
        if (_quickChatWindow != null)
        {
            try
            {
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
            _navigationService.EnsureNavPaneVisible();
        }
    }

    public async void CompleteOnboarding()
    {
        await _onboardingService.CompleteOnboardingAsync();
        NavView.SelectedItem = NavDashboard;
        ContentFrame.Navigate(typeof(DashboardPage));
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

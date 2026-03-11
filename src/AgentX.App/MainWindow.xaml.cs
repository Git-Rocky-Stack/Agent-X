using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.Documents;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Constants;
using AgentX.Core.Services.Settings;

namespace AgentX.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pageMap;
    private readonly KeyboardShortcutService _keyboardShortcutService;
    private DispatcherTimer? _statusTimer;
    private bool _lastConnectionState;
    private bool _suppressNavigation;

    /// <summary>
    /// Map from page tags to their corresponding NavigationViewItem controls.
    /// Used to programmatically select nav items when navigating via command palette or shortcuts.
    /// </summary>
    private readonly Dictionary<string, NavigationViewItem> _navItemMap;

    public MainWindow()
    {
        InitializeComponent();

        _pageMap = new Dictionary<string, Type>
        {
            ["Dashboard"] = typeof(Views.DashboardPage),
            ["Digest"] = typeof(Views.DigestPage),
            ["Settings"] = typeof(Views.SettingsPage),
            ["Chat"] = typeof(Views.ChatPage),
            ["AskFiles"] = typeof(Views.AskFilesPage),
            ["QuickActions"] = typeof(Views.QuickActionsPage),
            ["Workflows"] = typeof(Views.WorkflowBuilderPage),
            ["KnowledgeVault"] = typeof(Views.KnowledgeVaultPage),
            ["WebImport"] = typeof(Views.WebImportPage),
            ["Collections"] = typeof(Views.CollectionManagerPage),
            ["Search"] = typeof(Views.SearchPage),
            ["KnowledgeGraph"] = typeof(Views.KnowledgeGraphPage),
            ["ModelManager"] = typeof(Views.ModelManagerPage),
            ["HardwareAdvisor"] = typeof(Views.HardwareAdvisorPage),
            ["BackupRestore"] = typeof(Views.BackupRestorePage),
            ["Annotations"] = typeof(Views.AnnotationsPage),
            ["Inbox"] = typeof(Views.InboxPage),
            ["Comparison"] = typeof(Views.ComparisonPage),
            ["WorkspaceProfiles"] = typeof(Views.WorkspaceProfilePage),
            ["PluginManager"] = typeof(Views.PluginManagerPage),
            ["SyncSettings"] = typeof(Views.SyncSettingsPage),
            ["Analytics"] = typeof(Views.AnalyticsPage),
            ["Onboarding"] = typeof(Views.OnboardingPage),
            ["UserGuide"] = typeof(Views.UserGuidePage),
            ["PrivacyPolicy"] = typeof(Views.PrivacyPolicyPage),
            ["TermsOfService"] = typeof(Views.TermsOfServicePage),
        };

        _navItemMap = new Dictionary<string, NavigationViewItem>
        {
            ["Dashboard"] = NavDashboard,
            ["Digest"] = NavDigest,
            ["Chat"] = NavChat,
            ["AskFiles"] = NavAskFiles,
            ["QuickActions"] = NavQuickActions,
            ["Workflows"] = NavWorkflows,
            ["KnowledgeVault"] = NavVault,
            ["WebImport"] = NavWebImport,
            ["Collections"] = NavCollections,
            ["Search"] = NavSearch,
            ["KnowledgeGraph"] = NavKnowledgeGraph,
            ["ModelManager"] = NavModels,
            ["HardwareAdvisor"] = NavHardware,
            ["BackupRestore"] = NavBackupRestore,
            ["Annotations"] = NavAnnotations,
            ["Inbox"] = NavInbox,
            ["Comparison"] = NavComparison,
            ["WorkspaceProfiles"] = NavWorkspaceProfiles,
            ["PluginManager"] = NavPluginManager,
            ["SyncSettings"] = NavSyncSettings,
            ["Settings"] = NavSettings,
            ["UserGuide"] = NavUserGuide,
            ["PrivacyPolicy"] = NavPrivacyPolicy,
            ["TermsOfService"] = NavTermsOfService,
        };

        // Initialize keyboard shortcut service and register default shortcuts
        _keyboardShortcutService = App.GetService<KeyboardShortcutService>();
        RegisterDefaultShortcuts();

        // Wire up command palette callbacks
        ConfigureCommandPalette();

        // Attach keyboard handler to the root content element
        RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;

        ConfigureWindow();
        ConfigureTitleBar();
        ConfigureBackdrop();

        // Navigate to Dashboard on launch (onboarding check may override this)
        ContentFrame.Navigate(typeof(Views.DashboardPage));

        // Check if onboarding is needed (first run)
        CheckOnboardingAsync();

        // Wire up live status bar polling
        InitializeStatusBar();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  KEYBOARD SHORTCUTS
    // ═══════════════════════════════════════════════════════════════════

    private void RegisterDefaultShortcuts()
    {
        // ── Navigation Shortcuts ──────────────────────────────────
        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.K, ctrl: true, shift: false, alt: false,
            () => CommandPalette.Toggle(),
            "cmd.palette", "Command Palette", "Navigation");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.N, ctrl: true, shift: false, alt: false,
            () => NavigateToPage("Chat"),
            "nav.chat", "New Conversation", "Navigation");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.I, ctrl: true, shift: false, alt: false,
            () => NavigateToPage("KnowledgeVault"),
            "nav.vault", "Knowledge Vault", "Navigation");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.F, ctrl: true, shift: false, alt: false,
            () => NavigateToPage("Search"),
            "nav.search", "Semantic Search", "Navigation");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.F, ctrl: true, shift: true, alt: false,
            () => NavigateToPage("Search"),
            "nav.search.alt", "Semantic Search", "Navigation");

        _keyboardShortcutService.RegisterShortcut(
            (VirtualKey)188, ctrl: true, shift: false, alt: false,
            () => NavigateToPage("Settings"),
            "nav.settings", "Settings", "Navigation");

        // ── Page Quick-Access (Ctrl+1 through Ctrl+9) ─────────────
        var pageOrder = new[] { "Dashboard", "Chat", "AskFiles", "Search", "KnowledgeVault", "Collections", "Workflows", "ModelManager", "Settings" };
        for (int i = 0; i < pageOrder.Length; i++)
        {
            var pageTag = pageOrder[i];
            var num = i + 1;
            _keyboardShortcutService.RegisterShortcut(
                (VirtualKey)(num + 48), ctrl: true, shift: false, alt: false,
                () => NavigateToPage(pageTag),
                $"nav.page{num}", $"{pageTag} (Ctrl+{num})", "Quick Access");
        }

        // ── App Actions ───────────────────────────────────────────
        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.W, ctrl: true, shift: true, alt: false,
            () => NavigateToPage("Workflows"),
            "nav.workflows", "Workflows", "Actions");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.E, ctrl: true, shift: true, alt: false,
            () => NavigateToPage("WebImport"),
            "nav.webimport", "Web Import", "Actions");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.D, ctrl: true, shift: false, alt: false,
            () => NavigateToPage("Dashboard"),
            "nav.dashboard", "Dashboard", "Actions");

        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.G, ctrl: true, shift: false, alt: false,
            () => NavigateToPage("KnowledgeGraph"),
            "nav.graph", "Knowledge Graph", "Actions");

        // ── Keyboard Shortcuts Help (Ctrl+?) ──────────────────────
        _keyboardShortcutService.RegisterShortcut(
            (VirtualKey)191, ctrl: true, shift: true, alt: false,
            () => ShowShortcutsOverlay(),
            "help.shortcuts", "Show Keyboard Shortcuts", "Help");

        // ── Escape — Close Command Palette ────────────────────────
        _keyboardShortcutService.RegisterShortcut(
            VirtualKey.Escape, ctrl: false, shift: false, alt: false,
            () =>
            {
                if (CommandPalette.IsOpen)
                {
                    CommandPalette.Hide();
                }
            });

        Log.Information("Registered {Count} default keyboard shortcuts", _keyboardShortcutService.RegisteredCount);
    }

    private async void ShowShortcutsOverlay()
    {
        var shortcuts = _keyboardShortcutService.GetAllShortcuts();
        var categories = _keyboardShortcutService.GetCategories();

        var content = new StackPanel { Spacing = 16 };
        foreach (var category in categories)
        {
            var catShortcuts = shortcuts.Where(s => s.Category == category).ToList();
            if (catShortcuts.Count == 0) continue;

            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.5,
                Margin = new Thickness(0, 4, 0, 0)
            });

            foreach (var shortcut in catShortcuts)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = shortcut.DisplayName,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameBlock, 0);
                row.Children.Add(nameBlock);

                var keyBorder = new Microsoft.UI.Xaml.Controls.Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InputBackgroundBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderSubtleBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 8, 2)
                };
                var keyBlock = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = shortcut.KeyCombo,
                    FontSize = 12,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    Opacity = 0.7
                };
                keyBorder.Child = keyBlock;
                Grid.SetColumn(keyBorder, 1);
                row.Children.Add(keyBorder);

                content.Children.Add(row);
            }
        }

        var scrollViewer = new Microsoft.UI.Xaml.Controls.ScrollViewer
        {
            Content = content,
            MaxHeight = 500,
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled
        };

        // Use the current theme for the dialog instead of hardcoding Dark
        var currentTheme = Microsoft.UI.Xaml.ElementTheme.Dark;
        try
        {
            var themeService = App.GetService<IThemeService>();
            currentTheme = themeService.CurrentTheme;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not resolve theme service for shortcuts dialog, defaulting to Dark");
        }

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Keyboard Shortcuts",
            Content = scrollViewer,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = currentTheme
        };

        await dialog.ShowAsync();
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Determine modifier key states
        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        var altState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);

        bool ctrl = (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        bool shift = (shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        bool alt = (altState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

        // If the command palette is open and the user presses Escape without modifiers,
        // let the command palette handle it through its own KeyDown handler to avoid
        // double-processing. Only handle Escape at the root level when the palette
        // search box does not have focus.
        if (e.Key == VirtualKey.Escape && !ctrl && !shift && !alt && CommandPalette.IsOpen)
        {
            // The command palette's SearchInput_KeyDown handler will process this.
            // We only handle Escape here if it somehow reaches the root without
            // being caught by the palette (e.g., focus is elsewhere).
            // Check if the command palette search box has focus:
            if (FocusManager.GetFocusedElement(Content.XamlRoot) is not Microsoft.UI.Xaml.Controls.TextBox)
            {
                CommandPalette.Hide();
                e.Handled = true;
                return;
            }
            return;
        }

        if (_keyboardShortcutService.HandleKeyDown(e.Key, ctrl, shift, alt))
        {
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMMAND PALETTE INTEGRATION
    // ═══════════════════════════════════════════════════════════════════

    private void ConfigureCommandPalette()
    {
        // Wire page navigation: when the command palette selects a page, navigate to it
        CommandPalette.NavigateToPageRequested = NavigateToPage;

        // Wire action execution: when the command palette selects an action, execute it
        CommandPalette.ExecuteActionRequested = ExecuteAction;
    }

    /// <summary>
    /// Navigates to a page by its tag name, updating both the content frame and
    /// the NavigationView selection indicator to keep them in sync.
    /// </summary>
    internal void NavigateToPage(string pageTag)
    {
        if (_pageMap.TryGetValue(pageTag, out var pageType))
        {
            ContentFrame.Navigate(pageType);

            // Sync the NavigationView selection to reflect the new page
            if (_navItemMap.TryGetValue(pageTag, out var navItem))
            {
                NavView.SelectedItem = navItem;
            }

            Log.Debug("Navigated to {Page} via shortcut/command palette", pageTag);
        }
        else
        {
            Log.Debug("Attempted to navigate to unknown page: {Page}", pageTag);
        }
    }

    /// <summary>
    /// Executes a non-navigation action from the command palette.
    /// </summary>
    private void ExecuteAction(string actionId)
    {
        switch (actionId)
        {
            case "NewConversation":
                NavigateToPage("Chat");
                break;

            case "ImportFiles":
                NavigateToPage("KnowledgeVault");
                break;

            case "RefreshDashboard":
                // Navigate to dashboard, which triggers a fresh data load
                NavigateToPage("Dashboard");
                break;

            case "ToggleTheme":
                try
                {
                    var themeService = App.GetService<IThemeService>();
                    var newTheme = themeService.CurrentTheme == Microsoft.UI.Xaml.ElementTheme.Dark
                        ? Microsoft.UI.Xaml.ElementTheme.Light
                        : Microsoft.UI.Xaml.ElementTheme.Dark;
                    _ = themeService.SetThemeAsync(newTheme);
                    Log.Information("Theme toggled to {Theme} via command palette", newTheme);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to toggle theme");
                }
                break;

            default:
                Log.Warning("Unknown command palette action: {Action}", actionId);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ONBOARDING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if onboarding has been completed. If not, navigates to the
    /// onboarding wizard and hides the navigation pane for a focused experience.
    /// Includes robust error recovery — if anything fails, the nav pane stays visible
    /// and the user lands on the Dashboard.
    /// </summary>
    private async void CheckOnboardingAsync()
    {
        try
        {
            var settingsService = App.GetService<ISettingsService>();
            var settings = await settingsService.GetSettingsAsync();
            if (!settings.OnboardingCompleted)
            {
                try
                {
                    // Suppress NavView_SelectionChanged from re-navigating to Dashboard
                    // when we clear the selected item and hide the pane.
                    _suppressNavigation = true;

                    var navigated = ContentFrame.Navigate(typeof(Views.OnboardingPage));
                    if (navigated)
                    {
                        NavView.SelectedItem = null;
                        NavView.IsPaneVisible = false;
                        Log.Information("First run detected — navigating to Onboarding wizard");
                    }
                    else
                    {
                        Log.Error("Frame.Navigate returned false for OnboardingPage, skipping onboarding");
                        EnsureNavPaneVisible();
                        settings.OnboardingCompleted = true;
                        await settingsService.SaveSettingsAsync(settings);
                    }
                }
                catch (Exception navEx)
                {
                    Log.Error(navEx, "OnboardingPage failed to load, skipping onboarding");
                    EnsureNavPaneVisible();
                    ContentFrame.Navigate(typeof(Views.DashboardPage));
                    settings.OnboardingCompleted = true;
                    await settingsService.SaveSettingsAsync(settings);
                }
                finally
                {
                    _suppressNavigation = false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check onboarding status, proceeding to Dashboard");
            EnsureNavPaneVisible();
        }
    }

    /// <summary>
    /// Called by the OnboardingViewModel when onboarding is complete.
    /// Restores the navigation pane and navigates to the Dashboard.
    /// </summary>
    public void CompleteOnboarding()
    {
        EnsureNavPaneVisible();
        NavView.SelectedItem = NavDashboard;
        ContentFrame.Navigate(typeof(Views.DashboardPage));
        Log.Information("Onboarding completed, navigated to Dashboard");
    }

    /// <summary>
    /// Ensures the NavigationView pane is visible and open.
    /// Called as a safety net after onboarding completes or fails.
    /// </summary>
    private void EnsureNavPaneVisible()
    {
        NavView.IsPaneVisible = true;
        NavView.IsPaneOpen = true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  STATUS BAR — Live Ollama connection, indexing, and document count
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initializes the status bar polling timer. Delays the first check by 5 seconds
    /// to let the UI render first, then polls every 30 seconds to keep the status bar current.
    /// </summary>
    private void InitializeStatusBar()
    {
        _statusTimer = new DispatcherTimer { Interval = AppConstants.StatusBarPollInterval };
        _statusTimer.Tick += async (s, e) => await UpdateStatusBarAsync();
        _statusTimer.Start();

        // Delay the initial status check to let the UI render first.
        // The DashboardPage and App initialization already check the connection;
        // no need to pile on a third concurrent check at startup.
        _ = DelayedInitialStatusCheckAsync();
    }

    private async Task DelayedInitialStatusCheckAsync()
    {
        try
        {
            await Task.Delay(AppConstants.InitialStatusCheckDelay);
            await UpdateStatusBarAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Initial delayed status check failed");
        }
    }

    /// <summary>
    /// Polls the AI service, indexing service, and document service to update the
    /// status bar indicators (connection dot, model name, indexing progress, doc count).
    /// </summary>
    private async Task UpdateStatusBarAsync()
    {
        // Safety net: if nav pane is hidden but we're not on the onboarding page, restore it.
        // This catches edge cases where the pane got stuck hidden.
        if (!NavView.IsPaneVisible && ContentFrame.Content is not Views.OnboardingPage)
        {
            EnsureNavPaneVisible();
            Log.Warning("Nav pane was hidden outside of onboarding — restored");
        }

        // --- Connection status ---
        try
        {
            var aiService = App.GetService<IAiService>();
            var connected = await aiService.ActiveProvider.CheckConnectionAsync();

            StatusIndicator.Fill = connected
                ? (SolidColorBrush)Application.Current.Resources["OnlineBrush"]
                : (SolidColorBrush)Application.Current.Resources["OfflineBrush"];

            if (connected)
            {
                var modelId = aiService.ActiveModelId;
                StatusText.Text = !string.IsNullOrEmpty(modelId)
                    ? $"Connected \u2014 {modelId}"
                    : "Connected to Ollama";
            }
            else
            {
                StatusText.Text = "Ollama not detected";
            }

            _lastConnectionState = connected;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Status bar connection check failed");
            StatusIndicator.Fill = (SolidColorBrush)Application.Current.Resources["OfflineBrush"];
            StatusText.Text = "Connection check failed";
        }

        // --- Indexing status ---
        try
        {
            var indexingService = App.GetService<IIndexingService>();
            if (indexingService.IsProcessing)
            {
                var queueLength = await indexingService.GetQueueLengthAsync();
                IndexingRing.IsActive = true;
                IndexingText.Text = $"Indexing ({queueLength} remaining)";
            }
            else
            {
                IndexingRing.IsActive = false;
                IndexingText.Text = "";
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Indexing status check failed");
        }

        // --- Document count ---
        try
        {
            var docService = App.GetService<IDocumentService>();
            var docCount = await docService.GetTotalDocumentCountAsync();
            DocCountText.Text = docCount > 0 ? $"{docCount} docs" : "";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Document count check failed");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  WINDOW CONFIGURATION
    // ═══════════════════════════════════════════════════════════════════

    private void ConfigureWindow()
    {
        // Set window size and center on screen
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(1440, 900));

        // Center the window
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        // Set minimum size
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - 1440) / 2;
            var centerY = (displayArea.WorkArea.Height - 900) / 2;
            appWindow.Move(new PointInt32(centerX, centerY));
        }

        Title = "Agent-X — Intelligence Hub";
        Log.Information("Window configured: 1440x900");
    }

    private void ConfigureTitleBar()
    {
        // Extend content into title bar for seamless look
        ExtendsContentIntoTitleBar = true;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            // Make title bar buttons blend with dark theme
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(20, 255, 255, 255);

            // Button foreground
            titleBar.ButtonForegroundColor = Color.FromArgb(200, 255, 255, 255);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(100, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(160, 255, 255, 255);

            // Close button with subtle red on hover
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 255, 255, 255);
        }

        Log.Debug("Title bar configured with custom dark theme colors");
    }

    private void ConfigureBackdrop()
    {
        // Try Mica Alt first (deepest material), fall back to Mica, then Acrylic
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };
            Log.Debug("Backdrop: Mica Alt applied");
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            Log.Debug("Backdrop: Desktop Acrylic applied");
        }
        else
        {
            // Fallback: solid dark background (already set in XAML)
            Log.Debug("Backdrop: Solid fallback (no system backdrop support)");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  NAVIGATION VIEW EVENT HANDLER
    // ═══════════════════════════════════════════════════════════════════

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Guard: don't navigate when onboarding setup is modifying NavView state
        if (_suppressNavigation) return;

        if (args.SelectedItemContainer is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (tag != null && _pageMap.TryGetValue(tag, out var pageType))
            {
                ContentFrame.Navigate(pageType);
                Log.Debug("Navigated to {Page}", tag);
            }
            else if (tag != null)
            {
                // Page not yet implemented — show placeholder
                Log.Debug("Page not yet implemented: {Page}", tag);
            }
        }
    }

}

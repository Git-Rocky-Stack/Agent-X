using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.Views;
using AgentX.App.Views.Dialogs;
using AgentX.Core.Services.Shortcuts;
using Serilog;

namespace AgentX.App;

public sealed partial class MainWindow : Window
{
    private readonly IAppNavigationService _navigationService;
    private readonly IStatusBarService _statusBarService;
    private readonly IOnboardingService _onboardingService;
    private readonly IChromeService _chromeService;
    private readonly SystemTrayService _systemTrayService;
    private readonly IShortcutRegistry _shortcutRegistry;
    private ShortcutInputRouter _shortcutInputRouter = null!;
    private QuickChatWindow? _quickChatWindow;

    private static readonly Dictionary<string, Type> PageMap = new()
    {
        ["Dashboard"] = typeof(Views.DashboardPage),
        ["Operations"] = typeof(Views.OperationsPage),
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
        ["CalendarSettings"] = typeof(Views.CalendarSettingsPage),
        ["EmailSettings"] = typeof(Views.EmailSettingsPage),
        ["Analytics"] = typeof(Views.AnalyticsPage),
        ["PastSelf"] = typeof(Views.PastSelfPage),
        ["Onboarding"] = typeof(Views.OnboardingPage),
        ["UserGuide"] = typeof(Views.UserGuidePage),
        ["PrivacyPolicy"] = typeof(Views.PrivacyPolicyPage),
        ["TermsOfService"] = typeof(Views.TermsOfServicePage),
    };

    private readonly Dictionary<string, NavigationViewItem> _navItemMap;

    public MainWindow()
    {
        InitializeComponent();

        // A1 — Bind root FlowDirection to the current UI culture
        RootGrid.FlowDirection = FlowDirectionHelper.Current();

        // Resolve services from DI
        _navigationService = App.GetService<IAppNavigationService>();
        _statusBarService = App.GetService<IStatusBarService>();
        _onboardingService = App.GetService<IOnboardingService>();
        _chromeService = App.GetService<IChromeService>();
        _systemTrayService = App.GetService<SystemTrayService>();
        _shortcutRegistry = App.GetService<IShortcutRegistry>();

        _navItemMap = BuildNavItemMap();

        ContentFrame.NavigationFailed += (_, args) =>
        {
            Log.Fatal(args.Exception, "Navigation failed for source page {SourcePageType}", args.SourcePageType);
            Log.CloseAndFlush();
            args.Handled = true;
        };

        // Initialize navigation service with XAML control references
        _navigationService.Initialize(PageMap, _navItemMap, ContentFrame, NavView);

        // Configure keyboard shortcuts
        ConfigureShortcuts();

        // Wire up command palette callbacks
        CommandPalette.NavigateToPageRequested = _navigationService.NavigateToPage;
        CommandPalette.ExecuteActionRequested = _navigationService.ExecuteAction;

        // Attach keyboard handler
        RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;
        _shortcutInputRouter.Attach(RootGrid);

        // Configure window chrome and status bar. Tray/window lifecycle hooks are
        // attached after the first activation so they cannot intercept WinUI's
        // startup show messages.
        _chromeService.ConfigureWindow(this);
        _chromeService.ConfigureTitleBar(this);
        _chromeService.ConfigureBackdrop(this);

        ConfigureStatusBar();

        QueueInitialNavigation();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  KEYBOARD SHORTCUTS
    // ═══════════════════════════════════════════════════════════════════

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && CommandPalette.IsOpen)
        {
            if (FocusManager.GetFocusedElement(Content.XamlRoot) is not TextBox)
            {
                CommandPalette.Hide();
                e.Handled = true;
            }
        }
    }

    private void ConfigureShortcuts()
    {
        App.GetService<ShortcutCatalog>().SeedDefaults(new ShortcutCatalogActions(
            (pageTag, _) => { _navigationService.NavigateToPage(pageTag); return Task.CompletedTask; },
            _ => ShowCommandPaletteAsync(),
            _ => ShowJumpToDialogAsync(),
            _ => ShowCheatsheetDialogAsync()));

        _shortcutInputRouter = new ShortcutInputRouter(
            _shortcutRegistry,
            App.GetService<ChordStateMachine>(),
            () => ContentFrame.CurrentSourcePageType?.Name,
            ShowCommandPaletteAsync,
            ShowJumpToDialogAsync,
            ShowCheatsheetDialogAsync);
    }

    private Task ShowCommandPaletteAsync() { CommandPalette.Show(); return Task.CompletedTask; }

    private void QueueInitialNavigation()
    {
        var queued = DispatcherQueue.TryEnqueue(() =>
        {
            NavigateStartupPage(typeof(Views.DashboardPage), "Dashboard");

            // Check if onboarding is needed (first run)
            _ = CheckOnboardingAsync();
        });

        if (!queued)
        {
            Log.Warning("Failed to queue startup navigation; navigating synchronously");
            NavigateStartupPage(typeof(Views.DashboardPage), "Dashboard");
            _ = CheckOnboardingAsync();
        }
    }

    private void NavigateStartupPage(Type pageType, string pageName)
    {
        try
        {
            Log.Debug("Navigating to {PageName} on startup", pageName);
            ContentFrame.Navigate(pageType);
            Log.Debug("Startup navigation to {PageName} completed", pageName);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup navigation to {PageName} failed", pageName);
            Log.CloseAndFlush();
        }
    }

    private async Task ShowJumpToDialogAsync()
    {
        var dialog = new JumpToDialog(new JumpToViewModel(LoadJumpToCandidatesAsync))
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = GetDialogTheme()
        };
        await dialog.ShowAsync();
    }

    private async Task ShowCheatsheetDialogAsync()
    {
        var dialog = new CheatsheetDialog(new CheatsheetViewModel(_shortcutRegistry, ContentFrame.CurrentSourcePageType?.Name))
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = GetDialogTheme()
        };
        await dialog.ShowAsync();
    }

    private Dictionary<string, NavigationViewItem> BuildNavItemMap() => new()
    {
        ["Dashboard"] = NavDashboard, ["Operations"] = NavOperations, ["Digest"] = NavDigest, ["Analytics"] = NavAnalytics, ["Chat"] = NavChat,
        ["AskFiles"] = NavAskFiles, ["QuickActions"] = NavQuickActions, ["Workflows"] = NavWorkflows,
        ["KnowledgeVault"] = NavVault, ["WebImport"] = NavWebImport, ["Collections"] = NavCollections,
        ["Search"] = NavSearch, ["KnowledgeGraph"] = NavKnowledgeGraph, ["ModelManager"] = NavModels,
        ["HardwareAdvisor"] = NavHardware, ["BackupRestore"] = NavBackupRestore, ["Annotations"] = NavAnnotations,
        ["Inbox"] = NavInbox, ["Comparison"] = NavComparison, ["WorkspaceProfiles"] = NavWorkspaceProfiles,
        ["PluginManager"] = NavPluginManager, ["SyncSettings"] = NavSyncSettings,
        ["CalendarSettings"] = NavCalendarSettings, ["EmailSettings"] = NavEmailSettings,
        ["Settings"] = NavSettings, ["UserGuide"] = NavUserGuide,
        ["PrivacyPolicy"] = NavPrivacyPolicy, ["TermsOfService"] = NavTermsOfService,
    };

    /// <summary>
    /// Public navigation entry point used by pages (e.g., DashboardPage) that
    /// need to navigate to other pages. Delegates to the navigation service.
    /// </summary>
    internal void NavigateToPage(string pageTag) => _navigationService.NavigateToPage(pageTag);
}

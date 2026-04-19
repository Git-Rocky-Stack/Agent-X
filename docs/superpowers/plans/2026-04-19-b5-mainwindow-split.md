# B5: MainWindow Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce `MainWindow.xaml.cs` (896 LOC) to ≤200 LOC by extracting concerns into dedicated services. ShortcutInputRouter already extracted in A2.

**Architecture:** Extract StatusBar, SystemTray, Navigation, Onboarding, and Chrome concerns into services. MainWindow becomes constructor + service wiring + minimal event handlers.

**Tech Stack:** C#, .NET 8, WinUI 3, CommunityToolkit.Mvvm, xUnit

---

### Task 1: StatusBarService

**Files:**
- Create: `src/AgentX.App/Services/StatusBarService.cs`
- Create: `src/AgentX.App/Services/IStatusBarService.cs`
- Create: `tests/AgentX.Tests/Services/StatusBarServiceTests.cs`

- [ ] **Step 1: Define IStatusBarService interface**

```csharp
public interface IStatusBarService : IDisposable
{
    string ConnectionStatus { get; }
    string ActiveModelName { get; }
    bool IsConnected { get; }
    void StartPolling(int intervalMs = 5000);
    void StopPolling();
    event EventHandler<StatusBarState>? StateChanged;
}

public record StatusBarState(string ConnectionStatus, string ActiveModelName, bool IsConnected);
```

- [ ] **Step 2: Write failing tests**

Tests: StartPolling fires StateChanged events, StopPolling stops updates, UpdateStatusAsync queries services and raises event, Dispose cleans up timer.

- [ ] **Step 3: Extract status bar logic from MainWindow (lines 478-603)**

Move: InitializeStatusBar, DelayedInitialStatusCheck, UpdateStatusBarAsync, timer management, connection state tracking.

- [ ] **Step 4: Wire into MainWindow**

Replace inline status bar code with StatusBarService subscription.

- [ ] **Step 5: Run tests**

---

### Task 2: SystemTrayService Expansion

**Files:**
- Read/Modify: `src/AgentX.App/Services/SystemTrayService.cs` (existing)
- Create: `tests/AgentX.Tests/Services/SystemTrayServiceTests.cs`

- [ ] **Step 1: Expand SystemTrayService with window lifecycle methods**

Add methods: ConfigureTray, RestoreFromTray, OnWindowClosing (minimize-to-tray vs close), CloseAppForReal, tray menu handlers (Open, QuickChat, Settings, Exit).

- [ ] **Step 2: Write tests for tray lifecycle**

- [ ] **Step 3: Extract tray logic from MainWindow (lines 644-672, 673-729, 775-794)**

Move: ConfigureSystemTray, RestoreFromTray, OnWindowClosing, CloseAppForReal, OpenQuickChat, OnTraySettingsRequested, all tray menu handlers.

- [ ] **Step 4: Wire into MainWindow**

MainWindow subscribes to SystemTrayService events for window state changes.

- [ ] **Step 5: Run tests**

---

### Task 3: NavigationService

**Files:**
- Create: `src/AgentX.App/Services/AppNavigationService.cs`
- Create: `src/AgentX.App/Services/IAppNavigationService.cs`
- Create: `tests/AgentX.Tests/Services/AppNavigationServiceTests.cs`

- [ ] **Step 1: Define IAppNavigationService interface**

```csharp
public interface IAppNavigationService
{
    void Initialize(Frame contentFrame, NavigationView navView);
    void NavigateToPage(string pageKey);
    void EnsureNavPaneVisible();
    string CurrentPage { get; }
    event EventHandler<string>? PageChanged;
}
```

- [ ] **Step 2: Write failing tests**

Tests: NavigateToPage updates Frame content, tracks current page, fires PageChanged, EnsureNavPaneVisible sets NavView state.

- [ ] **Step 3: Extract navigation logic from MainWindow (lines 464-477, 851-895)**

Move: NavView_SelectionChanged, NavigateToPage, EnsureNavPaneVisible, page key resolution.

- [ ] **Step 4: Wire into MainWindow**

- [ ] **Step 5: Run tests**

---

### Task 4: OnboardingService + ChromeService

**Files:**
- Create: `src/AgentX.App/Services/OnboardingService.cs`
- Create: `src/AgentX.App/Services/IOnboardingService.cs`
- Create: `src/AgentX.App/Services/ChromeService.cs`
- Create: `src/AgentX.App/Services/IChromeService.cs`
- Create: `tests/AgentX.Tests/Services/OnboardingServiceTests.cs`
- Create: `tests/AgentX.Tests/Services/ChromeServiceTests.cs`

- [ ] **Step 1: Define interfaces**

```csharp
public interface IOnboardingService
{
    Task<bool> ShouldShowOnboardingAsync();
    Task CompleteOnboardingAsync();
}

public interface IChromeService
{
    void ConfigureWindow(Window window);
    void ConfigureTitleBar(Window window);
    void ConfigureBackdrop(Window window);
}
```

- [ ] **Step 2: Write tests**

- [ ] **Step 3: Extract onboarding (lines 398-463) and chrome (lines 604-643, 795-850)**

Move: CheckOnboardingAsync, CompleteOnboarding, ConfigureWindow, ConfigureTitleBar, ConfigureBackdrop.

- [ ] **Step 4: Thin MainWindow to ≤200 LOC**

Final MainWindow should be: constructor (DI services + wire events) + RootGrid_PreviewKeyDown (delegates to ShortcutInputRouter) + ShowCommandPaletteAsync/JumpTo/Cheatsheet stubs.

- [ ] **Step 5: Register all services in DI**

- [ ] **Step 6: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

MainWindow.xaml.cs ≤ 200 LOC. All new service tests pass. App launches and functions identically.

## Commit Strategy

- `refactor(ui): StatusBarService extracted from MainWindow`
- `refactor(ui): SystemTrayService expanded with lifecycle management`
- `refactor(ui): AppNavigationService for page navigation`
- `refactor(ui): OnboardingService + ChromeService, MainWindow ≤200 LOC`

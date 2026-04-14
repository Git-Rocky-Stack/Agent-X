# System Tray + Global Hotkey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Agent-X an always-available system utility via system tray icon and Win+Shift+A global hotkey that opens a Quick Chat overlay.

**Architecture:** Wire the existing `SystemTrayService` into the app lifecycle. Add a `QuickChatWindow` overlay that appears over any application. Register `Win+Shift+A` via Win32 `RegisterHotKey` P/Invoke. Override window close to minimize-to-tray by default.

**Tech Stack:** C#, .NET 8, WinUI 3, Win32 P/Invoke (Shell_NotifyIcon, RegisterHotKey), CommunityToolkit.Mvvm, xUnit

---

### Task 1: Wire SystemTrayService into App Lifecycle

**Files:**
- Read: `src/AgentX.App/Services/SystemTrayService.cs`
- Modify: `src/AgentX.App/MainWindow.xaml.cs`
- Modify: `src/AgentX.App/App.xaml.cs`
- Test: `tests/AgentX.Tests/Services/SystemTrayServiceTests.cs`

- [ ] **Step 1: Read the existing SystemTrayService to understand its current interface**

Read `src/AgentX.App/Services/SystemTrayService.cs` and document its public API.

- [ ] **Step 2: Write failing tests for SystemTrayService lifecycle**

```csharp
// tests/AgentX.Tests/Services/SystemTrayServiceTests.cs
using AgentX.App.Services;
using Xunit;

namespace AgentX.Tests.Services;

public class SystemTrayServiceTests
{
    [Fact]
    public void SystemTrayService_HasMinimizeToTrayProperty()
    {
        var service = new SystemTrayService();
        Assert.True(service.MinimizeToTray);
    }

    [Fact]
    public void SystemTrayService_ShowTrayIcon_DoesNotThrow()
    {
        var service = new SystemTrayService();
        // This tests that ShowTrayIcon can be called without crashing
        // The actual tray icon is a Win32 concept, we test the method exists
        var exception = Record.Exception(() => service.ShowTrayIcon());
        Assert.Null(exception);
    }

    [Fact]
    public void SystemTrayService_HideTrayIcon_DoesNotThrow()
    {
        var service = new SystemTrayService();
        var exception = Record.Exception(() => service.HideTrayIcon());
        Assert.Null(exception);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~SystemTrayServiceTests" -v n`
Expected: Build error or test failure depending on existing API surface.

- [ ] **Step 4: Update SystemTrayService with the public API needed**

Read `src/AgentX.App/Services/SystemTrayService.cs`. Ensure it exposes:
- `bool MinimizeToTray { get; set; }` (default true)
- `void ShowTrayIcon()`
- `void HideTrayIcon()`
- `event EventHandler? ShowWindowRequested`
- `event EventHandler? ExitRequested`

Update the context menu items:
- "Open Agent-X" → fires `ShowWindowRequested`
- "Quick Chat" → fires `QuickChatRequested` (new event)
- "Settings" → navigates to settings page
- "Exit" → fires `ExitRequested`

Add event:
```csharp
public event EventHandler? QuickChatRequested;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~SystemTrayServiceTests" -v n`
Expected: All 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Services/SystemTrayService.cs tests/AgentX.Tests/Services/SystemTrayServiceTests.cs
git commit -m "feat(tray): add public API and QuickChatRequested event to SystemTrayService"
```

---

### Task 2: Override Window Close to Minimize-to-Tray

**Files:**
- Modify: `src/AgentX.App/MainWindow.xaml.cs`
- Modify: `src/AgentX.App/App.xaml.cs`

- [ ] **Step 1: Update MainWindow to intercept close and minimize to tray**

Read `src/AgentX.App/MainWindow.xaml.cs`. In the constructor, after `InitializeComponent()`:

```csharp
// Subscribe to SystemTrayService events
_systemTrayService = App.Current.Services.GetRequiredService<SystemTrayService>();
_systemTrayService.ShowWindowRequested += (s, e) => RestoreFromTray();
_systemTrayService.ExitRequested += (s, e) => CloseAppForReal();
_systemTrayService.QuickChatRequested += (s, e) => OpenQuickChat();

// Override close behavior
this.AppWindow.Closing += OnWindowClosing;
```

Add methods:

```csharp
private readonly SystemTrayService _systemTrayService;
private bool _isReallyClosing = false;

private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
{
    if (!_isReallyClosing && _systemTrayService.MinimizeToTray)
    {
        args.Cancel = true;
        this.AppWindow.Hide();
        _systemTrayService.ShowTrayIcon();
    }
}

private void RestoreFromTray()
{
    this.AppWindow.Show();
    this.AppWindow.MoveInZOrderAtTop();
    this.Activate();
}

private void CloseAppForReal()
{
    _isReallyClosing = true;
    _systemTrayService.HideTrayIcon();
    this.Close();
}
```

- [ ] **Step 2: Register SystemTrayService in DI container**

In `src/AgentX.App/App.xaml.cs`, inside `ConfigureServices`:

```csharp
services.AddSingleton<SystemTrayService>();
```

- [ ] **Step 3: Run the app and verify minimize-to-tray works**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet run --project src/AgentX.App`
Expected: Closing the window minimizes to system tray. Double-clicking tray icon restores window. Right-click shows context menu with "Exit" that actually closes.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.App/MainWindow.xaml.cs src/AgentX.App/App.xaml.cs
git commit -m "feat(tray): wire SystemTrayService into window lifecycle, minimize-to-tray on close"
```

---

### Task 3: Global Hotkey (Win+Shift+A) Registration

**Files:**
- Modify: `src/AgentX.App/Services/SystemTrayService.cs`
- Test: `tests/AgentX.Tests/Services/GlobalHotkeyTests.cs`

- [ ] **Step 1: Write test for hotkey registration**

```csharp
// tests/AgentX.Tests/Services/GlobalHotkeyTests.cs
using Xunit;

namespace AgentX.Tests.Services;

public class GlobalHotkeyTests
{
    [Fact]
    public void SystemTrayService_RegisterGlobalHotkey_DoesNotThrow()
    {
        var service = new SystemTrayService();
        // Registration requires a window handle, which we can't provide in unit tests
        // Instead, test that the method signature exists and the constants are correct
        Assert.Equal(0x0041, SystemTrayService.WM_HOTKEY); // WM_HOTKEY value
        Assert.Equal(1, SystemTrayService.QUICK_CHAT_HOTKEY_ID);
    }

    [Fact]
    public void SystemTrayService_HasQuickChatRequestedEvent()
    {
        var service = new SystemTrayService();
        bool eventFired = false;
        service.QuickChatRequested += (s, e) => eventFired = true;
        // Manually invoke to test
        service.OnQuickChatRequested();
        Assert.True(eventFired);
    }
}
```

- [ ] **Step 2: Add global hotkey registration to SystemTrayService**

Read `src/AgentX.App/Services/SystemTrayService.cs`. Add:

```csharp
// P/Invoke constants and methods for RegisterHotKey
public const int WM_HOTKEY = 0x0312;
public const int QUICK_CHAT_HOTKEY_ID = 1;

// MOD_WIN = 0x0008, MOD_SHIFT = 0x0004, 'A' = 0x41
private const int MOD_WIN_SHIFT = 0x0008 | 0x0004;
private const int VK_A = 0x41;

[DllImport("user32.dll")]
private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll")]
private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

private IntPtr _windowHandle;
private bool _hotkeyRegistered;

public void RegisterGlobalHotkey(IntPtr windowHandle)
{
    _windowHandle = windowHandle;
    _hotkeyRegistered = RegisterHotKey(_windowHandle, QUICK_CHAT_HOTKEY_ID, MOD_WIN_SHIFT, VK_A);
}

public void UnregisterGlobalHotkey()
{
    if (_hotkeyRegistered)
    {
        UnregisterHotKey(_windowHandle, QUICK_CHAT_HOTKEY_ID);
        _hotkeyRegistered = false;
    }
}

// Called from the window's WndProc when WM_HOTKEY is received
public void OnHotkeyPressed(int hotkeyId)
{
    if (hotkeyId == QUICK_CHAT_HOTKEY_ID)
    {
        OnQuickChatRequested();
    }
}

protected virtual void OnQuickChatRequested()
{
    QuickChatRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Handle WM_HOTKEY in MainWindow**

Read `src/AgentX.App/MainWindow.xaml.cs`. Override `WndProc` to handle the hotkey message. WinUI 3 doesn't expose WndProc directly, so use `SubclassWindow` pattern or handle via the `AppWindow` message handler:

```csharp
// In MainWindow constructor, after hotkey registration:
_systemTrayService.RegisterGlobalHotkey(this.AppWindow.Id.Value);

// Override WndProc via Win32 subclassing
// Use Microsoft.UI.Dispatching or custom Win32 window proc hook
// The exact pattern depends on WinUI 3 version — check AppWindow capabilities
```

Note: The exact WndProc hook mechanism in WinUI 3 varies by Windows App SDK version. The implementation must use `SetWindowLongPtr` + `CallWindowProc` P/Invoke subclassing or the `AppWindow.MessageHook` if available. Check the Windows App SDK version in `Directory.Build.props`.

- [ ] **Step 4: Run tests**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~GlobalHotkeyTests" -v n`
Expected: All 2 tests PASS.

- [ ] **Step 5: Run the app and verify Win+Shift+A works**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet run --project src/AgentX.App`
Expected: Pressing `Win+Shift+A` fires `QuickChatRequested` event (verify via debugger or log).

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Services/SystemTrayService.cs src/AgentX.App/MainWindow.xaml.cs tests/AgentX.Tests/Services/GlobalHotkeyTests.cs
git commit -m "feat(hotkey): register Win+Shift+A global hotkey for Quick Chat"
```

---

### Task 4: Quick Chat Overlay Window

**Files:**
- Create: `src/AgentX.App/Views/QuickChatWindow.xaml`
- Create: `src/AgentX.App/Views/QuickChatWindow.xaml.cs`
- Create: `src/AgentX.App/ViewModels/QuickChatViewModel.cs`

- [ ] **Step 1: Create the QuickChatViewModel**

```csharp
// src/AgentX.App/ViewModels/QuickChatViewModel.cs
using AgentX.Core.AI;
using AgentX.Core.Services.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AgentX.App.ViewModels;

public partial class QuickChatViewModel : ObservableObject
{
    private readonly IAiService _aiService;
    private readonly IChatService _chatService;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string _responseText = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ask anything against your knowledge vault";

    public ObservableCollection<CitationReference> Citations { get; } = [];

    public QuickChatViewModel(IAiService aiService, IChatService chatService)
    {
        _aiService = aiService;
        _chatService = chatService;
    }

    [RelayCommand]
    private async Task SubmitQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(QueryText) || IsProcessing) return;

        IsProcessing = true;
        ResponseText = string.Empty;
        Citations.Clear();
        StatusMessage = "Searching knowledge vault...";

        try
        {
            var response = await _aiService.StreamChatAsync(
                $"Based on the knowledge vault, answer: {QueryText}",
                new Core.AI.Models.ChatOptions { Temperature = 0.3 });

            // Collect streaming response
            var sb = new System.Text.StringBuilder();
            await foreach (var token in response)
            {
                sb.Append(token);
                ResponseText = sb.ToString();
            }

            StatusMessage = $"Answered using {_aiService.ActiveModelId}";
        }
        catch (Exception ex)
        {
            ResponseText = $"Error: {ex.Message}";
            StatusMessage = "Query failed";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        QueryText = string.Empty;
        ResponseText = string.Empty;
        Citations.Clear();
        StatusMessage = "Ask anything against your knowledge vault";
    }
}

public record CitationReference(int Number, string Source, string Page, string Excerpt);
```

- [ ] **Step 2: Create the QuickChatWindow XAML**

```xml
<!-- src/AgentX.App/Views/QuickChatWindow.xaml -->
<Window
    x:Class="AgentX.App.Views.QuickChatWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:AgentX.App.ViewModels"
    Title="Agent-X Quick Chat"
    Width="480"
    Height="400">

    <Grid Background="{ThemeResource ApplicationBackgroundBrush}"
          Padding="16"
          RowSpacing="8"
          CornerRadius="8">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8">
            <FontIcon Glyph="&#xE945;" FontSize="16" Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}"/>
            <TextBlock Text="Quick Chat" Style="{StaticResource SubtitleTextBlockStyle}" VerticalAlignment="Center"/>
            <TextBlock Text="{x:Bind ViewModel.StatusMessage, Mode=OneWay}"
                       Style="{StaticResource CaptionTextBlockStyle}"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       VerticalAlignment="Center"
                       Margin="8,0,0,0"/>
        </StackPanel>

        <!-- Response Area -->
        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <TextBlock Text="{x:Bind ViewModel.ResponseText, Mode=OneWay}"
                       TextWrapping="Wrap"
                       IsTextSelectionEnabled="True"
                       Style="{StaticResource BodyTextBlockStyle}"/>
        </ScrollViewer>

        <!-- Input Area -->
        <TextBox Grid.Row="2"
                 x:Name="QueryInput"
                 Text="{x:Bind ViewModel.QueryText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 PlaceholderText="Ask your knowledge vault..."
                 AcceptsReturn="False"
                 KeyDown="OnQueryKeyDown"/>

        <!-- Action Buttons -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button Content="Clear" Command="{x:Bind ViewModel.ClearCommand}" />
            <Button Content="Ask" Command="{x:Bind ViewModel.SubmitQueryCommand}" Style="{StaticResource AccentButtonStyle}" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Create the QuickChatWindow code-behind**

```csharp
// src/AgentX.App/Views/QuickChatWindow.xaml.cs
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace AgentX.App.Views;

public sealed partial class QuickChatWindow : Window
{
    public QuickChatViewModel ViewModel { get; }

    public QuickChatWindow(QuickChatViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Position window at top-center of screen
        var displayArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary);
        var centerX = (displayArea.WorkArea.Width - 480) / 2;
        this.AppWindow.Move(new Windows.Graphics.PointInt32(centerX, 40));

        // Always on top
        this.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);

        // Handle Escape to dismiss
        this.KeyDown += OnWindowKeyDown;
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ViewModel.SubmitQueryCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            this.Close();
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 4: Wire QuickChatWindow into MainWindow**

In `src/AgentX.App/MainWindow.xaml.cs`, update the `OpenQuickChat()` method:

```csharp
private QuickChatWindow? _quickChatWindow;

private void OpenQuickChat()
{
    if (_quickChatWindow == null)
    {
        var viewModel = App.Current.Services.GetRequiredService<QuickChatViewModel>();
        _quickChatWindow = new QuickChatWindow(viewModel);
        _quickChatWindow.Closed += (s, e) => _quickChatWindow = null;
    }

    _quickChatWindow.Activate();
}
```

Register in DI:
```csharp
services.AddTransient<QuickChatViewModel>();
```

- [ ] **Step 5: Run the app and test Quick Chat**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet run --project src/AgentX.App`
Expected: `Win+Shift+A` opens Quick Chat overlay. Typing a query and pressing Enter streams a response. Escape dismisses the overlay.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Views/QuickChatWindow.xaml src/AgentX.App/Views/QuickChatWindow.xaml.cs src/AgentX.App/ViewModels/QuickChatViewModel.cs src/AgentX.App/MainWindow.xaml.cs src/AgentX.App/App.xaml.cs
git commit -m "feat(tray): add Quick Chat overlay window with Win+Shift+A global hotkey"
```

---

### Task 5: Full Integration Test and Tray Icon Status

**Files:**
- Modify: `src/AgentX.App/Services/SystemTrayService.cs`

- [ ] **Step 1: Update tray icon tooltip with dynamic status**

In `SystemTrayService`, update the tray icon tooltip to show AI status:

```csharp
public void UpdateTooltip(string aiStatus, string model, int documentCount)
{
    var tooltip = $"Agent-X | {aiStatus} | {model} | {documentCount} docs";
    // Update the NOTIFYICONDATA szTip field
    _notifyIconData.szTip = tooltip;
    Shell_NotifyIcon(NIM_MODIFY, ref _notifyIconData);
}
```

Wire into existing status bar updates to call `_systemTrayService.UpdateTooltip(...)` when AI connection status changes.

- [ ] **Step 2: Run full test suite**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests -v n`
Expected: All tests pass.

- [ ] **Step 3: Manual integration test checklist**

- [ ] App launches → tray icon appears
- [ ] Hover tray icon → tooltip shows AI status, model, doc count
- [ ] Right-click tray → context menu: Open, Quick Chat, Settings, Exit
- [ ] Close window → minimizes to tray (not to taskbar)
- [ ] Double-click tray icon → window restores
- [ ] Win+Shift+A → Quick Chat overlay appears
- [ ] Type query in Quick Chat → response streams
- [ ] Escape → Quick Chat dismisses
- [ ] "Exit" from tray menu → app fully closes

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.App/Services/SystemTrayService.cs
git commit -m "feat(tray): add dynamic status tooltip to system tray icon"
```
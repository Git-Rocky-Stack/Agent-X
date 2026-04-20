using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using Windows.Graphics;
using WinRT.Interop;

namespace AgentX.App.Services;

/// <summary>
/// Manages system tray icon presence using H.NotifyIcon library, provides
/// minimize-to-tray functionality with a global hotkey (Win+Shift+A), and
/// orchestrates window lifecycle (hide/show/close) for the minimize-to-tray pattern.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    // ── Win32 Constants ──────────────────────────────────────────
    private const int WM_HOTKEY = 0x0312;
    private const int GWLP_WNDPROC = -4;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;
    private const int HOTKEY_ID = 9001;

    private TaskbarIcon? _trayIcon;
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private bool _hotkeyRegistered;
    private bool _disposed;

    // ── Window lifecycle state ──────────────────────────────────
    private Window? _window;
    private AppWindow? _appWindow;
    private bool _isReallyClosing;

    // Keep delegate alive to prevent GC during window subclass
    private WndProcDelegate? _wndProc;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Gets or sets whether closing the window minimizes to the system tray instead of exiting.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Gets or sets the tooltip text displayed when hovering over the tray icon.
    /// </summary>
    public string TooltipText { get; set; } = "Agent-X \u2014 Intelligence Hub";

    /// <summary>
    /// Raised when the user double-clicks the tray icon.
    /// Subscribed by MainWindow to restore the window from the tray.
    /// </summary>
    public event Action? RestoreRequested;

    /// <summary>
    /// Raised when the user selects "Exit" from the tray context menu.
    /// </summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Raised when the user selects "Quick Chat" from the tray context menu.
    /// </summary>
    public event Action? QuickChatRequested;

    /// <summary>
    /// Raised when the user selects "Settings" from the tray context menu.
    /// </summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// Initializes the service with the main window handle and the XAML TaskbarIcon.
    /// Installs a window subclass to receive WM_HOTKEY messages for the global hotkey.
    /// The TaskbarIcon's ContextFlyout and DoubleClickCommand should be configured
    /// in XAML/code-behind; this service handles icon lifecycle and hotkey routing.
    /// </summary>
    public void Initialize(IntPtr hwnd, TaskbarIcon trayIcon)
    {
        _hwnd = hwnd;
        _trayIcon = trayIcon;

        // Install window subclass for global hotkey (WM_HOTKEY)
        InstallSubclass();

        Log.Information("SystemTrayService initialized with HWND {Handle}", hwnd);
    }

    /// <summary>
    /// Registers the global hotkey (Win+Shift+A by default).
    /// </summary>
    public void RegisterGlobalHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;

        try
        {
            var result = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_WIN | MOD_SHIFT | MOD_NOREPEAT, 0x41); // 0x41 = 'A'
            if (result)
            {
                _hotkeyRegistered = true;
                Log.Information("Global hotkey Win+Shift+A registered successfully");
            }
            else
            {
                Log.Warning("Failed to register global hotkey Win+Shift+A \u2014 may already be in use");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register global hotkey");
        }
    }

    /// <summary>
    /// Unregisters the global hotkey.
    /// </summary>
    public void UnregisterGlobalHotkey()
    {
        if (!_hotkeyRegistered || _hwnd == IntPtr.Zero) return;

        try
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _hotkeyRegistered = false;
            Log.Debug("Global hotkey unregistered");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to unregister global hotkey");
        }
    }

    /// <summary>
    /// Forces creation of the system tray icon. The icon is also created
    /// automatically when the TaskbarIcon is loaded in the visual tree.
    /// </summary>
    public void ShowTrayIcon()
    {
        if (_trayIcon == null)
        {
            Log.Warning("Cannot show tray icon: TaskbarIcon not initialized");
            return;
        }

        try
        {
            _trayIcon.ForceCreate();
            Log.Information("System tray icon shown");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show system tray icon");
        }
    }

    /// <summary>
    /// Disposes the system tray icon, removing it from the system tray.
    /// </summary>
    public void HideTrayIcon()
    {
        if (_trayIcon == null) return;

        try
        {
            _trayIcon.Dispose();
            Log.Information("System tray icon hidden");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to hide system tray icon");
        }
    }

    /// <summary>
    /// Updates the tray icon tooltip text (e.g., to show active model status).
    /// </summary>
    public void UpdateTooltip(string text)
    {
        TooltipText = text;
        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = text;
            Log.Debug("Tray tooltip updated: {Text}", text);
        }
    }

    /// <summary>
    /// Updates the tray icon tooltip with dynamic status information.
    /// Formats as: "Agent-X | Connected | model-name | 42 docs"
    /// or: "Agent-X | Disconnected" when AI is not available.
    /// </summary>
    public void UpdateTooltip(string aiStatus, string model, long documentCount)
    {
        // Build tooltip segments — only include model/docs when connected
        var parts = new List<string> { "Agent-X", aiStatus };

        if (aiStatus == "Connected" && !string.IsNullOrEmpty(model))
            parts.Add(model);

        if (documentCount > 0)
            parts.Add($"{documentCount} docs");

        var text = string.Join(" | ", parts);
        UpdateTooltip(text);
    }

    /// <summary>
    /// Processes window messages. Kept for API compatibility; messages
    /// are now handled by the window subclass and H.NotifyIcon internally.
    /// </summary>
    public bool ProcessMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Messages are handled by the WndProc subclass and H.NotifyIcon.
        return false;
    }

    /// <summary>
    /// Raises the RestoreRequested event. Called by MainWindow when the
    /// tray icon is double-clicked (via DoubleClickCommand) or when the
    /// "Open Agent-X" context menu item is clicked.
    /// </summary>
    public void InvokeRestoreRequested() => RestoreRequested?.Invoke();

    /// <summary>
    /// Raises the QuitRequested event. Called by MainWindow when the
    /// "Exit" context menu item is clicked.
    /// </summary>
    public void InvokeQuitRequested() => QuitRequested?.Invoke();

    /// <summary>
    /// Raises the QuickChatRequested event. Called by MainWindow when the
    /// "Quick Chat" context menu item is clicked.
    /// </summary>
    public void InvokeQuickChatRequested() => QuickChatRequested?.Invoke();

    /// <summary>
    /// Raises the SettingsRequested event. Called by MainWindow when the
    /// "Settings" context menu item is clicked.
    /// </summary>
    public void InvokeSettingsRequested() => SettingsRequested?.Invoke();

    // ═══════════════════════════════════════════════════════════════════
    //  WINDOW LIFECYCLE (minimize-to-tray, restore, close)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the system tray for a specific window. Wires up the tray icon,
    /// global hotkey, and the window closing event for minimize-to-tray behavior.
    /// The <paramref name="doubleClickCommand"/> is wired to the tray icon's
    /// DoubleClickCommand (e.g., a DelegateCommand that calls <see cref="RestoreFromTray"/>).
    /// </summary>
    public void ConfigureTray(Window window, TaskbarIcon trayIcon, System.Windows.Input.ICommand? doubleClickCommand = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));

        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        Initialize(hwnd, trayIcon);
        ShowTrayIcon();
        RegisterGlobalHotkey();

        if (doubleClickCommand != null)
            trayIcon.DoubleClickCommand = doubleClickCommand;

        // Intercept window closing to minimize to tray instead of exiting
        _appWindow.Closing += OnWindowClosing;

        Log.Information("System tray integration configured with window lifecycle");
    }

    /// <summary>
    /// Intercepts the window close event. If <see cref="MinimizeToTray"/> is enabled
    /// and the user hasn't explicitly chosen "Exit", hides the window instead of closing.
    /// </summary>
    public void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (MinimizeToTray && !_isReallyClosing)
        {
            args.Cancel = true;

            // Use H.NotifyIcon's WindowExtensions.Hide for Windows 11 Efficiency Mode
            _window!.Hide(enableEfficiencyMode: true);

            Log.Information("Window hidden to system tray (minimize-to-tray)");
        }
    }

    /// <summary>
    /// Restores the main window from the system tray.
    /// Shows the window, brings it to the foreground, and deactivates Efficiency Mode.
    /// </summary>
    public void RestoreFromTray()
    {
        if (_window == null || _appWindow == null)
        {
            Log.Warning("Cannot restore window: not configured");
            return;
        }

        // Use H.NotifyIcon's WindowExtensions.Show to restore with Efficiency Mode disabled
        _window.Show(disableEfficiencyMode: true);
        _window.Activate();

        // If the window was minimized before hiding, restore it to its previous state
        if (_appWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        Log.Information("Window restored from system tray");
    }

    /// <summary>
    /// Actually closes the application. Sets the flag so <see cref="OnWindowClosing"/>
    /// doesn't cancel the close, hides the tray icon, and disposes resources.
    /// </summary>
    public void CloseAppForReal()
    {
        _isReallyClosing = true;

        // Clean up tray resources before closing
        HideTrayIcon();
        UnregisterGlobalHotkey();

        // Hide the window first to avoid visual artifacts during teardown
        _appWindow?.Hide();
        _window?.Close();

        Log.Information("Application exiting via tray Exit command");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterGlobalHotkey();

        // Restore original WndProc before removing the tray icon
        if (_oldWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            try
            {
                SetWindowLongPtrSafe(_hwnd, GWLP_WNDPROC, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
                Log.Debug("Window subclass removed");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restore original WndProc");
            }
        }

        // Dispose the tray icon (removes it from the system tray)
        if (_trayIcon != null)
        {
            try
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to dispose tray icon");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE: WINDOW SUBCLASS FOR GLOBAL HOTKEY
    // ═══════════════════════════════════════════════════════════════════

    private void InstallSubclass()
    {
        if (_hwnd == IntPtr.Zero) return;

        _wndProc = new WndProcDelegate(WndProc);
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProc);
        _oldWndProc = SetWindowLongPtrSafe(_hwnd, GWLP_WNDPROC, wndProcPtr);
        Log.Debug("Window subclass installed for global hotkey (WM_HOTKEY)");
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Log.Debug("Global hotkey Win+Shift+A activated — requesting Quick Chat");
            QuickChatRequested?.Invoke();
            return IntPtr.Zero;
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  P/INVOKE DECLARATIONS
    // ═══════════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Safe wrapper for SetWindowLongPtr that handles both 32-bit and 64-bit.
    /// </summary>
    private static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
}

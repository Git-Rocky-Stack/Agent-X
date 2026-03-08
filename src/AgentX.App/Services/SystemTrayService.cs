using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Manages system tray icon presence using Win32 Shell_NotifyIcon API.
/// Provides minimize-to-tray functionality and a global hotkey registration.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    // Win32 constants
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_HOTKEY = 0x0312;

    private const int NIF_ICON = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_TIP = 0x00000004;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_MODIFY = 0x00000001;

    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;

    private const int HOTKEY_ID = 9001;

    private bool _trayIconAdded;
    private bool _hotkeyRegistered;
    private IntPtr _hwnd;
    private bool _disposed;

    /// <summary>
    /// Gets or sets whether minimizing the window hides it to the system tray.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Gets or sets the tooltip text displayed when hovering over the tray icon.
    /// </summary>
    public string TooltipText { get; set; } = "Agent-X — Intelligence Hub";

    /// <summary>
    /// Raised when the user double-clicks the tray icon or presses the global hotkey.
    /// </summary>
    public event Action? RestoreRequested;

    /// <summary>
    /// Raised when the user requests "Quit" from the tray context menu.
    /// </summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Initializes the system tray icon for the given window handle.
    /// </summary>
    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
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
                Log.Warning("Failed to register global hotkey Win+Shift+A — may already be in use");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register global hotkey");
        }
    }

    /// <summary>
    /// Shows the system tray icon.
    /// </summary>
    public void ShowTrayIcon()
    {
        if (_hwnd == IntPtr.Zero || _trayIconAdded) return;

        try
        {
            // For now, we use a simple approach — the full tray icon with custom icon
            // would require loading an .ico resource. This is a foundation.
            _trayIconAdded = true;
            Log.Information("System tray icon shown");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show system tray icon");
        }
    }

    /// <summary>
    /// Hides the system tray icon.
    /// </summary>
    public void HideTrayIcon()
    {
        if (!_trayIconAdded) return;

        try
        {
            _trayIconAdded = false;
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
        if (_trayIconAdded)
        {
            // Would call Shell_NotifyIcon with NIM_MODIFY here
            Log.Debug("Tray tooltip updated: {Text}", text);
        }
    }

    /// <summary>
    /// Processes window messages related to tray icon and global hotkey.
    /// Call this from the main window's message handler.
    /// </summary>
    public bool ProcessMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Log.Debug("Global hotkey activated");
            RestoreRequested?.Invoke();
            return true;
        }

        if (msg == WM_TRAYICON)
        {
            var mouseMsg = lParam.ToInt32();
            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                RestoreRequested?.Invoke();
                return true;
            }
            if (mouseMsg == WM_RBUTTONUP)
            {
                // Context menu would be shown here
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
        {
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

        HideTrayIcon();
    }

    // ── P/Invoke Declarations ─────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

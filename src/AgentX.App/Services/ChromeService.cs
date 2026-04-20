using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace AgentX.App.Services;

/// <summary>
/// Configures the window chrome: size, position, title bar customization, and backdrop material.
/// Keeps window chrome logic out of MainWindow so it can be tested and reused.
/// </summary>
public sealed class ChromeService : IChromeService
{
    /// <inheritdoc />
    public void ConfigureWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(1440, 900));

        // Enable standard window chrome controls
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        // Center the window on the primary display
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - 1440) / 2;
            var centerY = (displayArea.WorkArea.Height - 900) / 2;
            appWindow.Move(new PointInt32(centerX, centerY));
        }

        window.Title = "Agent-X \u2014 Intelligence Hub";
        Log.Information("Window configured: 1440x900");
    }

    /// <inheritdoc />
    public void ConfigureTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.ExtendsContentIntoTitleBar = true;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = window.AppWindow.TitleBar;
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

            // Close button with subtle red on hover (overrides the generic hover above)
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 255, 255, 255);
        }

        Log.Debug("Title bar configured with custom dark theme colors");
    }

    /// <inheritdoc />
    public void ConfigureBackdrop(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Try Mica Alt first (deepest material), fall back to Mica, then Acrylic
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };
            Log.Debug("Backdrop: Mica Alt applied");
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
            Log.Debug("Backdrop: Desktop Acrylic applied");
        }
        else
        {
            Log.Debug("Backdrop: Solid fallback (no system backdrop support)");
        }
    }
}

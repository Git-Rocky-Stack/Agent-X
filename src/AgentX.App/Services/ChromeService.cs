using AgentX.App.Helpers;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace AgentX.App.Services;

/// <summary>
/// Configures the window chrome: size, position, title bar customization, and backdrop material.
/// Keeps window chrome logic out of MainWindow so it can be tested and reused.
/// </summary>
public sealed class ChromeService : IChromeService
{
    // Preferred first-launch size in physical pixels, clamped to the display's work area.
    private const int PreferredWidth = 1440;
    private const int PreferredHeight = 900;

    // Held for the life of the (singleton) service: a collected AccessibilitySettings
    // stops raising HighContrastChanged.
    private AccessibilitySettings? _accessibilitySettings;

    /// <inheritdoc />
    public void ConfigureWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Enable standard window chrome controls
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        // Size and center inside the work area of the display the window opens on. The
        // work area excludes the taskbar and carries its own origin, so a small screen, a
        // secondary monitor, or a top-docked taskbar never pushes the title bar off screen.
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var workArea = displayArea.WorkArea;
            var bounds = WindowPlacement.CenterWithin(
                new PixelRect(workArea.X, workArea.Y, workArea.Width, workArea.Height),
                PreferredWidth,
                PreferredHeight);

            appWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            Log.Information(
                "Window configured: {Width}x{Height} at {X},{Y} (work area {WorkWidth}x{WorkHeight})",
                bounds.Width, bounds.Height, bounds.X, bounds.Y, workArea.Width, workArea.Height);
        }
        else
        {
            appWindow.Resize(new SizeInt32(PreferredWidth, PreferredHeight));
            Log.Information("Window configured: {Width}x{Height} (no display area reported)",
                PreferredWidth, PreferredHeight);
        }

        window.Title = "Agent-X \u2014 Intelligence Hub";
    }

    /// <inheritdoc />
    public void ConfigureTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.ExtendsContentIntoTitleBar = true;

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            Log.Debug("Title bar customization unsupported; caption buttons keep system colors");
            return;
        }

        var titleBar = window.AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;

        var root = window.Content as FrameworkElement;
        ApplyCaptionColors(titleBar, root);

        if (root is not null)
        {
            // ThemeService switches shifts by setting RequestedTheme on this root, and a
            // root that follows Windows changes ActualTheme with the OS. Either way the
            // caption buttons, which ThemeResource bindings never reach, are repainted.
            root.ActualThemeChanged += (sender, _) => ApplyCaptionColors(titleBar, sender);
        }

        WatchHighContrast(window, titleBar, root);

        Log.Debug("Title bar configured with theme-resolved caption colors");
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

    /// <summary>
    /// Paints the caption buttons from the theme tokens of the root's current shift.
    /// Under HighContrast every color is reset to null so the system draws them.
    /// </summary>
    private static void ApplyCaptionColors(AppWindowTitleBar titleBar, FrameworkElement? root)
    {
        try
        {
            var tokens = CaptionButtonPalette.For(ThemeResources.IsHighContrast());
            if (tokens is null)
            {
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonInactiveBackgroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonInactiveForegroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
                return;
            }

            var theme = root?.ActualTheme ?? ElementTheme.Dark;

            // Resting backgrounds stay clear so the chassis shows through the caption strip.
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = ResolveColor(tokens.HoverBackground, theme);
            titleBar.ButtonPressedBackgroundColor = ResolveColor(tokens.PressedBackground, theme);

            titleBar.ButtonForegroundColor = ResolveColor(tokens.Foreground, theme);
            titleBar.ButtonInactiveForegroundColor = ResolveColor(tokens.InactiveForeground, theme);
            titleBar.ButtonHoverForegroundColor = ResolveColor(tokens.HoverForeground, theme);
            titleBar.ButtonPressedForegroundColor = ResolveColor(tokens.PressedForeground, theme);
        }
        catch (Exception ex)
        {
            // Chrome paint is cosmetic; a failure must never take down a theme switch.
            Log.Warning(ex, "Failed to apply caption button colors");
        }
    }

    /// <summary>A theme token's color, or null (the system default) when it cannot be resolved.</summary>
    private static Color? ResolveColor(string key, ElementTheme theme) =>
        (ThemeResources.Get(key, theme) as SolidColorBrush)?.Color;

    /// <summary>
    /// HighContrast is a system setting rather than an element theme, so turning it on or
    /// off does not necessarily raise ActualThemeChanged. Listen for it directly.
    /// </summary>
    private void WatchHighContrast(Window window, AppWindowTitleBar titleBar, FrameworkElement? root)
    {
        try
        {
            _accessibilitySettings ??= new AccessibilitySettings();
            _accessibilitySettings.HighContrastChanged += (_, _) =>
                window.DispatcherQueue.TryEnqueue(() => ApplyCaptionColors(titleBar, root));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "High contrast notifications unavailable; caption colors follow theme changes only");
        }
    }
}

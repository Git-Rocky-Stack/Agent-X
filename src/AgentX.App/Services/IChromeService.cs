using Microsoft.UI.Xaml;

namespace AgentX.App.Services;

/// <summary>
/// Configures the window chrome: size, position, title bar customization, and backdrop material.
/// Extracts the visual configuration logic from MainWindow so it can be tested and reused.
/// </summary>
public interface IChromeService
{
    /// <summary>
    /// Configures the window title, and a first-launch size clamped to the display's
    /// work area and centered in it.
    /// </summary>
    void ConfigureWindow(Window window);

    /// <summary>
    /// Extends content into the title bar and paints the caption buttons from the current
    /// shift's theme tokens, repainting them whenever the root's theme or the system
    /// high-contrast setting changes. Call after the window content exists.
    /// </summary>
    void ConfigureTitleBar(Window window);

    /// <summary>
    /// Applies the best available backdrop material (Mica Alt, Acrylic, or fallback).
    /// </summary>
    void ConfigureBackdrop(Window window);
}

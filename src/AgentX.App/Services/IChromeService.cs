using Microsoft.UI.Xaml;

namespace AgentX.App.Services;

/// <summary>
/// Configures the window chrome: size, position, title bar customization, and backdrop material.
/// Extracts the visual configuration logic from MainWindow so it can be tested and reused.
/// </summary>
public interface IChromeService
{
    /// <summary>
    /// Configures the window size, position, and title.
    /// </summary>
    void ConfigureWindow(Window window);

    /// <summary>
    /// Extends content into the title bar and applies custom dark-theme colors
    /// to the caption buttons.
    /// </summary>
    void ConfigureTitleBar(Window window);

    /// <summary>
    /// Applies the best available backdrop material (Mica Alt, Acrylic, or fallback).
    /// </summary>
    void ConfigureBackdrop(Window window);
}

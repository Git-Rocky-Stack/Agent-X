using Microsoft.UI.Xaml;

namespace AgentX.App.Services;

/// <summary>
/// Manages the application's visual theme (Dark, Light, or System Default).
/// Persists the user's preference and applies it to the UI root element.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets the currently applied theme.
    /// </summary>
    ElementTheme CurrentTheme { get; }

    /// <summary>
    /// Loads the persisted theme preference from user settings.
    /// Call once during app startup before the UI is shown.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Changes the theme, applies it immediately, and persists the preference.
    /// </summary>
    Task SetThemeAsync(ElementTheme theme);

    /// <summary>
    /// Applies the given theme to the UI root element without persisting.
    /// Used during initialization to set the theme on the UI thread.
    /// </summary>
    void ApplyTheme(ElementTheme theme);
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AgentX.App.Helpers;

/// <summary>
/// Resolves theme-varying resources against the window root's shift.
///
/// ThemeService applies the shift by setting RequestedTheme on the window root
/// element. An Application.Current.Resources lookup resolves ThemeDictionaries
/// against the application's own theme, which the root never updates, so a
/// code-behind lookup keeps returning Night Ops values after the operator
/// switches to Day Shift - light-on-silver text, and the hardware skin leaking
/// into HighContrast. This resolver reads the root's ActualTheme (and the
/// system high-contrast flag) and selects the matching ThemeDictionary, which is
/// the same technique StatusToColorConverter uses for LED tones.
///
/// Prefer a XAML Style with {ThemeResource} setters where the element has one:
/// ThemeResource re-evaluates on a live shift change, whereas a resolved brush
/// is a snapshot taken when the element was built. Use this resolver for
/// elements constructed in code that carry no style.
/// </summary>
public static class ThemeResources
{
    /// <summary>
    /// Looks up a resource in the ThemeDictionary matching the current shift,
    /// falling back to the application-level value when no theme-scoped
    /// definition exists (shift-invariant tokens such as the Red ramp).
    /// </summary>
    public static object? Get(string key) => Get(key, RootActualTheme());

    /// <summary>
    /// Same lookup against an explicit shift, for callers that hold the element whose
    /// theme matters (the window chrome resolves against its own root while the window
    /// is still being constructed, before App.MainWindow is assigned).
    /// </summary>
    public static object? Get(string key, ElementTheme actualTheme)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var app = Application.Current?.Resources;
        if (app is null)
        {
            return null;
        }

        var scoped = FindThemeScoped(app, ThemeKeyFor(actualTheme), key);
        if (scoped is not null)
        {
            return scoped;
        }

        return app.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Resolves a theme-varying brush, or null when the key is absent.</summary>
    public static Brush? Brush(string key) => Get(key) as Brush;

    /// <summary>
    /// Names the ThemeDictionary that matches a shift.
    /// HighContrast wins outright: it is exempt from the hardware skin.
    /// </summary>
    private static string ThemeKeyFor(ElementTheme actualTheme)
    {
        if (IsHighContrast())
        {
            return "HighContrast";
        }

        return actualTheme == ElementTheme.Light ? "Light" : "Default";
    }

    /// <summary>The window root's current shift, or Night Ops before the root exists.</summary>
    private static ElementTheme RootActualTheme()
    {
        try
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                return root.ActualTheme;
            }
        }
        catch
        {
            // Root not yet built - fall through to the Night Ops default.
        }

        return ElementTheme.Dark;
    }

    /// <summary>True when the system high-contrast setting is on.</summary>
    public static bool IsHighContrast()
    {
        try
        {
            return new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        }
        catch
        {
            // Unavailable outside a view context; treat as normal contrast.
            return false;
        }
    }

    /// <summary>
    /// Walks the merged-dictionary tree looking for a ThemeDictionary of the
    /// given name that actually defines the key. Colors.xaml is merged into the
    /// application dictionary, so its ThemeDictionaries are nested, not top level.
    /// </summary>
    private static object? FindThemeScoped(ResourceDictionary dictionary, string themeKey, string resourceKey)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themed)
            && themed is ResourceDictionary themedDictionary
            && themedDictionary.TryGetValue(resourceKey, out var value))
        {
            return value;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            var found = FindThemeScoped(merged, themeKey, resourceKey);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

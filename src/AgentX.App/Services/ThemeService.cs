using AgentX.Core.Services.Settings;
using Microsoft.UI.Xaml;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Default implementation of <see cref="IThemeService"/>.
/// Persists the theme preference via <see cref="ISettingsService"/> key-value store
/// and applies the theme by setting <see cref="FrameworkElement.RequestedTheme"/>
/// on the root element of the main window.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private const string ThemeSettingKey = "app.theme";

    /// <inheritdoc />
    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Dark;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            var savedTheme = await _settingsService.GetValueAsync<string>(ThemeSettingKey);
            if (!string.IsNullOrEmpty(savedTheme) && Enum.TryParse<ElementTheme>(savedTheme, out var theme))
            {
                CurrentTheme = theme;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load theme preference, defaulting to Dark");
        }
    }

    /// <inheritdoc />
    public async Task SetThemeAsync(ElementTheme theme)
    {
        CurrentTheme = theme;
        ApplyTheme(theme);

        try
        {
            await _settingsService.SetValueAsync(ThemeSettingKey, theme.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist theme preference");
        }

        Log.Information("Theme changed to {Theme}", theme);
    }

    /// <inheritdoc />
    public void ApplyTheme(ElementTheme theme)
    {
        if (App.MainWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }
    }
}

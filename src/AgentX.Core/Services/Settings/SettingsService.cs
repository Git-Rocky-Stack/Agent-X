using System.Text.Json;
using Serilog;

namespace AgentX.Core.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private AppSettings? _cachedSettings;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SettingsService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX");

        Directory.CreateDirectory(appDataDir);
        _settingsPath = Path.Combine(appDataDir, "settings.json");

        Log.Information("Settings path: {SettingsPath}", _settingsPath);
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        if (_cachedSettings != null)
            return _cachedSettings;

        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                Log.Debug("Settings loaded from disk");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load settings, using defaults");
                _cachedSettings = new AppSettings();
            }
        }
        else
        {
            _cachedSettings = new AppSettings();
            await SaveSettingsAsync(_cachedSettings);
            Log.Information("Default settings created");
        }

        return _cachedSettings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        _cachedSettings = settings;

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
            Log.Debug("Settings saved to disk");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
            throw;
        }
    }

    public async Task<T?> GetValueAsync<T>(string key)
    {
        var settings = await GetSettingsAsync();
        var property = typeof(AppSettings).GetProperty(key);
        if (property == null) return default;
        return (T?)property.GetValue(settings);
    }

    public async Task SetValueAsync<T>(string key, T value)
    {
        var settings = await GetSettingsAsync();
        var property = typeof(AppSettings).GetProperty(key);
        if (property == null) return;
        property.SetValue(settings, value);
        await SaveSettingsAsync(settings);
    }
}

using System.Text.Json;
using AgentX.Core.Services.Security;
using Serilog;

namespace AgentX.Core.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly IDpapiEncryptionService _encryptionService;
    private AppSettings? _cachedSettings;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SettingsService(IDpapiEncryptionService encryptionService)
    {
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));

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

                // Decrypt encrypted API keys; detect plaintext keys for auto-migration
                bool needsMigration = false;

                if (!string.IsNullOrEmpty(_cachedSettings.OpenAiApiKey))
                {
                    if (_encryptionService.IsEncrypted(_cachedSettings.OpenAiApiKey))
                        _cachedSettings.OpenAiApiKey = _encryptionService.Decrypt(_cachedSettings.OpenAiApiKey);
                    else
                        needsMigration = true;
                }

                if (!string.IsNullOrEmpty(_cachedSettings.AnthropicApiKey))
                {
                    if (_encryptionService.IsEncrypted(_cachedSettings.AnthropicApiKey))
                        _cachedSettings.AnthropicApiKey = _encryptionService.Decrypt(_cachedSettings.AnthropicApiKey);
                    else
                        needsMigration = true;
                }

                // Decrypt OAuth client secrets
                if (!string.IsNullOrEmpty(_cachedSettings.OAuth.Google.ClientSecret))
                {
                    if (_encryptionService.IsEncrypted(_cachedSettings.OAuth.Google.ClientSecret))
                        _cachedSettings.OAuth.Google.ClientSecret = _encryptionService.Decrypt(_cachedSettings.OAuth.Google.ClientSecret);
                    else
                        needsMigration = true;
                }

                if (!string.IsNullOrEmpty(_cachedSettings.OAuth.Microsoft.ClientSecret))
                {
                    if (_encryptionService.IsEncrypted(_cachedSettings.OAuth.Microsoft.ClientSecret))
                        _cachedSettings.OAuth.Microsoft.ClientSecret = _encryptionService.Decrypt(_cachedSettings.OAuth.Microsoft.ClientSecret);
                    else
                        needsMigration = true;
                }

                Log.Debug("Settings loaded from disk");

                // Auto-migrate plaintext keys to DPAPI encryption
                if (needsMigration)
                {
                    Log.Information("Plaintext API keys detected — migrating to DPAPI encryption");
                    await SaveSettingsAsync(_cachedSettings);
                }
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
            // Serialize a copy with encrypted API keys for on-disk storage.
            // The in-memory AppSettings always retains plaintext values.
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var onDiskSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
            onDiskSettings.OpenAiApiKey = EncryptIfNotEmpty(settings.OpenAiApiKey);
            onDiskSettings.AnthropicApiKey = EncryptIfNotEmpty(settings.AnthropicApiKey);
            onDiskSettings.OAuth.Google.ClientSecret = EncryptIfNotEmpty(settings.OAuth.Google.ClientSecret) ?? string.Empty;
            onDiskSettings.OAuth.Microsoft.ClientSecret = EncryptIfNotEmpty(settings.OAuth.Microsoft.ClientSecret) ?? string.Empty;

            var onDiskJson = JsonSerializer.Serialize(onDiskSettings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, onDiskJson);
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

    /// <summary>
    /// Encrypts a non-empty, non-already-encrypted value using DPAPI.
    /// Returns null/empty unchanged; skips double-encryption.
    /// </summary>
    private string? EncryptIfNotEmpty(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (_encryptionService.IsEncrypted(value))
            return value;

        return _encryptionService.Encrypt(value);
    }
}

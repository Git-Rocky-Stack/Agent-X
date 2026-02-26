namespace AgentX.Core.Services.Settings;

public interface ISettingsService
{
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task<T?> GetValueAsync<T>(string key);
    Task SetValueAsync<T>(string key, T value);
}

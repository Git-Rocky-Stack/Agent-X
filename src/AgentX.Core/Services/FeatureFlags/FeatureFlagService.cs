using System.Collections.Concurrent;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.FeatureFlags;

/// <summary>
/// Feature flag service backed by <see cref="UserSettingsEntity"/> for persistence.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class FeatureFlagService : IFeatureFlagService
{
    private const string KeyPrefix = "feature_flag:";
    private readonly AgentXDbContext _db;
    private readonly ConcurrentDictionary<string, bool> _overrides = new();

    public FeatureFlagService(AgentXDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _db.UserSettings
                .Where(s => s.Key.StartsWith(KeyPrefix))
                .ToListAsync();

            foreach (var setting in settings)
            {
                var flagName = setting.Key[KeyPrefix.Length..];
                if (bool.TryParse(setting.Value, out var value))
                {
                    _overrides[flagName] = value;
                }
            }

            Log.Debug("Loaded {Count} feature flag overrides from database", _overrides.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load feature flag overrides from database");
        }
    }

    public bool IsEnabled(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        if (_overrides.TryGetValue(featureName, out var overrideValue))
            return overrideValue;

        var knownFlag = FeatureFlags.All.FirstOrDefault(f => f.Name == featureName);
        return knownFlag?.DefaultValue ?? false;
    }

    public bool IsEnabled(string featureName, bool defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        if (_overrides.TryGetValue(featureName, out var overrideValue))
            return overrideValue;

        var knownFlag = FeatureFlags.All.FirstOrDefault(f => f.Name == featureName);
        return knownFlag?.DefaultValue ?? defaultValue;
    }

    public async Task SetFlagAsync(string featureName, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        _overrides[featureName] = enabled;

        var key = KeyPrefix + featureName;
        var existing = await _db.UserSettings.FirstOrDefaultAsync(s => s.Key == key);

        if (existing is not null)
        {
            existing.Value = enabled.ToString();
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.UserSettings.Add(new UserSettingsEntity
            {
                Key = key,
                Value = enabled.ToString(),
                ValueType = "bool",
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        Log.Debug("Feature flag '{FlagName}' set to {Value}", featureName, enabled);
    }

    public IReadOnlyDictionary<string, bool> GetAllFlags()
    {
        var result = new Dictionary<string, bool>();

        foreach (var flag in FeatureFlags.All)
            result[flag.Name] = _overrides.GetValueOrDefault(flag.Name, flag.DefaultValue);

        foreach (var kvp in _overrides)
        {
            if (!result.ContainsKey(kvp.Key))
                result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    public async Task ResetFlagAsync(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        _overrides.TryRemove(featureName, out _);

        var key = KeyPrefix + featureName;
        var existing = await _db.UserSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is not null)
        {
            _db.UserSettings.Remove(existing);
            await _db.SaveChangesAsync();
        }

        Log.Debug("Feature flag '{FlagName}' reset to default", featureName);
    }
}

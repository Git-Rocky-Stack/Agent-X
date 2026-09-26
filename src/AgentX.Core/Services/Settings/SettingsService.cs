using System.Globalization;
using System.Text;
using System.Text.Json;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Security;
using AgentX.Core.Validation;
using Serilog;

namespace AgentX.Core.Services.Settings;

public class SettingsService : ISettingsService
{
    private const int ReadAttempts = 5;

    /// <summary>
    /// Validation rules that describe an incomplete setup rather than a broken value (for example
    /// a cloud provider selected before its key is pasted, or a key that could not be decrypted on
    /// this machine). They are logged but never block a save.
    /// </summary>
    private static readonly HashSet<string> AdvisoryFields = new(StringComparer.Ordinal)
    {
        nameof(AppSettings.OpenAiApiKey),
        nameof(AppSettings.AnthropicApiKey),
    };

    /// <summary>
    /// Secrets stored DPAPI-encrypted on disk and plaintext in memory. Each one is decrypted on
    /// its own, so one value that cannot be decrypted does not cost the others.
    /// </summary>
    private static readonly SecretField[] SecretFields =
    {
        new(nameof(AppSettings.OpenAiApiKey), s => s.OpenAiApiKey, (s, v) => s.OpenAiApiKey = v),
        new(nameof(AppSettings.AnthropicApiKey), s => s.AnthropicApiKey, (s, v) => s.AnthropicApiKey = v),
        // The web search provider key (Brave/Serper), same as other provider secrets.
        new(nameof(AppSettings.WebSearchApiKey), s => s.WebSearchApiKey, (s, v) => s.WebSearchApiKey = v),
        // The local REST API token (bearer secret for the browser extension).
        new(nameof(AppSettings.LocalApiToken), s => s.LocalApiToken, (s, v) => s.LocalApiToken = v),
        // OAuth client secrets.
        new("OAuth.Google.ClientSecret", s => s.OAuth.Google.ClientSecret, (s, v) => s.OAuth.Google.ClientSecret = v ?? string.Empty),
        new("OAuth.Microsoft.ClientSecret", s => s.OAuth.Microsoft.ClientSecret, (s, v) => s.OAuth.Microsoft.ClientSecret = v ?? string.Empty),
        // The archive password for scheduled backups.
        new("BackupSchedule.EncryptionPassword", s => s.BackupSchedule.EncryptionPassword, (s, v) => s.BackupSchedule.EncryptionPassword = v),
    };

    private readonly string _settingsPath;
    private readonly IDpapiEncryptionService _encryptionService;
    private readonly IValidator<AppSettings> _validator;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AppSettings? _cachedSettings;

    // Fields already invalid in the file on disk. A save may keep such a value (blocking it would
    // lock every caller out of saving anything) but may not introduce a new invalid field.
    private HashSet<string> _invalidFieldsOnDisk = new(StringComparer.Ordinal);

    // Set when the file could not be read and could not be copied aside at load time: the next
    // save then moves the unreadable file to this path instead of overwriting it.
    private string? _pendingUnreadableBackupPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SettingsService(IDpapiEncryptionService encryptionService, IValidator<AppSettings>? validator = null)
        : this(encryptionService, validator, DefaultSettingsPath())
    {
    }

    /// <summary>Test seam: a settings service rooted at an explicit file path.</summary>
    internal SettingsService(IDpapiEncryptionService encryptionService, IValidator<AppSettings>? validator, string settingsPath)
    {
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _validator = validator ?? new AppSettingsValidator();
        _settingsPath = settingsPath;

        Log.Information("Settings path: {SettingsPath}", _settingsPath);
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        if (_cachedSettings != null)
            return _cachedSettings;

        if (!File.Exists(_settingsPath))
        {
            _cachedSettings = new AppSettings();
            await SaveSettingsAsync(_cachedSettings).ConfigureAwait(false);
            Log.Information("Default settings created");
            return _cachedSettings;
        }

        AppSettings loaded;
        try
        {
            var json = await ReadSettingsTextAsync().ConfigureAwait(false);
            loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new JsonException("The settings file is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Never replace an unreadable file with defaults: keep it so nothing the user
            // configured is lost, and run on defaults for this session.
            var preservedAt = PreserveUnreadableFile();
            Log.Error(ex, "Settings file could not be read; it is preserved at {PreservedAt} and defaults are in use", preservedAt);
            _cachedSettings = new AppSettings();
            return _cachedSettings;
        }

        Normalize(loaded);
        var (needsMigration, undecryptable) = DecryptSecrets(loaded);
        _invalidFieldsOnDisk = InvalidFields(loaded);
        _cachedSettings = loaded;
        Log.Debug("Settings loaded from disk");

        if (undecryptable.Count > 0)
        {
            // The stored ciphertext is kept in a copy: it may still decrypt under the account or
            // machine that wrote it.
            var copy = TryCopySettingsFile("undecryptable");
            Log.Warning(
                "Could not decrypt {Fields}; those values were cleared and must be re-entered (original file kept at {Copy})",
                string.Join(", ", undecryptable), copy);
        }

        // Auto-migrate plaintext keys to DPAPI encryption
        if (needsMigration)
        {
            Log.Information("Plaintext API keys detected; migrating to DPAPI encryption");
            try
            {
                await WriteSettingsAsync(loaded).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not rewrite settings with encrypted keys; will retry on the next save");
            }
        }

        return loaded;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                EnsureSavable(settings);
            }
            catch (SettingsValidationException ex)
            {
                // Callers usually mutate the cached instance before saving it, so drop the cache:
                // the next read returns the last saved settings instead of the rejected values.
                if (ReferenceEquals(settings, _cachedSettings))
                    _cachedSettings = null;

                Log.Warning("Settings save rejected: {Reason}", ex.Message);
                throw;
            }

            await WriteSettingsCoreAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<T?> GetValueAsync<T>(string key)
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        var property = typeof(AppSettings).GetProperty(key);
        if (property == null) return default;
        return (T?)property.GetValue(settings);
    }

    public async Task SetValueAsync<T>(string key, T value)
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        var property = typeof(AppSettings).GetProperty(key);
        if (property == null) return;
        property.SetValue(settings, value);
        await SaveSettingsAsync(settings).ConfigureAwait(false);
    }

    /// <summary>Writes <paramref name="settings"/> without validation (used for load-time rewrites).</summary>
    private async Task WriteSettingsAsync(AppSettings settings)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteSettingsCoreAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteSettingsCoreAsync(AppSettings settings)
    {
        _cachedSettings = settings;

        try
        {
            // Serialize a copy with encrypted API keys for on-disk storage.
            // The in-memory AppSettings always retains plaintext values.
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var onDiskSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
            Normalize(onDiskSettings);
            foreach (var field in SecretFields)
                field.Set(onDiskSettings, EncryptIfNotEmpty(field.Get(settings)));

            var onDiskJson = JsonSerializer.Serialize(onDiskSettings, JsonOptions);
            await ReplaceSettingsFileAsync(onDiskJson).ConfigureAwait(false);
            _invalidFieldsOnDisk = InvalidFields(settings);
            Log.Debug("Settings saved to disk");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
            throw;
        }
    }

    /// <summary>
    /// Rejects values that are clearly invalid. Advisory rules and fields that were already
    /// invalid on disk are logged instead, so existing callers are never locked out of saving.
    /// </summary>
    private void EnsureSavable(AppSettings settings)
    {
        var result = _validator.Validate(settings);
        if (result.IsValid)
            return;

        var blocking = new List<ValidationError>();
        foreach (var error in result.Errors)
        {
            if (AdvisoryFields.Contains(error.FieldName) || _invalidFieldsOnDisk.Contains(error.FieldName))
                Log.Warning("Saving settings with an unresolved issue: {Message}", error.Message);
            else
                blocking.Add(error);
        }

        if (blocking.Count > 0)
            throw new SettingsValidationException(blocking);
    }

    private HashSet<string> InvalidFields(AppSettings settings)
    {
        try
        {
            return _validator.Validate(settings).Errors
                .Select(e => e.FieldName)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Settings validation failed unexpectedly");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Decrypts every secret on its own. A value that cannot be decrypted (DPAPI data written by
    /// another Windows account or machine, or a damaged value) is cleared and reported instead of
    /// discarding every other setting.
    /// </summary>
    private (bool NeedsMigration, List<string> Undecryptable) DecryptSecrets(AppSettings settings)
    {
        var needsMigration = false;
        var undecryptable = new List<string>();

        foreach (var field in SecretFields)
        {
            var stored = field.Get(settings);
            if (string.IsNullOrEmpty(stored))
                continue;

            if (!_encryptionService.IsEncrypted(stored))
            {
                // Plaintext keys are auto-migrated to DPAPI encryption.
                needsMigration = true;
                continue;
            }

            try
            {
                field.Set(settings, _encryptionService.Decrypt(stored));
            }
            catch (Exception ex)
            {
                field.Set(settings, null);
                undecryptable.Add(field.Name);
                Log.Warning(ex, "Could not decrypt the stored {Field}", field.Name);
            }
        }

        return (needsMigration, undecryptable);
    }

    /// <summary>A JSON null for a nested section would otherwise throw on first use.</summary>
    private static void Normalize(AppSettings settings)
    {
        settings.OAuth ??= new OAuthSettings();
        settings.OAuth.Google ??= new GoogleOAuthSettings();
        settings.OAuth.Microsoft ??= new MicrosoftOAuthSettings();
        settings.CalendarConnector ??= new CalendarSettings();
        settings.EmailConnector ??= new EmailSettings();
        settings.BackupSchedule ??= new BackupScheduleConfig();
    }

    private async Task<string> ReadSettingsTextAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < ReadAttempts)
            {
                // Usually a transient sharing violation (for example an antivirus scan).
                await Task.Delay(100 * attempt).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Writes the settings to a sibling temp file and swaps it in, so a crash or power loss
    /// mid-write leaves the previous file intact instead of a truncated one.
    /// </summary>
    private async Task ReplaceSettingsFileAsync(string json)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!;
        Directory.CreateDirectory(directory);
        var tempPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            // WriteThrough: the bytes reach the disk before the swap, so the new file is never an
            // empty shell after a power loss.
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, _pendingUnreadableBackupPath, ignoreMetadataErrors: true);
                if (_pendingUnreadableBackupPath is not null)
                    Log.Warning("The unreadable settings file was moved to {Path}", _pendingUnreadableBackupPath);
                _pendingUnreadableBackupPath = null;
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Copies an unreadable settings file to <c>settings.json.corrupt-&lt;UTC timestamp&gt;</c>.
    /// When even the copy fails, the next save moves the file there instead of overwriting it.
    /// </summary>
    private string PreserveUnreadableFile()
    {
        var copy = TryCopySettingsFile("corrupt");
        if (copy is not null)
            return copy;

        _pendingUnreadableBackupPath = BackupPath("corrupt");
        return _pendingUnreadableBackupPath;
    }

    private string? TryCopySettingsFile(string reason)
    {
        var target = BackupPath(reason);
        try
        {
            File.Copy(_settingsPath, target, overwrite: false);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not copy the settings file to {Target}", target);
            return null;
        }
    }

    private string BackupPath(string reason)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return $"{_settingsPath}.{reason}-{stamp}";
    }

    private static string DefaultSettingsPath()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX");

        Directory.CreateDirectory(appDataDir);
        return Path.Combine(appDataDir, "settings.json");
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

    private sealed record SecretField(string Name, Func<AppSettings, string?> Get, Action<AppSettings, string?> Set);
}

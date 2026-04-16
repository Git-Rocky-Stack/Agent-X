using System.Text.Json;

namespace AgentX.Core.Services.Plugins.Email.Models;

/// <summary>
/// Per-plugin settings for the Email Connector, persisted as JSON
/// in the plugin's data directory.
/// </summary>
public sealed class EmailSyncSettings
{
    /// <summary>
    /// Which folders to sync. Key = folder ID, Value = enabled.
    /// </summary>
    public Dictionary<string, bool> EnabledFolders { get; set; } = new()
    {
        ["INBOX"] = true,
    };

    /// <summary>
    /// How often to poll for new emails (minutes).
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum number of messages to fetch per sync cycle.
    /// </summary>
    public int MaxMessagesPerSync { get; set; } = 50;

    /// <summary>
    /// How many days back to sync on first connection.
    /// </summary>
    public int SyncDaysBack { get; set; } = 30;

    /// <summary>
    /// Whether to use AI to categorize emails during triage.
    /// </summary>
    public bool EnableAiCategorization { get; set; } = true;

    /// <summary>
    /// Custom prompt for AI categorization (if null, uses default).
    /// </summary>
    public string? CategorizationPrompt { get; set; }

    /// <summary>
    /// Whether to include full HTML body in indexed content.
    /// </summary>
    public bool IncludeHtmlBody { get; set; }

    /// <summary>
    /// Whether to include attachment names in indexed content.
    /// </summary>
    public bool IncludeAttachmentNames { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static EmailSyncSettings Load(string path)
    {
        if (!File.Exists(path))
            return new EmailSyncSettings();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EmailSyncSettings>(json, JsonOptions) ?? new EmailSyncSettings();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }
}
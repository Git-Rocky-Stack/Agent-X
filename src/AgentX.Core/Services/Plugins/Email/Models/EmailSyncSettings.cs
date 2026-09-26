using System.Text;
using System.Text.Json;
using Serilog;

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
    /// How many days back the first (full) sync of a folder reaches. Later syncs are
    /// incremental and read whatever changed since the previous one.
    /// </summary>
    public int SyncDaysBack { get; set; } = 30;

    /// <summary>
    /// Not applied. Messages are categorized by the rule-based
    /// <see cref="EmailTriageProcessor.Classify"/>, which does not use AI; the property is kept
    /// so existing settings files still load.
    /// </summary>
    public bool EnableAiCategorization { get; set; } = true;

    /// <summary>
    /// Not applied (no AI categorization exists; see <see cref="EnableAiCategorization"/>).
    /// </summary>
    public string? CategorizationPrompt { get; set; }

    /// <summary>
    /// Not applied. The plain-text body is indexed when the provider supplies one, otherwise
    /// the HTML body with its tags stripped; raw HTML is never indexed.
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

    /// <summary>
    /// Reads the settings file. A missing file gives defaults; so does an unreadable or corrupt
    /// one, which must not stop the email connector (and the calendar connector initialized
    /// after it) from starting. A corrupt file is kept as <c>{path}.corrupt</c> for inspection.
    /// </summary>
    public static EmailSyncSettings Load(string path)
    {
        if (!File.Exists(path))
            return new EmailSyncSettings();

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<EmailSyncSettings>().Warning(ex, "Could not read email sync settings at {Path}; using defaults", path);
            return new EmailSyncSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<EmailSyncSettings>(json, JsonOptions) ?? new EmailSyncSettings();
        }
        catch (JsonException ex)
        {
            Log.ForContext<EmailSyncSettings>().Warning(ex, "Email sync settings at {Path} are corrupt; using defaults", path);
            TryKeepCorruptFile(path);
            return new EmailSyncSettings();
        }
    }

    /// <summary>
    /// Writes the settings atomically: a sibling temporary file is written and flushed, then
    /// moved over the target, so a crash or a full disk mid-write leaves the previous file
    /// intact instead of a truncated one.
    /// </summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void TryKeepCorruptFile(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<EmailSyncSettings>().Debug(ex, "Could not set aside the corrupt settings file {Path}", path);
        }
    }

}

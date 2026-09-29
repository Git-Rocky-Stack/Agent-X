using System.Text;
using System.Text.Json;
using AgentX.Core.Helpers;
using Serilog;

namespace AgentX.Core.AI.Models;

/// <summary>
/// Pricing information for a specific AI model, defining the cost per 1,000
/// input and output tokens.
/// </summary>
public class ModelCostInfo
{
    public string ModelId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public double InputCostPer1KTokens { get; set; }
    public double OutputCostPer1KTokens { get; set; }

    /// <summary>
    /// Cost per 1,000 prompt-cache read tokens. When null, cache reads cost 10% of the input
    /// price (the Anthropic default).
    /// </summary>
    public double? CacheReadCostPer1KTokens { get; set; }
}

/// <summary>
/// Records a single usage event including the model used, token counts,
/// estimated cost, and timestamp.
/// </summary>
public class UsageRecord
{
    public string ModelId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>All prompt tokens, including prompt-cache writes and reads.</summary>
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Prompt tokens written to the provider's prompt cache (part of <see cref="InputTokens"/>).</summary>
    public int CacheCreationInputTokens { get; set; }

    /// <summary>Prompt tokens read from the provider's prompt cache (part of <see cref="InputTokens"/>).</summary>
    public int CacheReadInputTokens { get; set; }
    public double EstimatedCostUsd { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks AI usage costs across providers and models.
/// Records per-request token usage and calculates estimated costs
/// based on known model pricing.
/// </summary>
public interface ICostTracker
{
    /// <summary>
    /// Records a usage event with the given token counts and automatically
    /// calculates the estimated cost based on known model pricing.
    /// </summary>
    /// <param name="modelId">The model identifier used for the request.</param>
    /// <param name="providerId">The provider identifier (e.g. "openai", "anthropic").</param>
    /// <param name="inputTokens">Number of input/prompt tokens consumed.</param>
    /// <param name="outputTokens">Number of output/completion tokens generated.</param>
    void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens);

    /// <summary>
    /// Records a usage event that includes prompt-cache activity. <paramref name="inputTokens"/>
    /// are the uncached prompt tokens; cache writes are billed at 1.25 times the input price and
    /// cache reads at the model's cache-read price.
    /// </summary>
    void RecordUsage(
        string modelId,
        string providerId,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens,
        int cacheReadInputTokens);

    /// <summary>
    /// Gets the total estimated cost across all recorded usage.
    /// </summary>
    double GetTotalCostUsd();

    /// <summary>
    /// Gets the estimated cost for a specific time period.
    /// </summary>
    /// <param name="start">Start of the period (inclusive).</param>
    /// <param name="end">End of the period (inclusive).</param>
    double GetCostForPeriod(DateTime start, DateTime end);

    /// <summary>
    /// Gets the most recent usage records, ordered by timestamp descending.
    /// </summary>
    /// <param name="limit">Maximum number of records to return.</param>
    IReadOnlyList<UsageRecord> GetUsageHistory(int limit = 50);

    /// <summary>
    /// Gets the total number of input tokens consumed across all usage.
    /// </summary>
    int GetTotalInputTokens();

    /// <summary>
    /// Gets the total number of output tokens generated across all usage.
    /// </summary>
    int GetTotalOutputTokens();
}

/// <summary>
/// Thread-safe implementation of <see cref="ICostTracker"/>.
/// Maintains a running log of usage records and provides cost calculations
/// based on known per-model pricing data for OpenAI and Anthropic models.
/// Local models (built-in and Ollama) are tracked as zero-cost.
/// <para>
/// The records outlive a restart: they are kept in <see cref="HistoryFileName"/> in the app data
/// folder, loaded when the tracker is created and written atomically (a temporary file moved over
/// the old one) a moment after new usage, and when the app shuts down. The history is bounded: a
/// record is kept for <see cref="HistoryRetention"/>, and at most <see cref="MaxStoredRecords"/> of
/// them. What a dropped record cost and used is carried into the totals, so the total cost and
/// token counts cover all tracked usage while <see cref="GetCostForPeriod"/> and
/// <see cref="GetUsageHistory"/> see the kept records only. Totals before this history existed
/// were never saved and are not included.
/// </para>
/// </summary>
public class CostTracker : ICostTracker, IDisposable
{
    private const double CacheWriteMultiplier = 1.25;
    private const double DefaultCacheReadMultiplier = 0.1;

    /// <summary>The usage history file, in the app data folder.</summary>
    public const string HistoryFileName = "usage-history.json";

    /// <summary>How long a usage record is kept.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(90);

    /// <summary>The most usage records kept, the newest ones, whatever their age.</summary>
    public const int MaxStoredRecords = 20_000;

    /// <summary>New usage is saved this long after the first unsaved record, so a burst is one write.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions HistoryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly List<UsageRecord> _records = new();
    private readonly object _lock = new();

    // Persistence; a null path keeps the usage in memory only.
    private readonly string? _historyPath;
    private readonly Func<DateTime> _utcNow;
    private readonly int _maxStoredRecords;
    private readonly Timer? _saveTimer;
    private readonly object _saveLock = new();
    private CarriedUsageTotals _carriedOver = new();
    private bool _dirty;
    private bool _saveScheduled;
    private bool _persistenceDisabled;
    private bool _disposed;

    private static ILogger Logger => Log.ForContext<CostTracker>();

    /// <summary>
    /// Keeps the usage history in <see cref="HistoryFileName"/> in the app data folder and loads
    /// what an earlier session saved there.
    /// </summary>
    public CostTracker()
        : this(Path.Combine(PathHelper.GetAppDataPath(), HistoryFileName))
    {
    }

    /// <param name="historyFilePath">The history file, or null to keep usage in memory only.</param>
    /// <param name="utcNow">The clock that stamps and ages records; the real one unless a test sets it.</param>
    /// <param name="maxStoredRecords">The most records kept; <see cref="MaxStoredRecords"/> unless a test sets it.</param>
    internal CostTracker(string? historyFilePath, Func<DateTime>? utcNow = null, int maxStoredRecords = MaxStoredRecords)
    {
        _historyPath = historyFilePath;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _maxStoredRecords = maxStoredRecords;

        if (_historyPath is not null)
        {
            _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
            Load();
        }
    }

    /// <summary>
    /// Known pricing for cloud models, per 1,000 tokens (as of 2026-09). Model ids with a date
    /// or version suffix match their base entry by longest prefix, so "gpt-4o-mini-2024-07-18"
    /// is priced as gpt-4o-mini, never as gpt-4o. Local models are not listed and cost nothing.
    /// </summary>
    private static readonly Dictionary<string, ModelCostInfo> KnownCosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // OpenAI models
            ["gpt-4o"] = OpenAi("gpt-4o", 0.0025, 0.01),
            ["gpt-4o-mini"] = OpenAi("gpt-4o-mini", 0.00015, 0.0006),
            ["gpt-4-turbo"] = OpenAi("gpt-4-turbo", 0.01, 0.03),
            ["o1"] = OpenAi("o1", 0.015, 0.06),
            ["o1-mini"] = OpenAi("o1-mini", 0.003, 0.012),
            ["o3-mini"] = OpenAi("o3-mini", 0.0011, 0.0044),

            // Anthropic models (input / output per 1K tokens)
            ["claude-fable-5-1"] = Anthropic("claude-fable-5-1", 0.010, 0.050, cacheRead: 0.00025),
            ["claude-fable-5"] = Anthropic("claude-fable-5", 0.010, 0.050),
            ["claude-opus-5-5"] = Anthropic("claude-opus-5-5", 0.004, 0.020, cacheRead: 0.0002),
            ["claude-opus-5"] = Anthropic("claude-opus-5", 0.005, 0.025),
            ["claude-opus-4-8"] = Anthropic("claude-opus-4-8", 0.005, 0.025),
            ["claude-opus-4-7"] = Anthropic("claude-opus-4-7", 0.005, 0.025),
            ["claude-opus-4-6"] = Anthropic("claude-opus-4-6", 0.005, 0.025),
            ["claude-opus-4-5"] = Anthropic("claude-opus-4-5", 0.005, 0.025),
            ["claude-opus-4-1"] = Anthropic("claude-opus-4-1", 0.015, 0.075),
            ["claude-opus-4-0"] = Anthropic("claude-opus-4-0", 0.015, 0.075),
            ["claude-opus-4-20250514"] = Anthropic("claude-opus-4-20250514", 0.015, 0.075),
            ["claude-sonnet-5"] = Anthropic("claude-sonnet-5", 0.002, 0.010),
            ["claude-sonnet-4-6"] = Anthropic("claude-sonnet-4-6", 0.003, 0.015),
            ["claude-sonnet-4-5"] = Anthropic("claude-sonnet-4-5", 0.003, 0.015),
            ["claude-sonnet-4-0"] = Anthropic("claude-sonnet-4-0", 0.003, 0.015),
            ["claude-sonnet-4-20250514"] = Anthropic("claude-sonnet-4-20250514", 0.003, 0.015),
            ["claude-haiku-4-5"] = Anthropic("claude-haiku-4-5", 0.001, 0.005),
            ["claude-3-5-sonnet-20241022"] = Anthropic("claude-3-5-sonnet-20241022", 0.003, 0.015),
            ["claude-3-5-haiku-20241022"] = Anthropic("claude-3-5-haiku-20241022", 0.0008, 0.004),
        };

    private static ModelCostInfo OpenAi(string id, double input, double output) =>
        new() { ModelId = id, ProviderId = "openai", InputCostPer1KTokens = input, OutputCostPer1KTokens = output };

    private static ModelCostInfo Anthropic(string id, double input, double output, double? cacheRead = null) =>
        new()
        {
            ModelId = id,
            ProviderId = "anthropic",
            InputCostPer1KTokens = input,
            OutputCostPer1KTokens = output,
            CacheReadCostPer1KTokens = cacheRead
        };

    /// <inheritdoc />
    public void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens) =>
        RecordUsage(modelId, providerId, inputTokens, outputTokens, 0, 0);

    /// <inheritdoc />
    public void RecordUsage(
        string modelId,
        string providerId,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens,
        int cacheReadInputTokens)
    {
        inputTokens = Math.Max(0, inputTokens);
        outputTokens = Math.Max(0, outputTokens);
        cacheCreationInputTokens = Math.Max(0, cacheCreationInputTokens);
        cacheReadInputTokens = Math.Max(0, cacheReadInputTokens);

        var cost = CalculateCost(
            modelId, providerId, inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens);

        lock (_lock)
        {
            _records.Add(new UsageRecord
            {
                ModelId = modelId,
                ProviderId = providerId,
                InputTokens = inputTokens + cacheCreationInputTokens + cacheReadInputTokens,
                OutputTokens = outputTokens,
                CacheCreationInputTokens = cacheCreationInputTokens,
                CacheReadInputTokens = cacheReadInputTokens,
                EstimatedCostUsd = cost,
                Timestamp = _utcNow()
            });

            ScheduleSave();
        }
    }

    /// <inheritdoc />
    public double GetTotalCostUsd()
    {
        lock (_lock)
        {
            return _carriedOver.EstimatedCostUsd + _records.Sum(r => r.EstimatedCostUsd);
        }
    }

    /// <inheritdoc />
    public double GetCostForPeriod(DateTime start, DateTime end)
    {
        lock (_lock)
        {
            return _records
                .Where(r => r.Timestamp >= start && r.Timestamp <= end)
                .Sum(r => r.EstimatedCostUsd);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UsageRecord> GetUsageHistory(int limit = 50)
    {
        lock (_lock)
        {
            return _records
                .OrderByDescending(r => r.Timestamp)
                .Take(limit)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <inheritdoc />
    /// <remarks>Summed as a long and capped at <see cref="int.MaxValue"/>, which a saved history can reach.</remarks>
    public int GetTotalInputTokens()
    {
        lock (_lock)
        {
            return CapToInt(_carriedOver.InputTokens + _records.Sum(r => (long)r.InputTokens));
        }
    }

    /// <inheritdoc />
    /// <remarks>Summed as a long and capped at <see cref="int.MaxValue"/>, which a saved history can reach.</remarks>
    public int GetTotalOutputTokens()
    {
        lock (_lock)
        {
            return CapToInt(_carriedOver.OutputTokens + _records.Sum(r => (long)r.OutputTokens));
        }
    }

    private static int CapToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    /// <summary>Saves what is unsaved. The app disposes the tracker when it shuts down.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _saveTimer?.Dispose();
        SaveNow();
    }

    // -- Usage history file --------------------------------------------------

    /// <summary>
    /// Marks the records unsaved and, unless a save is already due, starts the save delay. Called
    /// under the lock.
    /// </summary>
    private void ScheduleSave()
    {
        _dirty = true;
        if (_saveTimer is null || _disposed || _persistenceDisabled || _saveScheduled)
            return;

        _saveScheduled = true;
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Writes the history file now when something is unsaved: records past the retention window
    /// or the record cap are folded into the carried totals first. Never throws; a failed write
    /// is retried with the next save.
    /// </summary>
    internal void SaveNow()
    {
        if (_historyPath is null)
            return;

        try
        {
            lock (_saveLock)
            {
                UsageHistoryDocument document;
                lock (_lock)
                {
                    _saveScheduled = false;
                    if (!_dirty || _persistenceDisabled)
                        return;

                    Prune(_utcNow());
                    document = new UsageHistoryDocument
                    {
                        CarriedOver = _carriedOver.Copy(),
                        Records = _records.ToList(),
                    };
                    _dirty = false;
                }

                try
                {
                    WriteAtomically(_historyPath, JsonSerializer.Serialize(document, HistoryJsonOptions));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lock (_lock)
                    {
                        _dirty = true;
                    }

                    Logger.Warning(ex, "Could not save the usage history to {Path}; it is retried with the next save", _historyPath);
                }
            }
        }
        catch (Exception ex)
        {
            // A timer callback must not throw.
            Logger.Error(ex, "Saving the usage history failed");
        }
    }

    /// <summary>
    /// Loads the records and carried totals an earlier session saved. A corrupt file is kept as
    /// <c>{path}.corrupt</c> and a new history starts; a file that cannot be read at all (locked,
    /// no access) is left alone, and this session keeps its usage in memory rather than replace it.
    /// </summary>
    private void Load()
    {
        var path = _historyPath!;
        if (!File.Exists(path))
            return;

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _persistenceDisabled = true;
            Logger.Warning(ex, "Could not read the usage history at {Path}; this session's usage is not saved", path);
            return;
        }

        UsageHistoryDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<UsageHistoryDocument>(json, HistoryJsonOptions);
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "The usage history at {Path} is corrupt; a new history starts", path);
            KeepCorruptFile(path);
            return;
        }

        if (document is null)
            return;

        _carriedOver = document.CarriedOver ?? new CarriedUsageTotals();
        _records.AddRange((document.Records ?? new List<UsageRecord>())
            .Where(record => record is not null)
            .OrderBy(record => record.Timestamp));

        // Records that aged out since the last session are folded in with the next save.
        if (Prune(_utcNow()))
            _dirty = true;

        Logger.Debug("Loaded {Count} usage records from {Path}", _records.Count, path);
    }

    /// <summary>
    /// Drops the records older than the retention window, then the oldest beyond the record cap,
    /// and adds what they cost and used to the carried totals. Called under the lock; the records
    /// are in time order. Returns whether anything was dropped.
    /// </summary>
    private bool Prune(DateTime nowUtc)
    {
        var cutoff = nowUtc - HistoryRetention;
        var drop = 0;
        while (drop < _records.Count &&
               (_records[drop].Timestamp < cutoff || _records.Count - drop > _maxStoredRecords))
        {
            var record = _records[drop];
            _carriedOver.EstimatedCostUsd += record.EstimatedCostUsd;
            _carriedOver.InputTokens += record.InputTokens;
            _carriedOver.OutputTokens += record.OutputTokens;
            drop++;
        }

        if (drop == 0)
            return false;

        _records.RemoveRange(0, drop);
        return true;
    }

    /// <summary>
    /// Writes a sibling temporary file, flushed to disk, and moves it over the history, so a crash
    /// or a full disk mid-write leaves the previous file whole.
    /// </summary>
    private static void WriteAtomically(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json));
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void KeepCorruptFile(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Debug(ex, "Could not set aside the corrupt usage history {Path}", path);
        }
    }

    /// <summary>
    /// Finds the price entry for a model: an exact id, otherwise the longest known id the model
    /// id starts with. Only entries of the same provider match, so a local model that happens to
    /// share a cloud model's name is never billed. Returns null for local and unknown models.
    /// </summary>
    internal static ModelCostInfo? FindPricing(string modelId, string? providerId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        var id = modelId.Trim();
        ModelCostInfo? best = null;

        if (KnownCosts.TryGetValue(id, out var exact))
        {
            best = exact;
        }
        else
        {
            foreach (var (key, info) in KnownCosts)
            {
                if (id.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                    (best is null || key.Length > best.ModelId.Length))
                {
                    best = info;
                }
            }
        }

        if (best is not null && !string.IsNullOrEmpty(providerId) &&
            !string.Equals(best.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return best;
    }

    /// <summary>
    /// Calculates the estimated cost for a request based on the model's known pricing.
    /// Returns 0 for local/unknown models.
    /// </summary>
    private static double CalculateCost(
        string modelId,
        string providerId,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens,
        int cacheReadInputTokens)
    {
        var info = FindPricing(modelId, providerId);
        if (info is null)
            return 0.0;

        var cacheReadPrice = info.CacheReadCostPer1KTokens ?? info.InputCostPer1KTokens * DefaultCacheReadMultiplier;

        return (inputTokens / 1000.0 * info.InputCostPer1KTokens) +
               (cacheCreationInputTokens / 1000.0 * info.InputCostPer1KTokens * CacheWriteMultiplier) +
               (cacheReadInputTokens / 1000.0 * cacheReadPrice) +
               (outputTokens / 1000.0 * info.OutputCostPer1KTokens);
    }
}

/// <summary>The usage history file: the kept records and the totals of those dropped from it.</summary>
internal sealed class UsageHistoryDocument
{
    public int Version { get; set; } = 1;

    public CarriedUsageTotals? CarriedOver { get; set; }

    public List<UsageRecord>? Records { get; set; }
}

/// <summary>What the usage records dropped from the history (by age or by the record cap) cost and used.</summary>
internal sealed class CarriedUsageTotals
{
    public double EstimatedCostUsd { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public CarriedUsageTotals Copy() => new()
    {
        EstimatedCostUsd = EstimatedCostUsd,
        InputTokens = InputTokens,
        OutputTokens = OutputTokens,
    };
}

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
/// Thread-safe in-memory implementation of <see cref="ICostTracker"/>.
/// Maintains a running log of usage records and provides cost calculations
/// based on known per-model pricing data for OpenAI and Anthropic models.
/// Local models (built-in and Ollama) are tracked as zero-cost.
/// </summary>
public class CostTracker : ICostTracker
{
    private const double CacheWriteMultiplier = 1.25;
    private const double DefaultCacheReadMultiplier = 0.1;

    private readonly List<UsageRecord> _records = new();
    private readonly object _lock = new();

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
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <inheritdoc />
    public double GetTotalCostUsd()
    {
        lock (_lock)
        {
            return _records.Sum(r => r.EstimatedCostUsd);
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
    public int GetTotalInputTokens()
    {
        lock (_lock)
        {
            return _records.Sum(r => r.InputTokens);
        }
    }

    /// <inheritdoc />
    public int GetTotalOutputTokens()
    {
        lock (_lock)
        {
            return _records.Sum(r => r.OutputTokens);
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

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
}

/// <summary>
/// Records a single usage event including the model used, token counts,
/// estimated cost, and timestamp.
/// </summary>
public class UsageRecord
{
    public string ModelId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
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
/// Local models (Ollama) are tracked as zero-cost.
/// </summary>
public class CostTracker : ICostTracker
{
    private readonly List<UsageRecord> _records = new();
    private readonly object _lock = new();

    /// <summary>
    /// Known pricing for popular cloud models.
    /// Costs are per 1,000 tokens as of early 2026.
    /// Local Ollama models are not listed and default to zero cost.
    /// </summary>
    private static readonly Dictionary<string, ModelCostInfo> KnownCosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // OpenAI models
            ["gpt-4o"] = new()
            {
                ModelId = "gpt-4o",
                ProviderId = "openai",
                InputCostPer1KTokens = 0.0025,
                OutputCostPer1KTokens = 0.01
            },
            ["gpt-4o-mini"] = new()
            {
                ModelId = "gpt-4o-mini",
                ProviderId = "openai",
                InputCostPer1KTokens = 0.00015,
                OutputCostPer1KTokens = 0.0006
            },
            ["gpt-4-turbo"] = new()
            {
                ModelId = "gpt-4-turbo",
                ProviderId = "openai",
                InputCostPer1KTokens = 0.01,
                OutputCostPer1KTokens = 0.03
            },
            ["o1"] = new()
            {
                ModelId = "o1",
                ProviderId = "openai",
                InputCostPer1KTokens = 0.015,
                OutputCostPer1KTokens = 0.06
            },
            ["o1-mini"] = new()
            {
                ModelId = "o1-mini",
                ProviderId = "openai",
                InputCostPer1KTokens = 0.003,
                OutputCostPer1KTokens = 0.012
            },
            ["o3-mini"] = new()
            {
                ModelId = "o3-mini",
                ProviderId = "openai",
                InputCostPer1KTokens = 0.0011,
                OutputCostPer1KTokens = 0.0044
            },

            // Anthropic models
            ["claude-opus-4-20250514"] = new()
            {
                ModelId = "claude-opus-4-20250514",
                ProviderId = "anthropic",
                InputCostPer1KTokens = 0.015,
                OutputCostPer1KTokens = 0.075
            },
            ["claude-sonnet-4-20250514"] = new()
            {
                ModelId = "claude-sonnet-4-20250514",
                ProviderId = "anthropic",
                InputCostPer1KTokens = 0.003,
                OutputCostPer1KTokens = 0.015
            },
            ["claude-haiku-4-5-20251001"] = new()
            {
                ModelId = "claude-haiku-4-5-20251001",
                ProviderId = "anthropic",
                InputCostPer1KTokens = 0.0008,
                OutputCostPer1KTokens = 0.004
            },
            ["claude-3-5-sonnet-20241022"] = new()
            {
                ModelId = "claude-3-5-sonnet-20241022",
                ProviderId = "anthropic",
                InputCostPer1KTokens = 0.003,
                OutputCostPer1KTokens = 0.015
            },
            ["claude-3-5-haiku-20241022"] = new()
            {
                ModelId = "claude-3-5-haiku-20241022",
                ProviderId = "anthropic",
                InputCostPer1KTokens = 0.0008,
                OutputCostPer1KTokens = 0.004
            },
        };

    /// <inheritdoc />
    public void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens)
    {
        var cost = CalculateCost(modelId, inputTokens, outputTokens);

        lock (_lock)
        {
            _records.Add(new UsageRecord
            {
                ModelId = modelId,
                ProviderId = providerId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
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
    /// Calculates the estimated cost for a request based on the model's known pricing.
    /// Uses a prefix-match strategy so versioned model IDs (e.g. "gpt-4o-2024-08-06")
    /// still match the base pricing entry ("gpt-4o").
    /// Returns 0 for local/unknown models.
    /// </summary>
    private static double CalculateCost(string modelId, int inputTokens, int outputTokens)
    {
        // Try exact match first
        if (KnownCosts.TryGetValue(modelId, out var exactInfo))
        {
            return (inputTokens / 1000.0 * exactInfo.InputCostPer1KTokens) +
                   (outputTokens / 1000.0 * exactInfo.OutputCostPer1KTokens);
        }

        // Try prefix/contains match for versioned model IDs
        foreach (var (key, info) in KnownCosts)
        {
            if (modelId.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return (inputTokens / 1000.0 * info.InputCostPer1KTokens) +
                       (outputTokens / 1000.0 * info.OutputCostPer1KTokens);
            }
        }

        // Unknown model or local model (Ollama) — free
        return 0.0;
    }
}

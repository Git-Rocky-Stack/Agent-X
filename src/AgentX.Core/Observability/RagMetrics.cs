using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace AgentX.Core.Observability;

/// <summary>
/// Metrics collector for tracking RAG pipeline performance and quality.
/// Provides structured telemetry for monitoring and alerting.
/// </summary>
public sealed class RagMetrics : IRagMetrics
{
    private readonly ILogger _log;
    private readonly object _lock = new();

    // Search metrics
    private long _semanticSearchCount;
    private long _keywordSearchCount;
    private long _hybridSearchCount;
    private long _cacheHits;
    private long _cacheMisses;

    // Performance metrics (microseconds)
    private long _totalSearchLatencyUs;
    private long _maxSearchLatencyUs;

    // Quality metrics (0-1 scale)
    private double _avgContextRelevance;
    private double _avgFaithfulness;
    private double _avgAnswerRelevance;
    private long _evaluationCount;

    // Resource metrics
    private long _totalTokensProcessed;
    private long _totalChunksRetrieved;

    public RagMetrics(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public void RecordSearch(SearchMetrics metrics)
    {
        lock (_lock)
        {
            switch (metrics.SearchType)
            {
                case SearchType.Semantic:
                    _semanticSearchCount++;
                    break;
                case SearchType.Keyword:
                    _keywordSearchCount++;
                    break;
                case SearchType.Hybrid:
                    _hybridSearchCount++;
                    break;
            }

            _cacheHits += metrics.CacheHits;
            _cacheMisses += metrics.CacheMisses;
            _totalSearchLatencyUs += metrics.LatencyUs;
            _maxSearchLatencyUs = Math.Max(_maxSearchLatencyUs, metrics.LatencyUs);
            _totalChunksRetrieved += metrics.ResultsCount;
        }

        _log.Debug("Search recorded: Type={Type}, LatencyMs={Latency:F2}, Results={Results}",
            metrics.SearchType, metrics.LatencyUs / 1000.0, metrics.ResultsCount);
    }

    /// <inheritdoc />
    public void RecordEvaluation(EvaluationMetrics metrics)
    {
        lock (_lock)
        {
            _evaluationCount++;
            var n = (double)_evaluationCount;

            // Rolling average
            _avgContextRelevance = RollingAverage(_avgContextRelevance, metrics.ContextRelevance, n);
            _avgFaithfulness = RollingAverage(_avgFaithfulness, metrics.Faithfulness, n);
            _avgAnswerRelevance = RollingAverage(_avgAnswerRelevance, metrics.AnswerRelevance, n);
        }

        _log.Information("Evaluation: Context={CR:F2}, Faithfulness={F:F2}, Answer={AR:F2}",
            metrics.ContextRelevance, metrics.Faithfulness, metrics.AnswerRelevance);
    }

    /// <inheritdoc />
    public void RecordTokensProcessed(int tokenCount)
    {
        lock (_lock)
        {
            _totalTokensProcessed += tokenCount;
        }
    }

    /// <inheritdoc />
    public RagMetricsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var totalSearches = _semanticSearchCount + _keywordSearchCount + _hybridSearchCount;
            var avgLatencyMs = totalSearches > 0 ? _totalSearchLatencyUs / totalSearches / 1000.0 : 0;
            var cacheHitRate = (_cacheHits + _cacheMisses) > 0
                ? (double)_cacheHits / (_cacheHits + _cacheMisses)
                : 0;

            return new RagMetricsSnapshot
            {
                // Search metrics
                SemanticSearchCount = _semanticSearchCount,
                KeywordSearchCount = _keywordSearchCount,
                HybridSearchCount = _hybridSearchCount,
                TotalSearches = totalSearches,
                CacheHitRate = cacheHitRate,
                AverageSearchLatencyMs = avgLatencyMs,
                MaxSearchLatencyMs = _maxSearchLatencyUs / 1000.0,
                TotalChunksRetrieved = _totalChunksRetrieved,

                // Quality metrics
                AverageContextRelevance = _avgContextRelevance,
                AverageFaithfulness = _avgFaithfulness,
                AverageAnswerRelevance = _avgAnswerRelevance,
                EvaluationCount = _evaluationCount,

                // Resource metrics
                TotalTokensProcessed = _totalTokensProcessed,

                // Timestamp
                SnapshotAt = DateTime.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_lock)
        {
            _semanticSearchCount = 0;
            _keywordSearchCount = 0;
            _hybridSearchCount = 0;
            _cacheHits = 0;
            _cacheMisses = 0;
            _totalSearchLatencyUs = 0;
            _maxSearchLatencyUs = 0;
            _totalChunksRetrieved = 0;

            _avgContextRelevance = 0;
            _avgFaithfulness = 0;
            _avgAnswerRelevance = 0;
            _evaluationCount = 0;

            _totalTokensProcessed = 0;
        }

        _log.Information("Metrics reset");
    }

    private static double RollingAverage(double current, double newValue, double count)
        => (current * (count - 1) + newValue) / count;
}

/// <summary>
/// Interface for collecting and reporting RAG pipeline metrics.
/// </summary>
public interface IRagMetrics
{
    /// <summary>
    /// Records a search operation with its performance metrics.
    /// </summary>
    void RecordSearch(SearchMetrics metrics);

    /// <summary>
    /// Records an RAG evaluation with quality scores.
    /// </summary>
    void RecordEvaluation(EvaluationMetrics metrics);

    /// <summary>
    /// Records token processing for resource tracking.
    /// </summary>
    void RecordTokensProcessed(int tokenCount);

    /// <summary>
    /// Gets a snapshot of current metrics.
    /// </summary>
    RagMetricsSnapshot GetSnapshot();

    /// <summary>
    /// Resets all metrics to zero.
    /// </summary>
    void Reset();
}

/// <summary>
/// Metrics for a single search operation.
/// </summary>
public class SearchMetrics
{
    /// <summary>Type of search performed.</summary>
    public SearchType SearchType { get; set; }

    /// <summary>Number of results returned.</summary>
    public int ResultsCount { get; set; }

    /// <summary>Latency in microseconds.</summary>
    public long LatencyUs { get; set; }

    /// <summary>Number of cache hits during this search.</summary>
    public long CacheHits { get; set; }

    /// <summary>Number of cache misses during this search.</summary>
    public long CacheMisses { get; set; }
}

/// <summary>
/// Type of search operation.
/// </summary>
public enum SearchType
{
    Semantic,
    Keyword,
    Hybrid
}

/// <summary>
/// Quality metrics from RAG evaluation.
/// </summary>
public class EvaluationMetrics
{
    /// <summary>Context relevance score (0-1).</summary>
    public double ContextRelevance { get; set; }

    /// <summary>Faithfulness score (0-1).</summary>
    public double Faithfulness { get; set; }

    /// <summary>Answer relevance score (0-1).</summary>
    public double AnswerRelevance { get; set; }
}

/// <summary>
/// Snapshot of RAG metrics at a point in time.
/// </summary>
public class RagMetricsSnapshot
{
    // Search metrics
    public long SemanticSearchCount { get; set; }
    public long KeywordSearchCount { get; set; }
    public long HybridSearchCount { get; set; }
    public long TotalSearches { get; set; }
    public double CacheHitRate { get; set; }
    public double AverageSearchLatencyMs { get; set; }
    public double MaxSearchLatencyMs { get; set; }
    public long TotalChunksRetrieved { get; set; }

    // Quality metrics
    public double AverageContextRelevance { get; set; }
    public double AverageFaithfulness { get; set; }
    public double AverageAnswerRelevance { get; set; }
    public long EvaluationCount { get; set; }

    // Resource metrics
    public long TotalTokensProcessed { get; set; }

    // Metadata
    public DateTime SnapshotAt { get; set; }
}

/// <summary>
/// Extension methods for recording metrics with minimal code.
/// </summary>
public static class MetricsExtensions
{
    /// <summary>
    /// Records search metrics using a stopwatch for automatic latency measurement.
    /// </summary>
    public static void RecordSearch(this IRagMetrics metrics, SearchType type, int resultsCount,
        long cacheHits, long cacheMisses, Stopwatch stopwatch)
    {
        metrics.RecordSearch(new SearchMetrics
        {
            SearchType = type,
            ResultsCount = resultsCount,
            LatencyUs = stopwatch.Elapsed.Ticks / 10, // Ticks to microseconds (1 tick = 100ns)
            CacheHits = cacheHits,
            CacheMisses = cacheMisses
        });
    }

    /// <summary>
    /// Records search metrics synchronously with latency measurement.
    /// </summary>
    public static T RecordSearch<T>(this IRagMetrics metrics, SearchType type, Func<T> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            // Note: Cache metrics would need to be passed in or tracked separately
            metrics.RecordSearch(new SearchMetrics
            {
                SearchType = type,
                ResultsCount = 0, // Would need to be tracked
                LatencyUs = sw.Elapsed.Ticks / 10, // Ticks to microseconds (1 tick = 100ns)
                CacheHits = 0,
                CacheMisses = 0
            });
        }
    }
}

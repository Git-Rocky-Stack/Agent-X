namespace AgentX.Core.Services.Analytics.Models;

/// <summary>
/// Top-level aggregate summary of Agent-X usage across all feature areas.
/// </summary>
public sealed record AnalyticsSummary
{
    /// <summary>Total number of conversation sessions ever started.</summary>
    public int TotalConversations { get; init; }

    /// <summary>Total number of messages sent and received across all conversations.</summary>
    public int TotalMessages { get; init; }

    /// <summary>Cumulative token count across all conversations.</summary>
    public long TotalTokensUsed { get; init; }

    /// <summary>Total number of documents in the knowledge vault.</summary>
    public int TotalDocuments { get; init; }

    /// <summary>Total number of searches performed.</summary>
    public int TotalSearches { get; init; }

    /// <summary>Total number of workflow runs executed.</summary>
    public int TotalWorkflowRuns { get; init; }

    /// <summary>Average AI response generation time in milliseconds across all assistant messages.</summary>
    public double AverageResponseTimeMs { get; init; }

    /// <summary>Average token count per assistant message.</summary>
    public double AverageTokensPerMessage { get; init; }

    /// <summary>Number of documents with IndexingStatus == "completed".</summary>
    public int DocumentsIndexedCount { get; init; }

    /// <summary>Number of documents with IndexingStatus == "pending" or "processing".</summary>
    public int DocumentsPendingCount { get; init; }
}

/// <summary>
/// Represents an aggregated activity count for a single calendar day.
/// Used for trend charts spanning conversations, documents, and searches.
/// </summary>
public sealed record DailyMetric
{
    /// <summary>The date this metric corresponds to (UTC, time component is midnight).</summary>
    public DateTime Date { get; init; }

    /// <summary>The event count for this day.</summary>
    public int Count { get; init; }

    /// <summary>Human-readable label for the X-axis (e.g., "Mar 1").</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// Aggregated usage statistics for a single AI model.
/// </summary>
public sealed record ModelUsageMetric
{
    /// <summary>The model identifier string (e.g., "llama3.1:8b").</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Number of conversations that used this model.</summary>
    public int ConversationCount { get; init; }

    /// <summary>Total tokens consumed by messages generated with this model.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Percentage of total conversations that used this model (0–100).</summary>
    public double Percentage { get; init; }
}

/// <summary>
/// Aggregated statistics for a single document file type.
/// </summary>
public sealed record FileTypeMetric
{
    /// <summary>The file type extension without the leading dot (e.g., "pdf", "docx").</summary>
    public string FileType { get; init; } = string.Empty;

    /// <summary>Number of documents of this type.</summary>
    public int Count { get; init; }

    /// <summary>Combined file size of all documents of this type in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Percentage of total document count (0–100).</summary>
    public double Percentage { get; init; }
}

/// <summary>
/// Detailed inference performance statistics derived from message generation timing data.
/// </summary>
public sealed record PerformanceMetrics
{
    /// <summary>Mean response time across all timed assistant messages, in milliseconds.</summary>
    public double AverageResponseTimeMs { get; init; }

    /// <summary>Median (P50) response time, in milliseconds.</summary>
    public double MedianResponseTimeMs { get; init; }

    /// <summary>95th-percentile response time, in milliseconds.</summary>
    public double P95ResponseTimeMs { get; init; }

    /// <summary>Minimum observed response time, in milliseconds.</summary>
    public double FastestResponseMs { get; init; }

    /// <summary>Maximum observed response time, in milliseconds.</summary>
    public double SlowestResponseMs { get; init; }

    /// <summary>Sum of all recorded generation times, in milliseconds.</summary>
    public double TotalInferenceTimeMs { get; init; }

    /// <summary>Average tokens generated per second (TotalTokens / TotalInferenceTimeSec).</summary>
    public double AverageTokensPerSecond { get; init; }
}

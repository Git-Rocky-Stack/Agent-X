namespace AgentX.Core.Services.Feedback.Models;

/// <summary>
/// Aggregate statistics summarising all feedback stored in the database.
/// Returned by <see cref="IFeedbackService.GetFeedbackSummaryAsync"/>.
/// </summary>
public sealed class FeedbackSummary
{
    /// <summary>Total number of feedback records (all ratings).</summary>
    public int TotalFeedback { get; init; }

    /// <summary>Number of messages rated <c>"positive"</c>.</summary>
    public int PositiveCount { get; init; }

    /// <summary>Number of messages rated <c>"negative"</c>.</summary>
    public int NegativeCount { get; init; }

    /// <summary>
    /// Ratio of positive ratings to all rated messages (excludes <c>"none"</c>).
    /// Returns <c>0.0</c> when no actionable feedback exists.
    /// </summary>
    public double PositiveRate { get; init; }

    /// <summary>
    /// Per-category breakdown, sorted descending by count.
    /// Only categories with at least one feedback entry are included.
    /// </summary>
    public List<CategoryCount> TopCategories { get; init; } = [];

    /// <summary>
    /// Number of feedback entries that contain a user-supplied
    /// <c>PreferredResponse</c> — i.e., explicit correction examples.
    /// </summary>
    public int PreferredResponseCount { get; init; }
}

/// <summary>
/// The count of feedback records belonging to a single category label.
/// </summary>
public sealed class CategoryCount
{
    /// <summary>
    /// The category name (e.g., <c>"accuracy"</c>, <c>"style"</c>,
    /// <c>"relevance"</c>, <c>"completeness"</c>).
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Number of feedback records in this category.</summary>
    public int Count { get; init; }
}

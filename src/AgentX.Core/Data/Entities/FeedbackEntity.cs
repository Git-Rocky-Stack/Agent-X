namespace AgentX.Core.Data.Entities;

/// <summary>
/// Persists user feedback on individual assistant messages.
/// Feedback records drive the few-shot example pool used to enhance future system prompts
/// and provide signal for identifying anti-patterns in agent behavior.
/// </summary>
public class FeedbackEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Foreign key referencing the rated <see cref="MessageEntity"/>.</summary>
    public long MessageId { get; set; }

    /// <summary>
    /// Denormalized conversation ID stored alongside <see cref="MessageId"/> so that
    /// feedback can be queried by conversation without joining through messages.
    /// </summary>
    public long ConversationId { get; set; }

    /// <summary>
    /// User's qualitative assessment of the message.
    /// Valid values: <c>"positive"</c>, <c>"negative"</c>, <c>"none"</c>.
    /// </summary>
    public string Rating { get; set; } = "none";

    /// <summary>
    /// Optional corrected or preferred response supplied by the user.
    /// When present this is used as a positive few-shot example in subsequent prompts.
    /// </summary>
    public string? PreferredResponse { get; set; }

    /// <summary>Free-text explanation the user optionally provides with their rating.</summary>
    public string? FeedbackNote { get; set; }

    /// <summary>
    /// Broad category describing what aspect of the response the user is rating.
    /// Valid values: <c>"accuracy"</c>, <c>"style"</c>, <c>"relevance"</c>, <c>"completeness"</c>.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>UTC timestamp when the feedback was first submitted.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp of the most recent update (upsert re-uses the same row).</summary>
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <summary>The message this feedback is attached to.</summary>
    public MessageEntity Message { get; set; } = null!;
}

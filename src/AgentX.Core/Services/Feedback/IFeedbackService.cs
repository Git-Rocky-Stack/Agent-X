using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Feedback.Models;

namespace AgentX.Core.Services.Feedback;

/// <summary>
/// Manages user feedback on assistant messages and surfaces that feedback as
/// structured training signal for system-prompt enhancement.
///
/// <para>
/// The service operates in two modes:
/// <list type="bullet">
///   <item><description>
///     <b>Collection</b> — callers submit ratings and optional corrected responses via
///     <see cref="SubmitFeedbackAsync"/>. Each call upserts exactly one row per message so
///     users can revise their rating at any time.
///   </description></item>
///   <item><description>
///     <b>Retrieval</b> — positive examples are returned as formatted few-shot blocks
///     suitable for prepending to a system prompt, giving the model concrete demonstrations
///     of preferred output style and correctness.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Submits or updates feedback for a single assistant message.
    /// If a feedback record already exists for <paramref name="messageId"/> it is updated
    /// in-place; otherwise a new record is inserted.
    /// </summary>
    /// <param name="messageId">ID of the <see cref="MessageEntity"/> being rated.</param>
    /// <param name="conversationId">
    /// Conversation the message belongs to. Stored for efficient per-conversation queries.
    /// </param>
    /// <param name="rating">
    /// Qualitative verdict: <c>"positive"</c>, <c>"negative"</c>, or <c>"none"</c>.
    /// </param>
    /// <param name="preferredResponse">
    /// User-supplied corrected or improved response text. Used as a positive training example.
    /// </param>
    /// <param name="note">Free-text comment the user optionally attaches.</param>
    /// <param name="category">
    /// Aspect being rated: <c>"accuracy"</c>, <c>"style"</c>, <c>"relevance"</c>,
    /// or <c>"completeness"</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task SubmitFeedbackAsync(
        long messageId,
        long conversationId,
        string rating,
        string? preferredResponse = null,
        string? note = null,
        string? category = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the feedback record for <paramref name="messageId"/>, or <c>null</c>
    /// if the user has not yet rated this message.
    /// </summary>
    /// <param name="messageId">ID of the message to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<FeedbackEntity?> GetFeedbackForMessageAsync(
        long messageId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent positive feedback records, ordered newest-first.
    /// Used to build the few-shot example pool.
    /// </summary>
    /// <param name="limit">Maximum number of records to return. Defaults to 50.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<FeedbackEntity>> GetPositiveFeedbackAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent negative feedback records, ordered newest-first.
    /// Used to identify and log anti-patterns.
    /// </summary>
    /// <param name="limit">Maximum number of records to return. Defaults to 50.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<FeedbackEntity>> GetNegativeFeedbackAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Computes aggregate feedback statistics across all stored records.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<FeedbackSummary> GetFeedbackSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Builds a formatted few-shot block from positive feedback entries that contain
    /// a user-supplied <c>PreferredResponse</c>.
    ///
    /// <para>
    /// The returned string is ready for insertion into a system prompt and follows the
    /// format:
    /// <code>
    /// ### Example 1
    /// User: {original message content}
    /// Ideal Response: {preferred response}
    ///
    /// ### Example 2
    /// ...
    /// </code>
    /// An empty string is returned when no suitable examples exist.
    /// </para>
    /// </summary>
    /// <param name="maxExamples">
    /// Maximum number of few-shot examples to include. Defaults to 5.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> BuildFewShotExamplesAsync(
        int maxExamples = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Permanently removes the feedback record with the given <paramref name="feedbackId"/>.
    /// No-ops silently when the record does not exist.
    /// </summary>
    /// <param name="feedbackId">Primary key of the <see cref="FeedbackEntity"/> to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteFeedbackAsync(long feedbackId, CancellationToken ct = default);
}

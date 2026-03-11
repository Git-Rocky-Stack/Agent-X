using System.Text;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Feedback.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Feedback;

/// <summary>
/// EF Core-backed implementation of <see cref="IFeedbackService"/>.
///
/// <para>
/// All write operations are upserts: if a feedback row already exists for the given
/// <c>MessageId</c> it is updated rather than duplicated, ensuring exactly one feedback
/// record per message at all times.
/// </para>
/// <para>
/// Read operations use <c>AsNoTracking()</c> throughout to avoid unnecessary change-tracking
/// overhead on query paths.
/// </para>
/// </summary>
public sealed class FeedbackService : IFeedbackService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _log;

    // Valid rating tokens — enforced at the service boundary to keep the DB consistent.
    private static readonly HashSet<string> ValidRatings =
        new(StringComparer.OrdinalIgnoreCase) { "positive", "negative", "none" };

    // Valid category tokens.
    private static readonly HashSet<string> ValidCategories =
        new(StringComparer.OrdinalIgnoreCase) { "accuracy", "style", "relevance", "completeness" };

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// </summary>
    /// <param name="db">EF Core database context.</param>
    /// <param name="logger">Serilog logger (will be enriched with the service type context).</param>
    public FeedbackService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<FeedbackService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task SubmitFeedbackAsync(
        long messageId,
        long conversationId,
        string rating,
        string? preferredResponse = null,
        string? note = null,
        string? category = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rating);

        // Normalise and validate the rating token.
        var normalisedRating = rating.Trim().ToLowerInvariant();
        if (!ValidRatings.Contains(normalisedRating))
        {
            throw new ArgumentException(
                $"Invalid rating '{rating}'. Valid values are: {string.Join(", ", ValidRatings)}.",
                nameof(rating));
        }

        // Normalise the optional category token.
        string? normalisedCategory = null;
        if (category is not null)
        {
            var trimmed = category.Trim().ToLowerInvariant();
            if (!ValidCategories.Contains(trimmed))
            {
                _log.Warning(
                    "Unrecognised feedback category '{Category}' for message {MessageId} — storing as-is",
                    category, messageId);
                normalisedCategory = category.Trim();
            }
            else
            {
                normalisedCategory = trimmed;
            }
        }

        try
        {
            var now = DateTime.UtcNow;

            // Upsert: look for an existing row for this message.
            var existing = await _db.Set<FeedbackEntity>()
                .FirstOrDefaultAsync(f => f.MessageId == messageId, ct)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                // Update the existing record in-place.
                existing.Rating = normalisedRating;
                existing.PreferredResponse = preferredResponse;
                existing.FeedbackNote = note;
                existing.Category = normalisedCategory;
                existing.UpdatedAt = now;

                _log.Information(
                    "Updated feedback {FeedbackId} for message {MessageId}: rating={Rating}, category={Category}",
                    existing.Id, messageId, normalisedRating, normalisedCategory);
            }
            else
            {
                // Insert a new record.
                var feedback = new FeedbackEntity
                {
                    MessageId = messageId,
                    ConversationId = conversationId,
                    Rating = normalisedRating,
                    PreferredResponse = preferredResponse,
                    FeedbackNote = note,
                    Category = normalisedCategory,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                _db.Set<FeedbackEntity>().Add(feedback);

                _log.Information(
                    "Created feedback for message {MessageId} (conversation {ConversationId}): rating={Rating}, category={Category}",
                    messageId, conversationId, normalisedRating, normalisedCategory);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(
                ex,
                "Failed to submit feedback for message {MessageId} (conversation {ConversationId})",
                messageId, conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<FeedbackEntity?> GetFeedbackForMessageAsync(
        long messageId,
        CancellationToken ct = default)
    {
        try
        {
            var feedback = await _db.Set<FeedbackEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.MessageId == messageId, ct)
                .ConfigureAwait(false);

            if (feedback is null)
            {
                _log.Debug("No feedback found for message {MessageId}", messageId);
            }

            return feedback;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Failed to retrieve feedback for message {MessageId}", messageId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedbackEntity>> GetPositiveFeedbackAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        try
        {
            var results = await _db.Set<FeedbackEntity>()
                .AsNoTracking()
                .Where(f => f.Rating == "positive")
                .Include(f => f.Message)
                .OrderByDescending(f => f.CreatedAt)
                .Take(limit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            _log.Debug("Retrieved {Count} positive feedback records (limit={Limit})", results.Count, limit);

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Failed to retrieve positive feedback records");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedbackEntity>> GetNegativeFeedbackAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        try
        {
            var results = await _db.Set<FeedbackEntity>()
                .AsNoTracking()
                .Where(f => f.Rating == "negative")
                .Include(f => f.Message)
                .OrderByDescending(f => f.CreatedAt)
                .Take(limit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            _log.Debug("Retrieved {Count} negative feedback records (limit={Limit})", results.Count, limit);

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Failed to retrieve negative feedback records");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<FeedbackSummary> GetFeedbackSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            // Single round-trip: pull all ratings and categories in one query.
            var allFeedback = await _db.Set<FeedbackEntity>()
                .AsNoTracking()
                .Select(f => new { f.Rating, f.Category, HasPreferredResponse = f.PreferredResponse != null })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var total = allFeedback.Count;
            var positiveCount = allFeedback.Count(f => f.Rating == "positive");
            var negativeCount = allFeedback.Count(f => f.Rating == "negative");
            var preferredResponseCount = allFeedback.Count(f => f.HasPreferredResponse);

            // Positive rate is computed over records with an actionable rating only.
            var actionable = positiveCount + negativeCount;
            var positiveRate = actionable > 0 ? (double)positiveCount / actionable : 0.0;

            var topCategories = allFeedback
                .Where(f => f.Category is not null)
                .GroupBy(f => f.Category!)
                .Select(g => new CategoryCount { Category = g.Key, Count = g.Count() })
                .OrderByDescending(c => c.Count)
                .ToList();

            var summary = new FeedbackSummary
            {
                TotalFeedback = total,
                PositiveCount = positiveCount,
                NegativeCount = negativeCount,
                PositiveRate = positiveRate,
                TopCategories = topCategories,
                PreferredResponseCount = preferredResponseCount,
            };

            _log.Debug(
                "Feedback summary: total={Total}, positive={Positive}, negative={Negative}, rate={Rate:P1}",
                total, positiveCount, negativeCount, positiveRate);

            return summary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Failed to compute feedback summary");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> BuildFewShotExamplesAsync(
        int maxExamples = 5,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExamples);

        try
        {
            // Only entries with a user-supplied preferred response are useful as few-shot examples.
            var candidates = await _db.Set<FeedbackEntity>()
                .AsNoTracking()
                .Where(f => f.Rating == "positive" && f.PreferredResponse != null)
                .Include(f => f.Message)
                .OrderByDescending(f => f.CreatedAt)
                .Take(maxExamples)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                _log.Debug("No few-shot examples available (no positive feedback with preferred responses)");
                return string.Empty;
            }

            var sb = new StringBuilder(candidates.Count * 512);

            for (var i = 0; i < candidates.Count; i++)
            {
                var entry = candidates[i];

                // Derive the original user question from the associated message.
                // If the message is the assistant turn, use its content as the exchange context.
                var userContent = entry.Message?.Content ?? "(original question unavailable)";

                sb.AppendLine($"### Example {i + 1}");
                sb.Append("User: ");
                sb.AppendLine(userContent.Trim());
                sb.Append("Ideal Response: ");
                sb.AppendLine(entry.PreferredResponse!.Trim());

                // Separate examples with a blank line for readability in the prompt.
                if (i < candidates.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            var result = sb.ToString().TrimEnd();

            _log.Information(
                "Built {Count} few-shot example(s) ({Length} chars)",
                candidates.Count, result.Length);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Failed to build few-shot examples");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteFeedbackAsync(long feedbackId, CancellationToken ct = default)
    {
        try
        {
            var feedback = await _db.Set<FeedbackEntity>()
                .FindAsync([feedbackId], ct)
                .ConfigureAwait(false);

            if (feedback is null)
            {
                _log.Warning("DeleteFeedbackAsync: feedback {FeedbackId} not found — no-op", feedbackId);
                return;
            }

            _db.Set<FeedbackEntity>().Remove(feedback);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _log.Information("Deleted feedback {FeedbackId}", feedbackId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Failed to delete feedback {FeedbackId}", feedbackId);
            throw;
        }
    }
}

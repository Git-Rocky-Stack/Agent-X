using System.Globalization;
using System.Text;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.TemporalIdentity;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Annotations;

/// <summary>
/// EF Core-backed implementation of <see cref="IAnnotationService"/>.
/// Manages all annotation and highlight persistence operations against
/// the AgentX SQLite database.
/// </summary>
public class AnnotationService : IAnnotationService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _log;
    private readonly ITemporalIdentityService? _temporalIdentity;

    /// <summary>
    /// Valid colour labels accepted by the annotation system.
    /// </summary>
    private static readonly HashSet<string> ValidColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "yellow", "green", "blue", "red", "purple"
        };

    /// <param name="temporalIdentity">
    /// Optional. When supplied, every new annotation is handed to Temporal Identity, which keeps
    /// highlights as insight moments (resolved from DI when registered).
    /// </param>
    public AnnotationService(AgentXDbContext db, ILogger logger, ITemporalIdentityService? temporalIdentity = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<AnnotationService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _temporalIdentity = temporalIdentity;
    }

    /// <inheritdoc />
    public async Task<AnnotationEntity> CreateAnnotationAsync(
        long documentId,
        long? chunkId,
        int startOffset,
        int endOffset,
        string highlightedText,
        string color,
        string? noteText = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(highlightedText))
                throw new ArgumentException(
                    "Highlighted text must not be empty.", nameof(highlightedText));

            if (startOffset < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(startOffset), "Start offset must be zero or greater.");

            if (endOffset <= startOffset)
                throw new ArgumentOutOfRangeException(
                    nameof(endOffset), "End offset must be greater than start offset.");

            if (!ValidColors.Contains(color))
                throw new ArgumentException(
                    $"Color '{color}' is not valid. Accepted values: yellow, green, blue, red, purple.",
                    nameof(color));

            var now = DateTime.UtcNow;

            var annotation = new AnnotationEntity
            {
                DocumentId = documentId,
                ChunkId = chunkId,
                StartOffset = startOffset,
                EndOffset = endOffset,
                HighlightedText = highlightedText.Trim(),
                // A blank note is no note, the same rule UpdateAnnotationAsync applies.
                NoteText = string.IsNullOrWhiteSpace(noteText) ? null : noteText.Trim(),
                Color = color.ToLowerInvariant(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            _db.Annotations.Add(annotation);
            await _db.SaveChangesAsync();

            _log.Information(
                "Created annotation {AnnotationId} on document {DocumentId} " +
                "(offsets {Start}-{End}, color={Color})",
                annotation.Id, documentId, startOffset, endOffset, annotation.Color);

            await CaptureAsInsightAsync(annotation.Id);
            return annotation;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to create annotation on document {DocumentId}",
                documentId);
            throw;
        }
    }

    /// <summary>
    /// A highlight is a strong belief signal (the user chose to mark it), so Temporal Identity
    /// keeps it as an insight moment. Nothing called it before, so highlights never reached
    /// Past Self. The annotation is already saved: a failure here is logged, never thrown.
    /// </summary>
    private async Task CaptureAsInsightAsync(long annotationId)
    {
        if (_temporalIdentity is null)
        {
            return;
        }

        try
        {
            await _temporalIdentity.ProcessAnnotationAsync(annotationId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Temporal identity could not process annotation {AnnotationId}", annotationId);
        }
    }

    /// <inheritdoc />
    public async Task<AnnotationPassage?> GetPassageAsync(
        long documentId,
        int position,
        CancellationToken ct = default)
    {
        try
        {
            var chunks = _db.DocumentChunks
                .AsNoTracking()
                .Where(c => c.DocumentId == documentId);

            var count = await chunks.CountAsync(ct);
            if (count == 0)
            {
                return null;
            }

            var clamped = Math.Clamp(position, 0, count - 1);
            var chunk = await chunks
                .OrderBy(c => c.ChunkIndex)
                .ThenBy(c => c.Id)
                .Skip(clamped)
                .Select(c => new { c.Id, c.PageNumber, c.Content })
                .FirstOrDefaultAsync(ct);

            // The chunks can be replaced between the two reads (a re-index); the caller then
            // shows no text rather than a passage that no longer exists.
            return chunk is null
                ? null
                : new AnnotationPassage(chunk.Id, clamped, count, chunk.PageNumber, chunk.Content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to read passage {Position} of document {DocumentId}",
                position, documentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AnnotationEntity?> GetAnnotationAsync(long annotationId)
    {
        try
        {
            var annotation = await _db.Annotations
                .Include(a => a.Document)
                .FirstOrDefaultAsync(a => a.Id == annotationId);

            if (annotation is null)
            {
                _log.Warning("Annotation {AnnotationId} not found", annotationId);
            }

            return annotation;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get annotation {AnnotationId}", annotationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnnotationEntity>> GetAnnotationsForDocumentAsync(
        long documentId)
    {
        try
        {
            var annotations = await _db.Annotations
                .Include(a => a.Document)
                .Where(a => a.DocumentId == documentId)
                .OrderBy(a => a.StartOffset)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} annotations for document {DocumentId}",
                annotations.Count, documentId);

            return annotations;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to get annotations for document {DocumentId}",
                documentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnnotationEntity>> GetAnnotationsByColorAsync(string color)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(color))
                throw new ArgumentException(
                    "Color must not be empty.", nameof(color));

            var normalizedColor = color.Trim().ToLowerInvariant();

            var annotations = await _db.Annotations
                .Include(a => a.Document)
                .Where(a => a.Color == normalizedColor)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} annotations with color '{Color}'",
                annotations.Count, normalizedColor);

            return annotations;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get annotations by color '{Color}'", color);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnnotationEntity>> SearchAnnotationsAsync(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return await GetAllAnnotationsAsync();
            }

            var searchPattern = $"%{query.Trim()}%";

            var annotations = await _db.Annotations
                .Include(a => a.Document)
                .Where(a =>
                    EF.Functions.Like(a.HighlightedText, searchPattern) ||
                    (a.NoteText != null && EF.Functions.Like(a.NoteText, searchPattern)))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            _log.Debug(
                "Annotation search for '{Query}' returned {Count} results",
                query, annotations.Count);

            return annotations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to search annotations with query '{Query}'", query);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnnotationEntity>> GetAllAnnotationsAsync(
        int skip = 0,
        int take = 50)
    {
        try
        {
            var annotations = await _db.Annotations
                .Include(a => a.Document)
                .OrderByDescending(a => a.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} annotations (skip={Skip}, take={Take})",
                annotations.Count, skip, take);

            return annotations;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to get all annotations (skip={Skip}, take={Take})",
                skip, take);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetAnnotationCountAsync(long? documentId = null)
    {
        try
        {
            var count = documentId.HasValue
                ? await _db.Annotations.CountAsync(a => a.DocumentId == documentId.Value)
                : await _db.Annotations.CountAsync();

            return count;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to get annotation count (documentId={DocumentId})",
                documentId?.ToString() ?? "all");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AnnotationEntity> UpdateAnnotationAsync(
        long annotationId,
        string? noteText = null,
        string? color = null)
    {
        try
        {
            if (color is not null && !ValidColors.Contains(color))
                throw new ArgumentException(
                    $"Color '{color}' is not valid. Accepted values: yellow, green, blue, red, purple.",
                    nameof(color));

            var annotation = await _db.Annotations
                .Include(a => a.Document)
                .FirstOrDefaultAsync(a => a.Id == annotationId);

            if (annotation is null)
            {
                _log.Error(
                    "Cannot update: annotation {AnnotationId} not found",
                    annotationId);
                throw new InvalidOperationException(
                    $"Annotation {annotationId} not found.");
            }

            if (noteText is not null)
            {
                annotation.NoteText = noteText.Trim().Length == 0
                    ? null
                    : noteText.Trim();
            }

            if (color is not null)
            {
                annotation.Color = color.Trim().ToLowerInvariant();
            }

            annotation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Information(
                "Updated annotation {AnnotationId} (hasNoteChange={HasNote}, hasColorChange={HasColor})",
                annotationId, noteText is not null, color is not null);

            return annotation;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update annotation {AnnotationId}", annotationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAnnotationAsync(long annotationId)
    {
        try
        {
            var annotation = await _db.Annotations.FindAsync(annotationId);
            if (annotation is null)
            {
                _log.Warning(
                    "Cannot delete: annotation {AnnotationId} not found",
                    annotationId);
                return;
            }

            _db.Annotations.Remove(annotation);
            await _db.SaveChangesAsync();

            _log.Information("Deleted annotation {AnnotationId}", annotationId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete annotation {AnnotationId}", annotationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAnnotationsForDocumentAsync(long documentId)
    {
        try
        {
            var annotations = await _db.Annotations
                .Where(a => a.DocumentId == documentId)
                .ToListAsync();

            if (annotations.Count == 0)
            {
                _log.Debug(
                    "No annotations to delete for document {DocumentId}",
                    documentId);
                return;
            }

            _db.Annotations.RemoveRange(annotations);
            await _db.SaveChangesAsync();

            _log.Information(
                "Deleted {Count} annotations for document {DocumentId}",
                annotations.Count, documentId);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to delete annotations for document {DocumentId}",
                documentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnnotationEntity>> GetRecentAnnotationsAsync(
        int count = 20)
    {
        try
        {
            var annotations = await _db.Annotations
                .Include(a => a.Document)
                .OrderByDescending(a => a.CreatedAt)
                .Take(count)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} recent annotations",
                annotations.Count);

            return annotations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get recent annotations (count={Count})", count);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetColorDistributionAsync()
    {
        try
        {
            var distribution = await _db.Annotations
                .GroupBy(a => a.Color)
                .Select(g => new { Color = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = distribution.ToDictionary(
                x => x.Color,
                x => x.Count,
                StringComparer.OrdinalIgnoreCase);

            _log.Debug(
                "Color distribution: {Distribution}",
                string.Join(", ", result.Select(kvp => $"{kvp.Key}={kvp.Value}")));

            return result;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get annotation color distribution");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> ExportAnnotationsAsMarkdownAsync(long? documentId = null)
    {
        try
        {
            // Build the query: either scope to a single document or fetch all.
            var query = _db.Annotations
                .Include(a => a.Document)
                .AsQueryable();

            if (documentId.HasValue)
            {
                query = query.Where(a => a.DocumentId == documentId.Value);
            }

            var annotations = await query
                .OrderBy(a => a.DocumentId)
                .ThenBy(a => a.StartOffset)
                .ToListAsync();

            if (annotations.Count == 0)
            {
                _log.Debug(
                    "Export: no annotations found (documentId={DocumentId})",
                    documentId?.ToString() ?? "all");

                return documentId.HasValue
                    ? "# Annotations\n\n_No annotations found for this document._\n"
                    : "# Annotations\n\n_No annotations found._\n";
            }

            var sb = new StringBuilder();

            // Top-level heading.
            sb.AppendLine("# Annotations");
            sb.AppendLine();
            // Timestamps use the invariant culture: under th-TH or ar-SA the current culture's
            // calendar turned these ISO-style dates into Buddhist or Hijri years.
            sb.AppendLine(
                $"_Exported {annotations.Count} annotation{(annotations.Count == 1 ? string.Empty : "s")} " +
                $"on {IsoMinutes(DateTime.UtcNow)} UTC_");
            sb.AppendLine();

            // Group by document for readability.
            var groups = annotations
                .GroupBy(a => a.DocumentId)
                .OrderBy(g => g.First().Document?.FileName ?? string.Empty);

            foreach (var group in groups)
            {
                var documentName = group.First().Document?.FileName
                    ?? $"Document {group.Key}";

                sb.AppendLine($"## {documentName}");
                sb.AppendLine();

                foreach (var annotation in group)
                {
                    // Colour badge.
                    var colorLabel = annotation.Color.Length > 0
                        ? char.ToUpperInvariant(annotation.Color[0]) + annotation.Color[1..]
                        : annotation.Color;

                    sb.AppendLine($"### [{colorLabel}] Highlight (offset {annotation.StartOffset}-{annotation.EndOffset})");
                    sb.AppendLine();

                    // The highlighted text in a blockquote.
                    sb.AppendLine($"> {annotation.HighlightedText}");
                    sb.AppendLine();

                    // User note, if present.
                    if (!string.IsNullOrWhiteSpace(annotation.NoteText))
                    {
                        sb.AppendLine("**Note:**");
                        sb.AppendLine();
                        sb.AppendLine(annotation.NoteText);
                        sb.AppendLine();
                    }

                    sb.AppendLine(
                        $"_Created: {IsoMinutes(annotation.CreatedAt)} UTC" +
                        (annotation.UpdatedAt != annotation.CreatedAt
                            ? $" | Updated: {IsoMinutes(annotation.UpdatedAt)} UTC"
                            : string.Empty) +
                        "_");

                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            var markdown = sb.ToString().TrimEnd() + Environment.NewLine;

            _log.Information(
                "Exported {Count} annotations as Markdown ({Chars} chars, documentId={DocumentId})",
                annotations.Count, markdown.Length, documentId?.ToString() ?? "all");

            return markdown;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to export annotations as Markdown (documentId={DocumentId})",
                documentId?.ToString() ?? "all");
            throw;
        }
    }

    private static string IsoMinutes(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
